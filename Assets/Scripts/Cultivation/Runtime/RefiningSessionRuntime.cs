using System;
using Game.Cultivation.Protocol;
using Game.Inventory;
using Game.Inventory.Model.V2;
using UnityEngine;

namespace Game.Cultivation.Runtime
{
    [DisallowMultipleComponent]
    public sealed class RefiningSessionRuntime : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float requestTimeoutSeconds = 10f;

        private NetworkManager network;
        private NetMessageDispatcher dispatcher;
        private GameSession session;
        private long enteredEpoch;
        private long characterDbId;
        private string operationSessionId;
        private long pendingCatalogSequence;
        private long pendingSnapshotSequence;
        private bool refreshOnReady;
        private float nextRefreshAt;
        private float nextRecoveryAt;
        private bool timeoutReported;
        private int retryAttempts;

        public static RefiningSessionRuntime Instance { get; private set; }
        public RefiningResultDto Catalog { get; private set; }
        public RefiningSnapshotDto Snapshot { get; private set; }
        public RefiningResultDto LastSnapshotResult { get; private set; }
        public bool HasUnresolvedOperation => RefiningRequestSequence.HasPending;
        public event Action<RefiningResultDto> CatalogChanged;
        public event Action<RefiningResultDto> SnapshotChanged;
        public event Action<RefiningResultDto> StartResultReceived;
        public event Action<string> RequestRejected;
        public event Action RequestTimedOut;

        public static RefiningSessionRuntime Ensure(NetworkManager activeNetwork,
            GameSession activeSession)
        {
            if (activeNetwork == null || activeSession == null)
                throw new ArgumentNullException(activeNetwork == null
                    ? nameof(activeNetwork) : nameof(activeSession));
            RefiningSessionRuntime runtime =
                activeNetwork.GetComponent<RefiningSessionRuntime>();
            if (runtime == null)
                runtime = activeNetwork.gameObject.AddComponent<RefiningSessionRuntime>();
            runtime.Initialize(activeNetwork, activeSession);
            Instance = runtime;
            return runtime;
        }

        private void Initialize(NetworkManager activeNetwork,
            GameSession activeSession)
        {
            NetMessageDispatcher activeDispatcher = NetMessageDispatcher.Instance;
            if (dispatcher != activeDispatcher)
            {
                Unregister();
                dispatcher = activeDispatcher;
                if (dispatcher != null)
                {
                    dispatcher.Register(RefiningMessageIds.CatalogResult, HandleCatalog);
                    dispatcher.Register(RefiningMessageIds.SnapshotResult, HandleSnapshot);
                    dispatcher.Register(RefiningMessageIds.Changed, HandleChanged);
                    dispatcher.Register(RefiningMessageIds.StartResult, HandleStartResult);
                    dispatcher.Register("error", HandleServerError);
                }
            }
            if (network != activeNetwork)
            {
                if (network != null)
                    network.OnDisconnectedWithEpoch -= HandleDisconnected;
                network = activeNetwork;
                network.OnDisconnectedWithEpoch += HandleDisconnected;
                Suspend();
            }
            session = activeSession;
        }

        public void BeginSession(GameSession owner, long activeCharacterDbId,
            string sessionId)
        {
            if (session != owner || network == null || !network.IsConnected
                || activeCharacterDbId <= 0
                || !Guid.TryParseExact(sessionId, "D", out _))
                return;
            Suspend();
            characterDbId = activeCharacterDbId;
            operationSessionId = sessionId;
            enteredEpoch = network.ConnectionEpoch;
            RefiningRequestSequence.EnterWorld(sessionId, characterDbId);
            refreshOnReady = true;
            nextRefreshAt = 0f;
            nextRecoveryAt = 0f;
            retryAttempts = 0;
            timeoutReported = false;
        }

        public void SuspendSession(GameSession owner)
        {
            if (session == owner)
                Suspend();
        }

        public void Refresh()
        {
            if (!CanUseSession())
            {
                if (enteredEpoch > 0)
                    refreshOnReady = true;
                return;
            }
            if (TryRecoverPending())
            {
                refreshOnReady = true;
                return;
            }
            bool catalogSent = RequestCatalog();
            bool snapshotSent = RequestSnapshot();
            refreshOnReady = !catalogSent || !snapshotSent;
            nextRefreshAt = Time.unscaledTime + 1f;
            InventoryModuleRuntime inventory = FindObjectOfType<InventoryModuleRuntime>();
            if (inventory != null)
                inventory.RequestSnapshot();
        }

