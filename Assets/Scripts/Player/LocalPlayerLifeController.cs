using System;
using System.Collections;
using System.Globalization;
using Game.Client.BattleSync;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LocalPlayerLifeController : MonoBehaviour
{
    private enum LocalLifeState
    {
        Unknown,
        Alive,
        Dead,
        RespawnRequestPending,
        WaitingAuthoritativePosition
    }

    [Header("Death UI")]
    [SerializeField] private GameObject deathDialog;
    [SerializeField] private TMP_Text deathMessageText;
    [SerializeField] private Button respawnButton;
    [SerializeField] private TMP_Text respawnButtonText;

    [Header("Death Timeout")]
    [SerializeField, Min(0f)] private float timeoutMessageSeconds = 1.5f;
    [SerializeField, Min(1f)] private float respawnRecoveryTimeoutSeconds = 5f;

    [Header("References")]
    [SerializeField] private CombatStateStore stateStore;
    [SerializeField] private CombatMessageReceiver combatReceiver;
    [SerializeField] private GameSession gameSession;
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private NetMessageDispatcher messageDispatcher;

    private LocalLifeState currentState = LocalLifeState.Unknown;
    private long nextRespawnRequestSequence;
    private long pendingRespawnRequestSequence;
    private long sequenceConnectionEpoch;
    private long lifeCombatRevision;
    private long respawnServerTick;
    private long deathEventServerTime;
    private long respawnAvailableAt;
    private long deathKickAt;
    private double deathEventReceivedRealtimeMs;
    private float respawnLocationRetryAt = float.NegativeInfinity;
    private Coroutine deathCountdownCoroutine;
    private Coroutine timeoutReturnCoroutine;
    private Coroutine respawnRequestTimeoutCoroutine;
    private Coroutine authoritativeStateTimeoutCoroutine;
    private bool awaitingReenterSnapshot;
    private long reenterConnectionEpoch;
    private bool awaitingAliveAfterNotDead;
    private bool subscribed;

    private void Awake()
    {
        ResolveReferences();
        if (respawnButton != null)
        {
            respawnButton.onClick.AddListener(RequestRespawn);
        }
        SetDeathDialogVisible(false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        RefreshFromStore();
    }

    private void Start()
    {
        RefreshFromStore();
    }

    private void OnDisable()
    {
        Unsubscribe();
        StopDeathCountdown();
        StopTimeoutReturn();
        StopRespawnRequestTimeout();
        StopAuthoritativeStateTimeout();
    }

    private void OnDestroy()
    {
        if (respawnButton != null)
        {
            respawnButton.onClick.RemoveListener(RequestRespawn);
        }
    }

    public void RequestRespawn()
    {
        if (currentState != LocalLifeState.Dead
            || networkManager == null
            || gameSession == null
            || !IsRespawnAvailableNow())
        {
            return;
        }

        StopDeathCountdown();
        EnsureRespawnSequenceForConnection();
        long requestSequence = ++nextRespawnRequestSequence;
        RespawnRequest request = new RespawnRequest
        {
            requestSequence = requestSequence,
            clientTick = Math.Max(1L, gameSession.CurrentClientTick)
        };

        SetRespawnButton(false, "请求中...");
        if (!networkManager.SendRespawn(request))
        {
            nextRespawnRequestSequence--;
            currentState = LocalLifeState.Dead;
            SetDeathMessage("您已死亡\n连接不可用，请稍后重试");
            RefreshRespawnButton();
            return;
        }

        pendingRespawnRequestSequence = requestSequence;
        currentState = LocalLifeState.RespawnRequestPending;
        StartRespawnRequestTimeout();
    }

    private void HandleCombatStateChanged(CombatEntityKey key, CombatStateData state)
    {
        if (!IsLocalPlayer(key) || state == null)
        {
            return;
        }

        bool isDead = state.Hp <= 0
            || string.Equals(state.LifeState, "Dead", StringComparison.OrdinalIgnoreCase);
        if (isDead)
        {
            awaitingReenterSnapshot = false;
            reenterConnectionEpoch = 0;
            EnterDeadStateFromSnapshot(state.CombatRevision);
            return;
        }

        if (awaitingReenterSnapshot
            && networkManager != null
            && networkManager.ConnectionEpoch == reenterConnectionEpoch)
        {
            awaitingReenterSnapshot = false;
            awaitingAliveAfterNotDead = false;
            reenterConnectionEpoch = 0;
            lifeCombatRevision = state.CombatRevision;
            respawnServerTick = 0;
            ClearDeathTiming();
            SetDeathDialogVisible(false);
            if (gameSession.IsLocalPlayerDead)
            {
                currentState = LocalLifeState.WaitingAuthoritativePosition;
                StartAuthoritativeStateTimeout();
                HandleRespawnControl();
            }
            else
            {
                currentState = LocalLifeState.Alive;
            }
            return;
        }

        if (awaitingAliveAfterNotDead)
        {
            awaitingAliveAfterNotDead = false;
            currentState = LocalLifeState.WaitingAuthoritativePosition;
            respawnServerTick = 0;
            SetDeathDialogVisible(false);
            gameSession?.PrepareLocalRespawn();
            StartAuthoritativeStateTimeout();
            HandleRespawnControl();
            return;
        }

        if (currentState == LocalLifeState.Unknown)
        {
            currentState = LocalLifeState.Alive;
            SetDeathDialogVisible(false);
        }
    }

    private void HandleCombatEventApplied(CombatEventMessage message)
    {
        if (message == null || !IsLocalPlayer(SelectAffectedEntity(message)))
        {
            return;
        }

        if (string.Equals(message.eventType, "death", StringComparison.OrdinalIgnoreCase))
        {
            EnterDeadStateFromEvent(message);
            return;
        }

        bool waitingAfterSnapshot = currentState == LocalLifeState.WaitingAuthoritativePosition
            && respawnServerTick == 0;
        bool acceptsRespawn = currentState == LocalLifeState.Dead
            || currentState == LocalLifeState.RespawnRequestPending
            || waitingAfterSnapshot;
        bool validRespawnRevision = message.combatRevision > lifeCombatRevision
            || (waitingAfterSnapshot && message.combatRevision == lifeCombatRevision);
        if (!string.Equals(message.eventType, "respawn", StringComparison.OrdinalIgnoreCase)
            || !acceptsRespawn || !validRespawnRevision || message.hpAfter <= 0)
        {
            return;
        }

        StopDeathCountdown();
        StopRespawnRequestTimeout();
        pendingRespawnRequestSequence = 0;
        awaitingAliveAfterNotDead = false;
        lifeCombatRevision = message.combatRevision;
        respawnServerTick = message.serverTick;
        currentState = LocalLifeState.WaitingAuthoritativePosition;
        ClearDeathTiming();
        SetDeathDialogVisible(false);
        gameSession?.PrepareLocalRespawn();
        StartAuthoritativeStateTimeout();
        HandleRespawnControl();
    }

    private void HandleRespawnControl()
    {
        if (currentState != LocalLifeState.WaitingAuthoritativePosition)
        {
            return;
        }
        if (gameSession == null || !gameSession.TryApplyLocalRespawnControl())
        {
            return;
        }

        currentState = LocalLifeState.Alive;
        respawnServerTick = 0;
        StopAuthoritativeStateTimeout();
        gameSession?.ResumeLocalPlayerControl();
    }

    private void HandleServerError(string json)
    {
        ServerErrorMessage error;
        try
        {
            error = JsonUtility.FromJson<ServerErrorMessage>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Respawn error parse failed: " + exception.Message);
            return;
        }

        if (error == null
            || !string.Equals(error.requestMsgId, "respawn", StringComparison.OrdinalIgnoreCase)
            || currentState != LocalLifeState.RespawnRequestPending
            || pendingRespawnRequestSequence <= 0)
        {
            return;
        }

        StopRespawnRequestTimeout();
        long rejectedSequence = pendingRespawnRequestSequence;
        pendingRespawnRequestSequence = 0;

        if (error.errorCode == "player_not_in_world")
        {
            gameSession?.RecoverWorldSession("respawn_player_not_in_world");
            return;
        }

        if (error.errorCode == "respawn_not_dead")
        {
            awaitingAliveAfterNotDead = true;
            currentState = LocalLifeState.Dead;
            SetDeathDialogVisible(true);
            SetDeathMessage(string.IsNullOrWhiteSpace(error.message)
                ? "正在确认角色状态"
                : error.message);
            SetRespawnButton(false, "正在同步");
            StartAuthoritativeStateTimeout();
            return;
        }

        currentState = LocalLifeState.Dead;
        SetDeathDialogVisible(true);
        if (error.errorCode == "respawn_location_unavailable")
        {
            respawnLocationRetryAt = Time.realtimeSinceStartup
                + Mathf.Max(10f, respawnRecoveryTimeoutSeconds);
            SetDeathMessage(string.IsNullOrWhiteSpace(error.message)
                ? "当前位置暂无安全复活点，请稍后重试"
                : error.message);
            StartDeathCountdown();
            return;
        }
        if (error.errorCode == "respawn_sequence_gap")
        {
            long expectedSequence = error.expectedRequestSequence;
            if (expectedSequence <= 0)
            {
                TryExtractExpectedSequence(error.message, out expectedSequence);
            }
            if (expectedSequence > 0)
            {
                nextRespawnRequestSequence = expectedSequence - 1L;
            }
            SetDeathMessage("复活请求序号已校正，请重新请求");
        }
        else if (error.errorCode == "invalid_respawn")
        {
            if (rejectedSequence > 0)
            {
                nextRespawnRequestSequence = rejectedSequence - 1L;
            }
            SetDeathMessage(string.IsNullOrWhiteSpace(error.message)
                ? "复活请求无效，请重试"
                : error.message);
        }
        else
        {
            SetDeathMessage(string.IsNullOrWhiteSpace(error.message)
                ? "复活请求失败，请重试"
                : error.message);
        }
        RefreshRespawnButton();
    }

    private void HandleDeathTimeoutKick(string json)
    {
        DeathTimeoutKickMessage message;
        try
        {
            message = JsonUtility.FromJson<DeathTimeoutKickMessage>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Death timeout kick parse failed: " + exception.Message);
            return;
        }

        if (message == null
            || !string.Equals(message.errorCode, "death_respawn_timeout", StringComparison.Ordinal))
        {
            return;
        }

        StopDeathCountdown();
        StopRespawnRequestTimeout();
        StopAuthoritativeStateTimeout();
        pendingRespawnRequestSequence = 0;
        awaitingAliveAfterNotDead = false;
        awaitingReenterSnapshot = false;
        currentState = LocalLifeState.Dead;
        SetDeathDialogVisible(true);
        SetDeathMessage("复活超时，请重新登录");
        SetRespawnButton(false, "复活已超时");
        if (networkManager != null)
        {
            networkManager.Disconnect();
        }
        StartTimeoutReturn();
    }

    private void EnterDeadStateFromSnapshot(long combatRevision)
    {
        if (!CanAcceptDeathRevision(combatRevision)) return;

        lifeCombatRevision = Math.Max(lifeCombatRevision, combatRevision);
        if (currentState == LocalLifeState.Dead
            || currentState == LocalLifeState.RespawnRequestPending)
        {
            return;
        }

        currentState = LocalLifeState.Dead;
        respawnServerTick = 0;
        gameSession?.EnterLocalDeadState();
        SetDeathMessage("您已死亡");
        SetDeathDialogVisible(true);
        SetRespawnButton(false, "等待复活信息");
    }

    private void EnterDeadStateFromEvent(CombatEventMessage message)
    {
        if (!CanAcceptDeathRevision(message.combatRevision)) return;

        StopRespawnRequestTimeout();
        StopAuthoritativeStateTimeout();
        pendingRespawnRequestSequence = 0;
        awaitingAliveAfterNotDead = false;
        lifeCombatRevision = Math.Max(lifeCombatRevision, message.combatRevision);
        currentState = LocalLifeState.Dead;
        respawnServerTick = 0;
        respawnLocationRetryAt = float.NegativeInfinity;
        deathEventServerTime = message.serverTime;
        respawnAvailableAt = message.respawnAvailableAt;
        deathKickAt = message.deathKickAt;
        deathEventReceivedRealtimeMs = Time.realtimeSinceStartupAsDouble * 1000d;
        gameSession?.EnterLocalDeadState();

        string killerName = string.IsNullOrWhiteSpace(message.killerName)
            ? "未知目标"
            : message.killerName;
        SetDeathMessage("您被 " + killerName + " 击杀");
        SetDeathDialogVisible(true);
        StartDeathCountdown();
    }

    private bool CanAcceptDeathRevision(long revision)
    {
        return revision >= lifeCombatRevision
            && (revision != lifeCombatRevision || revision <= 0
                || (currentState != LocalLifeState.Alive
                    && currentState != LocalLifeState.WaitingAuthoritativePosition));
    }

    private void StartDeathCountdown()
    {
        StopDeathCountdown();
        RefreshRespawnButton();
        deathCountdownCoroutine = StartCoroutine(DeathCountdownCoroutine());
    }

    private IEnumerator DeathCountdownCoroutine()
    {
        while (currentState == LocalLifeState.Dead)
        {
            RefreshRespawnButton();
            yield return null;
        }
        deathCountdownCoroutine = null;
    }

    private void RefreshRespawnButton()
    {
        long estimatedServerTime = EstimatedServerTime();
        if (respawnAvailableAt <= 0 || deathEventServerTime <= 0)
        {
            SetRespawnButton(false, "等待复活信息");
            return;
        }
        if (deathKickAt > 0 && estimatedServerTime >= deathKickAt)
        {
            SetRespawnButton(false, "复活已超时");
            return;
        }
        if (estimatedServerTime < respawnAvailableAt)
        {
            long remainingMs = respawnAvailableAt - estimatedServerTime;
            long remainingSeconds = Math.Max(1L, (remainingMs + 999L) / 1000L);
            SetRespawnButton(false, remainingSeconds + "秒后可复活");
            return;
        }
        if (Time.realtimeSinceStartup < respawnLocationRetryAt)
        {
            SetRespawnButton(false, "暂无安全落点");
            return;
        }
        SetRespawnButton(true, "复活");
    }

    private bool IsRespawnAvailableNow()
    {
        long estimatedServerTime = EstimatedServerTime();
        return respawnAvailableAt > 0
            && estimatedServerTime >= respawnAvailableAt
            && Time.realtimeSinceStartup >= respawnLocationRetryAt
            && (deathKickAt <= 0 || estimatedServerTime < deathKickAt);
    }

    private long EstimatedServerTime()
    {
        if (deathEventServerTime <= 0)
        {
            return 0;
        }
        double elapsedMs = Time.realtimeSinceStartupAsDouble * 1000d
            - deathEventReceivedRealtimeMs;
        return deathEventServerTime + Math.Max(0L, (long)elapsedMs);
    }

    private void EnsureRespawnSequenceForConnection()
    {
        long epoch = networkManager == null ? 0 : networkManager.ConnectionEpoch;
        if (epoch == sequenceConnectionEpoch)
        {
            return;
        }
        sequenceConnectionEpoch = epoch;
        nextRespawnRequestSequence = 0;
    }

    private static bool TryExtractExpectedSequence(string message, out long expectedSequence)
    {
        expectedSequence = 0;
        const string marker = "expectedSequence=";
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        int start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }
        start += marker.Length;
        int end = start;
        while (end < message.Length && char.IsDigit(message[end]))
        {
            end++;
        }
        return end > start
            && long.TryParse(message.Substring(start, end - start),
                NumberStyles.None, CultureInfo.InvariantCulture, out expectedSequence)
            && expectedSequence > 0;
    }

    private void StartTimeoutReturn()
    {
        StopTimeoutReturn();
        timeoutReturnCoroutine = StartCoroutine(ReturnToLoginAfterTimeout());
    }

    private void HandleLocalWorldEntered(long connectionEpoch, long characterDbId)
    {
        if (gameSession == null || characterDbId <= 0
            || characterDbId != gameSession.LocalCharacterDbId)
        {
            return;
        }

        StopDeathCountdown();
        StopRespawnRequestTimeout();
        StopAuthoritativeStateTimeout();
        ClearDeathTiming();
        respawnServerTick = 0;
        nextRespawnRequestSequence = 0;
        pendingRespawnRequestSequence = 0;
        sequenceConnectionEpoch = connectionEpoch;
        awaitingAliveAfterNotDead = false;
        awaitingReenterSnapshot = true;
        reenterConnectionEpoch = connectionEpoch;
        lifeCombatRevision = 0;
        currentState = LocalLifeState.Unknown;
        RefreshFromStore();
    }

    private void HandleNetworkDisconnected(string reason)
    {
        StopRespawnRequestTimeout();
        StopAuthoritativeStateTimeout();
        pendingRespawnRequestSequence = 0;
        awaitingAliveAfterNotDead = false;
    }

    private void StartRespawnRequestTimeout()
    {
        StopRespawnRequestTimeout();
        respawnRequestTimeoutCoroutine = StartCoroutine(RespawnRequestTimeoutCoroutine());
    }

    private IEnumerator RespawnRequestTimeoutCoroutine()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(1f, respawnRecoveryTimeoutSeconds));
        respawnRequestTimeoutCoroutine = null;
        if (currentState == LocalLifeState.RespawnRequestPending)
        {
            gameSession?.RecoverWorldSession("respawn_request_timeout");
        }
    }

    private void StopRespawnRequestTimeout()
    {
        if (respawnRequestTimeoutCoroutine == null) return;
        StopCoroutine(respawnRequestTimeoutCoroutine);
        respawnRequestTimeoutCoroutine = null;
    }

    private void StartAuthoritativeStateTimeout()
    {
        StopAuthoritativeStateTimeout();
        authoritativeStateTimeoutCoroutine = StartCoroutine(AuthoritativeStateTimeoutCoroutine());
    }

    private IEnumerator AuthoritativeStateTimeoutCoroutine()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(1f, respawnRecoveryTimeoutSeconds));
        authoritativeStateTimeoutCoroutine = null;
        if (currentState == LocalLifeState.WaitingAuthoritativePosition
            || awaitingAliveAfterNotDead)
        {
            gameSession?.RecoverWorldSession("respawn_authoritative_state_timeout");
        }
    }

    private void StopAuthoritativeStateTimeout()
    {
        if (authoritativeStateTimeoutCoroutine == null) return;
        StopCoroutine(authoritativeStateTimeoutCoroutine);
        authoritativeStateTimeoutCoroutine = null;
    }

    private IEnumerator ReturnToLoginAfterTimeout()
    {
        float endTime = Time.realtimeSinceStartup + Mathf.Max(0f, timeoutMessageSeconds);
        while (Time.realtimeSinceStartup < endTime)
        {
            yield return null;
        }
        timeoutReturnCoroutine = null;
        gameSession?.ReturnToLogin("death_respawn_timeout");
    }

    private void StopDeathCountdown()
    {
        if (deathCountdownCoroutine == null)
        {
            return;
        }
        StopCoroutine(deathCountdownCoroutine);
        deathCountdownCoroutine = null;
    }

    private void StopTimeoutReturn()
    {
        if (timeoutReturnCoroutine == null)
        {
            return;
        }
        StopCoroutine(timeoutReturnCoroutine);
        timeoutReturnCoroutine = null;
    }

    private void ClearDeathTiming()
    {
        deathEventServerTime = 0;
        respawnAvailableAt = 0;
        deathKickAt = 0;
        deathEventReceivedRealtimeMs = 0d;
        respawnLocationRetryAt = float.NegativeInfinity;
    }

    private void RefreshFromStore()
    {
        ResolveReferences();
        if (stateStore == null || gameSession == null || gameSession.LocalCharacterDbId <= 0)
        {
            return;
        }
        string entityId = gameSession.LocalCharacterDbId.ToString(CultureInfo.InvariantCulture);
        if (stateStore.TryGetState("player", entityId, out CombatStateData state))
        {
            HandleCombatStateChanged(new CombatEntityKey("player", entityId), state);
        }
    }

    private bool IsLocalPlayer(CombatEntityKey key)
    {
        if (gameSession == null || gameSession.LocalCharacterDbId <= 0)
        {
            ResolveReferences();
        }
        return gameSession != null
            && gameSession.LocalCharacterDbId > 0
            && string.Equals(key.EntityType, "player", StringComparison.OrdinalIgnoreCase)
            && string.Equals(key.EntityId,
                gameSession.LocalCharacterDbId.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }

    private bool IsLocalPlayer(CombatEntityMessage entity)
    {
        return entity != null
            && IsLocalPlayer(new CombatEntityKey(entity.entityType, entity.entityId));
    }

    private static CombatEntityMessage SelectAffectedEntity(CombatEventMessage message)
    {
        return message.target != null
            && !string.IsNullOrEmpty(message.target.entityType)
            && !string.IsNullOrEmpty(message.target.entityId)
            ? message.target
            : message.source;
    }

    private void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        if (stateStore != null) stateStore.StateChanged += HandleCombatStateChanged;
        if (combatReceiver != null) combatReceiver.CombatEventApplied += HandleCombatEventApplied;
        if (gameSession != null) gameSession.LocalRespawnControlReceived += HandleRespawnControl;
        if (gameSession != null) gameSession.LocalWorldEntered += HandleLocalWorldEntered;
        if (networkManager != null) networkManager.OnDisconnected += HandleNetworkDisconnected;
        if (messageDispatcher != null)
        {
            messageDispatcher.Register("error", HandleServerError);
            messageDispatcher.Register(BattleSyncProtocolConstants.MessageIds.DeathTimeoutKick,
                HandleDeathTimeoutKick);
        }
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }
        if (stateStore != null) stateStore.StateChanged -= HandleCombatStateChanged;
        if (combatReceiver != null) combatReceiver.CombatEventApplied -= HandleCombatEventApplied;
        if (gameSession != null) gameSession.LocalRespawnControlReceived -= HandleRespawnControl;
        if (gameSession != null) gameSession.LocalWorldEntered -= HandleLocalWorldEntered;
        if (networkManager != null) networkManager.OnDisconnected -= HandleNetworkDisconnected;
        if (messageDispatcher != null)
        {
            messageDispatcher.Unregister("error", HandleServerError);
            messageDispatcher.Unregister(BattleSyncProtocolConstants.MessageIds.DeathTimeoutKick,
                HandleDeathTimeoutKick);
        }
        subscribed = false;
    }

    private void ResolveReferences()
    {
        if (stateStore == null) stateStore = FindObjectOfType<CombatStateStore>();
        if (combatReceiver == null) combatReceiver = FindObjectOfType<CombatMessageReceiver>();
        if (gameSession == null) gameSession = FindObjectOfType<GameSession>();
        if (NetworkManager.Instance != null) networkManager = NetworkManager.Instance;
        if (NetMessageDispatcher.Instance != null) messageDispatcher = NetMessageDispatcher.Instance;
    }

    private void SetDeathDialogVisible(bool visible)
    {
        if (deathDialog != null && deathDialog.activeSelf != visible)
        {
            deathDialog.SetActive(visible);
        }
    }

    private void SetDeathMessage(string message)
    {
        if (deathMessageText != null) deathMessageText.text = message;
    }

    private void SetRespawnButton(bool interactable, string label)
    {
        if (respawnButton != null) respawnButton.interactable = interactable;
        if (respawnButtonText != null) respawnButtonText.text = label;
    }
}
