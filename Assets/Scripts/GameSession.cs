using System.Collections;
using System.Collections.Generic;
using Game.Alchemy.Runtime;
using Game.Forging.Protocol;
using Game.Forging.Runtime;
using Game.Chat.Transport;
using Game.CharacterPanel.Runtime;
using Game.CharacterPanel.Protocol;
using Game.Cultivation.Runtime;
using Game.Client.BattleSync;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Game.Client.SafeZone;
using Game.Client.Vfx;

public partial class GameSession : MonoBehaviour
{
    private const string SelectedCharacterIdKey = "selected_character_id";
    private const string SelectedCharacterDbIdKey = "selected_character_db_id";
    private const float AttackModeReplyTimeoutSeconds = 2f;

    [Header("Session")]
    [SerializeField] private float authSessionWaitTimeout = 10f;
    [SerializeField] private string sceneId = "world_001";
    [SerializeField] private bool startOnSceneLoad = true;

    [Header("Server Spawn")]
    [SerializeField] private float serverSpawnWaitTimeout = 3f;
    [SerializeField] private string exitSceneName = "SampleScene";

    [Header("Session Recovery")]
    [SerializeField, Min(0f)] private float worldRecoveryReconnectDelay = 0.25f;
    [SerializeField, Min(1f)] private float worldRecoveryTimeout = 30f;
    [SerializeField, Min(1f)] private float gateEnterWorldTimeout = 10f;
    [SerializeField, Min(1f)] private float mapTransferSnapshotTimeout = 20f;

    [Header("References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private NetMessageDispatcher messageDispatcher;
    [SerializeField] private BattleSyncRuntime battleSyncRuntime;

    [Header("Scene References")]
    [FormerlySerializedAs("localPlayerMoveReporterObject")]
    [SerializeField] private GameObject localPlayerRootObject;
    [SerializeField] private GameObject entityManagerObject;

    [Header("Local Player Root")]
    [SerializeField] private string localPlayerRootName = "LocalPlayerRoot";
    [SerializeField] private Vector3 localPlayerControllerCenter = new Vector3(0f, 1f, 0f);
    [SerializeField] private float localPlayerControllerHeight = 2f;
    [SerializeField] private float localPlayerControllerRadius = 0.5f;
    [SerializeField] private float localPlayerControllerSlopeLimit = 45f;
    [SerializeField] private float localPlayerControllerStepOffset = 0.3f;
    [SerializeField] private float localPlayerControllerSkinWidth = 0.08f;
    [SerializeField] private float localPlayerControllerMinMoveDistance = 0.001f;

    [Header("Player Presentation")]
    [SerializeField, InspectorName("玩家公共表现配置")] private PlayerPresentationSettings playerPresentationSettings;

    [Header("Local Character Visuals")]
    [SerializeField] private Transform localCharacterVisualRoot;
    [SerializeField] private GameObject defaultLocalCharacterVisual;
    [SerializeField] private PlayerGroundRingEffect localGroundRingPrefab;
    [SerializeField] private LocalCharacterVisualSlot[] localCharacterVisualSlots =
    {
        new LocalCharacterVisualSlot("character_001"),
        new LocalCharacterVisualSlot("character_002"),
        new LocalCharacterVisualSlot("character_003"),
        new LocalCharacterVisualSlot("character_004"),
        new LocalCharacterVisualSlot("character_005"),
        new LocalCharacterVisualSlot("character_006")
    };

    private bool sessionStarted;
    private bool enterWorldConfirmed;
    private Coroutine enterWorldCoroutine;
    private Coroutine missingServerSpawnCoroutine;
    private Coroutine worldRecoveryCoroutine;
    private GameObject spawnedLocalCharacterVisual;
    private Transform localSkillReleasePoint;
    private float lastHeartbeatReplyTime;
    private long lastHeartbeatServerTime;
    private LocalPlayerInputController localPlayerInputController;
    private PredictedCharacterMotor predictedCharacterMotor;
    private LocalPlayerReconciler localPlayerReconciler;
    private LocalPlayerVisualSmoother localPlayerVisualSmoother;
    private PlayerGroundRingEffect localGroundRing;
    private bool localPlayerDead;
    private long localCharacterDbId;
    private bool legacyWorldSnapshotRegistered;
    private bool battleSyncReadySubscribed;
    private bool battleSyncRepairRecoverySubscribed;
    private bool worldRecoveryPending;
    private bool worldHandoffInProgress;
    private bool mapTransferPaused;
    private bool inputWasEnabledForTransfer;
    private MapTransferCoordinator mapTransferCoordinator;
    private MapTransferResult pendingMapTransfer;
    private Coroutine mapTransferLoadCoroutine;
    private AsyncOperationHandle<SceneInstance> mapTransferSceneOperation;
    private Scene mapTransferSourceScene;
    private float mapTransferSnapshotDeadline;
    private float worldRecoveryDeadline;
    private GateEntryCoordinator recoveryGate;
    private long lastWorldEnteredEpoch;
    private bool gameSessionAccepted;
    private bool returningToLogin;
    private string activeMapId;
    private long nextAttributeSnapshotSequence;
    private long pendingAttributeSnapshotSequence;
    private float lastAttributeSnapshotRequestAt = float.NegativeInfinity;
    private bool attributeRefreshQueued;
    private long nextProgressionSnapshotSequence;
    private long pendingProgressionSnapshotSequence;
    private float lastProgressionSnapshotRequestAt = float.NegativeInfinity;
    private bool progressionRefreshQueued;
    private long nextAttackModeRequestSequence;
    private long defaultAttackModeConnectionEpoch;
    private string defaultAttackModeSceneSessionId;
    private AttackModeSetRequest pendingDefaultAttackMode;
    private float defaultAttackModeReplyDeadline;
    private bool defaultAttackModeRetried;

    public float LastHeartbeatReplyTime => lastHeartbeatReplyTime;
    public long LastHeartbeatServerTime => lastHeartbeatServerTime;
    public long LocalCharacterDbId => localCharacterDbId;
    public string LocalNickname { get; private set; } = string.Empty;
    public string EquipmentOperationSessionId { get; private set; } = string.Empty;
    public string WorldDropOperationSessionId { get; private set; } = string.Empty;
    public string ItemUseOperationSessionId { get; private set; } = string.Empty;
    public string QuickbarOperationSessionId { get; private set; } = string.Empty;
    public int QuickbarSlotCount { get; private set; }
    public int QuickbarProtocolVersion { get; private set; }
    public string PlayerTradeOperationSessionId { get; private set; } = string.Empty;
    public string JingjieOperationSessionId { get; private set; } = string.Empty;
    public string CultivationOperationSessionId { get; private set; } = string.Empty;
    public string AlchemyOperationSessionId { get; private set; } = string.Empty;
    public int ForgingProtocolVersion { get; private set; }
    public string ForgingOperationSessionId { get; private set; } = string.Empty;
    public bool IsWorldReady => sessionStarted && gameSessionAccepted
        && enterWorldConfirmed && !returningToLogin;
    internal bool IsReturningToLogin => returningToLogin;
    internal bool IsLocalPlayerDead => localPlayerDead;
    internal bool IsSettingDefaultAttackMode => pendingDefaultAttackMode != null && IsCurrentAttackModeSession();
    internal BattleSyncRuntime BattleSync => battleSyncRuntime;
    public string SceneSessionId => networkManager == null ? null
        : networkManager.SceneSessionId;
    public bool IsCurrentMap(string worldSceneId, string mapId) => IsWorldReady
        && string.Equals(sceneId, worldSceneId, System.StringComparison.Ordinal)
        && string.Equals(activeMapId, mapId, System.StringComparison.Ordinal);
    public bool PvpEnabled { get; private set; }
    public int LocalEnterWorldHp { get; private set; } = -1;
    public long LocalEnterWorldCombatRevision { get; private set; } = -1;
    public AttributeSnapshotResult LocalAttributeSnapshot { get; private set; }
    public ProgressionSnapshotResult LocalProgressionSnapshot { get; private set; }
    public long CurrentClientTick => localPlayerInputController == null
        ? 1L
        : localPlayerInputController.CurrentClientTick;

    public event System.Action LocalRespawnControlReceived;
    public event System.Action<long, long> LocalWorldEntered;
    public event System.Action<AttributeSnapshotResult> LocalAttributeSnapshotChanged;
    public event System.Action<ProgressionSnapshotResult> LocalProgressionSnapshotChanged;

    public void Logout()
    {
        if (returningToLogin || (AuthSession.Instance != null && AuthSession.Instance.IsLoggingOut)) return;
        StopWorldRecovery();
        StopEnterWorldWhenReady();
        StopLocalPlayerInput();
        AuthSession auth = AuthSession.Instance;
        if (auth == null) ReturnToLogin("logout");
        else auth.LogoutGameSession(() => ReturnToLogin("logout"));
    }

    private void HandleGameSessionEnded(string reason)
    {
        GateEntryHandoff.SetFailure(AuthSession.GameSessionEndedMessage(reason));
        ReturnToLogin(reason);
    }

    public void ReturnToLogin(string reason)
    {
        if (returningToLogin) return;
        returningToLogin = true;
        WorldEntryLoadingView entryView = WorldEntryLoadingView.ActiveWorldEntry;
        LocalNickname = string.Empty;
        EquipmentOperationSessionId = string.Empty;
        WorldDropOperationSessionId = string.Empty;
        ItemUseOperationSessionId = string.Empty;
        QuickbarOperationSessionId = string.Empty;
        QuickbarSlotCount = 0;
        QuickbarProtocolVersion = 0;
        PlayerTradeOperationSessionId = string.Empty;
        JingjieOperationSessionId = string.Empty;
        JingjieBreakthroughRuntime.Instance?.SuspendSession(this);
        RefiningSessionRuntime.Instance?.SuspendSession(this);
        CultivationOperationSessionId = string.Empty;
        CultivationSessionRuntime.Instance?.SuspendSession(this);
        AlchemyOperationSessionId = string.Empty;
        AlchemySessionRuntime.Instance?.SuspendSession(this);
        ForgingProtocolVersion = 0;
        ForgingOperationSessionId = string.Empty;
        锻造会话运行时.Instance?.SuspendSession(this);
        Game.PlayerTrade.Protocol.PlayerTradeRequestSequence.Suspend();
        LocalEnterWorldHp = -1;
        LocalEnterWorldCombatRevision = -1;
        ResetAttributeSnapshot();
        ResetProgressionSnapshot();
        ResetSkillSnapshot();
        GateEntryHandoff.Clear();
        NetMessageDispatcher activeDispatcher = NetMessageDispatcher.Instance != null
            ? NetMessageDispatcher.Instance : messageDispatcher;
        activeDispatcher?.ClearDeferredWorldMessages();
        AuthSession auth = AuthSession.Instance;
        bool sessionRejected = reason == "game_session_rejected"
            || reason == "game_session_missing_or_expired"
            || AuthSession.IsTerminalGameSessionReason(reason);
        if (entryView != null && !sessionRejected && auth != null && auth.HasValidGameSession
            && long.TryParse(PlayerPrefs.GetString(SelectedCharacterDbIdKey, ""), out long retryCharacterDbId)
            && retryCharacterDbId > 0
            && !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(SelectedCharacterIdKey, "")))
        {
            GateEntryHandoff.AllowCharacterRetry(retryCharacterDbId);
        }
        else
        {
            auth?.ClearSession();
            PlayerPrefs.DeleteKey(SelectedCharacterIdKey);
            PlayerPrefs.DeleteKey(SelectedCharacterDbIdKey);
            PlayerPrefs.Save();
            if (entryView != null && sessionRejected)
                GateEntryHandoff.SetFailure(AuthSession.GameSessionEndedMessage(reason));
        }
        Debug.Log("Returning to login scene. reason=" + (reason ?? string.Empty));
        StopWorldRecovery();
        pendingMapTransfer = null;
        CancelMapTransferLoad();
        StopLocalPlayerInput();
        mapTransferPaused = false;
        StopEnterWorldWhenReady();
        StopMissingServerSpawnTimeout();

        NetworkManager activeNetwork = NetworkManager.Instance != null
            ? NetworkManager.Instance : networkManager;
        activeNetwork?.Disconnect();

        string loginScene = string.IsNullOrWhiteSpace(exitSceneName)
            ? RemoteGameContent.LoginSceneName : exitSceneName;
        if (entryView != null)
            entryView.ReturnToLoginScene(loginScene);
        else
            RemoteGameContent.LoadSceneOrLog(loginScene);
    }

    public void RecoverWorldSession(string reason)
    {
        if (!sessionStarted || worldRecoveryPending || returningToLogin
            || (AuthSession.Instance != null && AuthSession.Instance.IsLoggingOut))
            return;
        if (AuthSession.Instance == null || !AuthSession.Instance.HasValidGameSession)
        {
            ReturnToLogin("game_session_missing_or_expired");
            return;
        }

        ResolveReferences();
        if (networkManager == null)
        {
            Debug.LogError("World session recovery failed: NetworkManager is missing.");
            ReturnToLogin(reason);
            return;
        }

        BeginWorldRecovery(reason);
        worldRecoveryCoroutine = StartCoroutine(RecoverWorldSessionCoroutine());
    }

    private void BeginWorldRecovery(string reason)
    {
        Debug.LogWarning("Recovering world session with a new connection. reason="
            + (reason ?? string.Empty));
        worldRecoveryPending = true;
        pendingMapTransfer = null;
        mapTransferSnapshotDeadline = 0f;
        CancelMapTransferLoad();
        messageDispatcher?.ClearDeferredWorldMessages();
        mapTransferPaused = false;
        inputWasEnabledForTransfer = false;
        // Allow the Gate heartbeat timeout before the advertised reconnect window starts.
        const float gateHeartbeatTimeout = 60f;
        int advertisedWindow = AuthSession.Instance == null ? 0 : AuthSession.Instance.ReconnectWindowSeconds;
        worldRecoveryDeadline = Time.realtimeSinceStartup + Mathf.Max(
            worldRecoveryTimeout, gateHeartbeatTimeout + (advertisedWindow > 0 ? advertisedWindow : 60));
        gameSessionAccepted = false;
        enterWorldConfirmed = false;
        localCharacterDbId = 0;
        LocalNickname = string.Empty;
        EquipmentOperationSessionId = string.Empty;
        WorldDropOperationSessionId = string.Empty;
        ItemUseOperationSessionId = string.Empty;
        QuickbarOperationSessionId = string.Empty;
        QuickbarSlotCount = 0;
        QuickbarProtocolVersion = 0;
        PlayerTradeOperationSessionId = string.Empty;
        JingjieOperationSessionId = string.Empty;
        JingjieBreakthroughRuntime.Instance?.SuspendSession(this);
        RefiningSessionRuntime.Instance?.SuspendSession(this);
        CultivationOperationSessionId = string.Empty;
        CultivationSessionRuntime.Instance?.SuspendSession(this);
        AlchemyOperationSessionId = string.Empty;
        AlchemySessionRuntime.Instance?.SuspendSession(this);
        ForgingProtocolVersion = 0;
        ForgingOperationSessionId = string.Empty;
        锻造会话运行时.Instance?.SuspendSession(this);
        Game.PlayerTrade.Protocol.PlayerTradeRequestSequence.Suspend();
        LocalEnterWorldHp = -1;
        LocalEnterWorldCombatRevision = -1;
        ResetAttributeSnapshot();
        ResetProgressionSnapshot();
        ResetSkillSnapshot();
        ClearLocalPlayerProvider();
        StopLocalPlayerInput();
        StopEnterWorldWhenReady();
        StopMissingServerSpawnTimeout();
        messageDispatcher?.ClearDeferredWorldMessages();
    }

    private IEnumerator RecoverWorldSessionCoroutine()
    {
        networkManager.Disconnect();
        yield return null;

        float delay = Mathf.Max(0f, worldRecoveryReconnectDelay);
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        worldRecoveryCoroutine = null;
        if (!sessionStarted || !worldRecoveryPending || returningToLogin
            || (AuthSession.Instance != null && AuthSession.Instance.IsLoggingOut))
        {
            yield break;
        }

        networkManager.Connect();
    }

    private void BeginRecoveryGate()
    {
        if (!worldRecoveryPending || recoveryGate != null || !sessionStarted)
            return;

        string characterId = PlayerPrefs.GetString(SelectedCharacterIdKey, "");
        string selectedDbId = PlayerPrefs.GetString(SelectedCharacterDbIdKey, "");
        if (messageDispatcher == null || !long.TryParse(selectedDbId, out long characterDbId)
            || characterDbId <= 0 || string.IsNullOrWhiteSpace(characterId))
        {
            HandleRecoveryFailed("Selected character is missing. Please log in again.");
            return;
        }

        recoveryGate = new GateEntryCoordinator(networkManager, messageDispatcher,
            characterDbId, characterId, worldRecoveryDeadline - Time.realtimeSinceStartup,
            HandleRecoveryAccepted, HandleRecoveryFailed, retryConnectionLoss: true,
            responseTimeout: gateEnterWorldTimeout);
        recoveryGate.Begin();
        if (worldRecoveryPending && networkManager.IsConnected)
            battleSyncRuntime?.NotifyEnterWorldSent(networkManager.ConnectionEpoch);
    }

    private void HandleRecoveryAccepted()
    {
        if (!worldRecoveryPending || returningToLogin) return;

        string selectedDbId = PlayerPrefs.GetString(SelectedCharacterDbIdKey, "");
        long epoch = networkManager.ConnectionEpoch;
        if (!long.TryParse(selectedDbId, out long characterDbId)
            || !networkManager.IsConnected || messageDispatcher == null
            || messageDispatcher.HasDeferredWorldOverflow
            || !GateEntryHandoff.TryGetDestination(epoch, characterDbId,
                out string serverSceneId, out string serverMapId)
            || !WorldMapSceneCatalog.TryGetAddress(serverSceneId, serverMapId, out _))
        {
            HandleRecoveryFailed("Gate world destination was lost. Please log in again.");
            return;
        }

        if (string.Equals(sceneId, serverSceneId, System.StringComparison.Ordinal)
            && string.Equals(activeMapId, serverMapId, System.StringComparison.Ordinal))
        {
            recoveryGate?.Dispose();
            recoveryGate = null;
            if (!GateEntryHandoff.TryTake(epoch, characterDbId, sceneId, activeMapId,
                    out string acceptedResult))
            {
                HandleRecoveryFailed("Gate session changed during recovery. Please log in again.");
                return;
            }

            worldRecoveryPending = false;
            HandleEnterWorldResult(acceptedResult);
            if (!gameSessionAccepted || localCharacterDbId != characterDbId
                || returningToLogin)
            {
                messageDispatcher.ClearDeferredWorldMessages();
                if (!returningToLogin)
                    HandleRecoveryFailed("Gate recovery was rejected. Please log in again.");
                return;
            }

            messageDispatcher.ReplayDeferredWorldMessages();
            return;
        }

        worldHandoffInProgress = true;
        worldRecoveryDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, worldRecoveryTimeout);
        StartCoroutine(LoadRecoveredWorldShell());
    }

