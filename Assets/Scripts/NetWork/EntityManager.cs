using System.Collections.Generic;
using System.Globalization;
using Game.Client.BattleSync;
using Game.Client.BattleSync.Protocol;
using UnityEngine;

public class EntityManager : MonoBehaviour, IPlayerBattleSyncModule, IBattleSyncSnapshotRoutingControl
{
    [Header("Remote Player")]
    [SerializeField] private RemoteCharacterPrefabSlot[] remoteCharacterPrefabs;
    [SerializeField] private GameObject fallbackRemotePlayerPrefab;
    [SerializeField] private Transform remotePlayerRoot;
    [SerializeField, InspectorName("玩家公共表现配置")] private PlayerPresentationSettings playerPresentationSettings;

    [Header("Remote Player Health Bar")]
    [SerializeField] private GameObject remotePlayerHealthBarPrefab;
    [SerializeField] private Vector3 remotePlayerHealthBarOffset = new Vector3(0f, 2.2f, 0f);
    [SerializeField] private CombatStateStore combatStateStore;

    private readonly Dictionary<long, RemotePlayerSync> remotePlayers = new Dictionary<long, RemotePlayerSync>();
    private readonly Dictionary<long, long> playerLeaveTimes = new Dictionary<long, long>();
    private readonly RemoteSkillCastEventInbox remoteSkillCastInbox = new RemoteSkillCastEventInbox();
    private readonly RemoteSkillCastTimeline remoteSkillTimeline = new RemoteSkillCastTimeline();
    private readonly RemoteSkillCastEffectTracker remoteSkillEffects = new RemoteSkillCastEffectTracker();
    private 技能注册表 remoteSkillRegistry;
    private NetMessageDispatcher registeredDispatcher;
    private CombatStateStore subscribedCombatStateStore;
    private NetworkManager registeredNetworkManager;
    private long localCharacterDbId;
    private string activeSceneId;
    private long activeConnectionEpoch;
    private readonly Dictionary<long, PlayerMovementEventMessage> pendingMovement = new Dictionary<long, PlayerMovementEventMessage>();
    private BattleSyncSnapshotRoutingMode snapshotRoutingMode=BattleSyncSnapshotRoutingMode.LegacySelfRegistered;

    private void OnEnable()
    {
        ResolveCombatStateStore();
        EnsureMessageRegistration();
        EnsureNetworkRegistration();
    }

    private void Update()
    {
        EnsureMessageRegistration();
        EnsureNetworkRegistration();
        remoteSkillEffects.Prune();
    }

    private void OnDisable()
    {
        if (subscribedCombatStateStore != null)
            subscribedCombatStateStore.StateChanged -= OnPlayerCombatStateChanged;
        subscribedCombatStateStore = null;

        RemoveMessageRegistration();
        RemoveNetworkRegistration();
        remoteSkillCastInbox.EndSession(activeConnectionEpoch);
        remoteSkillTimeline.EndSession(activeConnectionEpoch);
        remoteSkillEffects.Clear();
    }

    private void EnsureNetworkRegistration()
    {
        NetworkManager network = NetworkManager.Instance;
        if (registeredNetworkManager == network) return;
        RemoveNetworkRegistration();
        if (network == null) return;
        network.OnDisconnectedWithEpoch += HandleNetworkDisconnected;
        registeredNetworkManager = network;
    }

    private void RemoveNetworkRegistration()
    {
        if (registeredNetworkManager == null) return;
        registeredNetworkManager.OnDisconnectedWithEpoch -= HandleNetworkDisconnected;
        registeredNetworkManager = null;
    }

    private void HandleNetworkDisconnected(long connectionEpoch, string reason)
    {
        remoteSkillCastInbox.EndSession(connectionEpoch);
        remoteSkillTimeline.EndSession(connectionEpoch);
        if (connectionEpoch == activeConnectionEpoch)
            remoteSkillEffects.Clear();
    }