        public bool RequestCatalog()
        {
            if (!CanUseSession() || !RefiningRequestSequence.TryNextQuery(
                characterDbId, operationSessionId, RefiningMessageIds.Catalog,
                out RefiningQueryRequestDto request))
                return false;
            if (!network.Send(request))
                return false;
            pendingCatalogSequence = request.requestSequence;
            return true;
        }

        public bool RequestSnapshot()
        {
            if (!CanUseSession() || !RefiningRequestSequence.TryNextQuery(
                characterDbId, operationSessionId, RefiningMessageIds.Snapshot,
                out RefiningQueryRequestDto request))
                return false;
            if (!network.Send(request))
                return false;
            pendingSnapshotSequence = request.requestSequence;
            return true;
        }

        public bool CanStart(long stackId, int count, long configurationRevision)
        {
            if (!CanUseSession() || RefiningRequestSequence.HasPending
                || Catalog == null || Snapshot == null
                || Snapshot.activeTask != null
                || Catalog.configurationRevision != configurationRevision
                || stackId <= 0 || count <= 0)
                return false;
            InventoryModuleRuntime inventory = FindObjectOfType<InventoryModuleRuntime>();
            InventoryOwnershipSnapshot ownership =
                inventory != null ? inventory.CurrentSnapshot : null;
            if (ownership == null || ownership.CharacterDbId != characterDbId)
                return false;
            foreach (InventoryStackState stack in ownership.Stacks)
            {
                if (stack.StackId != stackId || stack.Count < count)
                    continue;
                foreach (RefiningRuleDto rule in Catalog.rules)
                {
                    if (rule != null && string.Equals(rule.itemId, stack.ItemId,
                        StringComparison.Ordinal)
                        && RefiningPreviewCalculator.TryCalculate(rule, count,
                            out _, out _))
                        return true;
                }
                break;
            }
            return false;
        }

        public bool TryStart(long stackId, int count, long configurationRevision)
        {
            if (!CanStart(stackId, count, configurationRevision))
                return false;
            InventoryOwnershipSnapshot ownership =
                FindObjectOfType<InventoryModuleRuntime>().CurrentSnapshot;
            if (!RefiningRequestSequence.TryPrepareStart(characterDbId,
                operationSessionId, configurationRevision,
                ownership.InventoryRevision, stackId, count,
                out RefiningStartRequestDto request))
                return false;
            retryAttempts = 0;
            timeoutReported = false;
            if (network.Send(request))
                return true;
            // The write may have reached the Gate even if the transport reports failure.
            Debug.LogWarning("[Refining] Start send is unresolved; request ID retained.");
            return false;
        }

        public bool TryRetryTimedOut()
        {
            if (!CanUseSession()
                || !RefiningRequestSequence.TryGetSameSessionRetry(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    (long)(requestTimeoutSeconds * 1000f),
                    out RefiningStartRequestDto request)
                || !network.Send(request))
                return false;
            RefiningRequestSequence.MarkSent(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            timeoutReported = false;
            return true;
        }

        private bool CanUseSession()
        {
            return session != null && session.IsWorldReady && network != null
                && network.IsConnected && enteredEpoch > 0
                && enteredEpoch == network.ConnectionEpoch && characterDbId > 0
                && session.LocalCharacterDbId == characterDbId
                && string.Equals(session.JingjieOperationSessionId,
                    operationSessionId, StringComparison.Ordinal);
        }

        private void Update()
        {
            if (!CanUseSession())
                return;
            if (TryRecoverPending())
                return;
            if (refreshOnReady && Time.unscaledTime >= nextRefreshAt)
                Refresh();
            if (!HasUnresolvedOperation || RefiningRequestSequence.HasConflict)
                return;
            if (timeoutReported)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!RefiningRequestSequence.TryGetSameSessionRetry(now,
                (long)(requestTimeoutSeconds * 1000f),
                out RefiningStartRequestDto retry))
                return;
            if (retryAttempts < 2 && network.Send(retry))
            {
                RefiningRequestSequence.MarkSent(now);
                retryAttempts++;
                return;
            }
            timeoutReported = true;
            RequestTimedOut?.Invoke();
        }