    private IEnumerator LoadRecoveredWorldShell()
    {
        AsyncOperationHandle<SceneInstance> operation;
        try
        {
            operation = RemoteGameContent.LoadSceneAsync(RemoteGameContent.WorldSceneName);
        }
        catch (System.Exception error)
        {
            Debug.LogError("Recovered world shell load failed: " + error);
            HandleRecoveryFailed("World resources failed to load. Please update game resources.");
            yield break;
        }

        yield return operation;
        if (operation.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError("Recovered world shell load failed: " + operation.OperationException);
            if (operation.IsValid()) Addressables.Release(operation);
            HandleRecoveryFailed("World resources failed to load. Please update game resources.");
        }
    }

    private void HandleRecoveryFailed(string message)
    {
        if (!sessionStarted || returningToLogin) return;
        Debug.LogError("World session recovery failed: " + message);
        GateEntryHandoff.SetFailure(message);
        ReturnToLogin("world_recovery_failed");
    }

    private void Start()
    {
        if (startOnSceneLoad)
            StartSession();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GrassRenderDiagnostics.LogWorld();
#endif
    }

    private void Update()
    {
        if (AuthSession.Instance != null && AuthSession.Instance.IsLoggingOut) return;
        if (worldRecoveryPending && !returningToLogin)
        {
            if (Time.realtimeSinceStartup >= worldRecoveryDeadline)
            {
                HandleRecoveryFailed("Gate reconnection timed out. Please log in again.");
                return;
            }
            recoveryGate?.CheckTimeout();
        }
        if (pendingMapTransfer != null && mapTransferSnapshotDeadline > 0f
            && Time.realtimeSinceStartup >= mapTransferSnapshotDeadline)
        {
            RecoverWorldSession("map_transfer_snapshots_incomplete");
            return;
        }
        if (attributeRefreshQueued) RequestAttributeSnapshot();
        if (progressionRefreshQueued) RequestProgressionSnapshot();
        RetryTimedOutSkillLearn();
        UpdateSkillBarSave();
        UpdateDefaultAttackMode();
    }

    private void LateUpdate()
    {
        if (localGroundRing != null)
            localGroundRing.enabled = IsWorldReady && !mapTransferPaused && !localPlayerDead;
    }

    private void OnDisable()
    {
        if (localGroundRing != null)
            localGroundRing.enabled = false;
    }

    private void OnDestroy()
    {
        if (localGroundRing != null)
            Destroy(localGroundRing.gameObject);
        StopSession(false);
    }

