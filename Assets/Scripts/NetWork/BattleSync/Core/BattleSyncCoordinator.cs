using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Client.BattleSync.Protocol;
using BattleCombatSnapshot = Game.Client.BattleSync.Protocol.CombatSnapshotMessageDto;
using BattleLootSnapshot = Game.Client.BattleSync.Protocol.LootSnapshotMessageDto;
using BattleLootState = Game.Client.BattleSync.Protocol.LootStateDto;
using BattleMonsterSnapshot = Game.Client.BattleSync.Protocol.MonsterSnapshotMessageDto;
using BattleMonsterState = Game.Client.BattleSync.Protocol.MonsterStateDto;
using BattlePlayerState = Game.Client.BattleSync.Protocol.PlayerStateDto;
using BattleWorldSnapshot = Game.Client.BattleSync.Protocol.WorldSnapshotMessageDto;

namespace Game.Client.BattleSync
{
    public enum BattleSyncCoordinatorStatus
    {
        Applied = 0,
        AlreadyApplied = 1,
        NotInitialized = 2,
        InvalidArguments = 3,
        SessionRejected = 4,
        RegistryRejected = 5,
        BarrierRejected = 6,
        ModuleBeginFailed = 7,
        ModuleEndFailed = 8
    }

    public sealed class BattleSyncCoordinatorResult
    {
        public BattleSyncCoordinatorStatus Status { get; }

        public string Operation { get; }

        public long ConnectionEpoch { get; }

        public BattleSyncSessionPhase SessionPhase { get; }

        public string Detail { get; }

        public bool IsSuccess =>
            Status == BattleSyncCoordinatorStatus.Applied
            || Status == BattleSyncCoordinatorStatus.AlreadyApplied;

        public BattleSyncCoordinatorResult(
            BattleSyncCoordinatorStatus status,
            string operation,
            long connectionEpoch,
            BattleSyncSessionPhase sessionPhase,
            string detail)
        {
            Status = status;
            Operation = operation ?? string.Empty;
            ConnectionEpoch = connectionEpoch;
            SessionPhase = sessionPhase;
            Detail = detail ?? string.Empty;
        }
    }

    public sealed class BattleSyncCoordinator
    {
        public const InitialSnapshotKind EntrySnapshotKinds = InitialSnapshotKind.World
            | InitialSnapshotKind.Monster | InitialSnapshotKind.Loot | InitialSnapshotKind.Combat;

        private readonly IBattleSyncTransport transport;
        private readonly IBattleSyncMessageRouter messageRouter;
        private readonly IBattleSyncClock clock;
        private readonly IBattleSyncJsonCodec jsonCodec;
        private readonly BattleSyncSessionState sessionState;
        private readonly InitialSnapshotBarrier snapshotBarrier;
        private readonly BattleEntityRegistry entityRegistry;
        private readonly BattleSyncModuleBindings moduleBindings;
        private readonly ReliableMessageProcessor reliableMessageProcessor;
        private readonly ReliableAckService reliableAckService;
        private readonly SnapshotRepairService snapshotRepairService;
        private readonly StateFrameProcessor stateFrameProcessor;

        private bool initialized;
        private bool snapshotHandlersRegistered;
        private bool mapTransferInitializing;
        private long entrySnapshotRevision;
        private InitialSnapshotKind entryAppliedSnapshots;
        private bool entryRepairInProgress;
        private readonly bool diagnosticsEnabled;
        private readonly Dictionary<string,long> diagnosticLogTimes = new Dictionary<string,long>();

        public event Action<BattleSyncReadyContext> Ready;

        public bool IsInitialized => initialized;

        public BattleSyncSessionState SessionState => sessionState;

        public InitialSnapshotBarrier SnapshotBarrier => snapshotBarrier;

        public BattleEntityRegistry EntityRegistry => entityRegistry;

        public BattleSyncCoordinator(
            IBattleSyncTransport transport,
            IBattleSyncMessageRouter messageRouter,
            IBattleSyncClock clock,
            IBattleSyncJsonCodec jsonCodec,
            BattleSyncSessionState sessionState,
            InitialSnapshotBarrier snapshotBarrier,
            BattleEntityRegistry entityRegistry,
            BattleSyncModuleBindings moduleBindings,
            ReliableMessageProcessor reliableMessageProcessor,
            ReliableAckService reliableAckService,
            SnapshotRepairService snapshotRepairService,
            StateFrameProcessor stateFrameProcessor,
            bool diagnosticsEnabled = false)
        {
            this.transport = transport
                ?? throw new ArgumentNullException(nameof(transport));
            this.messageRouter = messageRouter
                ?? throw new ArgumentNullException(nameof(messageRouter));
            this.clock = clock
                ?? throw new ArgumentNullException(nameof(clock));
            this.jsonCodec = jsonCodec
                ?? throw new ArgumentNullException(nameof(jsonCodec));
            this.sessionState = sessionState
                ?? throw new ArgumentNullException(nameof(sessionState));
            this.snapshotBarrier = snapshotBarrier
                ?? throw new ArgumentNullException(nameof(snapshotBarrier));
            this.entityRegistry = entityRegistry
                ?? throw new ArgumentNullException(nameof(entityRegistry));
            this.moduleBindings = moduleBindings
                ?? throw new ArgumentNullException(nameof(moduleBindings));
            this.reliableMessageProcessor = reliableMessageProcessor
                ?? throw new ArgumentNullException(nameof(reliableMessageProcessor));
            this.reliableAckService = reliableAckService
                ?? throw new ArgumentNullException(nameof(reliableAckService));
            this.snapshotRepairService=snapshotRepairService
                ??throw new ArgumentNullException(nameof(snapshotRepairService));
            this.stateFrameProcessor=stateFrameProcessor
                ??throw new ArgumentNullException(nameof(stateFrameProcessor));
            this.diagnosticsEnabled=diagnosticsEnabled;
        }

        public BattleSyncCoordinatorResult Initialize()
        {
            const string operation = "Initialize";

            if (initialized)
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.AlreadyApplied,
                    operation,
                    sessionState.ConnectionEpoch,
                    "Coordinator is already initialized.");
            }

            initialized = true;
            RegisterSnapshotHandlers();

