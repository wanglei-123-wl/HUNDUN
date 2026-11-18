using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Game.Client.BattleSync
{
    [DefaultExecutionOrder(-150)]
    public sealed class BattleSyncRuntime : MonoBehaviour
    {
        [Header("Core References")]
        [SerializeField]
        private NetworkManager networkManager;

        [SerializeField]
        private NetMessageDispatcher messageDispatcher;

        [Header("Snapshot Modules")]
        [SerializeField] private EntityManager playerSnapshotModule;
        [SerializeField] private MonsterSnapshotManager monsterSnapshotModule;
        [SerializeField] private LootModuleRuntime lootSnapshotModule;
        [SerializeField] private CombatMessageReceiver combatSnapshotModule;
        [SerializeField] private Game.Inventory.InventoryModuleRuntime inventorySnapshotModule;

        [Header("Lifecycle")]
        [SerializeField]
        private bool initializeOnAwake = true;

        [SerializeField]
        private bool enableLifecycleTracking = true;

        [Header("Initialization")]
        [SerializeField, Min(1f)]
        private float initialSnapshotTimeoutSeconds = 10f;

        [Header("Reliable Messages")]
        [SerializeField, Min(128)]
        private int reliableRecordCapacity = 8192;

        [SerializeField, Min(32)]
        private int pendingAckCapacity = 2048;

        [SerializeField, Min(0.05f)]
        private float ackInitialRetrySeconds = 0.25f;

        [SerializeField, Min(0.25f)]
        private float ackMaximumRetrySeconds = 2f;

        [Header("Development Diagnostics")]
        [SerializeField]
        private bool logLifecycleTransitions = true;

        [SerializeField]
        private bool enableBattleSyncDiagnostics = true;

        [SerializeField]
        private bool enableBattleSyncFileDiagnostics = true;

        private BattleSyncCoordinator coordinator;
        private BattleSyncSessionState sessionState;
        private InitialSnapshotBarrier snapshotBarrier;
        private BattleEntityRegistry entityRegistry;
        private IBattleSyncClock clock;
        private ReliableAckService ackService;
        private ReliableMessageProcessor reliableMessageProcessor;
        private SnapshotRepairService snapshotRepairService;
        private CompactStateDecoder compactStateDecoder;
        private StateFrameProcessor stateFrameProcessor;

        private bool initialized;
        private bool networkEventsSubscribed;
        private bool timeoutLogged;

        public event Action<BattleSyncReadyContext> Ready;
        public event Action<InitialSnapshotKind> RepairRecoveryRequired;
        public bool OwnsSnapshotRouting => initialized && enableLifecycleTracking;

        public bool IsInitialized => initialized;

        public BattleSyncCoordinator Coordinator => coordinator;

        public BattleSyncSessionState SessionState => sessionState;

        public InitialSnapshotBarrier SnapshotBarrier => snapshotBarrier;

        public BattleEntityRegistry EntityRegistry => entityRegistry;

        private void Awake()
        {
            if (initializeOnAwake)
            {
                Initialize();
            }
        }

        private void OnDestroy()
        {
            Shutdown(false);
        }

        private void Update()
        {
            if (!initialized) return;

            if (ackService != null && clock != null)
            {
                ackService.Tick(clock.NowMilliseconds);
            }
            snapshotRepairService?.Update();

            if (coordinator == null || timeoutLogged) return;
            long timeoutMs=(long)(initialSnapshotTimeoutSeconds*1000f);
            if (coordinator.CheckInitializationTimeout(timeoutMs))
            {
                timeoutLogged=true;
                Debug.LogError("[BATTLE_SYNC][SNAPSHOT_TIMEOUT] missing=" + snapshotBarrier.MissingKinds, this);
            }
        }

        public bool Initialize()
        {
            if (initialized)
            {
                return true;
            }

            BattleSyncDiagnosticsLogger.Configure(
                enableBattleSyncDiagnostics
                && enableBattleSyncFileDiagnostics);
            if (BattleSyncDiagnosticsLogger.Enabled)
            {
                Debug.Log(
                    "[BATTLE_SYNC][DIAGNOSTICS] file="
                    + BattleSyncDiagnosticsLogger.LogPath,
                    this);
            }

            if (!enableLifecycleTracking)
            {
                return true;
            }

            ResolveReferences();

            if (networkManager == null || messageDispatcher == null
                || playerSnapshotModule == null || monsterSnapshotModule == null
                || lootSnapshotModule == null || combatSnapshotModule == null
                || inventorySnapshotModule == null)
            {
                Debug.LogError(
                    "[BATTLE_SYNC][RUNTIME] initialization failed: all explicit transport, router and snapshot module references are required.",
                    this);
                return false;
            }

            try
            {
                clock = new UnityRealtimeClock();
                IBattleSyncJsonCodec jsonCodec = new UnityJsonCodec();
                sessionState = new BattleSyncSessionState();
                snapshotBarrier = new InitialSnapshotBarrier(InitialSnapshotKind.World);
                entityRegistry = new BattleEntityRegistry();

                SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode.CoordinatorOwned);
                BattleSyncModuleBindings bindings = new BattleSyncModuleBindings(
                    playerSnapshotModule, monsterSnapshotModule, lootSnapshotModule,
                    combatSnapshotModule, inventorySnapshotModule);

                ackService = new ReliableAckService(
                    networkManager,
                    jsonCodec,
                    pendingAckCapacity,
                    SecondsToMilliseconds(ackInitialRetrySeconds),
                    SecondsToMilliseconds(ackMaximumRetrySeconds));
                reliableMessageProcessor = new ReliableMessageProcessor(
                    ackService,
                    reliableRecordCapacity);
                snapshotRepairService=new SnapshotRepairService(networkManager,jsonCodec,clock,
                    ()=>networkManager.SceneOperationsPaused ? null : networkManager.SceneSessionId);
                snapshotRepairService.RepairQueued+=HandleSnapshotRepairQueued;
                snapshotRepairService.RepairAttemptsExhausted+=HandleRepairAttemptsExhausted;
                inventorySnapshotModule.InventoryMismatchDetected+=HandleInventoryMismatchDetected;
                monsterSnapshotModule.CombatRevisionGapDetected+=HandleMonsterCombatRevisionGapDetected;
                monsterSnapshotModule.MonsterSnapshotRepairRequested+=HandleMonsterSnapshotRepairRequested;
                combatSnapshotModule.CombatRevisionGapDetected+=HandleCombatRevisionGapDetected;
                compactStateDecoder=new CompactStateDecoder();
                stateFrameProcessor=new StateFrameProcessor(jsonCodec,compactStateDecoder,entityRegistry,
                    playerSnapshotModule,monsterSnapshotModule,snapshotRepairService,clock,
                    enableBattleSyncDiagnostics);

                coordinator = new BattleSyncCoordinator(
                    networkManager,
                    messageDispatcher,
                    clock,
                    jsonCodec,
                    sessionState,
                    snapshotBarrier,
                    entityRegistry,
                    bindings,
                    reliableMessageProcessor,
                    ackService,
                    snapshotRepairService,
                    stateFrameProcessor,
                    enableBattleSyncDiagnostics);

                BattleSyncCoordinatorResult result =
                    coordinator.Initialize();
                coordinator.Ready += HandleReady;

                if (!result.IsSuccess)
                {
                    Debug.LogError(
                        "[BATTLE_SYNC][RUNTIME] coordinator initialization failed: "
                        + result.Detail,
                        this);
                    ClearRuntimeObjects();
                    return false;
                }

                combatSnapshotModule.CombatEventApplied += HandleCombatEventApplied;
                initialized = true;
                SubscribeNetworkEvents();

                if (networkManager.IsConnected
                    && networkManager.ConnectionEpoch > 0)
                {
                    LogResult(
                        coordinator.HandleNetworkConnected(
                            networkManager.ConnectionEpoch));
                }

                if (logLifecycleTransitions)
                {
                    Debug.Log("[BATTLE_SYNC][RUNTIME] initialized", this);
                }

                return true;
            }
            catch (Exception exception)
            {
                UnsubscribeNetworkEvents();
                SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode.LegacySelfRegistered);
                ClearRuntimeObjects();
                initialized = false;
                BattleSyncDiagnosticsLogger.Close();

                Debug.LogError(
                    "[BATTLE_SYNC][RUNTIME] initialization exception: "
                    + exception.Message,
                    this);
                return false;
            }
        }

        private void ResolveReferences()
        {
            if(networkManager==null)
                networkManager=NetworkManager.Instance??FindObjectOfType<NetworkManager>(true);
            if(messageDispatcher==null)
                messageDispatcher=NetMessageDispatcher.Instance??FindObjectOfType<NetMessageDispatcher>(true);
            if(playerSnapshotModule==null)
                playerSnapshotModule=FindObjectOfType<EntityManager>(true);
            if(monsterSnapshotModule==null)
                monsterSnapshotModule=FindObjectOfType<MonsterSnapshotManager>(true);
            if(lootSnapshotModule==null)
                lootSnapshotModule=FindObjectOfType<LootModuleRuntime>(true);
            if(combatSnapshotModule==null)
                combatSnapshotModule=FindObjectOfType<CombatMessageReceiver>(true);
            if(inventorySnapshotModule==null)
                inventorySnapshotModule=FindObjectOfType<Game.Inventory.InventoryModuleRuntime>(true);
        }

        public void NotifyEnterWorldSent(long connectionEpoch)
        {
            if (!initialized || coordinator == null)
            {
                return;
            }

            if (!EnsureConnectionObserved(connectionEpoch))
            {
                return;
            }

            LogResult(
                coordinator.HandleEnterWorldSent(
                    connectionEpoch));
        }

        public void NotifyEnterWorldAccepted(
            BattleSyncEnterWorldContext context, bool trackEntrySnapshots = false)
        {
            if (!initialized || coordinator == null)
            {
                return;
            }

            if (!EnsureConnectionObserved(context.ConnectionEpoch))
            {
                return;
            }

            LogResult(
                coordinator.HandleEnterWorldAccepted(
                    context, trackEntrySnapshots));
        }

        public bool BeginMapTransfer(BattleSyncEnterWorldContext context)
        {
            if (!initialized || coordinator == null || networkManager == null
                || !networkManager.IsConnected)
                return false;
            BattleSyncCoordinatorResult result =
                coordinator.HandleMapTransferAccepted(context);
            LogResult(result);
            timeoutLogged = false;
            return result.IsSuccess;
        }

        public void NotifyInitialWorldSeedApplied(long connectionEpoch, string sceneId,
            long snapshotRevision, long serverTime, string reason,
            PlayerNetworkState[] visiblePlayers)
        {
            if (!initialized || coordinator == null) return;
            bool viewsApplied = visiblePlayers != null && playerSnapshotModule != null;
            if (viewsApplied)
            {
                foreach (PlayerNetworkState player in visiblePlayers)
                {
                    if (player != null && player.characterDbId > 0
                        && string.Equals(player.sceneId, sceneId, StringComparison.Ordinal)
                        && playerSnapshotModule.HasPlayerView(player.characterDbId))
                        continue;
                    viewsApplied = false;
                    break;
                }
            }
            LogResult(coordinator.NotifyInitialWorldSeedApplied(connectionEpoch, sceneId,
                snapshotRevision, serverTime, reason, viewsApplied));
        }

        public InitialSnapshotKind GetMissingEntrySnapshots(long epoch, string sceneId) =>
            coordinator?.GetMissingEntrySnapshots(epoch, sceneId) ?? BattleSyncCoordinator.EntrySnapshotKinds;

        public bool RequestMissingEntrySnapshots(long epoch, string sceneId) =>
            coordinator?.RequestMissingEntrySnapshots(epoch, sceneId) == true;

        public void FinishEntrySnapshotTracking() => coordinator?.FinishEntrySnapshotTracking();

        public bool SeedInitialVisiblePlayers(long connectionEpoch, string sceneId,
            long snapshotRevision, PlayerNetworkState[] visiblePlayers)
        {
            if (!initialized || coordinator == null) return false;

            List<BattleEntityRegistration> registrations =
                new List<BattleEntityRegistration>();
            PlayerNetworkState[] players = visiblePlayers ?? new PlayerNetworkState[0];
            for (int i = 0; i < players.Length; i++)
            {
                PlayerNetworkState player = players[i];
                if (player == null || player.netId <= 0 || player.characterDbId <= 0
                    || player.visibilityRevision < 0
                    || !string.Equals(player.sceneId, sceneId,
                        System.StringComparison.Ordinal)) continue;

                registrations.Add(new BattleEntityRegistration(
                    connectionEpoch, sceneId, BattleEntityKind.Player,
                    player.characterDbId.ToString(CultureInfo.InvariantCulture),
                    player.netId, player.visibilityRevision,
                    System.Math.Max(1L, player.serverTick), 0));
            }

            BattleSyncCoordinatorResult result = coordinator.SeedInitialPlayers(
                connectionEpoch, sceneId, snapshotRevision, registrations);
            LogResult(result);
            return result.IsSuccess;
        }

        public void Shutdown()
        {
            Shutdown(true);
        }

        public void Shutdown(bool restoreLegacyRouting)
        {
            UnsubscribeNetworkEvents();

            if (coordinator != null)
            {
                coordinator.Ready -= HandleReady;
                LogResult(coordinator.Shutdown());
            }

            ClearRuntimeObjects();
            initialized = false;
            timeoutLogged = false;
            if (restoreLegacyRouting)
            {
                SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode.LegacySelfRegistered);
            }

            BattleSyncDiagnosticsLogger.Close();
        }

        private void HandleNetworkConnected(long connectionEpoch)
        {
            if (!initialized || coordinator == null)
            {
                return;
            }

            if (coordinator.SessionState.IsCurrentConnection(
                    connectionEpoch))
            {
                return;
            }

            LogResult(
                coordinator.HandleNetworkConnected(
                    connectionEpoch));
        }

        private void HandleNetworkDisconnected(
            long connectionEpoch,
            string reason)
        {
            if (!initialized || coordinator == null)
            {
                return;
            }

            if (connectionEpoch == 0
                && !coordinator.SessionState.HasActiveConnection)
            {
                return;
            }

            LogResult(
                coordinator.HandleNetworkDisconnected(
                    connectionEpoch,
                    reason));
        }

        private bool EnsureConnectionObserved(long connectionEpoch)
        {
            if (coordinator.SessionState.IsCurrentConnection(
                    connectionEpoch))
            {
                return true;
            }

            BattleSyncCoordinatorResult result =
                coordinator.HandleNetworkConnected(
                    connectionEpoch);

            LogResult(result);
            return result.IsSuccess;
        }

        private void HandleReady(BattleSyncReadyContext context)
        {
            timeoutLogged=false;
            Ready?.Invoke(context);
        }

        private void HandleRepairAttemptsExhausted()
        {
            if(!initialized||coordinator==null||networkManager==null)return;
            long connectionEpoch=networkManager.ConnectionEpoch;
            if(connectionEpoch<=0||!coordinator.SessionState.IsCurrentConnection(connectionEpoch))return;
            InitialSnapshotKind missing = coordinator.GetMissingEntrySnapshots(connectionEpoch,
                coordinator.SessionState.SceneId);
            BattleSyncCoordinatorResult result=coordinator.HandleNetworkDisconnected(
                connectionEpoch,"Snapshot repair attempts exhausted.");
            LogResult(result);
            if(!result.IsSuccess)return;
            timeoutLogged=false;
            RepairRecoveryRequired?.Invoke(missing);
        }

        private void HandleSnapshotRepairQueued()
        {
            if(!initialized||coordinator==null)return;
            LogResult(coordinator.HandleSnapshotRepairRequested());
        }

        private void HandleInventoryMismatchDetected()
        {
            if(!initialized||snapshotRepairService==null||sessionState==null||!sessionState.HasActiveConnection)return;
            long snapshotRevision=Math.Max(sessionState.CommittedSnapshotRevision,sessionState.PendingSnapshotRevision);
            snapshotRepairService.RequestRepair(SnapshotRepairReason.InventoryMismatch,BattleEntityKind.Unknown,
                "characterDbId:"+sessionState.CharacterDbId,snapshotRevision);
        }

        private void HandleMonsterCombatRevisionGapDetected(string entityId)
        {
            RequestCombatRevisionRepair(BattleEntityKind.Monster,"monsterInstanceId:"+entityId);
        }

        private void HandleMonsterSnapshotRepairRequested(string entityId)
        {
            if(!initialized||snapshotRepairService==null||sessionState==null||!sessionState.HasActiveConnection)return;
            snapshotRepairService.RequestRepair(SnapshotRepairReason.SnapshotMismatch,BattleEntityKind.Monster,
                "monsterInstanceId:"+entityId,
                Math.Max(sessionState.CommittedSnapshotRevision,sessionState.PendingSnapshotRevision));
        }

        private void HandleCombatRevisionGapDetected(string entityType,string entityId)
        {
            BattleEntityKind kind=entityType==BattleSyncProtocolConstants.EntityTypes.Player
                ?BattleEntityKind.Player:BattleEntityKind.Monster;
            string prefix=kind==BattleEntityKind.Player?"characterDbId:":"monsterInstanceId:";
            RequestCombatRevisionRepair(kind,prefix+entityId);
        }

        private void HandleCombatEventApplied(CombatEventMessage message)
        {
            if (message == null || message.eventType != "attack" || message.source == null)
                return;
            try
            {
                if (message.source.entityType == BattleSyncProtocolConstants.EntityTypes.Player
                    && long.TryParse(message.source.entityId, NumberStyles.None,
                        CultureInfo.InvariantCulture, out long characterDbId))
                    playerSnapshotModule?.TryPlayAttack(
                        characterDbId, message.actionSequence, message.eventId);
                else if (message.source.entityType == BattleSyncProtocolConstants.EntityTypes.Monster)
                    monsterSnapshotModule?.TryPlayAttack(
                        message.source.entityId, message.actionSequence, message.eventId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BATTLE_SYNC][ATTACK_VISUAL] " + exception.Message, this);
            }
        }

        private void RequestCombatRevisionRepair(BattleEntityKind kind,string entityId)
        {
            if(!initialized||snapshotRepairService==null||sessionState==null||!sessionState.HasActiveConnection)return;
            long snapshotRevision=Math.Max(sessionState.CommittedSnapshotRevision,sessionState.PendingSnapshotRevision);
            snapshotRepairService.RequestRepair(SnapshotRepairReason.RevisionGap,kind,entityId,snapshotRevision);
        }

        private void SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode mode)
        {
            if (playerSnapshotModule != null)
            {
                playerSnapshotModule.SetSnapshotRoutingMode(mode);
            }
            if (monsterSnapshotModule != null)
            {
                monsterSnapshotModule.SetSnapshotRoutingMode(mode);
            }
            if (lootSnapshotModule != null)
            {
                lootSnapshotModule.SetSnapshotRoutingMode(mode);
            }
            if (combatSnapshotModule != null)
            {
                combatSnapshotModule.SetSnapshotRoutingMode(mode);
            }
            if (inventorySnapshotModule != null)
            {
                inventorySnapshotModule.SetSnapshotRoutingMode(mode);
            }
        }

        private void SubscribeNetworkEvents()
        {
            if (networkEventsSubscribed || networkManager == null)
            {
                return;
            }

            networkManager.OnConnectedWithEpoch +=
                HandleNetworkConnected;
            networkManager.OnDisconnectedWithEpoch +=
                HandleNetworkDisconnected;
            networkEventsSubscribed = true;
        }

        private void UnsubscribeNetworkEvents()
        {
            if (!networkEventsSubscribed || networkManager == null)
            {
                networkEventsSubscribed = false;
                return;
            }

            networkManager.OnConnectedWithEpoch -=
                HandleNetworkConnected;
            networkManager.OnDisconnectedWithEpoch -=
                HandleNetworkDisconnected;
            networkEventsSubscribed = false;
        }

        private void LogResult(BattleSyncCoordinatorResult result)
        {
            if (result == null)
            {
                return;
            }

            string message =
                "[BATTLE_SYNC][SESSION] operation="
                + result.Operation
                + " status="
                + result.Status
                + " epoch="
                + result.ConnectionEpoch
                + " phase="
                + result.SessionPhase
                + " detail="
                + result.Detail;

            if (!result.IsSuccess)
            {
                Debug.LogError(message, this);
                return;
            }

            if (logLifecycleTransitions)
            {
                Debug.Log(message, this);
            }
        }

        private void ClearRuntimeObjects()
        {
            if(snapshotRepairService!=null)
            {
                snapshotRepairService.RepairQueued-=HandleSnapshotRepairQueued;
                snapshotRepairService.RepairAttemptsExhausted-=HandleRepairAttemptsExhausted;
            }
            if(inventorySnapshotModule!=null)
            {
                inventorySnapshotModule.InventoryMismatchDetected-=HandleInventoryMismatchDetected;
            }
            if(monsterSnapshotModule!=null)
            {
                monsterSnapshotModule.CombatRevisionGapDetected-=HandleMonsterCombatRevisionGapDetected;
                monsterSnapshotModule.MonsterSnapshotRepairRequested-=HandleMonsterSnapshotRepairRequested;
            }
            if(combatSnapshotModule!=null)
            {
                combatSnapshotModule.CombatRevisionGapDetected-=HandleCombatRevisionGapDetected;
                combatSnapshotModule.CombatEventApplied-=HandleCombatEventApplied;
            }
            coordinator = null;
            sessionState = null;
            snapshotBarrier = null;
            entityRegistry = null;
            clock = null;
            reliableMessageProcessor = null;
            ackService = null;
            stateFrameProcessor=null;
            compactStateDecoder=null;
            snapshotRepairService=null;
            BattleSyncDiagnosticsLogger.Close();
        }

        private static long SecondsToMilliseconds(float seconds)
        {
            return (long)Math.Round(seconds * 1000.0f);
        }

        private sealed class UnityRealtimeClock
            : IBattleSyncClock
        {
            public long NowMilliseconds =>
                (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
        }

        private sealed class UnityJsonCodec : IBattleSyncJsonCodec
        {
            public bool TryDeserialize<T>(string json,out T message,out string error) where T:class
            {
                message=null;error=string.Empty;if(string.IsNullOrWhiteSpace(json)){error="JSON is empty.";return false;}
                try{message=JsonUtility.FromJson<T>(json);if(message==null){error="JsonUtility returned null.";return false;}return true;}
                catch(Exception exception){error=exception.Message;return false;}
            }
            public bool TrySerialize<T>(T message,out string json,out string error) where T:class
            {
                json=string.Empty;error=string.Empty;if(message==null){error="Message is null.";return false;}
                try{json=JsonUtility.ToJson(message);return !string.IsNullOrWhiteSpace(json);}
                catch(Exception exception){error=exception.Message;return false;}
            }
        }
    }
}