    public void StartSession()
    {
        if (sessionStarted)
        {
            return;
        }

        if (GateEntryHandoff.RequiresHandoff)
        {
            NetworkManager gateNetwork = NetworkManager.Instance;
            string savedCharacterDbId = PlayerPrefs.GetString(SelectedCharacterDbIdKey, "");
            if (gateNetwork == null || !gateNetwork.IsConnected
                || !long.TryParse(savedCharacterDbId, out long selectedCharacterDbId)
                || !GateEntryHandoff.TryGetDestination(gateNetwork.ConnectionEpoch,
                    selectedCharacterDbId, out string serverSceneId, out string serverMapId)
                || !WorldMapSceneCatalog.TryGetAddress(serverSceneId, serverMapId, out _))
            {
                GateEntryHandoff.SetFailure("Gate world destination was lost. Please log in again.");
                ReturnToLogin("map_handoff_invalid");
                return;
            }

            sceneId = serverSceneId;
            activeMapId = serverMapId;
        }

        sessionStarted = true;
        enterWorldConfirmed = false;
        LocalNickname = string.Empty;
        EquipmentOperationSessionId = string.Empty;
        WorldDropOperationSessionId = string.Empty;
        ItemUseOperationSessionId = string.Empty;
        QuickbarOperationSessionId = string.Empty;
        QuickbarSlotCount = 0;
        QuickbarProtocolVersion = 0;
        PlayerTradeOperationSessionId = string.Empty;
        JingjieOperationSessionId = string.Empty;
        JingjieBreakthroughRuntime.Instance?.SuspendSession(this);
        RefiningSessionRuntime.Instance?.SuspendSession(this);
        CultivationOperationSessionId = string.Empty;
        CultivationSessionRuntime.Instance?.SuspendSession(this);
        AlchemyOperationSessionId = string.Empty;
        AlchemySessionRuntime.Instance?.SuspendSession(this);
        ForgingProtocolVersion = 0;
        ForgingOperationSessionId = string.Empty;
        锻造会话运行时.Instance?.SuspendSession(this);
        Game.PlayerTrade.Protocol.PlayerTradeRequestSequence.Suspend();
        LocalEnterWorldHp = -1;
        LocalEnterWorldCombatRevision = -1;
        ResetAttributeSnapshot();
        ResetProgressionSnapshot();

        ResolveReferences();

        if (networkManager != null && messageDispatcher != null)
        {
            JingjieBreakthroughRuntime.Ensure(networkManager, this);
            RefiningSessionRuntime.Ensure(networkManager, this);
            CultivationSessionRuntime.Ensure(networkManager, this);
            AlchemySessionRuntime.Ensure(networkManager, this);
            锻造会话运行时.Ensure(networkManager, this);
            Game.Friend.Runtime.FriendSessionRuntime
                .Ensure(networkManager, messageDispatcher, this);
            WorldChatTransportInbox inbox =
                WorldChatTransportInbox.Ensure(networkManager, messageDispatcher);
            if (!GateEntryHandoff.RequiresHandoff)
            {
                inbox.PrepareEnterWorld();
                Game.Chat.Runtime.WorldChatSessionRuntime
                    .Ensure(networkManager, inbox).BeginSession();
            }
            else
            {
                Game.Chat.Runtime.WorldChatSessionRuntime.Ensure(networkManager, inbox);
            }
        }

        if (battleSyncRuntime != null)
        {
            battleSyncRuntime.Initialize();
            if (battleSyncRuntime.OwnsSnapshotRouting && !battleSyncReadySubscribed)
            {
                battleSyncRuntime.Ready += HandleBattleSyncReady;
                battleSyncReadySubscribed = true;
            }
            if (battleSyncRuntime.OwnsSnapshotRouting && !battleSyncRepairRecoverySubscribed)
            {
                battleSyncRuntime.RepairRecoveryRequired += HandleBattleSyncRepairRecoveryRequired;
                battleSyncRepairRecoverySubscribed = true;
            }
        }

        RegisterMessages();
        mapTransferCoordinator = GetComponent<MapTransferCoordinator>();
        if (mapTransferCoordinator == null)
            mapTransferCoordinator = gameObject.AddComponent<MapTransferCoordinator>();
        mapTransferCoordinator.Initialize(this);
        if (!TryGetComponent(out MapTeleporterInteraction teleporterInteraction))
            teleporterInteraction = gameObject.AddComponent<MapTeleporterInteraction>();
        teleporterInteraction.Initialize(this);
        if (battleSyncRuntime != null && battleSyncRuntime.OwnsSnapshotRouting
            && messageDispatcher != null)
            mapTransferCoordinator.SetCommitHandler(HandleMapTransferCommitted);

        if (networkManager == null)
        {
            Debug.LogError("GameSession start failed: NetworkManager is missing.");
            return;
        }

        networkManager.OnConnected += HandleNetworkConnected;
        networkManager.OnDisconnected += HandleNetworkDisconnected;
        if (AuthSession.Instance != null) AuthSession.Instance.GameSessionEnded += HandleGameSessionEnded;

        if (GateEntryHandoff.RequiresHandoff)
        {
            string savedCharacterDbId = PlayerPrefs.GetString(SelectedCharacterDbIdKey, "");
            if (!long.TryParse(savedCharacterDbId, out long selectedCharacterDbId)
                || !networkManager.IsConnected
                || messageDispatcher == null || messageDispatcher.HasDeferredWorldOverflow
                || !GateEntryHandoff.TryTake(networkManager.ConnectionEpoch,
                    selectedCharacterDbId, sceneId, activeMapId, out string acceptedResult))
            {
                GateEntryHandoff.SetFailure(
                    "Gate session was lost while loading. Please log in again.");
                GateEntryHandoff.Clear();
                messageDispatcher?.ClearDeferredWorldMessages();
                ReturnToLogin("gate_entry_handoff_invalid");
                return;
            }

            battleSyncRuntime?.NotifyEnterWorldSent(networkManager.ConnectionEpoch);
            HandleEnterWorldResult(acceptedResult);
            if (!gameSessionAccepted || returningToLogin)
            {
                messageDispatcher?.ClearDeferredWorldMessages();
                ReturnToLogin("gate_entry_handoff_rejected");
                return;
            }

            messageDispatcher?.ReplayDeferredWorldMessages();
            return;
        }

        if (networkManager.IsConnected)
        {
            StartEnterWorldWhenReady();
        }
        else
        {
            networkManager.Connect();
        }
    }

    public void StopSession()
    {
        StopSession(true);
    }

    private void StopSession(bool restoreLegacyRouting)
    {
        if (!sessionStarted)
        {
            return;
        }

        sessionStarted = false;
        pendingDefaultAttackMode = null;
        defaultAttackModeSceneSessionId = null;
        mapTransferPaused = false;
        pendingMapTransfer = null;
        mapTransferSnapshotDeadline = 0f;
        CancelMapTransferLoad();
        if (mapTransferCoordinator != null)
        {
            mapTransferCoordinator.Shutdown();
            Destroy(mapTransferCoordinator);
            mapTransferCoordinator = null;
        }
        activeMapId = null;
        Game.Friend.Runtime.FriendSessionRuntime.Instance?.DetachSession(this);
        enterWorldConfirmed = false;
        LocalNickname = string.Empty;
        EquipmentOperationSessionId = string.Empty;
        WorldDropOperationSessionId = string.Empty;
        ItemUseOperationSessionId = string.Empty;
        QuickbarOperationSessionId = string.Empty;
        QuickbarSlotCount = 0;
        QuickbarProtocolVersion = 0;
        PlayerTradeOperationSessionId = string.Empty;
        JingjieOperationSessionId = string.Empty;
        JingjieBreakthroughRuntime.Instance?.SuspendSession(this);
        RefiningSessionRuntime.Instance?.SuspendSession(this);
        CultivationOperationSessionId = string.Empty;
        CultivationSessionRuntime.Instance?.SuspendSession(this);
        AlchemyOperationSessionId = string.Empty;
        AlchemySessionRuntime.Instance?.SuspendSession(this);
        ForgingProtocolVersion = 0;
        ForgingOperationSessionId = string.Empty;
        锻造会话运行时.Instance?.SuspendSession(this);
        Game.PlayerTrade.Protocol.PlayerTradeRequestSequence.Suspend();
        LocalEnterWorldHp = -1;
        LocalEnterWorldCombatRevision = -1;
        ResetAttributeSnapshot();
        ResetProgressionSnapshot();
        ResetSkillSnapshot();

        ClearLocalPlayerProvider();
        if (localPlayerRootObject != null && localPlayerRootObject.TryGetComponent(out PlayerEffectPresenter effects))
            effects.ClearBinding();
        StopLocalPlayerInput();
        StopEnterWorldWhenReady();
        StopMissingServerSpawnTimeout();
        StopWorldRecovery();
        if (!worldHandoffInProgress) SendLeaveWorld();
        UnregisterMessages();

        if (networkManager != null)
        {
            networkManager.OnConnected -= HandleNetworkConnected;
            networkManager.OnDisconnected -= HandleNetworkDisconnected;
        }
        if (AuthSession.Instance != null) AuthSession.Instance.GameSessionEnded -= HandleGameSessionEnded;

        if (battleSyncRuntime != null)
        {
            if (battleSyncReadySubscribed)
            {
                battleSyncRuntime.Ready -= HandleBattleSyncReady;
                battleSyncReadySubscribed = false;
            }
            if (battleSyncRepairRecoverySubscribed)
            {
                battleSyncRuntime.RepairRecoveryRequired -= HandleBattleSyncRepairRecoveryRequired;
                battleSyncRepairRecoverySubscribed = false;
            }
            battleSyncRuntime.Shutdown(restoreLegacyRouting);
        }
    }

    private void ResolveReferences()
    {
        if (battleSyncRuntime == null)
        {
            battleSyncRuntime = FindObjectOfType<BattleSyncRuntime>(true);
            if (battleSyncRuntime == null)
            {
                battleSyncRuntime = gameObject.AddComponent<BattleSyncRuntime>();
            }
        }

        if (NetworkManager.Instance != null)
        {
            networkManager = NetworkManager.Instance;
        }

        if (NetMessageDispatcher.Instance != null)
        {
            messageDispatcher = NetMessageDispatcher.Instance;
        }

        if (entityManagerObject == null)
        {
            EntityManager entityManager = FindObjectOfType<EntityManager>();
            if (entityManager != null)
            {
                entityManagerObject = entityManager.gameObject;
            }
        }
    }

    private void RegisterMessages()
    {
        ResolveReferences();

        if (messageDispatcher == null)
        {
            Debug.LogError("GameSession register failed: NetMessageDispatcher is missing.");
            return;
        }

        messageDispatcher.Register("enter_world_result", HandleEnterWorldResult);
        messageDispatcher.Register("attribute_snapshot_result", HandleAttributeSnapshotResult);
        messageDispatcher.Register("progression_snapshot_result", HandleProgressionSnapshotResult);
        messageDispatcher.Register("skill_snapshot_result", HandleSkillSnapshotResult);
        messageDispatcher.Register("skill_learn_result", HandleSkillLearnResult);
        messageDispatcher.Register("skill_bar_save_result", HandleSkillBarSaveResult);
        messageDispatcher.Register("skill_progress_result", HandleSkillProgressResult);
        messageDispatcher.Register("attack_mode_result", HandleAttackModeResult);
        legacyWorldSnapshotRegistered = battleSyncRuntime == null || !battleSyncRuntime.OwnsSnapshotRouting;
        if (legacyWorldSnapshotRegistered) messageDispatcher.Register("world_snapshot", HandleWorldSnapshot);
        messageDispatcher.Register("player_authoritative_state", HandlePlayerAuthoritativeState);
        messageDispatcher.Register("movement_correction", HandleMovementCorrection);
        messageDispatcher.Register("movement_control_result", HandleMovementControlResult);
        messageDispatcher.Register("movement_control", HandleMovementControl);
        messageDispatcher.Register("heartbeat_result", HandleHeartbeatReply);
    }

    private void UnregisterMessages()
    {
        if (messageDispatcher == null)
        {
            return;
        }

        messageDispatcher.Unregister("enter_world_result", HandleEnterWorldResult);
        messageDispatcher.Unregister("attribute_snapshot_result", HandleAttributeSnapshotResult);
        messageDispatcher.Unregister("progression_snapshot_result", HandleProgressionSnapshotResult);
        messageDispatcher.Unregister("skill_snapshot_result", HandleSkillSnapshotResult);
        messageDispatcher.Unregister("skill_learn_result", HandleSkillLearnResult);
        messageDispatcher.Unregister("skill_bar_save_result", HandleSkillBarSaveResult);
        messageDispatcher.Unregister("skill_progress_result", HandleSkillProgressResult);
        messageDispatcher.Unregister("attack_mode_result", HandleAttackModeResult);
        if (legacyWorldSnapshotRegistered)
        {
            messageDispatcher.Unregister("world_snapshot", HandleWorldSnapshot);
            legacyWorldSnapshotRegistered = false;
        }
        messageDispatcher.Unregister("player_authoritative_state", HandlePlayerAuthoritativeState);
        messageDispatcher.Unregister("movement_correction", HandleMovementCorrection);
        messageDispatcher.Unregister("movement_control_result", HandleMovementControlResult);
        messageDispatcher.Unregister("movement_control", HandleMovementControl);
        messageDispatcher.Unregister("heartbeat_result", HandleHeartbeatReply);
    }

    private void HandleNetworkConnected()
    {
        if (returningToLogin || (AuthSession.Instance != null && AuthSession.Instance.IsLoggingOut)) return;
        Debug.Log("GameSession network connected.");
        if (worldHandoffInProgress) return;
        if (worldRecoveryPending)
        {
            BeginRecoveryGate();
            return;
        }
        if (messageDispatcher != null)
        {
            WorldChatTransportInbox inbox =
                WorldChatTransportInbox.Ensure(networkManager, messageDispatcher);
            inbox.PrepareEnterWorld();
            Game.Chat.Runtime.WorldChatSessionRuntime
                .Ensure(networkManager, inbox).BeginSession();
        }
        StartEnterWorldWhenReady();
    }

    private void StartEnterWorldWhenReady()
    {
        StopEnterWorldWhenReady();
        enterWorldCoroutine = StartCoroutine(SendEnterWorldWhenReady());
    }