    private void EnsureMessageRegistration()
    {
        NetMessageDispatcher currentDispatcher = NetMessageDispatcher.Instance;

        if (registeredDispatcher == currentDispatcher)
        {
            return;
        }

        RemoveMessageRegistration();

        if (currentDispatcher == null)
        {
            return;
        }

        if(snapshotRoutingMode==BattleSyncSnapshotRoutingMode.LegacySelfRegistered)
        {
            currentDispatcher.Register("player_state_broadcast", HandlePlayerStateBroadcast);
            currentDispatcher.Register("player_enter_broadcast", HandlePlayerEnterBroadcast);
            currentDispatcher.Register("player_leave_broadcast", HandlePlayerLeave);
        }
        currentDispatcher.Register("player_movement_event", HandleMovementEvent);
        currentDispatcher.Register("skill_cast_event", HandleSkillCastEvent);
        registeredDispatcher = currentDispatcher;
    }

    private void RemoveMessageRegistration()
    {
        if (registeredDispatcher == null)
        {
            return;
        }

        registeredDispatcher.Unregister("player_state_broadcast", HandlePlayerStateBroadcast);
        registeredDispatcher.Unregister("player_enter_broadcast", HandlePlayerEnterBroadcast);
        registeredDispatcher.Unregister("player_leave_broadcast", HandlePlayerLeave);
        registeredDispatcher.Unregister("player_movement_event", HandleMovementEvent);
        registeredDispatcher.Unregister("skill_cast_event", HandleSkillCastEvent);
        registeredDispatcher = null;
    }

    private void HandleSkillCastEvent(string json)
    {
        SkillCastEvent message;
        try
        {
            message = JsonUtility.FromJson<SkillCastEvent>(json);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[RemoteSkillCast][SKIP] reason=invalid_json " + exception.Message);
            return;
        }

        NetworkManager network = NetworkManager.Instance;
        long epoch = network == null ? 0 : network.ConnectionEpoch;
        bool connected = network != null && network.IsConnected;
        if (!remoteSkillCastInbox.TryAccept(message, epoch, connected,
                Time.unscaledTime, out string reason))
        {
            Debug.Log("[RemoteSkillCast][SKIP] reason=" + reason
                + " epoch=" + epoch
                + " castId=" + (message == null ? "" : message.castId));
            return;
        }

        Debug.Log("[RemoteSkillCast][RX] epoch=" + epoch
            + " sceneId=" + message.sceneId
            + " castId=" + message.castId
            + " casterId=" + message.casterId
            + " skillId=" + message.skillId
            + " serverTick=" + message.serverTick
            + " hitTick=" + message.hitTick);

        if (!long.TryParse(message.casterId, NumberStyles.None,
                CultureInfo.InvariantCulture, out long casterId)
            || !remotePlayers.TryGetValue(casterId, out RemotePlayerSync caster)
            || caster == null)
        {
            Debug.LogWarning("[RemoteSkillCast][SKIP] reason=remote_caster_missing"
                + " castId=" + message.castId + " casterId=" + message.casterId);
            return;
        }

        if (caster.IsDead) return;

        if (remoteSkillRegistry == null)
            remoteSkillRegistry = FindObjectOfType<技能注册表>();
        if (remoteSkillRegistry == null
            || !remoteSkillRegistry.TryGet(message.skillId, out 技能释放配置 config))
        {
            Debug.LogWarning("[RemoteSkillCast][SKIP] reason=skill_not_registered"
                + " castId=" + message.castId + " skillId=" + message.skillId);
            return;
        }

        double receivedAt = Time.realtimeSinceStartupAsDouble;
        bool hasTiming = remoteSkillTimeline.TryGetMinimumElapsedSeconds(
            epoch, message.serverTime, receivedAt, out double minimumElapsed);
        remoteSkillTimeline.ObserveServerTime(epoch, message.serverTime, receivedAt);
        float elapsed = hasTiming
            ? (float)System.Math.Min(minimumElapsed, float.MaxValue) : 0f;
        if (!RemoteSkillCastPresenter.TryPlay(message, caster, config, elapsed,
                out GameObject instance, out string playReason))
        {
            Debug.LogWarning("[RemoteSkillCast][SKIP] reason=" + playReason
                + " castId=" + message.castId + " skillId=" + message.skillId);
            return;
        }

        remoteSkillEffects.Track(casterId,
            config.释放方式 == 技能释放方式.玩家释放节点, instance);
        Debug.Log("[RemoteSkillCast][FX] castId=" + message.castId
            + " skillId=" + message.skillId
            + " casterId=" + message.casterId
            + " minElapsedSeconds=" + elapsed
            + " timeSource=" + (hasTiming ? "server_lower_bound" : "receive_only")
            + " position=" + instance.transform.position);
    }