        private bool TryRecoverPending()
        {
            if (!CanUseSession()
                || !RefiningRequestSequence.TryGetRecoveryRequest(
                    out RefiningStartRequestDto recovered))
                return false;
            if (Time.unscaledTime < nextRecoveryAt)
                return true;
            nextRecoveryAt = Time.unscaledTime + 1f;
            if (network.Send(recovered))
            {
                RefiningRequestSequence.MarkSent(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                timeoutReported = false;
            }
            return true;
        }

        private bool TryParse(string json, string messageId,
            out RefiningResultDto result)
        {
            result = null;
            if (!CanUseSession())
                return false;
            try { result = JsonUtility.FromJson<RefiningResultDto>(json); }
            catch (Exception) { return false; }
            if (result == null || result.msgId != messageId
                || result.type != messageId
                || !string.Equals(result.jingjieOperationSessionId,
                    operationSessionId, StringComparison.Ordinal))
                return false;
            if (messageId == RefiningMessageIds.CatalogResult)
                return true;
            if (!RefiningSnapshotWireState.TryRead(json,
                out bool hasSnapshot, out bool snapshotIsNull,
                out bool hasActiveTask, out bool activeTaskIsNull))
                return false;
            if (!hasSnapshot || snapshotIsNull)
                result.snapshot = null;
            else if (result.snapshot == null
                || (hasActiveTask && !activeTaskIsNull
                    && result.snapshot.activeTask == null))
                return false;
            else if (activeTaskIsNull)
                result.snapshot.activeTask = null;
            return true;
        }

        private void HandleCatalog(string json)
        {
            if (!TryParse(json, RefiningMessageIds.CatalogResult,
                out RefiningResultDto result)
                || pendingCatalogSequence == 0
                || result.requestSequence != pendingCatalogSequence)
                return;
            pendingCatalogSequence = 0;
            if (!result.success)
            {
                RequestRejected?.Invoke(result.errorCode);
                return;
            }
            if (result.configurationRevision <= 0
                || result.rules == null || result.breakthroughs == null)
                return;
            Catalog = result;
            CatalogChanged?.Invoke(result);
        }

        private void HandleSnapshot(string json)
        {
            if (!TryParse(json, RefiningMessageIds.SnapshotResult,
                out RefiningResultDto result)
                || pendingSnapshotSequence == 0
                || result.requestSequence != pendingSnapshotSequence)
                return;
            pendingSnapshotSequence = 0;
            if (!result.success)
            {
                RequestRejected?.Invoke(result.errorCode);
                return;
            }
            ApplySnapshot(result);
        }

        private void HandleChanged(string json)
        {
            if (!TryParse(json, RefiningMessageIds.Changed,
                out RefiningResultDto result)
                || !result.success || result.requestSequence != 0)
                return;
            long previousPending = pendingSnapshotSequence;
            if (ApplySnapshot(result)
                && pendingSnapshotSequence == previousPending)
                pendingSnapshotSequence = 0;
        }

        private void HandleStartResult(string json)
        {
            if (!TryParse(json, RefiningMessageIds.StartResult,
                out RefiningResultDto result)
                || !RefiningRequestSequence.Matches(characterDbId,
                    result.jingjieOperationSessionId, result.requestSequence,
                    result.clientRequestId))
                return;
            if (!result.success)
            {
                if (result.errorCode == "refining_request_conflict")
                {
                    RefiningRequestSequence.MarkConflict();
                    timeoutReported = true;
                    RequestRejected?.Invoke(result.errorCode);
                    return;
                }
                if (result.errorCode == "refining_persist_failed")
                {
                    RefiningRequestSequence.MarkSent(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    retryAttempts = 0;
                    timeoutReported = false;
                    RequestRejected?.Invoke(result.errorCode);
                    return;
                }
                if (!RefiningRequestSequence.Complete(characterDbId,
                    result.jingjieOperationSessionId, result.requestSequence,
                    result.clientRequestId))
                    return;
                timeoutReported = false;
                if (result.errorCode == "refining_configuration_changed")
                    RequestCatalog();
                else if (result.errorCode == "refining_inventory_conflict"
                    || result.errorCode == "refining_count_insufficient"
                    || result.errorCode == "refining_stack_missing")
                    FindObjectOfType<InventoryModuleRuntime>()
                        ?.RequestReconciliation();
                else if (result.errorCode == "refining_task_active")
                    RequestSnapshot();
                StartResultReceived?.Invoke(result);
                return;
            }

            InventoryModuleRuntime inventory = FindObjectOfType<InventoryModuleRuntime>();
            if (inventory == null || !inventory.TryInitialize())
            {
                Debug.LogWarning("[Refining] Inventory is not ready; start remains unresolved.");
                return;
            }
            if (!result.idempotentReplay)
            {
                InventoryOwnershipSnapshot ownership = inventory.CurrentSnapshot;
                if (ownership == null || ownership.CharacterDbId != characterDbId)
                {
                    inventory.RequestReconciliation();
                    RequestSnapshot();
                    Debug.LogWarning("[Refining] Inventory ownership is not ready; "
                        + "start remains unresolved.");
                    return;
                }
                RefiningSnapshotDto state = result.snapshot;
                if (state == null || state.characterDbId != characterDbId
                    || state.revision < 0 || state.cultivation < 0
                    || state.totalCultivation < 0 || result.serverTime <= 0)
                {
                    inventory.RequestReconciliation();
                    RequestSnapshot();
                    Debug.LogWarning("[Refining] Invalid start snapshot; request remains unresolved.");
                    return;
                }
                if (!result.requiresInventorySnapshot
                    && (result.inventoryRevision <= 0
                        || result.stackItemChanges == null
                        || result.stackItemChanges.Length == 0))
                {
                    inventory.RequestReconciliation();
                    RequestSnapshot();
                    Debug.LogWarning("[Refining] Missing start inventory mutation; "
                        + "request remains unresolved.");
                    return;
                }
            }

            if (result.idempotentReplay || result.requiresInventorySnapshot)
                inventory.RequestReconciliation();
            else if (!inventory.ApplyRefiningInventoryChanges(
                result.inventoryRevision, result.stackItemChanges))
                inventory.RequestReconciliation();

            if (result.idempotentReplay)
                RequestSnapshot();
            else
            {
                pendingSnapshotSequence = 0;
                ApplySnapshot(result);
            }
            if (!RefiningRequestSequence.Complete(characterDbId,
                result.jingjieOperationSessionId, result.requestSequence,
                result.clientRequestId))
                return;
            timeoutReported = false;
            StartResultReceived?.Invoke(result);
        }

        private bool ApplySnapshot(RefiningResultDto result)
        {
            RefiningSnapshotDto value = result.snapshot;
            if (value == null || value.characterDbId != characterDbId
                || value.revision < 0 || value.cultivation < 0
                || value.totalCultivation < 0
                || (Snapshot != null && value.revision < Snapshot.revision))
                return false;
            bool cultivationChanged = Snapshot == null
                ? session.LocalProgressionSnapshot != null
                    && session.LocalProgressionSnapshot.jingjieExp
                        != value.cultivation
                : Snapshot.cultivation != value.cultivation;
            Snapshot = value;
            LastSnapshotResult = result;
            SnapshotChanged?.Invoke(result);
            if (cultivationChanged)
                session.QueueProgressionSnapshotRefresh();
            return true;
        }

        private void HandleServerError(string json)
        {
            if (!CanUseSession())
                return;
            ServerErrorMessage error;
            try { error = JsonUtility.FromJson<ServerErrorMessage>(json); }
            catch (Exception) { return; }
            if (error == null)
                return;
            if (error.requestMsgId == RefiningMessageIds.Catalog)
                pendingCatalogSequence = 0;
            else if (error.requestMsgId == RefiningMessageIds.Snapshot)
                pendingSnapshotSequence = 0;
            else if (error.requestMsgId != RefiningMessageIds.Start
                || !RefiningRequestSequence.HasPending)
                return;
            if (error.requestMsgId == RefiningMessageIds.Start)
            {
                timeoutReported = true;
                RequestSnapshot();
            }
            RequestRejected?.Invoke(error.errorCode);
        }

        private void HandleDisconnected(long epoch, string reason)
        {
            if (enteredEpoch == epoch)
                Suspend();
        }

        private void Suspend()
        {
            enteredEpoch = 0;
            characterDbId = 0;
            operationSessionId = null;
            pendingCatalogSequence = 0;
            pendingSnapshotSequence = 0;
            refreshOnReady = false;
            nextRefreshAt = 0f;
            nextRecoveryAt = 0f;
            retryAttempts = 0;
            timeoutReported = false;
            Catalog = null;
            Snapshot = null;
            LastSnapshotResult = null;
            RefiningRequestSequence.Suspend();
        }

        private void Unregister()
        {
            if (dispatcher == null)
                return;
            dispatcher.Unregister(RefiningMessageIds.CatalogResult, HandleCatalog);
            dispatcher.Unregister(RefiningMessageIds.SnapshotResult, HandleSnapshot);
            dispatcher.Unregister(RefiningMessageIds.Changed, HandleChanged);
            dispatcher.Unregister(RefiningMessageIds.StartResult, HandleStartResult);
            dispatcher.Unregister("error", HandleServerError);
        }

        private void OnDestroy()
        {
            if (network != null)
                network.OnDisconnectedWithEpoch -= HandleDisconnected;
            Unregister();
            if (Instance == this)
                Instance = null;
            Suspend();
        }
    }
}