    private void StopEnterWorldWhenReady()
    {
        if (enterWorldCoroutine != null)
        {
            StopCoroutine(enterWorldCoroutine);
            enterWorldCoroutine = null;
        }
    }

    private IEnumerator SendEnterWorldWhenReady()
    {
        float startTime = Time.realtimeSinceStartup;

        while (sessionStarted && (AuthSession.Instance == null || string.IsNullOrEmpty(AuthSession.Instance.UserId)))
        {
            if (authSessionWaitTimeout > 0f && Time.realtimeSinceStartup - startTime >= authSessionWaitTimeout)
            {
                Debug.LogWarning("Send enter_world waiting stopped: AuthSession.UserId is empty. Start from login scene and make sure login success saves userId.");
                enterWorldCoroutine = null;
                yield break;
            }

            yield return null;
        }

        enterWorldCoroutine = null;
        SendEnterWorld();
    }

    private void HandleNetworkDisconnected(string reason)
    {
        if (returningToLogin || (AuthSession.Instance != null && AuthSession.Instance.IsLoggingOut)) return;
        if (worldRecoveryPending && !worldHandoffInProgress) return;
        if (gameSessionAccepted && !worldHandoffInProgress)
        {
            if (AuthSession.Instance != null && AuthSession.Instance.HasValidGameSession)
                BeginWorldRecovery("game_session_closed: " + reason);
            else
                ReturnToLogin("game_session_missing_or_expired");
            return;
        }
        if (WorldEntryLoadingView.ActiveWorldEntry != null)
        {
            GateEntryHandoff.SetFailure("准备角色时与游戏服务器的连接已中断。");
            ReturnToLogin("initial_player_disconnected");
            return;
        }
        if (worldHandoffInProgress)
        {
            HandleRecoveryFailed("Gate disconnected while loading the world. Please log in again.");
            return;
        }
        Debug.LogWarning("GameSession network disconnected: " + reason);
        enterWorldConfirmed = false;
        LocalNickname = string.Empty;
        EquipmentOperationSessionId = string.Empty;
        WorldDropOperationSessionId = string.Empty;
        QuickbarOperationSessionId = string.Empty;
        QuickbarSlotCount = 0;
        QuickbarProtocolVersion = 0;
        PlayerTradeOperationSessionId = string.Empty;
        JingjieOperationSessionId = string.Empty;
        JingjieBreakthroughRuntime.Instance?.SuspendSession(this);
        RefiningSessionRuntime.Instance?.SuspendSession(this);
        CultivationOperationSessionId = string.Empty;
        CultivationSessionRuntime.Instance?.SuspendSession(this);
        AlchemyOperationSessionId = string.Empty;
        AlchemySessionRuntime.Instance?.SuspendSession(this);
        ForgingProtocolVersion = 0;
        ForgingOperationSessionId = string.Empty;
        锻造会话运行时.Instance?.SuspendSession(this);
        Game.PlayerTrade.Protocol.PlayerTradeRequestSequence.Suspend();
        LocalEnterWorldHp = -1;
        LocalEnterWorldCombatRevision = -1;
        ResetAttributeSnapshot();
        ResetProgressionSnapshot();
        ClearLocalPlayerProvider();
        StopLocalPlayerInput();
    }

    private void HandleBattleSyncRepairRecoveryRequired(InitialSnapshotKind missing)
    {
        if (!sessionStarted)
        {
            return;
        }

        if (WorldEntryLoadingView.ActiveWorldEntry != null)
        {
            var missingNames = new List<string>();
            if ((missing & InitialSnapshotKind.World) != 0) missingNames.Add("附近玩家");
            if ((missing & InitialSnapshotKind.Monster) != 0) missingNames.Add("附近怪物");
            if ((missing & InitialSnapshotKind.Loot) != 0) missingNames.Add("地面掉落");
            if ((missing & InitialSnapshotKind.Combat) != 0) missingNames.Add("战斗状态");
            string details = missingNames.Count == 0 ? "首批世界快照" : string.Join("、", missingNames);
            GateEntryHandoff.SetFailure("首批世界数据补发超时，未同步「" + details + "」。");
            ReturnToLogin("initial_world_snapshot_repair_exhausted");
            return;
        }

        RecoverWorldSession("battle_sync_repair_exhausted");
    }

    public void SendEnterWorld()
    {
        if (returningToLogin) return;
        ResolveReferences();

        if (networkManager == null || !networkManager.IsConnected)
        {
            Debug.LogWarning("Send enter_world failed: network is not connected.");
            return;
        }

        if (AuthSession.Instance == null || string.IsNullOrEmpty(AuthSession.Instance.UserId))
        {
            Debug.LogError("Send enter_world failed: userId is empty.");
            return;
        }

        if (!AuthSession.Instance.HasValidGameSession)
        {
            ReturnToLogin("game_session_missing_or_expired");
            return;
        }

        string selectedCharacterId = PlayerPrefs.GetString(SelectedCharacterIdKey, "");
        string selectedCharacterDbIdText = PlayerPrefs.GetString(SelectedCharacterDbIdKey, "");
        if (!long.TryParse(selectedCharacterDbIdText, out long selectedCharacterDbId) || selectedCharacterDbId <= 0)
        {
            Debug.LogError("Send enter_world failed: selected characterDbId is missing or invalid.");
            return;
        }

        bool sent = AuthSession.Instance.TrySendEnterWorld(networkManager, messageDispatcher,
            selectedCharacterDbId, selectedCharacterId);

        if (sent && battleSyncRuntime != null)
        {
            battleSyncRuntime.NotifyEnterWorldSent(
                networkManager.ConnectionEpoch);
        }

        if (sent)
        {
            Debug.Log("Send enter_world: characterDbId=" + selectedCharacterDbId + ", characterId=" + selectedCharacterId);
        }
        else
        {
            Debug.LogWarning("Send enter_world failed: network send returned false.");
        }
    }