    private void OnDestroy()
    {
        ClearAllRemotePlayers();
    }

    private void HandlePlayerStateBroadcast(string json)
    {
        PlayerStateBroadcast msg = JsonUtility.FromJson<PlayerStateBroadcast>(json);
        ApplyRemotePlayerState(msg == null ? null : msg.player);
    }

    public void SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode mode)
    {
        if(snapshotRoutingMode==mode)return;
        RemoveMessageRegistration();snapshotRoutingMode=mode;
        foreach (RemotePlayerSync remotePlayer in remotePlayers.Values)
            if (remotePlayer != null)
                remotePlayer.SetAttackEventRoutingEnabled(
                    mode == BattleSyncSnapshotRoutingMode.CoordinatorOwned);
        if(isActiveAndEnabled)EnsureMessageRegistration();
    }

    private void HandlePlayerEnterBroadcast(string json)
    {
        PlayerEnterBroadcast msg = JsonUtility.FromJson<PlayerEnterBroadcast>(json);
        ApplyRemotePlayerEnter(msg == null ? null : msg.player, msg == null ? 0 : msg.serverTime);
    }

    private void HandlePlayerLeave(string json)
    {
        RemoveRemotePlayerByLeaveJson(json);
    }

    public void ApplyWorldSnapshotJson(string json)
    {
        WorldSnapshotMessage snapshot = JsonUtility.FromJson<WorldSnapshotMessage>(json);

        if (snapshot == null || snapshot.players == null)
        {
            return;
        }

        for (int i = 0; i < snapshot.players.Length; i++)
        {
            ApplyRemotePlayerState(snapshot.players[i]);
        }
    }

    public void ApplyEnterWorldResultJson(string json)
    {
        EnterWorldResult result = JsonUtility.FromJson<EnterWorldResult>(json);
        if (result == null)
        {
            return;
        }

        ClearAllRemotePlayers();
        playerLeaveTimes.Clear();

        long resultCharacterDbId = result.characterDbId > 0
            ? result.characterDbId
            : (result.self == null ? 0 : result.self.characterDbId);
        SetLocalCharacterDbId(resultCharacterDbId);
        activeSceneId = !string.IsNullOrEmpty(result.sceneId)
            ? result.sceneId
            : (result.self == null ? "" : result.self.sceneId);
        NetworkManager network = NetworkManager.Instance;
        activeConnectionEpoch = network == null ? 0 : network.ConnectionEpoch;
        remoteSkillCastInbox.BeginSession(activeConnectionEpoch,
            activeSceneId, resultCharacterDbId);
        remoteSkillTimeline.BeginSession(activeConnectionEpoch,
            result.serverTime, Time.realtimeSinceStartupAsDouble);
        ResolveCombatStateStore();
        combatStateStore?.BeginScene(activeSceneId);

        if (result.visiblePlayers == null)
        {
            return;
        }

        for (int index = 0; index < result.visiblePlayers.Length; index++)
        {
            ApplyRemotePlayerEnter(result.visiblePlayers[index], result.serverTime);
        }
    }

    public void SetLocalCharacterDbId(long characterDbId)
    {
        localCharacterDbId = characterDbId;
    }

    internal bool HasPlayerView(long characterDbId) => characterDbId == localCharacterDbId
        || (remotePlayers.TryGetValue(characterDbId, out RemotePlayerSync player)
            && player != null && player.gameObject.activeInHierarchy);

    public bool TryPlayAttack(long characterDbId, long actionSequence, long eventId)
    {
        if (characterDbId <= 0) return false;
        if (characterDbId == localCharacterDbId) return true;
        return remotePlayers.TryGetValue(characterDbId, out RemotePlayerSync remotePlayer)
            && remotePlayer != null && remotePlayer.TryPlayAttack(actionSequence, eventId);
    }

    public BattleSyncApplyResult BeginSession(BattleSyncModuleSessionContext context)
    {
        if (!context.IsValid) return BattleSyncApplyResult.InvalidMessage;
        activeConnectionEpoch = context.ConnectionEpoch;
        activeSceneId = context.SceneId;
        localCharacterDbId = context.CharacterDbId;
        remoteSkillCastInbox.BeginSession(context.ConnectionEpoch,
            context.SceneId, context.CharacterDbId);
        remoteSkillTimeline.BeginSession(context.ConnectionEpoch,
            0, Time.realtimeSinceStartupAsDouble);
        ResolveCombatStateStore();
        combatStateStore?.BeginScene(activeSceneId);
        ClearAllRemotePlayers();
        playerLeaveTimes.Clear();
        return BattleSyncApplyResult.Applied;
    }

    public void EndSession(long connectionEpoch)
    {
        if (connectionEpoch != activeConnectionEpoch) return;
        ClearAllRemotePlayers();
        playerLeaveTimes.Clear();
        remoteSkillCastInbox.EndSession(connectionEpoch);
        remoteSkillTimeline.EndSession(connectionEpoch);
        activeConnectionEpoch = 0;
        activeSceneId = string.Empty;
    }

    public BattleSyncApplyResult ApplyWorldSnapshot(WorldSnapshotMessageDto message)
    {
        if (message == null) return BattleSyncApplyResult.InvalidMessage;
        if (!string.Equals(activeSceneId, message.sceneId, System.StringComparison.Ordinal))
            return BattleSyncApplyResult.IgnoredWrongScene;

        PlayerStateDto[] players = message.players ?? System.Array.Empty<PlayerStateDto>();
        HashSet<long> ids = new HashSet<long>();
        HashSet<long> unavailablePrefabIds = new HashSet<long>();
        for (int i = 0; i < players.Length; i++)
        {
            PlayerStateDto item = players[i];
            if (item.characterDbId == localCharacterDbId) continue;

            if (!ids.Add(item.characterDbId))
            {
                Debug.LogError(
                    "[CLIENT] WORLD_SNAPSHOT duplicate player"
                    + " characterDbId=" + item.characterDbId
                    + " characterId=" + item.characterId
                    + " sceneId=" + message.sceneId
                    + " revision=" + message.snapshotRevision);
                return BattleSyncApplyResult.InvalidMessage;
            }

            if (FindRemotePlayerPrefab(item.characterId) == null)
            {
                unavailablePrefabIds.Add(item.characterDbId);
                Debug.LogError(
                    "[CLIENT] WORLD_SNAPSHOT remote prefab unavailable"
                    + " index=" + i
                    + " characterDbId=" + item.characterDbId
                    + " characterId=" + item.characterId
                    + " sceneId=" + message.sceneId
                    + " revision=" + message.snapshotRevision);
            }
        }

        bool requireViews = WorldEntryLoadingView.ActiveWorldEntry != null;
        if (requireViews && unavailablePrefabIds.Count != 0)
            return BattleSyncApplyResult.RetryableFailure;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStateDto item = players[i];
            if (item.characterDbId != localCharacterDbId
                && !unavailablePrefabIds.Contains(item.characterDbId))
                ApplyRemotePlayerEnter(ConvertPlayer(item), message.serverTime);
            if (requireViews && !HasPlayerView(item.characterDbId))
                return BattleSyncApplyResult.RetryableFailure;
        }

        List<long> removeIds = new List<long>();
        foreach (long id in remotePlayers.Keys) if (!ids.Contains(id)) removeIds.Add(id);
        for (int i = 0; i < removeIds.Count; i++) RemoveRemotePlayer(removeIds[i], "world_snapshot");
        return BattleSyncApplyResult.Applied;
    }

    public BattleSyncApplyResult ApplyPlayerEnter(PlayerEnterMessageDto message)
    {
        if(message==null||message.player==null)return BattleSyncApplyResult.InvalidMessage;
        if(!string.Equals(activeSceneId,message.sceneId,System.StringComparison.Ordinal))return BattleSyncApplyResult.IgnoredWrongScene;
        if(message.player.characterDbId==localCharacterDbId)return BattleSyncApplyResult.AlreadyApplied;
        PlayerNetworkState state=ConvertPlayer(message.player);
        if (message.visibilityRevision > 0) state.visibilityRevision = message.visibilityRevision;
        if(FindRemotePlayerPrefab(state.characterId)==null)
        {
            Debug.LogError(
                "[CLIENT] PLAYER_ENTER ignored"
                + " reason=remote_prefab_unavailable"
                + " characterDbId=" + state.characterDbId
                + " characterId=" + state.characterId
                + " sceneId=" + state.sceneId);
            return BattleSyncApplyResult.AlreadyApplied;
        }
        ApplyRemotePlayerEnter(state,message.serverTime);
        return remotePlayers.ContainsKey(state.characterDbId)?BattleSyncApplyResult.Applied:BattleSyncApplyResult.RetryableFailure;
    }

    public BattleSyncApplyResult ApplyPlayerLeave(PlayerLeaveMessageDto message)
    {
        if(message==null||message.characterDbId<=0)return BattleSyncApplyResult.InvalidMessage;
        if(!string.Equals(activeSceneId,message.sceneId,System.StringComparison.Ordinal))return BattleSyncApplyResult.IgnoredWrongScene;
        if(message.characterDbId==localCharacterDbId)return BattleSyncApplyResult.AlreadyApplied;
        pendingMovement.Remove(message.characterDbId);
        if(!remotePlayers.ContainsKey(message.characterDbId))return BattleSyncApplyResult.AlreadyApplied;
        RemoveRemotePlayer(message.characterDbId,message.reason);
        return remotePlayers.ContainsKey(message.characterDbId)?BattleSyncApplyResult.RetryableFailure:BattleSyncApplyResult.Applied;
    }

    public BattleSyncApplyResult ApplyPlayerDynamicState(DecodedPlayerState state,long serverTick,long serverTime)
    {
        if(!long.TryParse(state.EntityId,out long characterDbId)||characterDbId<=0)return BattleSyncApplyResult.InvalidMessage;
        if(characterDbId==localCharacterDbId)return BattleSyncApplyResult.AlreadyApplied;
        if(!remotePlayers.TryGetValue(characterDbId,out RemotePlayerSync remotePlayer)||remotePlayer==null)
            return BattleSyncApplyResult.RetryableFailure;
        return remotePlayer.ApplyDynamicState(state,serverTick)?BattleSyncApplyResult.Applied:BattleSyncApplyResult.IgnoredStale;
    }

    private static PlayerNetworkState ConvertPlayer(PlayerStateDto item)
    {
        return new PlayerNetworkState
        {
            userId = item.userId, username = item.username, characterDbId = item.characterDbId,
            characterId = item.characterId, roleId = item.roleId, nickname = item.nickname,
            sceneId = item.sceneId, x = item.x, y = item.y, z = item.z, rotY = item.rotY,
            moveSpeed = item.moveSpeed, animation = item.animation, sequence = item.sequence,
            time = item.time, serverTime = item.serverTime, velocityX = item.velocityX,
            velocityY = item.velocityY, velocityZ = item.velocityZ, grounded = item.grounded,
            movementState = item.movementState, serverTick = item.serverTick,
            lastProcessedInputSequence = item.lastProcessedInputSequence, hp = item.hp,
            maxHp = item.maxHp, lifeState = item.lifeState, combatRevision = item.combatRevision,
            visibilityRevision = item.visibilityRevision, netId = item.netId, movement = item.movement
        };
    }

    public void RemoveRemotePlayerByLeaveJson(string json)
    {
        PlayerLeaveMessage msg = JsonUtility.FromJson<PlayerLeaveMessage>(json);

        if (msg == null || msg.characterDbId <= 0)
        {
            return;
        }

        Debug.Log(
            "[CLIENT] PLAYER_LEAVE received characterDbId=" + msg.characterDbId
            + " reason=" + msg.reason);

        playerLeaveTimes[msg.characterDbId] = msg.serverTime;
        RemoveRemotePlayer(msg.characterDbId, msg.reason);
    }

    public void SpawnOrUpdateRemotePlayer(PlayerNetworkState state)
    {
        ApplyRemotePlayerEnter(state, state == null ? 0 : state.serverTime);
    }

    private void ApplyRemotePlayerEnter(PlayerNetworkState state, long enterServerTime)
    {
        if (state == null || state.characterDbId <= 0)
        {
            return;
        }

        if (state.characterDbId == localCharacterDbId)
        {
            return;
        }

        if (!IsActiveScene(state.sceneId))
        {
            return;
        }

        if (playerLeaveTimes.TryGetValue(state.characterDbId, out long leaveServerTime)
            && enterServerTime < leaveServerTime)
        {
            Debug.Log(
                "[CLIENT] PLAYER_ENTER dropped stale characterDbId=" + state.characterDbId
                + " enterServerTime=" + enterServerTime
                + " leaveServerTime=" + leaveServerTime);
            return;
        }

        playerLeaveTimes.Remove(state.characterDbId);
        MergeRemotePlayerCombatState(state);
        RemotePlayerSync remotePlayer = GetOrCreateRemotePlayer(state);

        if (remotePlayer != null)
        {
            remotePlayer.Init(state.characterDbId, state.characterId, state.nickname);
            remotePlayer.ClearPendingSnapshots();
            remotePlayer.ApplyServerState(state);
            if (pendingMovement.TryGetValue(state.characterDbId, out var pending)
                && pending.visibilityRevision <= state.visibilityRevision)
            {
                pendingMovement.Remove(state.characterDbId);
                if (pending.visibilityRevision == state.visibilityRevision)
                    remotePlayer.ApplyMovementEvent(pending.movement);
            }
        }
    }

    private void HandleMovementEvent(string json)
    {
        var message = JsonUtility.FromJson<PlayerMovementEventMessage>(json);
        if (message?.movement?.IsValid != true || message.characterDbId <= 0
            || message.characterDbId == localCharacterDbId || !IsActiveScene(message.sceneId)
            || string.IsNullOrEmpty(message.sceneSessionId)
            || message.sceneSessionId != NetworkManager.Instance?.SceneSessionId)
            return;

        if (remotePlayers.TryGetValue(message.characterDbId, out var player) && player != null)
        {
            if (message.visibilityRevision == player.VisibilityRevision)
            {
                player.ApplyMovementEvent(message.movement);
                return;
            }
            if (message.visibilityRevision < player.VisibilityRevision) return;
        }

        if (pendingMovement.TryGetValue(message.characterDbId, out var previous))
        {
            if (message.visibilityRevision < previous.visibilityRevision
                || (message.visibilityRevision == previous.visibilityRevision
                    && message.movement.revision <= previous.movement.revision)) return;
        }
        else if (pendingMovement.Count >= 512) return;
        pendingMovement[message.characterDbId] = message;
    }

    private void ApplyRemotePlayerState(PlayerNetworkState state)
    {
        if (state == null
            || state.characterDbId <= 0
            || state.characterDbId == localCharacterDbId
            || !IsActiveScene(state.sceneId))
        {
            return;
        }

        if (!remotePlayers.TryGetValue(state.characterDbId, out RemotePlayerSync remotePlayer)
            || remotePlayer == null)
        {
            return;
        }

        if (remotePlayer.ApplyServerState(state))
        {
            Debug.Log(
                "[CLIENT] PLAYER_STATE applied characterDbId=" + state.characterDbId
                + " serverTick=" + state.serverTick);
        }
    }

    public void RemoveRemotePlayer(long characterDbId)
    {
        RemoveRemotePlayer(characterDbId, "");
    }

    private void RemoveRemotePlayer(long characterDbId, string reason)
    {
        if (characterDbId <= 0)
        {
            return;
        }

        if (characterDbId == localCharacterDbId)
        {
            Debug.LogWarning(
                "[CLIENT] PLAYER_LEAVE ignored local player characterDbId=" + characterDbId
                + " reason=" + reason);
            return;
        }

        remoteSkillEffects.RemoveCaster(characterDbId);
        if (!remotePlayers.TryGetValue(characterDbId, out RemotePlayerSync remotePlayer))
        {
            Debug.Log(
                "[CLIENT] PLAYER_LEAVE ignored remote player not found characterDbId=" + characterDbId
                + " reason=" + reason);
            return;
        }

        remotePlayers.Remove(characterDbId);

        if (remotePlayer != null)
        {
            remotePlayer.StopNetworkInterpolation();
            remotePlayer.ClearPendingSnapshots();
            remotePlayer.enabled = false;
            Destroy(remotePlayer.gameObject);
        }

        Debug.Log(
            "[CLIENT] PLAYER_LEAVE removed characterDbId=" + characterDbId
            + " remoteCount=" + remotePlayers.Count
            + " rootDestroyed=" + (remotePlayer != null));
    }

    public void ClearAllRemotePlayers()
    {
        pendingMovement.Clear();
        remoteSkillEffects.Clear();
        foreach (RemotePlayerSync remotePlayer in remotePlayers.Values)
        {
            if (remotePlayer != null)
            {
                remotePlayer.StopNetworkInterpolation();
                remotePlayer.ClearPendingSnapshots();
                remotePlayer.enabled = false;
                Destroy(remotePlayer.gameObject);
            }
        }

        remotePlayers.Clear();
    }

    private RemotePlayerSync GetOrCreateRemotePlayer(PlayerNetworkState state)
    {
        if (remotePlayers.TryGetValue(state.characterDbId, out RemotePlayerSync remotePlayer) && remotePlayer != null)
        {
            return remotePlayer;
        }

        GameObject remotePlayerPrefab = FindRemotePlayerPrefab(state.characterId);
        if (remotePlayerPrefab == null)
        {
            Debug.LogError("EntityManager create remote player failed: no prefab configured for characterId=" + state.characterId);
            return null;
        }

        Vector3 spawnPosition = state.GetPosition();
        Quaternion spawnRotation = state.GetRotation();

        Transform parent = remotePlayerRoot == null ? transform : remotePlayerRoot;
        GameObject playerObject = new GameObject("RemotePlayer");
        playerObject.SetActive(false);
        Transform playerRoot = playerObject.transform;
        playerRoot.SetParent(parent, false);
        playerRoot.SetPositionAndRotation(spawnPosition, spawnRotation);

        GameObject visual = Instantiate(remotePlayerPrefab, playerRoot, false);
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        Transform skillReleasePoint = new GameObject("施法挂点").transform;
        skillReleasePoint.SetParent(playerRoot, false);
        skillReleasePoint.localPosition = playerPresentationSettings != null
            ? playerPresentationSettings.SkillReleaseOffset
            : PlayerPresentationSettings.DefaultSkillReleaseOffset;

        remotePlayer = playerObject.AddComponent<RemotePlayerSync>();
        remotePlayer.BindPresentation(visual, skillReleasePoint);
        remotePlayer.Init(state.characterDbId, state.characterId, state.nickname);
        remotePlayer.SetAttackEventRoutingEnabled(
            snapshotRoutingMode == BattleSyncSnapshotRoutingMode.CoordinatorOwned);
        PlayerEffectPresenter.Bind(playerObject, playerPresentationSettings, playerRoot, skillReleasePoint, visual);
        playerObject.SetActive(true);
        CreateRemotePlayerHealthBar(playerRoot, state.characterDbId);
        remotePlayers[state.characterDbId] = remotePlayer;
        if (combatStateStore != null && combatStateStore.TryGetState("player",
            state.characterDbId.ToString(CultureInfo.InvariantCulture), out CombatStateData life))
            remotePlayer.ApplyCombatLife(life, alreadyDeadAtSpawn: true);

        Debug.Log(
            "[CLIENT] PLAYER_ENTER created characterDbId=" + state.characterDbId
            + " characterId=" + state.characterId
            + " prefab=" + remotePlayerPrefab.name
            + " position=" + spawnPosition
            + " sceneId=" + state.sceneId
            + " remoteCount=" + remotePlayers.Count);

        return remotePlayer;
    }

    private void MergeRemotePlayerCombatState(PlayerNetworkState state)
    {
        if (state == null || state.characterDbId <= 0 || (state.maxHp <= 0
            && !string.Equals(state.lifeState, "Dead", System.StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ResolveCombatStateStore();

        if (combatStateStore == null)
        {
            return;
        }

        string sceneId = string.IsNullOrEmpty(state.sceneId) ? activeSceneId : state.sceneId;
        combatStateStore.BeginScene(sceneId);
        combatStateStore.MergeState(sceneId, new CombatStateMessage
        {
            entity = new CombatEntityMessage
            {
                entityType = "player",
                entityId = state.characterDbId.ToString(CultureInfo.InvariantCulture)
            },
            hp = state.hp,
            maxHp = state.maxHp,
            lifeState = state.lifeState,
            combatRevision = state.combatRevision,
            spawnGeneration = 0
        });
    }

    private void CreateRemotePlayerHealthBar(Transform remotePlayerTransform, long characterDbId)
    {
        if (remotePlayerHealthBarPrefab == null || remotePlayerTransform == null || characterDbId <= 0)
        {
            return;
        }

        GameObject healthBarObject = Instantiate(remotePlayerHealthBarPrefab, remotePlayerTransform);
        healthBarObject.transform.localPosition = remotePlayerHealthBarOffset;
        healthBarObject.transform.localRotation = Quaternion.identity;

        RemotePlayerHealthBarPresenter presenter =
            healthBarObject.GetComponent<RemotePlayerHealthBarPresenter>();

        if (presenter == null)
        {
            presenter = healthBarObject.AddComponent<RemotePlayerHealthBarPresenter>();
        }

        ResolveCombatStateStore();
        presenter.Bind(combatStateStore, characterDbId);
    }

    private void ResolveCombatStateStore()
    {
        if (combatStateStore == null)
            combatStateStore = FindObjectOfType<CombatStateStore>();
        if (!isActiveAndEnabled || combatStateStore == subscribedCombatStateStore)
            return;

        if (subscribedCombatStateStore != null)
            subscribedCombatStateStore.StateChanged -= OnPlayerCombatStateChanged;
        subscribedCombatStateStore = combatStateStore;
        if (subscribedCombatStateStore != null)
            subscribedCombatStateStore.StateChanged += OnPlayerCombatStateChanged;
    }

    private void OnPlayerCombatStateChanged(CombatEntityKey key, CombatStateData state)
    {
        if (key.EntityType != "player" || state == null
            || !long.TryParse(key.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out long id)
            || !remotePlayers.TryGetValue(id, out RemotePlayerSync remotePlayer) || remotePlayer == null)
            return;

        bool wasDead = remotePlayer.IsDead;
        remotePlayer.ApplyCombatLife(state);
        if (!wasDead && remotePlayer.IsDead)
            remoteSkillEffects.RemoveCaster(id);
    }

    private bool IsActiveScene(string stateSceneId)
    {
        return string.IsNullOrEmpty(activeSceneId)
            || string.IsNullOrEmpty(stateSceneId)
            || string.Equals(activeSceneId, stateSceneId, System.StringComparison.Ordinal);
    }

    private GameObject FindRemotePlayerPrefab(string characterId)
    {
        if (remoteCharacterPrefabs != null)
        {
            for (int index = 0; index < remoteCharacterPrefabs.Length; index++)
            {
                RemoteCharacterPrefabSlot slot = remoteCharacterPrefabs[index];
                if (slot == null
                    || !string.Equals(
                        slot.characterId,
                        characterId,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (slot.prefab != null)
                {
                    return slot.prefab;
                }

                Debug.LogError(
                    "[CLIENT] REMOTE_PREFAB slot prefab is null"
                    + " index=" + index
                    + " characterId=" + characterId);
                break;
            }
        }

        if (fallbackRemotePlayerPrefab != null)
        {
            Debug.LogWarning(
                "[CLIENT] REMOTE_PREFAB fallback used"
                + " characterId=" + characterId
                + " prefab=" + fallbackRemotePlayerPrefab.name);
            return fallbackRemotePlayerPrefab;
        }

        return null;
    }

    private void OnValidate()
    {
        if (remoteCharacterPrefabs == null)
        {
            return;
        }

        HashSet<string> characterIds = new HashSet<string>();
        for (int index = 0; index < remoteCharacterPrefabs.Length; index++)
        {
            RemoteCharacterPrefabSlot slot = remoteCharacterPrefabs[index];
            if (slot == null)
            {
                Debug.LogWarning(
                    "[CLIENT] REMOTE_PREFAB null slot index=" + index,
                    this);
                continue;
            }

            if (string.IsNullOrWhiteSpace(slot.characterId))
            {
                Debug.LogWarning(
                    "[CLIENT] REMOTE_PREFAB characterId is empty index=" + index,
                    this);
            }
            else if (!characterIds.Add(slot.characterId))
            {
                Debug.LogWarning(
                    "[CLIENT] REMOTE_PREFAB duplicate characterId=" + slot.characterId,
                    this);
            }

            if (slot.prefab == null)
            {
                Debug.LogWarning(
                    "[CLIENT] REMOTE_PREFAB prefab is null"
                    + " index=" + index
                    + " characterId=" + slot.characterId,
                    this);
            }
        }
    }

}

[System.Serializable]
public class RemoteCharacterPrefabSlot
{
    public string characterId;
    public GameObject prefab;
}