            return CreateResult(
                BattleSyncCoordinatorStatus.Applied,
                operation,
                sessionState.ConnectionEpoch,
                "Coordinator initialized without registering protocol handlers.");
        }

        public BattleSyncCoordinatorResult HandleNetworkConnected(
            long connectionEpoch)
        {
            const string operation = "NetworkConnected";

            BattleSyncCoordinatorResult validation =
                ValidateInitializedAndEpoch(operation, connectionEpoch);

            if (validation != null)
            {
                return validation;
            }

            diagnosticLogTimes.Clear();
            mapTransferInitializing = false;
            BattleSyncSessionTransitionResult sessionResult =
                sessionState.BeginConnection(connectionEpoch);

            return FromSessionResult(operation, sessionResult);
        }

        public BattleSyncCoordinatorResult HandleEnterWorldSent(
            long connectionEpoch)
        {
            const string operation = "EnterWorldSent";

            BattleSyncCoordinatorResult validation =
                ValidateInitializedAndEpoch(operation, connectionEpoch);

            if (validation != null)
            {
                return validation;
            }

            BattleSyncSessionTransitionResult sessionResult =
                sessionState.MarkEnterWorldSent(connectionEpoch);

            return FromSessionResult(operation, sessionResult);
        }

        public BattleSyncCoordinatorResult HandleEnterWorldAccepted(
            BattleSyncEnterWorldContext context, bool trackEntrySnapshots = false)
        {
            const string operation = "EnterWorldAccepted";

            if (!initialized)
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.NotInitialized,
                    operation,
                    context.ConnectionEpoch,
                    "Coordinator is not initialized.");
            }

            if (!context.IsValid)
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.InvalidArguments,
                    operation,
                    context.ConnectionEpoch,
                    "Enter-world context is invalid.");
            }

            BattleSyncSessionTransitionResult sessionResult =
                sessionState.BeginInitialSnapshotBatch(
                    context.ConnectionEpoch,
                    context.SceneId,
                    context.CharacterDbId,
                    context.SnapshotRevision);

            if (!sessionResult.IsSuccess)
            {
                return FromSessionResult(operation, sessionResult);
            }

            try
            {
                entityRegistry.BeginSession(
                    context.ConnectionEpoch,
                    context.SceneId);
            }
            catch (Exception exception)
            {
                sessionState.EndConnection(context.ConnectionEpoch);

                return CreateResult(
                    BattleSyncCoordinatorStatus.RegistryRejected,
                    operation,
                    context.ConnectionEpoch,
                    "Entity registry rejected the session: "
                    + exception.Message);
            }

            long startedAtMs = clock.NowMilliseconds;
            InitialSnapshotBarrierResult barrierResult =
                snapshotBarrier.BeginBatch(
                    context.ConnectionEpoch,
                    context.SceneId,
                    context.SnapshotRevision,
                    startedAtMs, InitialSnapshotKind.World);

            if (!barrierResult.IsAccepted)
            {
                entityRegistry.EndSession(context.ConnectionEpoch);
                sessionState.EndConnection(context.ConnectionEpoch);

                return CreateResult(
                    BattleSyncCoordinatorStatus.BarrierRejected,
                    operation,
                    context.ConnectionEpoch,
                    barrierResult.Detail);
            }

            try
            {
                reliableAckService.BeginSession(
                    context.ConnectionEpoch,
                    context.SceneId);
                reliableMessageProcessor.BeginSession(
                    context.ConnectionEpoch,
                    context.SceneId);
                snapshotRepairService.BeginSession(context.ConnectionEpoch,context.SceneId,context.SnapshotRevision);
                stateFrameProcessor.BeginSession(context.ConnectionEpoch,context.SceneId,
                    context.SceneSessionId,context.MonsterSyncVersion);
            }
            catch (Exception exception)
            {
                snapshotBarrier.Invalidate(context.ConnectionEpoch);
                stateFrameProcessor.EndSession(context.ConnectionEpoch);
                reliableMessageProcessor.EndSession(context.ConnectionEpoch);
                reliableAckService.EndSession(context.ConnectionEpoch);
                snapshotRepairService.EndSession(context.ConnectionEpoch);
                entityRegistry.EndSession(context.ConnectionEpoch);
                sessionState.EndConnection(context.ConnectionEpoch);

                return CreateResult(
                    BattleSyncCoordinatorStatus.ModuleBeginFailed,
                    operation,
                    context.ConnectionEpoch,
                    "Reliable message session rejected initialization: "
                    + exception.Message);
            }

            BattleSyncModuleSessionContext moduleContext =
                new BattleSyncModuleSessionContext(
                    context.ConnectionEpoch,
                    context.SceneId,
                    context.CharacterDbId,
                    context.SnapshotRevision,
                    startedAtMs);

            if (!TryBeginModules(moduleContext, out string moduleError))
            {
                snapshotBarrier.Invalidate(context.ConnectionEpoch);
                stateFrameProcessor.EndSession(context.ConnectionEpoch);
                reliableMessageProcessor.EndSession(context.ConnectionEpoch);
                reliableAckService.EndSession(context.ConnectionEpoch);
                snapshotRepairService.EndSession(context.ConnectionEpoch);
                entityRegistry.EndSession(context.ConnectionEpoch);
                EndModules(context.ConnectionEpoch, out string cleanupError);
                sessionState.EndConnection(context.ConnectionEpoch);

                string detail = string.IsNullOrEmpty(cleanupError)
                    ? moduleError
                    : moduleError + " Cleanup: " + cleanupError;

                return CreateResult(
                    BattleSyncCoordinatorStatus.ModuleBeginFailed,
                    operation,
                    context.ConnectionEpoch,
                    detail);
            }

            entrySnapshotRevision = trackEntrySnapshots ? context.SnapshotRevision : 0;
            entryAppliedSnapshots = InitialSnapshotKind.None;
            entryRepairInProgress = false;
            return CreateResult(
                BattleSyncCoordinatorStatus.Applied,
                operation,
                context.ConnectionEpoch,
                "Enter-world lifecycle accepted; initial snapshots are pending.");
        }

        public InitialSnapshotKind GetMissingEntrySnapshots(long epoch, string sceneId)
        {
            if (entrySnapshotRevision <= 0 || !sessionState.IsCurrentConnection(epoch)
                || !sessionState.IsCurrentScene(sceneId))
                return EntrySnapshotKinds;

            long activeRevision = sessionState.IsReady ? sessionState.CommittedSnapshotRevision
                : sessionState.PendingSnapshotRevision;
            return activeRevision == entrySnapshotRevision
                ? EntrySnapshotKinds & ~entryAppliedSnapshots : EntrySnapshotKinds;
        }

        public bool RequestMissingEntrySnapshots(long epoch, string sceneId)
        {
            if (entrySnapshotRevision <= 0 || !sessionState.IsCurrentConnection(epoch)
                || !sessionState.IsCurrentScene(sceneId) || !sessionState.IsReady
                || snapshotRepairService.HasPendingRepair
                || GetMissingEntrySnapshots(epoch, sceneId) == InitialSnapshotKind.None)
                return false;

            entryRepairInProgress = true;
            entryAppliedSnapshots = InitialSnapshotKind.None;
            snapshotRepairService.RequestRepair(SnapshotRepairReason.ManualRequest,
                BattleEntityKind.Unknown, string.Empty, sessionState.CommittedSnapshotRevision);
            return true;
        }

        public void FinishEntrySnapshotTracking()
        {
            entrySnapshotRevision = 0;
            entryAppliedSnapshots = InitialSnapshotKind.None;
        }

        public BattleSyncCoordinatorResult HandleNetworkDisconnected(
            long connectionEpoch,
            string reason)
        {
            const string operation = "NetworkDisconnected";

            BattleSyncCoordinatorResult validation =
                ValidateInitializedAndEpoch(operation, connectionEpoch);

            if (validation != null)
            {
                return validation;
            }

            if (!sessionState.HasActiveConnection)
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.AlreadyApplied,
                    operation,
                    connectionEpoch,
                    "Session is already disconnected.");
            }

            if (!sessionState.IsCurrentConnection(connectionEpoch))
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.SessionRejected,
                    operation,
                    connectionEpoch,
                    "Disconnect epoch does not match the active session.");
            }

            snapshotBarrier.Invalidate(connectionEpoch);
            mapTransferInitializing = false;
            stateFrameProcessor.EndSession(connectionEpoch);
            reliableMessageProcessor.EndSession(connectionEpoch);
            reliableAckService.EndSession(connectionEpoch);
            snapshotRepairService.EndSession(connectionEpoch);
            entityRegistry.EndSession(connectionEpoch);
            bool modulesEnded =
                EndModules(connectionEpoch, out string moduleError);
            BattleSyncSessionTransitionResult sessionResult =
                sessionState.EndConnection(connectionEpoch);

            if (!sessionResult.IsSuccess)
            {
                return FromSessionResult(operation, sessionResult);
            }

            if (!modulesEnded)
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.ModuleEndFailed,
                    operation,
                    connectionEpoch,
                    moduleError);
            }

            string detail = string.IsNullOrWhiteSpace(reason)
                ? "Network session disconnected."
                : "Network session disconnected: " + reason;

            return CreateResult(
                BattleSyncCoordinatorStatus.Applied,
                operation,
                connectionEpoch,
                detail);
        }

        public BattleSyncCoordinatorResult HandleSnapshotRepairRequested()
        {
            const string operation="SnapshotRepairRequested";
            if(!initialized)return CreateResult(BattleSyncCoordinatorStatus.NotInitialized,operation,
                sessionState.ConnectionEpoch,"Coordinator is not initialized.");
            long connectionEpoch=sessionState.ConnectionEpoch;
            if(!sessionState.HasActiveConnection)return CreateResult(BattleSyncCoordinatorStatus.SessionRejected,
                operation,connectionEpoch,"There is no active session to repair.");
            BattleSyncSessionTransitionResult transition=sessionState.BeginRepair(connectionEpoch);
            if(!transition.IsSuccess)return FromSessionResult(operation,transition);
            if(transition.Status==BattleSyncSessionTransitionStatus.AlreadyInState)
                return CreateResult(BattleSyncCoordinatorStatus.AlreadyApplied,operation,connectionEpoch,
                    "Snapshot repair is already active.");
            InitialSnapshotBarrierResult barrierResult=snapshotBarrier.BeginBatch(connectionEpoch,
                sessionState.SceneId,sessionState.CommittedSnapshotRevision,clock.NowMilliseconds,
                entryRepairInProgress ? EntrySnapshotKinds : snapshotBarrier.RequiredKinds);
            if(!barrierResult.IsAccepted)return CreateResult(BattleSyncCoordinatorStatus.BarrierRejected,
                operation,connectionEpoch,barrierResult.Detail);
            return CreateResult(BattleSyncCoordinatorStatus.Applied,operation,connectionEpoch,
                "Snapshot repair session started.");
        }

        public BattleSyncCoordinatorResult Shutdown()
        {
            const string operation = "Shutdown";

            if (!initialized)
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.AlreadyApplied,
                    operation,
                    sessionState.ConnectionEpoch,
                    "Coordinator is already shut down.");
            }

            UnregisterSnapshotHandlers();
            mapTransferInitializing = false;
            long connectionEpoch = sessionState.ConnectionEpoch;
            bool modulesEnded = true;
            string moduleError = string.Empty;

            if (sessionState.HasActiveConnection)
            {
                snapshotBarrier.Invalidate(connectionEpoch);
                stateFrameProcessor.EndSession(connectionEpoch);
                reliableMessageProcessor.EndSession(connectionEpoch);
                reliableAckService.EndSession(connectionEpoch);
                snapshotRepairService.EndSession(connectionEpoch);
                entityRegistry.EndSession(connectionEpoch);
                modulesEnded =
                    EndModules(connectionEpoch, out moduleError);
                sessionState.EndConnection(connectionEpoch);
            }

            initialized = false;

            if (!modulesEnded)
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.ModuleEndFailed,
                    operation,
                    connectionEpoch,
                    moduleError);
            }

            return CreateResult(
                BattleSyncCoordinatorStatus.Applied,
                operation,
                connectionEpoch,
                "Coordinator shut down.");
        }

        public BattleSyncCoordinatorResult NotifyInitialWorldSeedApplied(
            long connectionEpoch, string sceneId, long snapshotRevision,
            long serverTime, string reason, bool visiblePlayersApplied = true)
        {
            const string operation = "InitialWorldSeed";
            if (!initialized) return CreateResult(BattleSyncCoordinatorStatus.NotInitialized, operation, connectionEpoch, "Coordinator is not initialized.");
            if (!sessionState.CanAcceptSnapshots(connectionEpoch, sceneId)) return CreateResult(BattleSyncCoordinatorStatus.SessionRejected, operation, connectionEpoch, "Session cannot accept the world seed.");
            InitialSnapshotReceipt receipt = new InitialSnapshotReceipt(connectionEpoch, sceneId,
                InitialSnapshotKind.World, snapshotRevision, 0, serverTime,
                string.IsNullOrWhiteSpace(reason) ? "enter_world" : reason,
                clock.NowMilliseconds, InitialSnapshotSource.EnterWorldResult);
            InitialSnapshotBarrierResult result = snapshotBarrier.MarkApplied(receipt);
            if (!result.IsAccepted) return CreateResult(BattleSyncCoordinatorStatus.BarrierRejected, operation, connectionEpoch, result.Detail);
            if (visiblePlayersApplied)
                RecordEntrySnapshot(InitialSnapshotKind.World, snapshotRevision);
            return TryCompleteReady(operation, connectionEpoch);
        }

        public BattleSyncCoordinatorResult HandleMapTransferAccepted(
            BattleSyncEnterWorldContext context)
        {
            const string operation = "MapTransferAccepted";
            if (!initialized || !context.IsValid
                || context.ConnectionEpoch != sessionState.ConnectionEpoch
                || !sessionState.IsReady
                || context.CharacterDbId != sessionState.CharacterDbId)
                return CreateResult(BattleSyncCoordinatorStatus.SessionRejected,
                    operation, context.ConnectionEpoch,
                    "Transfer does not match the ready character and connection.");

            BattleSyncSessionTransitionResult transition =
                sessionState.BeginMapTransferSnapshotBatch(
                    context.ConnectionEpoch, context.SceneId, context.CharacterDbId,
                    context.SnapshotRevision);
            if (!transition.IsSuccess)
                return FromSessionResult(operation, transition);

            long epoch = context.ConnectionEpoch;
            snapshotBarrier.Invalidate(epoch);
            stateFrameProcessor.EndSession(epoch);
            reliableMessageProcessor.EndSession(epoch);
            reliableAckService.EndSession(epoch);
            snapshotRepairService.EndSession(epoch);
            entityRegistry.EndSession(epoch);
            string moduleError = string.Empty;
            EndModule(moduleBindings.Player, epoch, "Player", ref moduleError);
            EndModule(moduleBindings.Monster, epoch, "Monster", ref moduleError);
            EndModule(moduleBindings.Combat, epoch, "Combat", ref moduleError);
            if (!string.IsNullOrEmpty(moduleError))
                return CreateResult(BattleSyncCoordinatorStatus.ModuleEndFailed,
                    operation, epoch, moduleError);

            try
            {
                entityRegistry.BeginSession(epoch, context.SceneId);
                reliableAckService.BeginSession(epoch, context.SceneId);
                reliableMessageProcessor.BeginSession(epoch, context.SceneId);
                snapshotRepairService.BeginSession(epoch, context.SceneId,
                    context.SnapshotRevision);
                stateFrameProcessor.BeginSession(epoch, context.SceneId, context.SceneSessionId, context.MonsterSyncVersion);
                BattleSyncModuleSessionContext moduleContext =
                    new BattleSyncModuleSessionContext(epoch, context.SceneId,
                        context.CharacterDbId, context.SnapshotRevision,
                        clock.NowMilliseconds);
                if (!TryBeginModule(moduleBindings.Player, moduleContext, "Player", out moduleError)
                    || !TryBeginModule(moduleBindings.Monster, moduleContext, "Monster", out moduleError))
                    return CreateResult(BattleSyncCoordinatorStatus.ModuleBeginFailed,
                        operation, epoch, moduleError);

                ILootBattleSyncModule loot = moduleBindings.Loot;
                if (!IsUnavailableModule(loot))
                {
                    BattleSyncApplyResult lootResult = loot.SwitchScene(moduleContext);
                    if (lootResult != BattleSyncApplyResult.Applied
                        && lootResult != BattleSyncApplyResult.AlreadyApplied)
                    {
                        return CreateResult(BattleSyncCoordinatorStatus.ModuleBeginFailed,
                            operation, epoch, "Loot module rejected SwitchScene with " + lootResult + ".");
                    }
                }

                if (!TryBeginModule(moduleBindings.Combat, moduleContext, "Combat", out moduleError))
                    return CreateResult(BattleSyncCoordinatorStatus.ModuleBeginFailed,
                        operation, epoch, moduleError);
            }
            catch (Exception exception)
            {
                return CreateResult(BattleSyncCoordinatorStatus.ModuleBeginFailed,
                    operation, epoch, exception.Message);
            }

            InitialSnapshotBarrierResult barrier = snapshotBarrier.BeginBatch(
                epoch, context.SceneId, context.SnapshotRevision,
                clock.NowMilliseconds, InitialSnapshotKind.World
                    | InitialSnapshotKind.Monster | InitialSnapshotKind.Loot
                    | InitialSnapshotKind.Combat);
            if (!barrier.IsAccepted)
                return CreateResult(BattleSyncCoordinatorStatus.BarrierRejected,
                    operation, epoch, barrier.Detail);
            mapTransferInitializing = true;
            return CreateResult(BattleSyncCoordinatorStatus.Applied, operation,
                epoch, "Destination snapshots are pending; inventory session is retained.");
        }

        public BattleSyncCoordinatorResult SeedInitialPlayers(
            long connectionEpoch, string sceneId, long snapshotRevision,
            IReadOnlyList<BattleEntityRegistration> registrations)
        {
            const string operation = "InitialPlayerRegistrySeed";
            if (!initialized)
                return CreateResult(BattleSyncCoordinatorStatus.NotInitialized, operation,
                    connectionEpoch, "Coordinator is not initialized.");
            if (!sessionState.CanAcceptSnapshots(connectionEpoch, sceneId))
                return CreateResult(BattleSyncCoordinatorStatus.SessionRejected, operation,
                    connectionEpoch, "Session cannot accept the initial player seed.");

            BattleEntitySnapshotPlan plan;
            BattleEntityRegistryResult prepared = entityRegistry.TryPrepareSnapshotReplacement(
                connectionEpoch, sceneId, BattleEntityKind.Player, snapshotRevision,
                registrations, out plan);
            if (!prepared.IsSuccess)
                return CreateResult(BattleSyncCoordinatorStatus.RegistryRejected, operation,
                    connectionEpoch, "Initial player seed preparation failed: " + prepared.Status);

            BattleEntityRegistryResult committed =
                entityRegistry.CommitSnapshotReplacement(plan, clock.NowMilliseconds);
            return committed.IsSuccess
                ? CreateResult(BattleSyncCoordinatorStatus.Applied, operation, connectionEpoch,
                    "Initial player registry seed committed.")
                : CreateResult(BattleSyncCoordinatorStatus.RegistryRejected, operation,
                    connectionEpoch, "Initial player seed commit failed: " + committed.Status);
        }

        public bool CheckInitializationTimeout(long timeoutMs)
        {
            return initialized
                && sessionState.Phase == BattleSyncSessionPhase.InitializingSnapshots
                && snapshotBarrier.HasTimedOut(clock.NowMilliseconds, timeoutMs);
        }

        public ReliableMessageProcessResult ProcessReliableMessage(
            ReliableMessageContext context,
            Func<BattleSyncApplyResult> applyBusiness)
        {
            return reliableMessageProcessor.Process(context, applyBusiness);
        }

        private void HandleWorldSnapshotJson(string json) =>
            ProcessSnapshot<BattleWorldSnapshot>(json, InitialSnapshotKind.World,
                BattleSyncSnapshotValidator.TryValidate,
                moduleBindings.Player.ApplyWorldSnapshot, BuildPlayerRegistrations);

        private void HandleMonsterSnapshotJson(string json) =>
            ProcessSnapshot<BattleMonsterSnapshot>(json, InitialSnapshotKind.Monster,
                BattleSyncSnapshotValidator.TryValidate,
                moduleBindings.Monster.ApplyMonsterSnapshot, BuildMonsterRegistrations);

        private void HandleLootSnapshotJson(string json) =>
            ProcessSnapshot<BattleLootSnapshot>(json, InitialSnapshotKind.Loot,
                BattleSyncSnapshotValidator.TryValidate,
                moduleBindings.Loot.ApplyLootSnapshot, BuildLootRegistrations);

        private void HandleCombatSnapshotJson(string json) =>
            ProcessSnapshot<BattleCombatSnapshot>(json, InitialSnapshotKind.Combat,
                BattleSyncSnapshotValidator.TryValidate,
                moduleBindings.Combat.ApplyCombatSnapshot);

        private void HandlePlayerEnterJson(string json)
        {
            if(!TryReadReliable(json,out Game.Client.BattleSync.Protocol.PlayerEnterMessageDto message))return;
            if(!ValidatePlayerEnter(message))
            {
                LogReliableRejected("PlayerEnter",message.sceneId,message.netId,message.serverTick,
                    message.visibilityRevision,0,message.player==null?string.Empty:
                    message.player.characterDbId.ToString(CultureInfo.InvariantCulture),
                    message.reliableMessageId);
                return;
            }
            string entityId=message.player.characterDbId.ToString(CultureInfo.InvariantCulture);
            ProcessReliable(message,"player_enter_broadcast|characterDbId:"+entityId+"|netId:"
                +message.netId.ToString(CultureInfo.InvariantCulture)+"|visibilityRevision:"
                +message.visibilityRevision.ToString(CultureInfo.InvariantCulture),message.serverTick,()=>
            {
                if(message.player.characterDbId==sessionState.CharacterDbId)return BattleSyncApplyResult.AlreadyApplied;
                BattleEntityRegistryResult registered=entityRegistry.RegisterOrUpdate(new BattleEntityRegistration(
                    sessionState.ConnectionEpoch,message.sceneId,BattleEntityKind.Player,entityId,message.netId,
                    message.visibilityRevision,message.serverTick,0),clock.NowMilliseconds);
                return ApplyRegistryResult(registered,BattleEntityKind.Player,entityId,
                    ()=>moduleBindings.Player.ApplyPlayerEnter(message));
            });
        }

        private void HandlePlayerLeaveJson(string json)
        {
            if(!TryReadReliable(json,out Game.Client.BattleSync.Protocol.PlayerLeaveMessageDto message))return;
            if(!ValidatePlayerLeave(message))
            {
                LogReliableRejected("PlayerLeave",message.sceneId,message.netId,message.serverTick,
                    message.visibilityRevision,0,message.characterDbId.ToString(CultureInfo.InvariantCulture),
                    message.reliableMessageId);
                return;
            }
            string entityId=message.characterDbId.ToString(CultureInfo.InvariantCulture);
            ProcessReliable(message,"player_leave_broadcast|characterDbId:"+entityId+"|netId:"
                +message.netId.ToString(CultureInfo.InvariantCulture)+"|visibilityRevision:"
                +message.visibilityRevision.ToString(CultureInfo.InvariantCulture),message.serverTick,()=>
            {
                if(message.characterDbId==sessionState.CharacterDbId)
                {
                    snapshotRepairService.RequestRepair(SnapshotRepairReason.SnapshotMismatch,BattleEntityKind.Player,
                        "characterDbId:"+entityId,CurrentSnapshotRevision());
                    return BattleSyncApplyResult.AlreadyApplied;
                }
                BattleEntityRegistryResult removed=entityRegistry.Remove(new BattleEntityRemoval(sessionState.ConnectionEpoch,
                    message.sceneId,BattleEntityKind.Player,entityId,message.netId,message.visibilityRevision,
                    message.serverTick,0,message.reason),clock.NowMilliseconds);
                return ApplyRegistryResult(removed,BattleEntityKind.Player,entityId,
                    ()=>moduleBindings.Player.ApplyPlayerLeave(message));
            });
        }

        private void HandleMonsterEnterJson(string json)
        {
            if(!TryReadReliable(json,out Game.Client.BattleSync.Protocol.MonsterEnterMessageDto message))return;
            if(!ValidateMonsterEnter(message))
            {
                LogReliableRejected("MonsterEnter",message.sceneId,message.netId,message.serverTick,
                    message.visibilityRevision,message.monster==null?-1:message.monster.spawnGeneration,
                    message.monster==null?string.Empty:message.monster.monsterInstanceId,
                    message.reliableMessageId);
                return;
            }
            string id=message.monster.monsterInstanceId;
            ProcessReliable(message,"monster_enter|monsterInstanceId:"+id+"|netId:"
                +message.netId.ToString(CultureInfo.InvariantCulture)+"|spawnGeneration:"
                +message.monster.spawnGeneration.ToString(CultureInfo.InvariantCulture)+"|visibilityRevision:"
                +message.visibilityRevision.ToString(CultureInfo.InvariantCulture),message.serverTick,()=>
            {
                BattleEntityRegistryResult registered=entityRegistry.RegisterOrUpdate(new BattleEntityRegistration(
                    sessionState.ConnectionEpoch,message.sceneId,BattleEntityKind.Monster,id,message.netId,
                    message.visibilityRevision,message.serverTick,message.monster.spawnGeneration),clock.NowMilliseconds);
                if (registered.Status == BattleEntityRegistryStatus.AlreadyRegistered && stateFrameProcessor.HasMonsterBaseline(id))
                    return BattleSyncApplyResult.AlreadyApplied;
                BattleSyncApplyResult applied=ApplyRegistryResult(registered,BattleEntityKind.Monster,id,
                    ()=>moduleBindings.Monster.ApplyMonsterEnter(message));
                if (registered.IsSuccess && registered.Status != BattleEntityRegistryStatus.AlreadyRegistered)
                    stateFrameProcessor.ForgetMonster(id);
                if(registered!=null && registered.IsSuccess && applied==BattleSyncApplyResult.RetryableFailure)
                    snapshotRepairService.RequestRepair(SnapshotRepairReason.MissingEntity,BattleEntityKind.Monster,
                        "monsterInstanceId:"+id,CurrentSnapshotRevision());
                return applied;
            });
        }

        private void HandleMonsterUpsertJson(string json)
        {
            if (!TryReadReliable(json, out MonsterUpsertMessageDto message))
            {
                return;
            }

            if (!ValidateBase(message, BattleSyncProtocolConstants.LegacyMessageIds.MonsterUpsert)
                || message.monster == null
                || string.IsNullOrWhiteSpace(message.monster.monsterInstanceId))
            {
                BattleSyncDiagnosticsLogger.LogValidationFailure(
                    message == null ? "monster_upsert" : message.msgId,
                    message == null ? 0 : message.reliableMessageId,
                    message == null ? string.Empty : message.sceneId,
                    "payload_validation_failed");
                return;
            }
            string identity = "monster_upsert|monsterInstanceId:" + message.monster.monsterInstanceId
                + "|aoiRevision:" + message.aoiRevision.ToString(CultureInfo.InvariantCulture)
                + "|serverTime:" + message.serverTime.ToString(CultureInfo.InvariantCulture);
            ProcessReliable(message, identity, 0,
                () => moduleBindings.Monster.ApplyMonsterUpsert(message));
        }

        private void HandleMonsterRemoveJson(string json)
        {
            if(!TryReadReliable(json,out Game.Client.BattleSync.Protocol.MonsterRemoveMessageDto message))return;
            if(!ValidateMonsterRemove(message))
            {
                LogReliableRejected("MonsterRemove",message.sceneId,message.netId,message.serverTick,
                    message.visibilityRevision,message.spawnGeneration,message.monsterInstanceId,
                    message.reliableMessageId);
                return;
            }
            string id=message.monsterInstanceId;
            ProcessReliable(message,"monster_remove|monsterInstanceId:"+id+"|netId:"
                +message.netId.ToString(CultureInfo.InvariantCulture)+"|spawnGeneration:"
                +message.spawnGeneration.ToString(CultureInfo.InvariantCulture)+"|visibilityRevision:"
                +message.visibilityRevision.ToString(CultureInfo.InvariantCulture),message.serverTick,()=>
            {
                if(entityRegistry.TryGetByEntity(BattleEntityKind.Monster,id,out BattleEntityRegistryEntry entry)
                    &&message.spawnGeneration>entry.SpawnGeneration)
                {
                    snapshotRepairService.RequestRepair(SnapshotRepairReason.GenerationMismatch,BattleEntityKind.Monster,
                        "monsterInstanceId:"+id,CurrentSnapshotRevision());
                }
                BattleEntityRegistryResult removed=entityRegistry.Remove(new BattleEntityRemoval(sessionState.ConnectionEpoch,
                    message.sceneId,BattleEntityKind.Monster,id,message.netId,message.visibilityRevision,message.serverTick,
                    message.spawnGeneration,message.reason),clock.NowMilliseconds);
                if (removed.IsSuccess) stateFrameProcessor.ForgetMonster(id);
                return ApplyRegistryResult(removed,BattleEntityKind.Monster,id,
                    ()=>moduleBindings.Monster.ApplyMonsterRemove(message));
            });
        }

        private void HandleLootUpsertJson(string json)
        {
            if(!TryReadReliable(json,out Game.Client.BattleSync.Protocol.LootUpsertMessageDto message))
                return;
            if(!ValidateLootUpsert(message))
            {
                BattleSyncDiagnosticsLogger.LogValidationFailure(
                    message.msgId,
                    message.reliableMessageId,
                    message.sceneId,
                    "payload_validation_failed");
                return;
            }
            string id=message.loot.lootId;
            ProcessReliable(message,"loot_upsert|lootId:"+id+"|netId:"+message.netId.ToString(CultureInfo.InvariantCulture)
                +"|visibilityRevision:"+message.visibilityRevision.ToString(CultureInfo.InvariantCulture),0,()=>
            {
                BattleEntityRegistryResult registered=entityRegistry.RegisterOrUpdate(new BattleEntityRegistration(
                    sessionState.ConnectionEpoch,message.sceneId,BattleEntityKind.Loot,id,message.netId,
                    message.visibilityRevision,0,0),clock.NowMilliseconds);
                return ApplyRegistryResult(registered,BattleEntityKind.Loot,id,
                    ()=>moduleBindings.Loot.ApplyLootUpsert(message));
            });
        }

        private void HandleLootRemoveJson(string json)
        {
            if(!TryReadReliable(json,out Game.Client.BattleSync.Protocol.LootRemoveMessageDto message))
                return;
            if(!ValidateLootRemove(message))
            {
                BattleSyncDiagnosticsLogger.LogValidationFailure(
                    message.msgId,
                    message.reliableMessageId,
                    message.sceneId,
                    "payload_validation_failed");
                return;
            }
            string id=message.lootId;
            ProcessReliable(message,"loot_remove|lootId:"+id+"|netId:"+message.netId.ToString(CultureInfo.InvariantCulture)
                +"|visibilityRevision:"+message.visibilityRevision.ToString(CultureInfo.InvariantCulture),0,()=>
            {
                BattleEntityRegistryResult removed=entityRegistry.Remove(new BattleEntityRemoval(sessionState.ConnectionEpoch,
                    message.sceneId,BattleEntityKind.Loot,id,message.netId,message.visibilityRevision,0,0,message.reason),
                    clock.NowMilliseconds);
                return ApplyRegistryResult(removed,BattleEntityKind.Loot,id,
                    ()=>moduleBindings.Loot.ApplyLootRemove(message));
            });
        }

        private void HandleCombatEventJson(string json)
        {
            if(!TryReadReliable(json,out Game.Client.BattleSync.Protocol.CombatEventMessageDto message))
                return;
            if(!ValidateCombatEvent(message))
            {
                BattleSyncDiagnosticsLogger.LogValidationFailure(
                    message.msgId,
                    message.reliableMessageId,
                    message.sceneId,
                    "payload_validation_failed");
                return;
            }
            string identity="combat_event|eventId:"+message.eventId.ToString(CultureInfo.InvariantCulture)
                +"|eventType:"+message.eventType
                +"|target:"+(message.target==null?"none":message.target.entityType+":"+message.target.entityId)
                +"|combatRevision:"
                +message.combatRevision.ToString(CultureInfo.InvariantCulture);
            ProcessReliable(message,identity,message.serverTick,()=>moduleBindings.Combat.ApplyCombatEvent(message));
        }

        private void HandleCombatEventBatchJson(string json)
        {
            if (!TryReadReliable(json, out CombatEventBatchMessageDto batch))
                return;
            if (!ValidateBase(batch, BattleSyncProtocolConstants.MessageIds.CombatEventBatch)
                || batch.events == null || batch.events.Length == 0)
            {
                BattleSyncDiagnosticsLogger.LogValidationFailure(
                    batch.msgId,
                    batch.reliableMessageId,
                    batch.sceneId,
                    "batch_payload_validation_failed");
                return;
            }
            for (int index = 0; index < batch.events.Length; index++)
            {
                CombatEventMessageDto item = batch.events[index];
                if (item == null
                    || (!string.IsNullOrEmpty(item.sceneId) && item.sceneId != batch.sceneId)
                    || !ValidateCombatEventPayload(item, batch.serverTick))
                {
                    BattleSyncDiagnosticsLogger.LogValidationFailure(
                        batch.msgId,
                        batch.reliableMessageId,
                        batch.sceneId,
                        "batch_event_validation_failed:index=" + index.ToString(CultureInfo.InvariantCulture));
                    return;
                }
            }

            string identity = "combat_event_batch|firstEventId:"
                + batch.events[0].eventId.ToString(CultureInfo.InvariantCulture)
                + "|lastEventId:" + batch.events[batch.events.Length - 1].eventId.ToString(CultureInfo.InvariantCulture)
                + "|firstEventType:" + batch.events[0].eventType
                + "|lastEventType:" + batch.events[batch.events.Length - 1].eventType
                + "|count:" + batch.events.Length.ToString(CultureInfo.InvariantCulture);
            ProcessReliable(batch, identity, batch.serverTick, () =>
            {
                bool applied = false;
                foreach (CombatEventMessageDto item in batch.events)
                {
                    item.sceneId = batch.sceneId;
                    if (item.serverTime == 0) item.serverTime = batch.serverTime;
                    if (item.serverTick == 0) item.serverTick = batch.serverTick;
                    BattleSyncApplyResult result = moduleBindings.Combat.ApplyCombatEvent(item);
                    if (result == BattleSyncApplyResult.RetryableFailure
                        || result == BattleSyncApplyResult.InvalidMessage
                        || result == BattleSyncApplyResult.IgnoredWrongScene)
                        return result;
                    if (result == BattleSyncApplyResult.Applied) applied = true;
                }
                return applied ? BattleSyncApplyResult.Applied : BattleSyncApplyResult.AlreadyApplied;
            });
        }

        private void HandleStateFrameJson(string json)
        {
            long epoch=transport.ConnectionEpoch;
            if(!sessionState.CanProcessStateFrames(epoch,sessionState.SceneId))return;
            stateFrameProcessor.Process(epoch,json);
        }

        private bool TryReadReliable<T>(string json,out T message) where T:ReliableStructureMessageDtoBase
        {
            message=null;
            long epoch=transport.ConnectionEpoch;
            string failure=string.Empty;
            ReliableWireHeader header=ReadReliableHeader(json);

            if(!initialized)
            {
                failure="coordinator_not_initialized";
            }
            else if(!sessionState.IsCurrentConnection(epoch))
            {
                failure="connection_epoch_not_current";
            }
            else if(sessionState.Phase!=BattleSyncSessionPhase.InitializingSnapshots
                &&sessionState.Phase!=BattleSyncSessionPhase.Ready
                &&sessionState.Phase!=BattleSyncSessionPhase.Repairing)
            {
                failure="session_phase_rejected";
            }
            else if(!jsonCodec.TryDeserialize(json,out message,out string deserializeError)
                ||message==null)
            {
                failure=string.IsNullOrWhiteSpace(deserializeError)
                    ?"deserialize_failed":deserializeError;
            }
            else if(!sessionState.IsCurrentScene(message.sceneId))
            {
                failure="scene_mismatch";
            }

            if(!string.IsNullOrEmpty(failure))
            {
                BattleSyncDiagnosticsLogger.LogValidationFailure(
                    string.IsNullOrWhiteSpace(header.MessageId)
                        ?typeof(T).Name:header.MessageId,
                    header.ReliableMessageId,
                    string.IsNullOrWhiteSpace(header.SceneId)
                        ?(message==null?string.Empty:message.sceneId):header.SceneId,
                    failure);
                return false;
            }

            return true;
        }

        private ReliableWireHeader ReadReliableHeader(string json)
        {
            if(string.IsNullOrWhiteSpace(json))
            {
                return new ReliableWireHeader();
            }

            if(jsonCodec.TryDeserialize(
                json,
                out ReliableWireHeader header,
                out _)
                &&header!=null)
            {
                return header;
            }

            return new ReliableWireHeader();
        }

        private void ProcessReliable(ReliableStructureMessageDtoBase message,string identity,long serverTick,
            Func<BattleSyncApplyResult> apply)
        {
            ReliableMessageContext context=new ReliableMessageContext(sessionState.ConnectionEpoch,message.sceneId,
                message.msgId,message.reliableMessageId,identity,clock.NowMilliseconds,message.serverTime,serverTick);
            ReliableMessageProcessResult result=reliableMessageProcessor.Process(context,apply);
            BattleSyncDiagnosticsLogger.LogReliableProcess(
                context,
                result,
                sessionState.Phase.ToString(),
                clock.NowMilliseconds);
            if(result.RequiresSnapshotRepair)snapshotRepairService.RequestRepair(SnapshotRepairReason.SnapshotMismatch,
                BattleEntityKind.Unknown,string.Empty,CurrentSnapshotRevision());
        }

        private BattleSyncApplyResult ApplyRegistryResult(BattleEntityRegistryResult result,BattleEntityKind kind,
            string entityId,Func<BattleSyncApplyResult> apply)
        {
            if(result==null)return BattleSyncApplyResult.RetryableFailure;
            if(result.Status==BattleEntityRegistryStatus.StaleVisibilityRevision
                ||result.Status==BattleEntityRegistryStatus.StaleSpawnGeneration
                ||result.Status==BattleEntityRegistryStatus.TombstoneRejected)return BattleSyncApplyResult.IgnoredStale;
            if(!result.IsSuccess)
            {
                if(result.RequiresSnapshotRepair)snapshotRepairService.RequestRepair(MapRepairReason(result.RepairReason),kind,
                    FormatRepairEntityId(kind,entityId),CurrentSnapshotRevision());
                return BattleSyncApplyResult.RetryableFailure;
            }
            return apply();
        }

        private long CurrentSnapshotRevision()=>Math.Max(sessionState.CommittedSnapshotRevision,sessionState.PendingSnapshotRevision);
        private static string FormatRepairEntityId(BattleEntityKind kind,string id)=>kind==BattleEntityKind.Player
            ?"characterDbId:"+id:kind==BattleEntityKind.Monster?"monsterInstanceId:"+id:kind==BattleEntityKind.Loot?"lootId:"+id:string.Empty;
        private static SnapshotRepairReason MapRepairReason(string reason)
        {
            if(reason==BattleSyncProtocolConstants.RepairReasons.UnknownEntityState)return SnapshotRepairReason.UnknownEntityState;
            if(reason==BattleSyncProtocolConstants.RepairReasons.RevisionGap)return SnapshotRepairReason.RevisionGap;
            if(reason==BattleSyncProtocolConstants.RepairReasons.MissingEntity)return SnapshotRepairReason.MissingEntity;
            if(reason==BattleSyncProtocolConstants.RepairReasons.InventoryMismatch)return SnapshotRepairReason.InventoryMismatch;
            if(reason==BattleSyncProtocolConstants.RepairReasons.GenerationMismatch)return SnapshotRepairReason.GenerationMismatch;
            return SnapshotRepairReason.SnapshotMismatch;
        }

        private static bool ValidateBase(ReliableStructureMessageDtoBase message,string id)=>message!=null
            &&message.msgId==id&&message.reliableMessageId>0&&!string.IsNullOrWhiteSpace(message.sceneId)&&message.serverTime>=0;
        private static bool ValidatePlayerEnter(Game.Client.BattleSync.Protocol.PlayerEnterMessageDto m)=>ValidateBase(m,BattleSyncProtocolConstants.MessageIds.PlayerEnterBroadcast)
            &&m.netId>0&&m.visibilityRevision>=0&&m.serverTick>0&&m.player!=null&&m.player.characterDbId>0
            &&m.player.sceneId==m.sceneId&&m.player.netId==m.netId&&m.player.visibilityRevision==m.visibilityRevision
            &&m.player.maxHp>=0&&m.player.hp>=0&&m.player.hp<=m.player.maxHp;
        private static bool ValidatePlayerLeave(Game.Client.BattleSync.Protocol.PlayerLeaveMessageDto m)=>ValidateBase(m,BattleSyncProtocolConstants.MessageIds.PlayerLeaveBroadcast)
            &&m.netId>0&&m.visibilityRevision>=0&&m.serverTick>0&&m.characterDbId>0;
        private static bool ValidateMonsterEnter(Game.Client.BattleSync.Protocol.MonsterEnterMessageDto m)=>ValidateBase(m,BattleSyncProtocolConstants.MessageIds.MonsterEnter)
            &&m.netId>0&&m.serverTick>0&&m.monster!=null&&!string.IsNullOrWhiteSpace(m.monster.monsterInstanceId)
            &&!string.IsNullOrWhiteSpace(m.monster.monsterId)&&m.monster.sceneId==m.sceneId&&m.monster.netId==m.netId
            &&m.monster.visibilityRevision==m.visibilityRevision&&m.monster.spawnGeneration>=0&&m.monster.combatRevision>=0
            &&m.monster.maxHp>=0&&m.monster.hp>=0&&m.monster.hp<=m.monster.maxHp;
        private static bool ValidateMonsterRemove(Game.Client.BattleSync.Protocol.MonsterRemoveMessageDto m)=>ValidateBase(m,BattleSyncProtocolConstants.MessageIds.MonsterRemove)
            &&m.netId>0&&!string.IsNullOrWhiteSpace(m.monsterInstanceId)&&m.visibilityRevision>=0
            &&m.spawnGeneration>=0&&m.combatRevision>=0&&m.serverTick>0;
        private static bool ValidateLootUpsert(Game.Client.BattleSync.Protocol.LootUpsertMessageDto m)=>ValidateBase(m,BattleSyncProtocolConstants.MessageIds.LootUpsert)
            &&m.netId>0&&m.visibilityRevision>=0&&m.loot!=null&&Guid.TryParse(m.loot.lootId,out _)
            &&m.loot.sceneId==m.sceneId&&m.loot.netId==m.netId&&m.loot.visibilityRevision==m.visibilityRevision&&m.loot.count>0;
        private static bool ValidateLootRemove(Game.Client.BattleSync.Protocol.LootRemoveMessageDto m)=>ValidateBase(m,BattleSyncProtocolConstants.MessageIds.LootRemove)
            &&m.netId>0&&m.visibilityRevision>=0&&Guid.TryParse(m.lootId,out _);
        private static bool ValidateCombatEvent(Game.Client.BattleSync.Protocol.CombatEventMessageDto m)=>
            ValidateBase(m,BattleSyncProtocolConstants.MessageIds.CombatEvent)
            &&ValidateCombatEventPayload(m,0);
        private static bool ValidateCombatEventPayload(CombatEventMessageDto m,long batchServerTick)
        {
            if(m==null||m.eventId<=0||string.IsNullOrWhiteSpace(m.eventType)
                ||!IsCombatEntity(m.source)||(m.serverTick<=0&&batchServerTick<=0))
                return false;
            if(m.eventType=="attack")
            {
                bool noTarget=m.target==null
                    ||(m.target.entityType=="none"
                        &&(string.IsNullOrEmpty(m.target.entityId)||m.target.entityId=="none"));
                return (noTarget||IsCombatEntity(m.target))
                    &&m.damage>=0&&m.hpAfter>=0&&m.maxHp>=0&&m.combatRevision>=0;
            }
            return IsCombatEntity(m.target)&&m.damage>=0&&m.hpAfter>=0&&m.maxHp>=0
                &&m.hpAfter<=m.maxHp&&m.combatRevision>0;
        }
        private static bool IsCombatEntity(CombatEntityDto e)=>e!=null&&!string.IsNullOrWhiteSpace(e.entityId)
            &&(e.entityType==BattleSyncProtocolConstants.EntityTypes.Player||e.entityType==BattleSyncProtocolConstants.EntityTypes.Monster);

        private void ProcessSnapshot<T>(string json, InitialSnapshotKind kind,
            TryValidateSnapshot<T> validate, Func<T, BattleSyncApplyResult> apply,
            Func<T, IReadOnlyList<BattleEntityRegistration>> buildRegistrations = null)
            where T : SnapshotMessageDtoBase
        {
            if (!initialized) return;
            if (!jsonCodec.TryDeserialize(json, out T message, out string error))
            {
                LogSnapshotRejected(kind, null, "deserialize", error);
                return;
            }
            if (entrySnapshotRevision > 0 && !HasEntrySnapshotList(message))
            {
                LogSnapshotRejected(kind, message, "entry_payload", "The initial entity list is missing.");
                return;
            }
            if (!validate(message, out error))
            {
                LogSnapshotRejected(kind, message, "validate", error);
                return;
            }

            if (mapTransferInitializing
                && (message.snapshotRevision != sessionState.PendingSnapshotRevision
                    || !string.Equals(message.reason, "map_transfer",
                        StringComparison.Ordinal)))
            {
                LogSnapshotRejected(kind, message, "transfer_revision",
                    "Expected the committed map transfer snapshot batch.");
                return;
            }

            long epoch = sessionState.ConnectionEpoch;
            if (sessionState.Phase == BattleSyncSessionPhase.Ready
                && kind == InitialSnapshotKind.World
                && message.snapshotRevision > sessionState.CommittedSnapshotRevision)
            {
                BattleSyncSessionTransitionResult repair = sessionState.BeginRepair(epoch);
                if (!repair.IsSuccess)
                {
                    LogSnapshotRejected(kind, message, "begin_newer_batch", repair.Detail);
                    return;
                }
                InitialSnapshotBarrierResult started = snapshotBarrier.BeginBatch(
                    epoch, message.sceneId, sessionState.CommittedSnapshotRevision,
                    clock.NowMilliseconds);
                if (!started.IsAccepted)
                {
                    LogSnapshotRejected(kind, message, "begin_newer_batch", started.Detail);
                    return;
                }
            }
            bool tracksSnapshotMessages =
                snapshotBarrier.RequiredKinds != InitialSnapshotKind.World;
            bool isRepairWorldSnapshot =
                sessionState.Phase == BattleSyncSessionPhase.Repairing
                && kind == InitialSnapshotKind.World;
            bool isRequiredSnapshot = isRepairWorldSnapshot
                || (tracksSnapshotMessages
                    && (snapshotBarrier.RequiredKinds & kind) == kind);
            bool canAcceptSnapshot = sessionState.CanAcceptSnapshots(epoch, message.sceneId)
                || (!isRequiredSnapshot
                    && sessionState.CanProcessStateFrames(epoch, message.sceneId));
            if (!canAcceptSnapshot)
            {
                LogSnapshotRejected(kind, message, "session",
                    "Snapshot is not accepted in the current session phase.");
                return;
            }

            InitialSnapshotReceipt receipt = default;
            if (isRequiredSnapshot)
            {
                receipt = new InitialSnapshotReceipt(epoch, message.sceneId, kind,
                    message.snapshotRevision, message.serverTick, message.serverTime,
                    message.reason, clock.NowMilliseconds, InitialSnapshotSource.SnapshotMessage);
                InitialSnapshotBarrierResult inspection = snapshotBarrier.Inspect(receipt);
                if (!inspection.IsAccepted || inspection.Status == InitialSnapshotBarrierStatus.Duplicate)
                {
                    LogSnapshotRejected(kind, message, "barrier",
                        inspection.Status + ": " + inspection.Detail);
                    return;
                }
            }
            else
            {
                long expectedRevision = sessionState.Phase == BattleSyncSessionPhase.Ready
                    ? sessionState.CommittedSnapshotRevision
                    : sessionState.PendingSnapshotRevision;
                if (expectedRevision > 0 && message.snapshotRevision != expectedRevision)
                {
                    LogSnapshotRejected(kind, message, "revision",
                        "Expected revision " + expectedRevision + ".");
                    return;
                }
            }

            BattleEntitySnapshotPlan registryPlan=null;
            if (buildRegistrations != null)
            {
                IReadOnlyList<BattleEntityRegistration> registrations=buildRegistrations(message);
                BattleEntityKind entityKind=kind==InitialSnapshotKind.World?BattleEntityKind.Player:
                    kind==InitialSnapshotKind.Monster?BattleEntityKind.Monster:BattleEntityKind.Loot;
                BattleEntityRegistryResult prepared=entityRegistry.TryPrepareSnapshotReplacement(
                    epoch,message.sceneId,entityKind,message.snapshotRevision,registrations,out registryPlan);
                if(!prepared.IsSuccess)
                {
                    if(kind==InitialSnapshotKind.Monster
                        &&(prepared.Status==BattleEntityRegistryStatus.StaleSpawnGeneration
                            ||prepared.Status==BattleEntityRegistryStatus.StaleVisibilityRevision
                            ||prepared.Status==BattleEntityRegistryStatus.TombstoneRejected
                            ||prepared.RequiresSnapshotRepair))
                        snapshotRepairService.RequestRepair(
                            prepared.Status==BattleEntityRegistryStatus.StaleSpawnGeneration
                                ?SnapshotRepairReason.GenerationMismatch:SnapshotRepairReason.SnapshotMismatch,
                            BattleEntityKind.Monster,string.Empty,CurrentSnapshotRevision());
                    LogSnapshotRejected(kind, message, "registry_prepare",
                        "Registry status=" + prepared.Status);
                    return;
                }
            }
            BattleSyncApplyResult applyResult = apply(message);
            if (!IsApplied(applyResult))
            {
                if (kind == InitialSnapshotKind.Monster
                    && applyResult == BattleSyncApplyResult.IgnoredStale)
                    snapshotRepairService.RequestRepair(SnapshotRepairReason.GenerationMismatch,
                        BattleEntityKind.Monster,string.Empty,CurrentSnapshotRevision());
                LogSnapshotRejected(kind, message, "module_apply",
                    "Apply result=" + applyResult);
                return;
            }
            if (registryPlan != null)
            {
                BattleEntityRegistryResult committed =
                    entityRegistry.CommitSnapshotReplacement(registryPlan,clock.NowMilliseconds);
                if(!committed.IsSuccess)
                {
                    LogSnapshotRejected(kind, message, "registry_commit",
                        "Registry status=" + committed.Status);
                    return;
                }
                if (kind == InitialSnapshotKind.Monster) stateFrameProcessor.ClearMonsterBaselines();
            }

            if (!isRequiredSnapshot)
            {
                RecordEntrySnapshot(kind, message.snapshotRevision);
                return;
            }

            InitialSnapshotBarrierResult marked = snapshotBarrier.MarkApplied(receipt);
            if (!marked.IsAccepted) return;
            if (message.snapshotRevision != sessionState.PendingSnapshotRevision)
            {
                BattleSyncSessionTransitionResult adopted = sessionState.AdoptPendingSnapshotRevision(epoch, message.sceneId, message.snapshotRevision);
                if (!adopted.IsSuccess) return;
            }
            RecordEntrySnapshot(kind, message.snapshotRevision);
            TryCompleteReady("Snapshot:" + kind, epoch);
        }

        private static bool HasEntrySnapshotList(SnapshotMessageDtoBase message)
        {
            switch (message)
            {
                case BattleWorldSnapshot world: return world.players != null;
                case BattleMonsterSnapshot monsters: return monsters.monsters != null;
                case BattleLootSnapshot loot: return loot.loots != null;
                case BattleCombatSnapshot combat: return combat.states != null;
                default: return false;
            }
        }

        private void RecordEntrySnapshot(InitialSnapshotKind kind, long revision)
        {
            if (entrySnapshotRevision <= 0 || revision < entrySnapshotRevision)
                return;
            if (revision > entrySnapshotRevision)
            {
                entrySnapshotRevision = revision;
                entryAppliedSnapshots = InitialSnapshotKind.None;
            }
            entryAppliedSnapshots |= kind;
        }

        private void LogSnapshotRejected(InitialSnapshotKind kind,
            SnapshotMessageDtoBase message, string stage, string detail)
        {
            if (!diagnosticsEnabled) return;
            long incomingRevision = message == null ? 0 : message.snapshotRevision;
            long expectedRevision = sessionState.Phase == BattleSyncSessionPhase.Ready
                ? sessionState.CommittedSnapshotRevision
                : sessionState.PendingSnapshotRevision;
            string key = kind + "|" + stage + "|" + sessionState.Phase + "|"
                + incomingRevision + "|" + expectedRevision;
            long nowMs = clock.NowMilliseconds;
            if (diagnosticLogTimes.TryGetValue(key, out long lastMs)
                && nowMs - lastMs < 1000) return;
            diagnosticLogTimes[key] = nowMs;
            UnityEngine.Debug.LogWarning(
                "[BATTLE_SYNC][SNAPSHOT_REJECTED] kind=" + kind
                + " stage=" + stage
                + " phase=" + sessionState.Phase
                + " incomingRevision=" + incomingRevision
                + " expectedRevision=" + expectedRevision
                + " detail=" + (detail ?? string.Empty));
        }

        private void LogReliableRejected(string messageType,string sceneId,int netId,
            long serverTick,long visibilityRevision,long spawnGeneration,string entityId,
            long reliableMessageId=0)
        {
            if(!diagnosticsEnabled)return;
            string key="reliable|"+messageType+"|"+netId+"|"+serverTick+"|"+visibilityRevision;
            long nowMs=clock.NowMilliseconds;
            if(diagnosticLogTimes.TryGetValue(key,out long lastMs)&&nowMs-lastMs<1000)return;
            diagnosticLogTimes[key]=nowMs;
            UnityEngine.Debug.LogWarning("[BATTLE_SYNC][RELIABLE_REJECTED] type="+messageType
                +" entityId="+(entityId??string.Empty)+" netId="+netId
                +" serverTick="+serverTick+" visibilityRevision="+visibilityRevision
                +" spawnGeneration="+spawnGeneration+" sceneId="+(sceneId??string.Empty));
            BattleSyncDiagnosticsLogger.LogValidationFailure(
                messageType,
                reliableMessageId,
                sceneId,
                "payload_validation_failed");
        }

        private BattleSyncCoordinatorResult TryCompleteReady(string operation, long epoch)
        {
            if (!snapshotBarrier.IsComplete) return CreateResult(BattleSyncCoordinatorStatus.Applied, operation, epoch, "Snapshot accepted; batch remains incomplete.");
            long revision=snapshotBarrier.SnapshotRevision; long tick=snapshotBarrier.BatchServerTick;
            long serverTime=snapshotBarrier.BatchServerTime; string reason=snapshotBarrier.Reason;
            BattleSyncSessionTransitionResult completed=sessionState.CompleteSnapshotBatch(epoch,snapshotBarrier.SceneId,revision);
            if(!completed.IsSuccess) return FromSessionResult(operation,completed);
            BattleSyncReadyContext context=new BattleSyncReadyContext(epoch,sessionState.SceneId,sessionState.CharacterDbId,
                revision,tick,serverTime,reason,clock.NowMilliseconds);
            mapTransferInitializing = false;
            Ready?.Invoke(context);
            snapshotRepairService.NotifySnapshotBatchCompleted(revision);
            snapshotBarrier.ResetCompletedBatch(epoch, revision,
                entryRepairInProgress ? InitialSnapshotKind.World : snapshotBarrier.RequiredKinds);
            entryRepairInProgress = false;
            return CreateResult(BattleSyncCoordinatorStatus.Applied,operation,epoch,"Snapshot batch completed.");
        }

        private void RegisterSnapshotHandlers()
        {
            if(snapshotHandlersRegistered) return;
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.WorldSnapshot,HandleWorldSnapshotJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.MonsterSnapshot,HandleMonsterSnapshotJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.LootSnapshot,HandleLootSnapshotJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.CombatSnapshot,HandleCombatSnapshotJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.PlayerEnterBroadcast,HandlePlayerEnterJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.PlayerLeaveBroadcast,HandlePlayerLeaveJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.MonsterEnter,HandleMonsterEnterJson);
            messageRouter.Register(BattleSyncProtocolConstants.LegacyMessageIds.MonsterUpsert,HandleMonsterUpsertJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.MonsterRemove,HandleMonsterRemoveJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.LootUpsert,HandleLootUpsertJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.LootRemove,HandleLootRemoveJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.CombatEvent,HandleCombatEventJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.CombatEventBatch,HandleCombatEventBatchJson);
            messageRouter.Register(BattleSyncProtocolConstants.MessageIds.StateFrame,HandleStateFrameJson);
            snapshotHandlersRegistered=true;
        }

        private void UnregisterSnapshotHandlers()
        {
            if(!snapshotHandlersRegistered) return;
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.WorldSnapshot,HandleWorldSnapshotJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.MonsterSnapshot,HandleMonsterSnapshotJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.LootSnapshot,HandleLootSnapshotJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.CombatSnapshot,HandleCombatSnapshotJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.PlayerEnterBroadcast,HandlePlayerEnterJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.PlayerLeaveBroadcast,HandlePlayerLeaveJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.MonsterEnter,HandleMonsterEnterJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.LegacyMessageIds.MonsterUpsert,HandleMonsterUpsertJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.MonsterRemove,HandleMonsterRemoveJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.LootUpsert,HandleLootUpsertJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.LootRemove,HandleLootRemoveJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.CombatEvent,HandleCombatEventJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.CombatEventBatch,HandleCombatEventBatchJson);
            messageRouter.Unregister(BattleSyncProtocolConstants.MessageIds.StateFrame,HandleStateFrameJson);
            snapshotHandlersRegistered=false;
        }

        private static bool IsApplied(BattleSyncApplyResult result) =>
            result == BattleSyncApplyResult.Applied || result == BattleSyncApplyResult.AlreadyApplied;

        [Serializable]
        private sealed class ReliableWireHeader
        {
            public string msgId;
            public string type;
            public long reliableMessageId;
            public string sceneId;

            public string MessageId => msgId ?? type ?? string.Empty;

            public long ReliableMessageId => reliableMessageId;

            public string SceneId => sceneId ?? string.Empty;
        }

        private delegate bool TryValidateSnapshot<T>(T message, out string error);

        private IReadOnlyList<BattleEntityRegistration> BuildPlayerRegistrations(BattleWorldSnapshot message)
        {
            List<BattleEntityRegistration> result=new List<BattleEntityRegistration>();
            foreach(BattlePlayerState item in message.players) result.Add(new BattleEntityRegistration(
                sessionState.ConnectionEpoch,message.sceneId,BattleEntityKind.Player,
                item.characterDbId.ToString(CultureInfo.InvariantCulture),item.netId,item.visibilityRevision,message.serverTick,0));
            return result;
        }

        private IReadOnlyList<BattleEntityRegistration> BuildMonsterRegistrations(BattleMonsterSnapshot message)
        {
            List<BattleEntityRegistration> result=new List<BattleEntityRegistration>();
            foreach(BattleMonsterState item in message.monsters) result.Add(new BattleEntityRegistration(
                sessionState.ConnectionEpoch,message.sceneId,BattleEntityKind.Monster,item.monsterInstanceId,
                item.netId,item.visibilityRevision,message.serverTick,item.spawnGeneration));
            return result;
        }

        private IReadOnlyList<BattleEntityRegistration> BuildLootRegistrations(BattleLootSnapshot message)
        {
            List<BattleEntityRegistration> result=new List<BattleEntityRegistration>();
            foreach(BattleLootState item in message.loots) result.Add(new BattleEntityRegistration(
                sessionState.ConnectionEpoch,message.sceneId,BattleEntityKind.Loot,item.lootId,
                item.netId,item.visibilityRevision,message.serverTick,0));
            return result;
        }

        private BattleSyncCoordinatorResult ValidateInitializedAndEpoch(
            string operation,
            long connectionEpoch)
        {
            if (!initialized)
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.NotInitialized,
                    operation,
                    connectionEpoch,
                    "Coordinator is not initialized.");
            }

            if (connectionEpoch <= 0)
            {
                return CreateResult(
                    BattleSyncCoordinatorStatus.InvalidArguments,
                    operation,
                    connectionEpoch,
                    "Connection epoch must be greater than zero.");
            }

            return null;
        }

        private BattleSyncCoordinatorResult FromSessionResult(
            string operation,
            BattleSyncSessionTransitionResult sessionResult)
        {
            BattleSyncCoordinatorStatus status;

            if (sessionResult.Status
                == BattleSyncSessionTransitionStatus.Applied)
            {
                status = BattleSyncCoordinatorStatus.Applied;
            }
            else if (sessionResult.Status
                == BattleSyncSessionTransitionStatus.AlreadyInState)
            {
                status = BattleSyncCoordinatorStatus.AlreadyApplied;
            }
            else
            {
                status = BattleSyncCoordinatorStatus.SessionRejected;
            }

            return CreateResult(
                status,
                operation,
                sessionResult.ConnectionEpoch,
                sessionResult.Detail);
        }

        private bool TryBeginModules(
            BattleSyncModuleSessionContext context,
            out string error)
        {
            if (!context.IsValid)
            {
                error = "Module session context is invalid.";
                return false;
            }

            if (!TryBeginModule(moduleBindings.Player, context, "Player", out error)
                || !TryBeginModule(moduleBindings.Monster, context, "Monster", out error)
                || !TryBeginModule(moduleBindings.Loot, context, "Loot", out error)
                || !TryBeginModule(moduleBindings.Combat, context, "Combat", out error)
                || !TryBeginModule(moduleBindings.Inventory, context, "Inventory", out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryBeginModule(
            IBattleSyncModuleLifecycle module,
            BattleSyncModuleSessionContext context,
            string moduleName,
            out string error)
        {
            if (IsUnavailableModule(module))
            {
                error = string.Empty;
                return true;
            }

            try
            {
                BattleSyncApplyResult result = module.BeginSession(context);

                if (result == BattleSyncApplyResult.Applied
                    || result == BattleSyncApplyResult.AlreadyApplied)
                {
                    error = string.Empty;
                    return true;
                }

                error = moduleName
                    + " module rejected BeginSession with "
                    + result
                    + ".";
                return false;
            }
            catch (Exception exception)
            {
                error = moduleName
                    + " module threw during BeginSession: "
                    + exception.Message;
                return false;
            }
        }

        private bool EndModules(
            long connectionEpoch,
            out string error)
        {
            string firstError = string.Empty;

            EndModule(moduleBindings.Player, connectionEpoch, "Player", ref firstError);
            EndModule(moduleBindings.Monster, connectionEpoch, "Monster", ref firstError);
            EndModule(moduleBindings.Loot, connectionEpoch, "Loot", ref firstError);
            EndModule(moduleBindings.Combat, connectionEpoch, "Combat", ref firstError);
            EndModule(moduleBindings.Inventory, connectionEpoch, "Inventory", ref firstError);

            error = firstError;
            return string.IsNullOrEmpty(firstError);
        }

        private static void EndModule(
            IBattleSyncModuleLifecycle module,
            long connectionEpoch,
            string moduleName,
            ref string firstError)
        {
            if (IsUnavailableModule(module))
            {
                return;
            }

            try
            {
                module.EndSession(connectionEpoch);
            }
            catch (Exception exception)
            {
                if (string.IsNullOrEmpty(firstError))
                {
                    firstError = moduleName
                        + " module threw during EndSession: "
                        + exception.Message;
                }
            }
        }

        private static bool IsUnavailableModule(IBattleSyncModuleLifecycle module)
        {
            return module == null
                || module is UnityEngine.Object unityObject && unityObject == null;
        }

        private BattleSyncCoordinatorResult CreateResult(
            BattleSyncCoordinatorStatus status,
            string operation,
            long connectionEpoch,
            string detail)
        {
            return new BattleSyncCoordinatorResult(
                status,
                operation,
                connectionEpoch,
                sessionState.Phase,
                detail);
        }
    }
}