    public void HandleEnterWorldResult(string json)
    {
        if (returningToLogin || worldRecoveryPending) return;
        AuthSession auth = AuthSession.Instance;
        long epoch = networkManager == null ? 0 : networkManager.ConnectionEpoch;
        if (auth == null || !auth.IsCurrentGameEntry(networkManager, epoch)) return;
        JingjieBreakthroughRuntime.Instance?.SuspendSession(this);
        RefiningSessionRuntime.Instance?.SuspendSession(this);
        CultivationSessionRuntime.Instance?.SuspendSession(this);
        AlchemySessionRuntime.Instance?.SuspendSession(this);
        锻造会话运行时.Instance?.SuspendSession(this);
        JingjieOperationSessionId = string.Empty;
        CultivationOperationSessionId = string.Empty;
        AlchemyOperationSessionId = string.Empty;
        ForgingProtocolVersion = 0;
        ForgingOperationSessionId = string.Empty;
        EnterWorldResult result = JsonUtility.FromJson<EnterWorldResult>(json);

        if (result == null || !result.success)
        {
            ResetSkillSnapshot();
            ItemUseOperationSessionId = string.Empty;
            QuickbarOperationSessionId = string.Empty;
            QuickbarSlotCount = 0;
            QuickbarProtocolVersion = 0;
            PlayerTradeOperationSessionId = string.Empty;
            Game.PlayerTrade.Protocol.PlayerTradeRequestSequence.Suspend();
            string message = result == null ? "Bad enter_world_result" : result.message;
            Debug.LogError("Enter world failed: " + message);
            enterWorldConfirmed = false;
            StopLocalPlayerInput();
            if (AuthSession.IsTerminalGameSessionReason(result?.reason))
                HandleGameSessionEnded(result.reason);
            else if (AuthSession.IsRetryableGameSessionReason(result?.reason))
                RecoverWorldSession(result.reason);
            else if (!string.IsNullOrEmpty(message)
                && (message.IndexOf("token format is invalid", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("token is invalid or expired", System.StringComparison.OrdinalIgnoreCase) >= 0))
                ReturnToLogin("game_session_rejected");
            return;
        }

        if (!WorldMapSceneCatalog.TryGetAddress(result.sceneId, result.mapId, out _)
            || !string.Equals(sceneId, result.sceneId, System.StringComparison.Ordinal)
            || !string.Equals(activeMapId, result.mapId, System.StringComparison.Ordinal))
        {
            GateEntryHandoff.SetFailure(
                "Server world map does not match the loaded map. Please update game resources.");
            ReturnToLogin("world_map_mismatch");
            return;
        }

        if (!MovementEventPolicy.Accepts(result))
        {
            GateEntryHandoff.SetFailure("游戏服务器未提供移动协议 V2，请更新服务端。");
            ReturnToLogin("movement_protocol_version_invalid");
            return;
        }

        if (!auth.AcceptGameEntry(networkManager, epoch, result)
            || !networkManager.SetSceneSessionId(epoch, result.sceneSessionId))
        {
            GateEntryHandoff.SetFailure("Gate did not provide a valid scene session.");
            ReturnToLogin("scene_session_missing");
            return;
        }

        gameSessionAccepted = true;
        PlayerNetworkState selfState = result.self;
        if (selfState == null && !HasServerSpawnTransform(json))
        {
            ResetSkillSnapshot();
            Debug.LogError("Enter world success but server spawn transform is missing. Waiting " + serverSpawnWaitTimeout + " seconds before leaving game scene.");
            enterWorldConfirmed = false;
            StopLocalPlayerInput();
            StartMissingServerSpawnTimeout();
            return;
        }

        StopMissingServerSpawnTimeout();

        string characterId = selfState != null && !string.IsNullOrWhiteSpace(selfState.characterId)
            ? selfState.characterId
            : GetEnterWorldCharacterId(result);
        Vector3 spawnPosition = result.movementControl.Position;
        Quaternion spawnRotation = result.movementControl.Rotation;
        long resultCharacterDbId = result.characterDbId > 0
            ? result.characterDbId
            : (selfState == null ? 0 : selfState.characterDbId);

        if (resultCharacterDbId <= 0)
        {
            ResetSkillSnapshot();
            Debug.LogError("Enter world success but characterDbId is missing or invalid.");
            enterWorldConfirmed = false;
            StopLocalPlayerInput();
            return;
        }

        localCharacterDbId = resultCharacterDbId;
        LocalNickname = ResolveLocalNickname(result, resultCharacterDbId);
        EquipmentOperationSessionId = result.equipmentOperationSessionId ?? string.Empty;
        WorldDropOperationSessionId = result.worldDropOperationSessionId ?? string.Empty;
        ItemUseOperationSessionId = result.itemUseOperationSessionId ?? string.Empty;
        QuickbarOperationSessionId = result.quickbarOperationSessionId ?? string.Empty;
        QuickbarSlotCount = result.quickbarSlotCount;
        QuickbarProtocolVersion = result.quickbarProtocolVersion;
        PlayerTradeOperationSessionId = result.playerTradeOperationSessionId ?? string.Empty;
        JingjieOperationSessionId = result.jingjieOperationSessionId ?? string.Empty;
        CultivationOperationSessionId = result.cultivationProtocolVersion == 1
            ? result.cultivationOperationSessionId ?? string.Empty
            : string.Empty;
        AlchemyOperationSessionId = result.alchemyOperationSessionId ?? string.Empty;
        ForgingProtocolVersion = result.forgingProtocolVersion;
        ForgingOperationSessionId = 锻造消息.IsSupportedVersion(result.forgingProtocolVersion)
            && System.Guid.TryParseExact(result.forgingOperationSessionId, "D",
                out _)
            ? result.forgingOperationSessionId : string.Empty;
        Debug.Log("[Forging][EnterWorld] protocolVersion="
            + ForgingProtocolVersion + " supported="
            + 锻造消息.IsSupportedVersion(ForgingProtocolVersion)
            + " sessionIdValid=" + System.Guid.TryParseExact(
                result.forgingOperationSessionId, "D", out _)
            + " runtimePresent=" + (锻造会话运行时.Instance != null));
        Debug.Log("[Cultivation][EnterWorld] protocolVersion="
            + result.cultivationProtocolVersion
            + " sessionIdValid="
            + System.Guid.TryParseExact(CultivationOperationSessionId, "D", out _)
            + " runtimePresent=" + (CultivationSessionRuntime.Instance != null));
        JingjieBreakthroughRuntime.Instance?.BeginSession(this,
            resultCharacterDbId, JingjieOperationSessionId);
        RefiningSessionRuntime.Instance?.BeginSession(this,
            resultCharacterDbId, JingjieOperationSessionId);
        CultivationSessionRuntime.Instance?.BeginSession(this,
            resultCharacterDbId, CultivationOperationSessionId);
        AlchemySessionRuntime.Instance?.BeginSession(this,
            resultCharacterDbId, AlchemyOperationSessionId);
        锻造会话运行时.Instance?.BeginSession(this,
            resultCharacterDbId, ForgingProtocolVersion, ForgingOperationSessionId);
        Game.PlayerTrade.Protocol.PlayerTradeRequestSequence.EnterWorld(
            PlayerTradeOperationSessionId, resultCharacterDbId);
        LocalEnterWorldHp = selfState != null && selfState.maxHp > 0
            ? Mathf.Clamp(selfState.hp, 0, selfState.maxHp) : -1;
        SetLocalPlayerDead(IsLocalPlayerDeadAtSpawn(selfState));
        LocalEnterWorldCombatRevision = selfState == null
            ? -1 : selfState.combatRevision;
        ResetAttributeSnapshot();
        ResetProgressionSnapshot();
        BeginSkillSession(resultCharacterDbId, result.learnSessionId);

        if (battleSyncRuntime != null)
        {
            if (string.IsNullOrWhiteSpace(result.sceneId))
            {
                Debug.LogWarning("Battle sync lifecycle skipped: enter_world_result sceneId is missing.");
            }
            else if (result.snapshotRevision <= 0)
            {
                Debug.LogWarning("Battle sync lifecycle skipped: enter_world_result snapshotRevision is missing or invalid.");
            }
            else if (result.serverTime <= 0)
            {
                Debug.LogWarning("Battle sync lifecycle skipped: enter_world_result serverTime is missing or invalid.");
            }
            else
            {
                BattleSyncEnterWorldContext context =
                    new BattleSyncEnterWorldContext(
                        networkManager.ConnectionEpoch,
                        result.sceneId,
                        resultCharacterDbId,
                        result.snapshotRevision,
                        result.serverTime,
                        result.reason);

                battleSyncRuntime.NotifyEnterWorldAccepted(
                    context, WorldEntryLoadingView.ActiveWorldEntry != null);
            }
        }

        ApplyLocalCharacterVisual(characterId, spawnPosition, spawnRotation);
        localPlayerInputController?.BeginMovement(result.movementControl, result.movementPolicy);
        ResolveReferences();

        if (entityManagerObject != null)
        {
            entityManagerObject.SendMessage("ApplyEnterWorldResultJson", json, SendMessageOptions.DontRequireReceiver);
            if (battleSyncRuntime != null && battleSyncRuntime.OwnsSnapshotRouting)
            {
                bool registrySeeded = battleSyncRuntime.SeedInitialVisiblePlayers(
                    networkManager.ConnectionEpoch, result.sceneId,
                    result.snapshotRevision, result.visiblePlayers);
                if (registrySeeded)
                {
                    battleSyncRuntime.NotifyInitialWorldSeedApplied(networkManager.ConnectionEpoch,
                        result.sceneId, result.snapshotRevision, result.serverTime,
                        result.reason, result.visiblePlayers);
                }
                else
                {
                    Debug.LogError("Enter world visible player registry seed failed; local input remains disabled.");
                }
            }
        }
        else
        {
            Debug.LogError("Enter world visible players were not applied because EntityManager is missing.");
        }

        if (WorldEntryLoadingView.ActiveWorldEntry != null && LocalEnterWorldHp >= 0)
        {
            CombatStateStore store = FindObjectOfType<CombatStateStore>();
            if (store != null)
            {
                store.BeginScene(sceneId);
                store.MergeState(sceneId, new CombatStateMessage
                {
                    entity = new CombatEntityMessage
                    {
                        entityType = "player",
                        entityId = localCharacterDbId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    },
                    hp = LocalEnterWorldHp,
                    maxHp = selfState.maxHp,
                    lifeState = selfState.lifeState,
                    combatRevision = selfState.combatRevision
                });
            }
        }

        bool battleSyncOwnsSnapshotRouting =
            battleSyncRuntime != null && battleSyncRuntime.OwnsSnapshotRouting;
        if (!battleSyncOwnsSnapshotRouting) enterWorldConfirmed = true;
        Debug.Log("Enter world success: characterDbId=" + localCharacterDbId + ", characterId=" + characterId + ", spawn=" + spawnPosition);

        BeginDefaultAttackMode();
        long connectionEpoch = networkManager == null ? 0 : networkManager.ConnectionEpoch;
        if (connectionEpoch > 0 && connectionEpoch != lastWorldEnteredEpoch
            && localPlayerRootObject != null)
        {
            lastWorldEnteredEpoch = connectionEpoch;
            LocalWorldEntered?.Invoke(connectionEpoch, localCharacterDbId);
        }

        RequestAttributeSnapshot();
        RequestProgressionSnapshot();
        if (enterWorldConfirmed) StartLocalPlayerInput();
    }

    private void BeginDefaultAttackMode()
    {
        if (localPlayerDead) return;

        long epoch = networkManager.ConnectionEpoch;
        string sceneSessionId = networkManager.SceneSessionId;
        if (epoch == defaultAttackModeConnectionEpoch && sceneSessionId == defaultAttackModeSceneSessionId)
            return;

        defaultAttackModeConnectionEpoch = epoch;
        defaultAttackModeSceneSessionId = sceneSessionId;
        pendingDefaultAttackMode = new AttackModeSetRequest(++nextAttackModeRequestSequence,
            AttackModeSetRequest.FreeMode);
        defaultAttackModeReplyDeadline = Time.realtimeSinceStartup + AttackModeReplyTimeoutSeconds;
        defaultAttackModeRetried = false;
        networkManager.Send(pendingDefaultAttackMode);
    }

    private bool IsCurrentAttackModeSession() => sessionStarted && gameSessionAccepted
        && !returningToLogin && !worldRecoveryPending && !mapTransferPaused
        && networkManager != null && networkManager.IsConnected
        && networkManager.ConnectionEpoch == defaultAttackModeConnectionEpoch
        && networkManager.SceneSessionId == defaultAttackModeSceneSessionId;

    private void UpdateDefaultAttackMode()
    {
        if (pendingDefaultAttackMode == null)
            return;
        if (!IsCurrentAttackModeSession())
        {
            pendingDefaultAttackMode = null;
            return;
        }
        if (Time.realtimeSinceStartup < defaultAttackModeReplyDeadline)
            return;

        if (!defaultAttackModeRetried)
        {
            defaultAttackModeRetried = true;
            defaultAttackModeReplyDeadline = Time.realtimeSinceStartup + AttackModeReplyTimeoutSeconds;
            networkManager.Send(pendingDefaultAttackMode);
            return;
        }

        pendingDefaultAttackMode = null;
        Debug.LogWarning("[AttackMode] 未收到全体模式切换回执。");
    }

    private void HandleAttackModeResult(string json)
    {
        if (!IsSettingDefaultAttackMode)
            return;

        AttackModeResult result = ReadSkillMessage<AttackModeResult>(json);
        if (result == null || result.requestSequence != pendingDefaultAttackMode.requestSequence)
            return;

        pendingDefaultAttackMode = null;
        if (result.success && result.mode == AttackModeSetRequest.FreeMode)
            Debug.Log("[AttackMode] 服务端已确认全体模式。");
        else
            Debug.LogWarning("[AttackMode] 全体模式切换未确认。errorCode=" + result.errorCode);
    }

    public void RequestAttributeSnapshot()
    {
        if (!sessionStarted || returningToLogin || !gameSessionAccepted
            || localCharacterDbId <= 0 || networkManager == null
            || !networkManager.IsConnected
            || Time.unscaledTime - lastAttributeSnapshotRequestAt < 1f) return;
        if (pendingAttributeSnapshotSequence != 0
            && Time.unscaledTime - lastAttributeSnapshotRequestAt < 10f) return;

        long sequence = nextAttributeSnapshotSequence + 1;
        if (!networkManager.Send(new AttributeSnapshotRequest(sequence))) return;
        nextAttributeSnapshotSequence = sequence;
        pendingAttributeSnapshotSequence = sequence;
        lastAttributeSnapshotRequestAt = Time.unscaledTime;
        attributeRefreshQueued = false;
        Debug.Log("[AttributeSnapshot][TX] requestSequence=" + sequence);
    }

    public void QueueAttributeSnapshotRefresh()
    {
        if (!sessionStarted || returningToLogin || !gameSessionAccepted) return;
        attributeRefreshQueued = true;
        RequestAttributeSnapshot();
    }

    public void RequestProgressionSnapshot()
    {
        if (!sessionStarted || returningToLogin || !gameSessionAccepted
            || localCharacterDbId <= 0 || networkManager == null
            || !networkManager.IsConnected
            || Time.unscaledTime - lastProgressionSnapshotRequestAt < 1f) return;
        if (pendingProgressionSnapshotSequence != 0
            && Time.unscaledTime - lastProgressionSnapshotRequestAt < 10f) return;

        long sequence = nextProgressionSnapshotSequence + 1;
        if (!networkManager.Send(new ProgressionSnapshotRequest(sequence))) return;
        nextProgressionSnapshotSequence = sequence;
        pendingProgressionSnapshotSequence = sequence;
        lastProgressionSnapshotRequestAt = Time.unscaledTime;
        progressionRefreshQueued = false;
        Debug.Log("[ProgressionSnapshot][TX] requestSequence=" + sequence);
    }

    public void QueueProgressionSnapshotRefresh()
    {
        if (!IsWorldReady)
            return;
        progressionRefreshQueued = true;
        RequestProgressionSnapshot();
    }

    public void ReconcileJingjieBreakthrough(JingjieBreakthroughResultDto result)
    {
        if (!IsWorldReady || result == null
            || result.characterDbId != localCharacterDbId)
            return;

        // Any pre-breakthrough snapshot still in flight cannot overwrite this result.
        pendingProgressionSnapshotSequence = 0;
        progressionRefreshQueued = true;
        if (result.success && !result.idempotentReplay
            && LocalProgressionSnapshot != null
            && !string.IsNullOrWhiteSpace(result.jingjieId)
            && !string.IsNullOrWhiteSpace(result.jingjieName)
            && result.jingjieStage >= 0 && result.jingjieExp >= 0
            && result.requiredJingjieExp >= 0 && result.totalJingjieExp >= 0
            && JingjieNextRealmFields.IsValid(result.hasNextJingjie,
                result.nextJingjieId, result.nextJingjieStage,
                result.nextJingjieName))
        {
            LocalProgressionSnapshot.jingjieId = result.jingjieId;
            LocalProgressionSnapshot.jingjieStage = result.jingjieStage;
            LocalProgressionSnapshot.jingjieName = result.jingjieName;
            LocalProgressionSnapshot.hasNextJingjie = result.hasNextJingjie;
            LocalProgressionSnapshot.nextJingjieId = result.nextJingjieId;
            LocalProgressionSnapshot.nextJingjieStage = result.nextJingjieStage;
            LocalProgressionSnapshot.nextJingjieName = result.nextJingjieName;
            LocalProgressionSnapshot.jingjieExp = result.jingjieExp;
            LocalProgressionSnapshot.requiredJingjieExp = result.requiredJingjieExp;
            LocalProgressionSnapshot.totalJingjieExp = result.totalJingjieExp;
            LocalProgressionSnapshotChanged?.Invoke(LocalProgressionSnapshot);
        }
        RequestProgressionSnapshot();
        if (result.success)
            RefiningSessionRuntime.Instance?.RequestSnapshot();
    }

    public bool ApplyItemUseMana(int currentMp, int maxMp)
    {
        if (currentMp < 0 || maxMp < 0 || currentMp > maxMp)
            return false;
        if (LocalProgressionSnapshot == null)
        {
            RequestProgressionSnapshot();
            return false;
        }

        LocalProgressionSnapshot.currentMp = currentMp;
        LocalProgressionSnapshot.maxMp = maxMp;
        LocalProgressionSnapshotChanged?.Invoke(LocalProgressionSnapshot);
        return true;
    }

    public void ApplyItemUseJingjieBalance(long? current, long? total)
    {
        if (!IsWorldReady)
            return;
        if ((current != null || total != null)
            && LocalProgressionSnapshot != null
            && (total == null || total >= LocalProgressionSnapshot.totalJingjieExp))
        {
            if (current != null) LocalProgressionSnapshot.jingjieExp = current.Value;
            if (total != null) LocalProgressionSnapshot.totalJingjieExp = total.Value;
            LocalProgressionSnapshotChanged?.Invoke(LocalProgressionSnapshot);
        }
        // A snapshot sent before the use result must not replace its newer balance.
        pendingProgressionSnapshotSequence = 0;
        QueueProgressionSnapshotRefresh();
    }

    private void HandleAttributeSnapshotResult(string json)
    {
        AttributeSnapshotResult result;
        try { result = JsonUtility.FromJson<AttributeSnapshotResult>(json); }
        catch (System.Exception) { return; }
        if (result == null || pendingAttributeSnapshotSequence == 0
            || result.requestSequence != pendingAttributeSnapshotSequence
            || result.characterDbId != localCharacterDbId
            || !sessionStarted || !gameSessionAccepted || returningToLogin) return;

        pendingAttributeSnapshotSequence = 0;
        Debug.Log("[AttributeSnapshot][RX] requestSequence=" + result.requestSequence
            + " success=" + result.success
            + " attributeRevision=" + result.attributeRevision);
        if (!result.success || result.final == null)
        {
            Debug.LogWarning("Attribute snapshot failed: " + (result.errorCode ?? result.message));
            return;
        }
        if (LocalAttributeSnapshot != null
            && result.attributeRevision < LocalAttributeSnapshot.attributeRevision) return;
        LocalAttributeSnapshot = result;
        LocalAttributeSnapshotChanged?.Invoke(result);
    }

    private void HandleProgressionSnapshotResult(string json)
    {
        ProgressionSnapshotResult result;
        try { result = JsonUtility.FromJson<ProgressionSnapshotResult>(json); }
        catch (System.Exception) { return; }

        if (result == null || !sessionStarted || !gameSessionAccepted || returningToLogin
            || pendingProgressionSnapshotSequence == 0)
            return;
        if (result.requestSequence != pendingProgressionSnapshotSequence)
            return;
        if (result.characterDbId != localCharacterDbId)
            return;
        if (!result.success)
        {
            pendingProgressionSnapshotSequence = 0;
            Debug.LogWarning("Progression snapshot failed: "
                + (result.errorCode ?? result.message));
            return;
        }
        if (result.currentMp < 0 || result.maxMp < 0 || result.currentMp > result.maxMp)
            return;
        if (string.IsNullOrWhiteSpace(result.jingjieId)
            || string.IsNullOrWhiteSpace(result.jingjieName)
            || result.jingjieStage < 0 || result.jingjieExp < 0
            || result.requiredJingjieExp < 0 || result.totalJingjieExp < 0
            || !JingjieNextRealmFields.IsValid(result.hasNextJingjie,
                result.nextJingjieId, result.nextJingjieStage,
                result.nextJingjieName))
        {
            pendingProgressionSnapshotSequence = 0;
            Debug.LogWarning("Progression snapshot has invalid realm fields.");
            return;
        }

        pendingProgressionSnapshotSequence = 0;
        LocalProgressionSnapshot = result;
        Debug.Log("[ProgressionSnapshot][RX] requestSequence=" + result.requestSequence
            + " currentMp=" + result.currentMp
            + " maxMp=" + result.maxMp);
        LocalProgressionSnapshotChanged?.Invoke(result);
    }

    private void ResetAttributeSnapshot()
    {
        LocalAttributeSnapshot = null;
        nextAttributeSnapshotSequence = 0;
        pendingAttributeSnapshotSequence = 0;
        lastAttributeSnapshotRequestAt = float.NegativeInfinity;
        attributeRefreshQueued = false;
    }

    private void ResetProgressionSnapshot()
    {
        LocalProgressionSnapshot = null;
        nextProgressionSnapshotSequence = 0;
        pendingProgressionSnapshotSequence = 0;
        lastProgressionSnapshotRequestAt = float.NegativeInfinity;
        progressionRefreshQueued = false;
    }

    private void HandleBattleSyncReady(BattleSyncReadyContext context)
    {
        if (pendingMapTransfer != null)
        {
            MapTransferResult transfer = pendingMapTransfer;
            if (!sessionStarted || networkManager == null || !networkManager.IsConnected
                || networkManager.ConnectionEpoch != context.ConnectionEpoch
                || localCharacterDbId != context.CharacterDbId
                || context.SceneId != transfer.sceneId
                || context.SnapshotRevision != transfer.snapshotRevision
                || context.Reason != "map_transfer")
                return;

            if (!EnsureLocalPlayerScene()
                || mapTransferSourceScene == gameObject.scene
                || !TryGetLocalPlayerInputController(out LocalPlayerInputController transferInput))
            {
                RecoverWorldSession("map_transfer_local_player_unavailable");
                return;
            }

            pendingMapTransfer = null;
            mapTransferSnapshotDeadline = 0f;
            enterWorldConfirmed = true;
            mapTransferPaused = false;
            if (!networkManager.SetSceneOperationsPaused(context.ConnectionEpoch, false))
            {
                RecoverWorldSession("map_transfer_input_resume_failed");
                return;
            }
            if (!localPlayerDead && inputWasEnabledForTransfer)
                transferInput.SetInputEnabled(true);
            if (mapTransferSourceScene.IsValid() && mapTransferSourceScene.isLoaded
                && mapTransferSourceScene != SceneManager.GetActiveScene())
            {
                Scene oldMap = mapTransferSourceScene;
                AsyncOperation unload = SceneManager.UnloadSceneAsync(oldMap);
                if (unload == null)
                {
                    RecoverWorldSession("map_transfer_source_unload_failed");
                    return;
                }
                unload.completed += _ =>
                {
                    if (this == null || !sessionStarted || worldRecoveryPending
                        || networkManager == null || !networkManager.IsConnected
                        || networkManager.ConnectionEpoch != context.ConnectionEpoch)
                        return;
                    if (localPlayerRootObject == null || localPlayerInputController == null)
                        RecoverWorldSession("map_transfer_local_player_unavailable");
                    else if (oldMap.isLoaded)
                        RecoverWorldSession("map_transfer_source_unload_failed");
                    else
                        mapTransferCoordinator?.CompleteCommittedTransfer();
                };
            }
            else
                mapTransferCoordinator?.CompleteCommittedTransfer();
            mapTransferSourceScene = default;
            return;
        }
        if (!sessionStarted || enterWorldConfirmed || networkManager == null
            || networkManager.ConnectionEpoch != context.ConnectionEpoch
            || localCharacterDbId != context.CharacterDbId
            || !string.Equals(sceneId, context.SceneId, System.StringComparison.Ordinal)) return;
        enterWorldConfirmed = true;
        if (mapTransferSourceScene.IsValid() && mapTransferSourceScene.isLoaded
            && mapTransferSourceScene != SceneManager.GetActiveScene())
            SceneManager.UnloadSceneAsync(mapTransferSourceScene);
        mapTransferSourceScene = default;
        StartLocalPlayerInput();
    }

    private void CancelMapTransferLoad()
    {
        if (mapTransferLoadCoroutine != null)
        {
            StopCoroutine(mapTransferLoadCoroutine);
            mapTransferLoadCoroutine = null;
        }
        if (!mapTransferSceneOperation.IsValid())
            return;

        AsyncOperationHandle<SceneInstance> abandoned = mapTransferSceneOperation;
        mapTransferSceneOperation = default;
        abandoned.Completed += completed =>
        {
            if (completed.Status == AsyncOperationStatus.Succeeded
                && completed.Result.Scene.IsValid()
                && completed.Result.Scene.isLoaded
                && completed.Result.Scene != SceneManager.GetActiveScene())
                SceneManager.UnloadSceneAsync(completed.Result.Scene);
            else if (completed.Status != AsyncOperationStatus.Succeeded
                && completed.IsValid())
                Addressables.Release(completed);
        };
    }

    private void HandleMapTransferCommitted(MapTransferResult result)
    {
        if (result == null || pendingMapTransfer != null || !mapTransferPaused
            || battleSyncRuntime == null || !battleSyncRuntime.OwnsSnapshotRouting
            || messageDispatcher == null || networkManager == null
            || !networkManager.IsConnected || localPlayerRootObject == null
            || result.movementControl?.IsValidFor(result.sceneSessionId) != true
            || result.self == null || result.self.characterDbId != localCharacterDbId
            || result.self.sceneId != result.sceneId || result.serverTime <= 0
            || !WorldMapSceneCatalog.TryGetAddress(result.sceneId, result.mapId,
                out string mapAddress))
        {
            RecoverWorldSession("map_transfer_commit_invalid");
            return;
        }

        mapTransferSourceScene = SceneManager.GetActiveScene();
        if (!mapTransferSourceScene.IsValid()
            || mapTransferSourceScene.name == RemoteGameContent.WorldSceneName)
        {
            RecoverWorldSession("map_transfer_source_map_missing");
            return;
        }

        pendingMapTransfer = result;
        enterWorldConfirmed = false;
        mapTransferSnapshotDeadline = Time.realtimeSinceStartup
            + Mathf.Max(1f, mapTransferSnapshotTimeout);
        messageDispatcher.BeginDeferredMapTransferMessages();
        long epoch = networkManager.ConnectionEpoch;
        mapTransferLoadCoroutine = StartCoroutine(
            LoadMapTransferDestination(result, mapAddress, epoch));
    }

    private IEnumerator LoadMapTransferDestination(
        MapTransferResult result, string mapAddress, long epoch)
    {
        AsyncOperationHandle<SceneInstance> operation;
        try
        {
            operation = RemoteGameContent.LoadMapSceneAsync(mapAddress);
            mapTransferSceneOperation = operation;
        }
        catch (System.Exception error)
        {
            Debug.LogError("Map transfer load failed: " + error);
            RecoverWorldSession("map_transfer_load_failed");
            yield break;
        }

        yield return operation;
        mapTransferLoadCoroutine = null;
        mapTransferSceneOperation = default;
        if (pendingMapTransfer != result || networkManager == null
            || !networkManager.IsConnected || networkManager.ConnectionEpoch != epoch
            || messageDispatcher == null || messageDispatcher.HasDeferredWorldOverflow)
        {
            if (operation.Status == AsyncOperationStatus.Succeeded
                && operation.Result.Scene.IsValid())
                SceneManager.UnloadSceneAsync(operation.Result.Scene);
            RecoverWorldSession("map_transfer_connection_changed");
            yield break;
        }

        if (operation.Status != AsyncOperationStatus.Succeeded
            || !operation.Result.Scene.IsValid())
        {
            Debug.LogError("Map transfer load failed: " + operation.OperationException);
            if (operation.IsValid()) Addressables.Release(operation);
            RecoverWorldSession("map_transfer_load_failed");
            yield break;
        }

        if (!SceneManager.SetActiveScene(operation.Result.Scene)
            || !networkManager.SetSceneSessionId(epoch, result.sceneSessionId)
            || !battleSyncRuntime.BeginMapTransfer(new BattleSyncEnterWorldContext(
                epoch, result.sceneId, localCharacterDbId, result.snapshotRevision,
                result.serverTime, "map_transfer")))
        {
            RecoverWorldSession("map_transfer_destination_initialization_failed");
            yield break;
        }

        sceneId = result.sceneId;
        activeMapId = result.mapId;
        if (!TryGetLocalPlayerInputController(out var movementInput) || !movementInput.ApplyMovementControl(result.movementControl))
        {
            RecoverWorldSession("map_transfer_movement_control_failed");
            yield break;
        }
        LocalEnterWorldHp = result.self.maxHp > 0
            ? Mathf.Clamp(result.self.hp, 0, result.self.maxHp) : -1;
        if (IsLocalPlayerDeadAtSpawn(result.self)) EnterLocalDeadState();
        if (localGroundRing != null)
            localGroundRing.SetGroundScene(operation.Result.Scene);
        LocalEnterWorldCombatRevision = result.self.combatRevision;
        PvpEnabled = result.pvpEnabled;
        SafeZoneBoundaryRenderer boundary =
            FindObjectOfType<SafeZoneBoundaryRenderer>(true);
        if (boundary != null)
        {
            if (result.safeZone != null && result.safeZone.radius > 0f)
                boundary.SetZone(result.safeZone.x, result.y,
                    result.safeZone.z, result.safeZone.radius);
            else
                boundary.Hide();
        }

        messageDispatcher.ReplayDeferredWorldMessages();
        if (messageDispatcher.HasDeferredWorldOverflow)
            RecoverWorldSession("map_transfer_message_overflow");
    }

    public void HandleWorldSnapshot(string json)
    {
        if (entityManagerObject == null)
        {
            Debug.Log("World snapshot received, but EntityManager is not assigned yet.");
            return;
        }

        entityManagerObject.SendMessage("ApplyWorldSnapshotJson", json, SendMessageOptions.DontRequireReceiver);
    }

    public void HandlePlayerLeave(string json)
    {
        if (entityManagerObject == null)
        {
            return;
        }

        entityManagerObject.SendMessage("RemoveRemotePlayerByLeaveJson", json, SendMessageOptions.DontRequireReceiver);
    }

    public void HandlePlayerAuthoritativeState(string json)
    {
        var state = JsonUtility.FromJson<PlayerAuthoritativeState>(json);
        if (state != null && !localPlayerDead && !mapTransferPaused)
            localPlayerInputController?.ApplyOwnedMovement(state);
    }

    private void HandleMovementControl(string json) =>
        ApplyMovementControl(JsonUtility.FromJson<MovementControlNotification>(json)?.control);

    private void HandleMovementControlResult(string json) =>
        localPlayerInputController?.ReceiveMovementControlResult(JsonUtility.FromJson<MovementControlReply>(json)?.result);

    private void HandleMovementCorrection(string json)
    {
        var result = JsonUtility.FromJson<MovementControlReply>(json)?.result;
        if (result?.needsControlAck == true)
            ApplyMovementControl(result.control);
    }

    private void ApplyMovementControl(MovementEventControl control)
    {
        if (control == null || (mapTransferPaused && pendingMapTransfer == null && control.mode == "normal"))
            return;
        if (localPlayerInputController?.ApplyMovementControl(control) == true && control.reason == "respawn")
            LocalRespawnControlReceived?.Invoke();
    }

    public void HandleHeartbeatReply(string json)
    {
        HeartbeatAckMessage result = JsonUtility.FromJson<HeartbeatAckMessage>(json);
        if (result == null)
        {
            Debug.LogWarning("Heartbeat result parse failed.");
            return;
        }

        lastHeartbeatReplyTime = Time.unscaledTime;
        lastHeartbeatServerTime = result.time;
    }

    public void HandlePlayerEnter(string json)
    {
    }

    internal bool HasPreparedLocalCharacter(ThirdPersonCameraFollow cameraFollow)
    {
        GameObject visual = spawnedLocalCharacterVisual != null
            ? spawnedLocalCharacterVisual : defaultLocalCharacterVisual;
        return localPlayerRootObject != null && localPlayerRootObject.activeInHierarchy
            && visual != null && visual.activeInHierarchy
            && localPlayerInputController != null && localPlayerInputController.isActiveAndEnabled
            && cameraFollow != null && cameraFollow.isActiveAndEnabled
            && cameraFollow.rotationTarget == localPlayerRootObject.transform;
    }

    internal void StartLocalPlayerInput()
    {
        if (localPlayerDead || !enterWorldConfirmed || WorldEntryLoadingView.ActiveWorldEntry != null)
        {
            return;
        }

        if (localPlayerRootObject == null)
        {
            Debug.LogError("Start local player input failed: server spawn has not created local player root.");
            return;
        }

        BindLocalPlayerComponents(localPlayerRootObject, null);
        if (TryGetLocalPlayerInputController(out LocalPlayerInputController inputController))
        {
            inputController.SetInputEnabled(true, resetSequence: true);
        }
    }

    public bool TryPauseForMapTransfer()
    {
        if (!IsWorldReady || localPlayerDead || mapTransferPaused || networkManager == null
            || battleSyncRuntime == null || !battleSyncRuntime.OwnsSnapshotRouting
            || battleSyncRuntime.SessionState == null
            || !battleSyncRuntime.SessionState.IsReady
            || !networkManager.MovementReady
            || localPlayerInputController?.StopMovementForTransfer() != true
            || !networkManager.SetSceneOperationsPaused(networkManager.ConnectionEpoch, true))
            return false;

        mapTransferPaused = true;
        inputWasEnabledForTransfer = TryGetLocalPlayerInputController(
            out LocalPlayerInputController input) && input.IsInputEnabled;
        StopLocalPlayerInput();
        return true;
    }

    public void ResumeAfterMapTransferFailure(MovementEventControl control)
    {
        if (!mapTransferPaused)
            return;

        mapTransferPaused = false;
        if (networkManager == null
            || !networkManager.SetSceneOperationsPaused(networkManager.ConnectionEpoch, false))
            return;

        if (TryGetLocalPlayerInputController(out LocalPlayerInputController input))
        {
            if (control != null) input.ApplyMovementControl(control);
            if (IsWorldReady && !localPlayerDead && inputWasEnabledForTransfer)
                input.SetInputEnabled(true);
        }
    }

    private void SetLocalPlayerDead(bool dead)
    {
        localPlayerDead = dead;
        networkManager?.SetLocalPlayerActionsBlocked(dead);
        PlayerDeathAnimation.SetDead(predictedCharacterMotor?.VisualAnimator, dead);
    }

    public void EnterLocalDeadState()
    {
        if (!localPlayerDead) localPlayerInputController?.RecordDeathMovementEpoch();
        SetLocalPlayerDead(true);
        StopLocalPlayerInput();
        predictedCharacterMotor?.StopForDeath();
    }

    public void PrepareLocalRespawn()
    {
        EnterLocalDeadState();
    }

    public bool TryApplyLocalRespawnControl()
    {
        if (!IsWorldReady || localPlayerInputController?.RestoreRespawnControl() != true)
            return false;
        SetLocalPlayerDead(false);
        return true;
    }

    private static bool IsLocalPlayerDeadAtSpawn(PlayerNetworkState state)
    {
        return state != null && ((state.maxHp > 0 && state.hp <= 0)
            || string.Equals(state.lifeState, "Dead", System.StringComparison.OrdinalIgnoreCase));
    }

    public void ResumeLocalPlayerControl()
    {
        if (localPlayerDead || mapTransferPaused || !enterWorldConfirmed
            || WorldEntryLoadingView.ActiveWorldEntry != null)
        {
            return;
        }

        if (networkManager != null) BeginDefaultAttackMode();
        if (TryGetLocalPlayerInputController(out LocalPlayerInputController inputController))
        {
            inputController.SetInputEnabled(true);
        }
    }

    private void ApplyLocalCharacterVisual(string characterId, Vector3 spawnPosition, Quaternion spawnRotation)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            Debug.LogError("Apply local character visual failed: characterId is empty.");
            return;
        }

        GameObject localPlayerRoot = EnsureLocalPlayerRoot(spawnPosition, spawnRotation);
        if (localPlayerRoot == null)
        {
            Debug.LogError("Apply local character visual failed: local player root is missing.");
            return;
        }

        LocalCharacterVisualSlot slot = FindLocalCharacterVisualSlot(characterId);
        if (slot == null || slot.prefab == null)
        {
            UseDefaultLocalCharacterVisual(localPlayerRoot, characterId);
            PlayerEffectPresenter.Bind(localPlayerRoot, playerPresentationSettings,
                localCharacterVisualRoot, localSkillReleasePoint, defaultLocalCharacterVisual);
            return;
        }

        if (spawnedLocalCharacterVisual != null)
        {
            Destroy(spawnedLocalCharacterVisual);
            spawnedLocalCharacterVisual = null;
        }

        Transform parent = localCharacterVisualRoot;

        if (defaultLocalCharacterVisual != null)
        {
            defaultLocalCharacterVisual.SetActive(false);
        }

        spawnedLocalCharacterVisual = Instantiate(slot.prefab, parent);
        spawnedLocalCharacterVisual.name = "LocalCharacter_" + characterId;
        spawnedLocalCharacterVisual.transform.localPosition = Vector3.zero;
        spawnedLocalCharacterVisual.transform.localRotation = Quaternion.identity;

        DisableGameplayComponentsOnVisual(spawnedLocalCharacterVisual);
        BindLocalPlayerComponents(localPlayerRoot, spawnedLocalCharacterVisual.GetComponentInChildren<Animator>(true));
        PlayerEffectPresenter.Bind(localPlayerRoot, playerPresentationSettings,
            localCharacterVisualRoot, localSkillReleasePoint, spawnedLocalCharacterVisual);
    }

    private GameObject EnsureLocalPlayerRoot(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        if (localPlayerRootObject == null)
        {
            GameObject existingRoot = string.IsNullOrWhiteSpace(localPlayerRootName)
                ? null
                : GameObject.Find(localPlayerRootName);

            localPlayerRootObject = existingRoot == null
                ? CreateLocalPlayerRoot()
                : existingRoot;
        }

        if (!EnsureLocalPlayerScene())
            return null;

        localPlayerRootObject.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        EnsureLocalCharacterVisualRoot(localPlayerRootObject.transform);
        BindLocalPlayerComponents(localPlayerRootObject, null);
        BindLocalPlayerProvider(localPlayerRootObject.transform);
        return localPlayerRootObject;
    }

    private bool EnsureLocalPlayerScene()
    {
        Scene sessionScene = gameObject.scene;
        if (localPlayerRootObject == null || !sessionScene.IsValid() || !sessionScene.isLoaded)
            return false;

        if (localPlayerRootObject.scene != sessionScene)
        {
            localPlayerRootObject.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(localPlayerRootObject, sessionScene);
        }

        return localPlayerRootObject.scene == sessionScene;
    }

    private void BindLocalPlayerProvider(Transform playerTransform)
    {
        LocalPlayerProvider provider = LocalPlayerProvider.Instance;
        if (provider == null)
        {
            Debug.LogWarning(
                "Local player was created, but LocalPlayerProvider is missing from the scene.");
            return;
        }

        provider.SetPlayer(playerTransform);
    }

    private void ClearLocalPlayerProvider()
    {
        LocalPlayerProvider provider = LocalPlayerProvider.Instance;
        if (provider == null)
        {
            return;
        }

        Transform playerTransform = localPlayerRootObject == null
            ? null
            : localPlayerRootObject.transform;
        provider.ClearPlayer(playerTransform);
    }

    private GameObject CreateLocalPlayerRoot()
    {
        return new GameObject(string.IsNullOrWhiteSpace(localPlayerRootName) ? "LocalPlayerRoot" : localPlayerRootName);
    }

    private void EnsureLocalCharacterVisualRoot(Transform localPlayerRoot)
    {
        if (localCharacterVisualRoot == null)
        {
            GameObject visualRootObject = new GameObject("LocalCharacterVisualRoot");
            localCharacterVisualRoot = visualRootObject.transform;
        }

        if (localCharacterVisualRoot != localPlayerRoot && !localCharacterVisualRoot.IsChildOf(localPlayerRoot))
        {
            localCharacterVisualRoot.SetParent(localPlayerRoot, false);
        }

        localCharacterVisualRoot.localPosition = Vector3.zero;
        localCharacterVisualRoot.localRotation = Quaternion.identity;
        localCharacterVisualRoot.localScale = Vector3.one;
    }

    private void UseDefaultLocalCharacterVisual(GameObject localPlayerRoot, string characterId)
    {
        if (spawnedLocalCharacterVisual != null)
        {
            Destroy(spawnedLocalCharacterVisual);
            spawnedLocalCharacterVisual = null;
        }

        if (defaultLocalCharacterVisual == null)
        {
            Debug.LogWarning("No local character visual prefab configured for characterId: " + characterId);
            BindLocalPlayerComponents(localPlayerRoot, null);
            return;
        }

        Transform parent = localCharacterVisualRoot;
        if (defaultLocalCharacterVisual.transform != parent && !defaultLocalCharacterVisual.transform.IsChildOf(parent))
        {
            defaultLocalCharacterVisual.transform.SetParent(parent, false);
        }

        defaultLocalCharacterVisual.transform.localPosition = Vector3.zero;
        defaultLocalCharacterVisual.transform.localRotation = Quaternion.identity;
        defaultLocalCharacterVisual.SetActive(true);
        BindLocalPlayerComponents(localPlayerRoot, defaultLocalCharacterVisual.GetComponentInChildren<Animator>(true));
    }

    private void BindLocalPlayerComponents(GameObject localPlayerRoot, Animator animator)
    {
        if (localPlayerRoot == null)
        {
            return;
        }

        CharacterController controller = localPlayerRoot.GetComponent<CharacterController>();
        if (controller == null)
        {
            controller = localPlayerRoot.AddComponent<CharacterController>();
        }

        controller.center = localPlayerControllerCenter;
        controller.height = Mathf.Max(localPlayerControllerHeight, localPlayerControllerRadius * 2f);
        controller.radius = Mathf.Max(0.01f, localPlayerControllerRadius);
        controller.slopeLimit = Mathf.Clamp(localPlayerControllerSlopeLimit, 0f, 90f);
        controller.stepOffset = Mathf.Clamp(
            localPlayerControllerStepOffset,
            0f,
            controller.height
        );
        controller.skinWidth = Mathf.Max(0.001f, localPlayerControllerSkinWidth);
        controller.minMoveDistance = Mathf.Max(0f, localPlayerControllerMinMoveDistance);
        controller.detectCollisions = true;
        controller.enableOverlapRecovery = true;

        predictedCharacterMotor = localPlayerRoot.GetComponent<PredictedCharacterMotor>();
        if (predictedCharacterMotor == null)
        {
            predictedCharacterMotor = localPlayerRoot.AddComponent<PredictedCharacterMotor>();
        }

        Animator boundAnimator = animator != null
            ? animator
            : localPlayerRoot.GetComponentInChildren<Animator>(true);
        predictedCharacterMotor.BindAnimator(boundAnimator);
        PlayerDeathAnimation.SetDead(boundAnimator, localPlayerDead, alreadyDeadAtSpawn: true);

        localPlayerVisualSmoother = localPlayerRoot.GetComponent<LocalPlayerVisualSmoother>();
        if (localPlayerVisualSmoother == null)
        {
            localPlayerVisualSmoother = localPlayerRoot.AddComponent<LocalPlayerVisualSmoother>();
        }

        localPlayerVisualSmoother.Bind(localPlayerRoot.transform, localCharacterVisualRoot);

        localPlayerReconciler = localPlayerRoot.GetComponent<LocalPlayerReconciler>();
        if (localPlayerReconciler == null)
        {
            localPlayerReconciler = localPlayerRoot.AddComponent<LocalPlayerReconciler>();
        }

        localPlayerInputController = localPlayerRoot.GetComponent<LocalPlayerInputController>();
        if (localPlayerInputController == null)
        {
            localPlayerInputController = localPlayerRoot.AddComponent<LocalPlayerInputController>();
        }

        localPlayerReconciler.Bind(predictedCharacterMotor, localPlayerVisualSmoother);
        localPlayerInputController.Bind(
            predictedCharacterMotor,
            localPlayerReconciler,
            localPlayerVisualSmoother
        );

        BindLocalSkillRelease(localPlayerRoot);

        RemotePlayerSync remoteSync = localPlayerRoot.GetComponent<RemotePlayerSync>();
        if (remoteSync != null)
        {
            remoteSync.enabled = false;
        }

        Transform cameraFollowTarget = localCharacterVisualRoot == null
            ? localPlayerRoot.transform
            : localCharacterVisualRoot;
        BindCameraFollow(cameraFollowTarget, localPlayerRoot.transform);
        BindLocalGroundRing(cameraFollowTarget, localPlayerRoot.transform);
    }

    private void BindLocalSkillRelease(GameObject localPlayerRoot)
    {
        if (localSkillReleasePoint == null)
        {
            localSkillReleasePoint = new GameObject("施法挂点").transform;
            localSkillReleasePoint.SetParent(localCharacterVisualRoot, false);
        }

        localSkillReleasePoint.localPosition = playerPresentationSettings != null
            ? playerPresentationSettings.SkillReleaseOffset
            : PlayerPresentationSettings.DefaultSkillReleaseOffset;

        if (!localPlayerRoot.TryGetComponent(out 技能目标瞄准器 aimer))
        {
            aimer = localPlayerRoot.AddComponent<技能目标瞄准器>();
        }

        localPlayerRoot.GetComponent<LocalPlayerCombatController>()
            .BindSkillRelease(localSkillReleasePoint, aimer);
    }

    private void BindLocalGroundRing(Transform followTarget, Transform playerRoot)
    {
        if (localGroundRing == null)
        {
            if (localGroundRingPrefab == null)
                return;

            localGroundRing = Instantiate(localGroundRingPrefab, playerRoot, false);
            localGroundRing.enabled = false;
        }

        localGroundRing.SetFollowTarget(followTarget, playerRoot, GroundRingSurfaceFilter.Allows);
        localGroundRing.SetGroundScene(SceneManager.GetActiveScene());
    }

    private void BindCameraFollow(Transform followTarget, Transform rotationTarget)
    {
        if (followTarget == null || rotationTarget == null)
        {
            return;
        }

        ThirdPersonCameraFollow cameraFollow = FindObjectOfType<ThirdPersonCameraFollow>();
        if (cameraFollow != null)
        {
            cameraFollow.BindTargets(followTarget, rotationTarget);
        }
    }

    private void DisableGameplayComponentsOnVisual(GameObject visualObject)
    {
        if (visualObject == null)
        {
            return;
        }

        SetBehavioursEnabled<RemotePlayerSync>(visualObject, false);
        SetBehavioursEnabled<LocalPlayerInputController>(visualObject, false);
        SetBehavioursEnabled<PredictedCharacterMotor>(visualObject, false);
        SetBehavioursEnabled<LocalPlayerReconciler>(visualObject, false);
        SetBehavioursEnabled<LocalPlayerVisualSmoother>(visualObject, false);

        CharacterController[] controllers = visualObject.GetComponentsInChildren<CharacterController>(true);
        for (int index = 0; index < controllers.Length; index++)
        {
            controllers[index].enabled = false;
        }
    }

    private void SetBehavioursEnabled<T>(GameObject root, bool enabled) where T : Behaviour
    {
        T[] behaviours = root.GetComponentsInChildren<T>(true);
        for (int index = 0; index < behaviours.Length; index++)
        {
            behaviours[index].enabled = enabled;
        }
    }

    private string GetEnterWorldCharacterId(EnterWorldResult result)
    {
        if (result == null)
        {
            return PlayerPrefs.GetString(SelectedCharacterIdKey, "");
        }

        if (!string.IsNullOrWhiteSpace(result.characterId))
        {
            return result.characterId;
        }

        if (!string.IsNullOrWhiteSpace(result.roleId))
        {
            return result.roleId;
        }

        return PlayerPrefs.GetString(SelectedCharacterIdKey, "");
    }

    private static string ResolveLocalNickname(EnterWorldResult result, long characterDbId)
    {
        if (result == null || characterDbId <= 0) return string.Empty;
        if (!long.TryParse(PlayerPrefs.GetString(SelectedCharacterDbIdKey, ""),
                out long selectedCharacterDbId) || selectedCharacterDbId != characterDbId)
            return string.Empty;
        if (result.self != null && result.self.characterDbId > 0
            && result.self.characterDbId != characterDbId)
            return string.Empty;
        if (result.self != null && result.self.characterDbId == characterDbId
            && !string.IsNullOrWhiteSpace(result.self.nickname))
            return result.self.nickname.Trim();
        if (result.characterDbId == characterDbId
            && !string.IsNullOrWhiteSpace(result.nickname))
            return result.nickname.Trim();
        return string.Empty;
    }

    private bool HasServerSpawnTransform(string json)
    {
        return JsonHasField(json, "x")
            && JsonHasField(json, "y")
            && JsonHasField(json, "z")
            && JsonHasField(json, "rotY");
    }

    private bool JsonHasField(string json, string fieldName)
    {
        return !string.IsNullOrEmpty(json) && json.Contains("\"" + fieldName + "\"");
    }

    private void StartMissingServerSpawnTimeout()
    {
        StopMissingServerSpawnTimeout();
        missingServerSpawnCoroutine = StartCoroutine(ExitGameSceneAfterMissingServerSpawnTimeout());
    }

    private void StopMissingServerSpawnTimeout()
    {
        if (missingServerSpawnCoroutine != null)
        {
            StopCoroutine(missingServerSpawnCoroutine);
            missingServerSpawnCoroutine = null;
        }
    }

    private void StopWorldRecovery()
    {
        recoveryGate?.Dispose();
        recoveryGate = null;
        if (worldRecoveryCoroutine != null)
        {
            StopCoroutine(worldRecoveryCoroutine);
            worldRecoveryCoroutine = null;
        }
        worldRecoveryPending = false;
    }

    private IEnumerator ExitGameSceneAfterMissingServerSpawnTimeout()
    {
        float startTime = Time.realtimeSinceStartup;
        float timeout = Mathf.Max(0f, serverSpawnWaitTimeout);

        while (sessionStarted && Time.realtimeSinceStartup - startTime < timeout)
        {
            yield return null;
        }

        missingServerSpawnCoroutine = null;

        if (!sessionStarted)
        {
            yield break;
        }

        Debug.LogError("Server did not provide spawn coordinates. Returning to exit scene: " + exitSceneName);
        ReturnToLogin("server_spawn_missing");
    }

    private LocalCharacterVisualSlot FindLocalCharacterVisualSlot(string characterId)
    {
        if (localCharacterVisualSlots == null)
        {
            return null;
        }

        for (int index = 0; index < localCharacterVisualSlots.Length; index++)
        {
            LocalCharacterVisualSlot slot = localCharacterVisualSlots[index];
            if (slot != null && string.Equals(slot.characterId, characterId, System.StringComparison.Ordinal))
            {
                return slot;
            }
        }

        return null;
    }

    private void StopLocalPlayerInput()
    {
        if (TryGetLocalPlayerInputController(out LocalPlayerInputController inputController))
        {
            inputController.SetInputEnabled(false);
        }
    }

    private bool TryGetLocalPlayerInputController(out LocalPlayerInputController inputController)
    {
        inputController = localPlayerInputController;
        if (inputController != null)
        {
            return true;
        }

        localPlayerInputController = null;
        return false;
    }

    private void SendLeaveWorld()
    {
        if (networkManager == null || !networkManager.IsConnected)
        {
            return;
        }

        if (AuthSession.Instance == null || string.IsNullOrEmpty(AuthSession.Instance.UserId))
        {
            return;
        }

        LeaveWorldRequest request = new LeaveWorldRequest();
        request.userId = AuthSession.Instance.UserId;
        request.username = AuthSession.Instance.Username;

        networkManager.Send(request);
    }
}

[System.Serializable]
public class LocalCharacterVisualSlot
{
    public string characterId;
    public GameObject prefab;

    public LocalCharacterVisualSlot()
    {
    }

    public LocalCharacterVisualSlot(string characterId)
    {
        this.characterId = characterId;
    }
}
