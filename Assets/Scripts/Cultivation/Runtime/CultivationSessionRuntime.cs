using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Cultivation.Protocol;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Cultivation.Runtime
{
    [DisallowMultipleComponent]
    public sealed class CultivationSessionRuntime : MonoBehaviour
    {
        private NetworkManager network;
        private NetMessageDispatcher dispatcher;
        private GameSession session;
        private long enteredEpoch;
        private long characterDbId;
        private string operationSessionId;
        private long pendingCatalogSequence;
        private long pendingSnapshotSequence;
        private long nextItemCatalogSequence;
        private long pendingItemCatalogSequence;
        private long pendingCatalogForConfiguration;
        private long pendingItemForConfiguration;
        private long catalogSyncedConfiguration;
        private long itemSyncedConfiguration;
        private float catalogLastProgressAt;
        private float snapshotSentAt;
        private float itemCatalogLastProgressAt;
        private float nextInventoryCheckAt;
        private float nextUnknownItemRefreshAt;
        private float nextLearnRetryAt;
        private bool learnRetryBlockedForSession;
        private long awaitingStartRevision = -1;
        private bool refreshOnReady;
        private float nextRefreshAt;
        private bool itemCatalogRefreshOnReady;
        private bool pendingBlueprintCatalogRefresh;
        private float nextItemCatalogRefreshAt;
        private readonly HashSet<string> queuedBlueprintRecipes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> attemptedBlueprintRecipes = new HashSet<string>(StringComparer.Ordinal);
        private readonly CultivationCatalogBatches catalogBatches =
            new CultivationCatalogBatches();
        private readonly CultivationItemCatalogBatches itemCatalogBatches =
            new CultivationItemCatalogBatches();
        private Dictionary<string, CultivationItemDefinitionDto> itemById =
            new Dictionary<string, CultivationItemDefinitionDto>(StringComparer.Ordinal);
        private Dictionary<string, CultivationItemDefinitionDto> itemByEquipmentId =
            new Dictionary<string, CultivationItemDefinitionDto>(StringComparer.Ordinal);

        public static CultivationSessionRuntime Instance { get; private set; }
        public CultivationCatalogResultDto Catalog { get; private set; }
        public CultivationItemCatalogResultDto ItemCatalog { get; private set; }
        public CultivationSnapshotDto Snapshot { get; private set; }
        public long LastSnapshotServerTime { get; private set; }
        public bool HasAcceptedSnapshot { get; private set; }
        public bool IsSessionReady => CanUseSession();
        public bool IsItemCatalogReady => CanUseSession() && ItemCatalog != null;
        public bool IsConfigurationReady => IsItemCatalogReady && HasAcceptedSnapshot
            && Catalog != null
            && Catalog.configurationRevision == Snapshot.configurationRevision
            && catalogSyncedConfiguration == Snapshot.configurationRevision
            && itemSyncedConfiguration == Snapshot.configurationRevision;
        public bool HasUnresolvedOperation => CultivationRequestSequence.HasPending;
        public event Action<CultivationCatalogResultDto> CatalogChanged;
        public event Action<CultivationItemCatalogResultDto> ItemCatalogChanged;
        public event Action<CultivationSnapshotDto> SnapshotChanged;
        public event Action<CultivationWriteResultDto> LearnResultReceived;
        public event Action<string> LearnRejected;
        public event Action<CultivationWriteResultDto> StartResultReceived;
        public event Action<string> StartRejected;

        public static CultivationSessionRuntime Ensure(NetworkManager activeNetwork,
            GameSession activeSession)
        {
            if (activeNetwork == null || activeSession == null)
                throw new ArgumentNullException(activeNetwork == null
                    ? nameof(activeNetwork) : nameof(activeSession));
            CultivationSessionRuntime runtime =
                activeNetwork.GetComponent<CultivationSessionRuntime>();
            if (runtime == null)
                runtime = activeNetwork.gameObject.AddComponent<CultivationSessionRuntime>();
            runtime.Initialize(activeNetwork, activeSession);
            Instance = runtime;
            return runtime;
        }

        private void Initialize(NetworkManager activeNetwork, GameSession activeSession)
        {
            NetMessageDispatcher activeDispatcher = NetMessageDispatcher.Instance;
            if (dispatcher != activeDispatcher)
            {
                Unregister();
                dispatcher = activeDispatcher;
                if (dispatcher != null)
                {
                    dispatcher.Register(CultivationMessageIds.CatalogResult, HandleCatalog);
                    dispatcher.Register(CultivationMessageIds.SnapshotResult, HandleSnapshot);
                    dispatcher.Register(CultivationMessageIds.Changed, HandleChanged);
                    dispatcher.Register(CultivationItemCatalogMessageIds.Result,
                        HandleItemCatalog);
                    dispatcher.Register(CultivationMessageIds.LearnResult,
                        HandleLearnResult);
                    dispatcher.Register(CultivationMessageIds.StartResult,
                        HandleStartResult);
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
            bool sessionIdValid = Guid.TryParseExact(sessionId, "D", out _);
            if (session != owner || network == null || !network.IsConnected
                || activeCharacterDbId <= 0 || !sessionIdValid)
            {
                Debug.LogWarning("[Cultivation][Entry] rejected ownerMatch="
                    + (session == owner) + " connected="
                    + (network != null && network.IsConnected)
                    + " characterValid=" + (activeCharacterDbId > 0)
                    + " sessionIdValid=" + sessionIdValid);
                return;
            }
            Suspend();
            characterDbId = activeCharacterDbId;
            operationSessionId = sessionId;
            enteredEpoch = network.ConnectionEpoch;
            CultivationRequestSequence.EnterWorld(sessionId, characterDbId);
            nextItemCatalogSequence = 1;
            nextLearnRetryAt = 0f;
            learnRetryBlockedForSession = false;
            refreshOnReady = true;
            itemCatalogRefreshOnReady = true;
            Debug.Log("[Cultivation][Entry] accepted characterDbId="
                + characterDbId + " epoch=" + enteredEpoch);
        }

        public void SuspendSession(GameSession owner)
        {
            if (session == owner)
                Suspend();
        }

        public void Refresh()
        {
            if (!CanUseSession())
                return;
            bool catalogSent = RequestCatalog();
            bool snapshotSent = RequestSnapshot();
            refreshOnReady = (!catalogSent && pendingCatalogSequence == 0)
                || (!snapshotSent && pendingSnapshotSequence == 0);
            nextRefreshAt = Time.unscaledTime + 2f;
            RefreshItemCatalogIfNeeded();
        }

        public bool RequestCatalog()
        {
            if (!CanUseSession() || pendingCatalogSequence != 0
                || !CultivationRequestSequence.TryNextQuery(characterDbId,
                    operationSessionId, CultivationMessageIds.Catalog,
                    out CultivationQueryRequestDto request))
                return false;
            bool hasScopedCache = Catalog?.characterTechniqueIds != null;
            string[] knownIds = null;
            if (hasScopedCache)
            {
                knownIds = new string[Math.Min(Catalog.techniques.Length, 512)];
                for (int i = 0; i < knownIds.Length; i++)
                    knownIds[i] = Catalog.techniques[i].techniqueId;
            }
            var scopedRequest = new CultivationCatalogRequestDto(request.requestSequence,
                hasScopedCache ? Catalog.configurationRevision : 0, knownIds);
            if (!network.Send(scopedRequest))
                return false;
            pendingCatalogSequence = request.requestSequence;
            pendingCatalogForConfiguration = HasAcceptedSnapshot
                ? Snapshot.configurationRevision : 0;
            catalogLastProgressAt = Time.unscaledTime;
            return true;
        }

        public bool RequestItemCatalog() => RequestItemCatalog(false);

        private bool RequestItemCatalog(bool blueprintRefresh)
        {
            if (!CanUseSession() || pendingItemCatalogSequence != 0
                || nextItemCatalogSequence <= 0
                || nextItemCatalogSequence == long.MaxValue)
                return false;
            var request = new CultivationItemCatalogRequestDto(
                nextItemCatalogSequence++,
                ItemCatalog == null ? 0 : ItemCatalog.catalogVersion);
            if (!network.Send(request))
            {
                Debug.LogWarning("[Cultivation][ItemCatalog][TX] send failed sequence="
                    + request.requestSequence);
                return false;
            }
            pendingItemCatalogSequence = request.requestSequence;
            pendingBlueprintCatalogRefresh = blueprintRefresh;
            itemCatalogRefreshOnReady = false;
            pendingItemForConfiguration = HasAcceptedSnapshot
                ? Snapshot.configurationRevision : 0;
            itemCatalogLastProgressAt = Time.unscaledTime;
            Debug.Log("[Cultivation][ItemCatalog][TX] sequence="
                + request.requestSequence + " knownVersion="
                + request.knownCatalogVersion + " epoch=" + enteredEpoch
                + " blueprintRefresh=" + blueprintRefresh);
            return true;
        }

        public void QueueMissingForgingBlueprint(string recipeId)
        {
            if (CanUseSession() && ItemCatalog != null && !string.IsNullOrWhiteSpace(recipeId)
                && !attemptedBlueprintRecipes.Contains(recipeId))
                queuedBlueprintRecipes.Add(recipeId);
        }

        public bool TryGetItem(string itemId, out CultivationItemDefinitionDto item)
        {
            item = null;
            return IsItemCatalogReady
                && !string.IsNullOrWhiteSpace(itemId)
                && itemById.TryGetValue(itemId, out item);
        }

        public bool TryGetItemByEquipmentId(string equipmentId, out CultivationItemDefinitionDto item)
        {
            item = null;
            return IsItemCatalogReady
                && !string.IsNullOrWhiteSpace(equipmentId)
                && itemByEquipmentId.TryGetValue(equipmentId, out item);
        }

        public bool TryResolveTechniqueBook(string itemId,
            out CultivationItemDefinitionDto book,
            out CultivationTechniqueDto technique)
        {
            book = null;
            technique = null;
            if (!IsConfigurationReady || !TryGetItem(itemId, out book)
                || book.kind != "techniquebook"
                || string.IsNullOrWhiteSpace(book.learnTechniqueId))
                return false;
            foreach (CultivationTechniqueDto entry in Catalog.techniques)
                if (entry.techniqueId == book.learnTechniqueId)
                {
                    technique = entry;
                    return true;
                }
            return false;
        }

        public void NotifyUnknownItemId(string itemId)
        {
            if (!CanUseSession() || string.IsNullOrWhiteSpace(itemId)
                || itemById.ContainsKey(itemId)
                || Time.unscaledTime < nextUnknownItemRefreshAt)
                return;
            nextUnknownItemRefreshAt = Time.unscaledTime + 10f;
            if (pendingItemCatalogSequence == 0 && !RequestItemCatalog())
                ScheduleItemCatalogRefresh();
        }

        public bool RequestSnapshot()
        {
            if (!CanUseSession() || pendingSnapshotSequence != 0
                || !CultivationRequestSequence.TryNextQuery(characterDbId,
                    operationSessionId, CultivationMessageIds.Snapshot,
                    out CultivationQueryRequestDto request))
                return false;
            if (!network.Send(request))
                return false;
            pendingSnapshotSequence = request.requestSequence;
            snapshotSentAt = Time.unscaledTime;
            return true;
        }

        public bool CanLearn(long stackId)
        {
            if (!IsConfigurationReady || HasUnresolvedOperation
                || stackId <= 0)
                return false;
            InventoryOwnershipSnapshot ownership =
                FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot;
            if (ownership == null || ownership.CharacterDbId != characterDbId
                || ownership.InventoryRevision < 0)
                return false;
            foreach (InventoryStackState stack in ownership.Stacks)
            {
                if (stack.StackId != stackId || stack.Count <= 0
                    || !TryResolveTechniqueBook(stack.ItemId, out _,
                        out CultivationTechniqueDto technique)
                    || !technique.enabled || technique.levels.Length == 0)
                    continue;
                foreach (CultivationLearnedTechniqueDto learned
                    in Snapshot.techniques)
                    if (learned.techniqueId == technique.techniqueId)
                        return false;
                return true;
            }
            return false;
        }

        public bool TryLearn(long stackId)
        {
            if (!CanLearn(stackId))
                return false;
            InventoryOwnershipSnapshot ownership =
                FindObjectOfType<InventoryModuleRuntime>().CurrentSnapshot;
            if (!CultivationRequestSequence.TryPrepareLearn(characterDbId,
                operationSessionId, Catalog.configurationRevision,
                ownership.InventoryRevision, stackId,
                out CultivationLearnRequestDto request))
                return false;
            nextLearnRetryAt = Time.unscaledTime + 10f;
            if (network.Send(request))
                return true;
            Debug.LogWarning("[Cultivation] Learn send is unresolved; request ID retained.");
            return false;
        }

        public bool TryGetNextLevel(string techniqueId, out CultivationLevelDto next)
        {
            next = null;
            if (!IsConfigurationReady || string.IsNullOrWhiteSpace(techniqueId))
                return false;
            CultivationTechniqueDto definition = null;
            foreach (CultivationTechniqueDto technique in Catalog.techniques)
                if (technique.techniqueId == techniqueId)
                {
                    definition = technique;
                    break;
                }
            if (definition == null || definition.levels == null)
                return false;
            foreach (CultivationLearnedTechniqueDto learned in Snapshot.techniques)
            {
                if (learned.techniqueId != techniqueId
                    || learned.level == int.MaxValue)
                    continue;
                foreach (CultivationLevelDto level in definition.levels)
                    if (level.level == learned.level + 1)
                    {
                        next = level;
                        return true;
                    }
            }
            return false;
        }

        public static bool TryEstimateDurationSeconds(CultivationLevelDto level,
            long totalComprehension, out long seconds)
        {
            seconds = 0;
            if (level == null || level.durationSeconds <= 0
                || totalComprehension < 0
                || (level.hasReferenceComprehension
                    && level.referenceComprehension < 0))
                return false;
            decimal estimate = level.hasReferenceComprehension
                ? decimal.Ceiling((decimal)level.durationSeconds
                    * level.referenceComprehension
                    / Math.Max(totalComprehension, 1L))
                : level.durationSeconds;
            if (estimate > long.MaxValue)
                return false;
            seconds = (long)Math.Max(1m, estimate);
            return true;
        }

        public bool CanStart(string techniqueId,
            InventoryOwnershipSnapshot ownership = null)
        {
            if (!IsConfigurationReady || HasUnresolvedOperation
                || awaitingStartRevision >= 0
                || Snapshot.activeTask != null
                || !TryGetNextLevel(techniqueId, out CultivationLevelDto next)
                || next.materials == null
                || !TryEstimateDurationSeconds(next,
                    Snapshot.totalComprehension, out _))
                return false;
            foreach (CultivationTechniqueDto technique in Catalog.techniques)
                if (technique.techniqueId == techniqueId && !technique.enabled)
                    return false;
            if (ownership == null)
                ownership = FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot;
            if (ownership == null || ownership.CharacterDbId != characterDbId
                || ownership.InventoryRevision < 0)
                return false;
            foreach (CultivationMaterialDto material in next.materials)
            {
                if (material == null || string.IsNullOrWhiteSpace(material.itemId)
                    || material.quantity <= 0)
                    return false;
            }
            foreach (CultivationMaterialDto material in next.materials)
            {
                long required = 0;
                foreach (CultivationMaterialDto entry in next.materials)
                    if (entry.itemId == material.itemId)
                        required += entry.quantity;
                if (!ownership.TryGetItemCount(material.itemId, out long count)
                    || count < required)
                    return false;
            }
            return true;
        }

        public bool TryStart(string techniqueId)
        {
            if (!CanStart(techniqueId))
                return false;
            InventoryOwnershipSnapshot ownership =
                FindObjectOfType<InventoryModuleRuntime>().CurrentSnapshot;
            if (!CultivationRequestSequence.TryPrepareStart(characterDbId,
                operationSessionId, Catalog.configurationRevision,
                ownership.InventoryRevision, techniqueId,
                out CultivationStartRequestDto request))
                return false;
            nextLearnRetryAt = Time.unscaledTime + 10f;
            if (network.Send(request))
                return true;
            Debug.LogWarning("[Cultivation] Start send is unresolved; request ID retained.");
            return false;
        }

        private bool CanUseSession()
        {
            return session != null && session.IsWorldReady && network != null
                && network.IsConnected && enteredEpoch > 0
                && enteredEpoch == network.ConnectionEpoch && characterDbId > 0
                && session.LocalCharacterDbId == characterDbId
                && string.Equals(session.CultivationOperationSessionId,
                    operationSessionId, StringComparison.Ordinal);
        }

        private void Update()
        {
            if (CanUseSession())
            {
                if (pendingCatalogSequence != 0
                    && Time.unscaledTime - catalogLastProgressAt >= 10f)
                {
                    ClearPendingCatalog();
                    ScheduleRefresh();
                }
                if (pendingSnapshotSequence != 0
                    && Time.unscaledTime - snapshotSentAt >= 10f)
                {
                    pendingSnapshotSequence = 0;
                    ScheduleRefresh();
                }
                if (pendingItemCatalogSequence != 0
                    && Time.unscaledTime - itemCatalogLastProgressAt >= 10f)
                {
                    Debug.LogWarning("[Cultivation][ItemCatalog][RX] timeout sequence="
                        + pendingItemCatalogSequence + " epoch=" + enteredEpoch);
                    FailPendingItemCatalog();
                }
                if (ItemCatalog != null && Time.unscaledTime >= nextInventoryCheckAt)
                {
                    nextInventoryCheckAt = Time.unscaledTime + 2f;
                    InventoryOwnershipSnapshot inventory =
                        FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot;
                    if (!refreshOnReady && pendingCatalogSequence == 0
                        && HasNewerCatalogScope(Catalog, inventory))
                        ScheduleRefresh();
                    if (inventory != null && inventory.CharacterDbId == characterDbId)
                        foreach (InventoryStackState stack in inventory.Stacks)
                            if (stack != null && !string.IsNullOrWhiteSpace(stack.ItemId)
                                && !itemById.ContainsKey(stack.ItemId))
                            {
                                NotifyUnknownItemId(stack.ItemId);
                                break;
                            }
                }
            }
            if (refreshOnReady && CanUseSession()
                && Time.unscaledTime >= nextRefreshAt)
                Refresh();
            else if (CanUseSession())
                RefreshItemCatalogIfNeeded();
            if (CanUseSession())
                RefreshMissingForgingBlueprints();
            if (CanUseSession() && !learnRetryBlockedForSession
                && Time.unscaledTime >= nextLearnRetryAt
                && CultivationRequestSequence.TryGetPending(
                    out NetMessageBase pending)
                && (pending is CultivationLearnRequestDto
                    || pending is CultivationStartRequestDto))
            {
                nextLearnRetryAt = Time.unscaledTime + 5f;
                network.Send(pending);
            }
        }

        private void HandleLearnResult(string json)
        {
            HandleWriteResult(json, CultivationMessageIds.Learn,
                CultivationMessageIds.LearnResult);
        }

        private void HandleStartResult(string json)
        {
            HandleWriteResult(json, CultivationMessageIds.Start,
                CultivationMessageIds.StartResult);
        }

        private void HandleWriteResult(string json, string requestType,
            string resultType)
        {
            if (!CanUseSession())
                return;
            bool isStart = requestType == CultivationMessageIds.Start;
            CultivationWriteResultDto result;
            try { result = JsonUtility.FromJson<CultivationWriteResultDto>(json); }
            catch (Exception) { ScheduleRefresh(); return; }
            if (result == null || result.msgId != resultType
                || result.type != resultType
                || !CultivationRequestSequence.Matches(characterDbId,
                    result.cultivationOperationSessionId,
                    requestType, result.requestSequence,
                    result.clientRequestId))
                return;
            if (result.serverTime <= 0 || result.errorCode == null)
            {
                nextLearnRetryAt = Time.unscaledTime + 5f;
                RequestSnapshot();
                FindObjectOfType<InventoryModuleRuntime>()
                    ?.RequestReconciliation();
                return;
            }

            if (!result.success)
            {
                if (result.errorCode == "cultivation_persist_failed")
                {
                    nextLearnRetryAt = Time.unscaledTime + 5f;
                    RequestSnapshot();
                    FindObjectOfType<InventoryModuleRuntime>()
                        ?.RequestReconciliation();
                }
                else if (result.errorCode == "cultivation_operation_conflict"
                    || result.errorCode == "invalid_cultivation_request")
                {
                    CultivationRequestSequence.MarkConflict();
                }
                else if (result.errorCode == "session_lease_required"
                    || result.errorCode == "session_lease_lost"
                    || result.errorCode == "not_authenticated")
                {
                    learnRetryBlockedForSession = true;
                }
                else
                {
                    if (isStart && result.errorCode == "cultivation_task_active"
                        && Snapshot?.activeTask == null)
                    {
                        awaitingStartRevision = Snapshot?.revision ?? 0;
                        pendingSnapshotSequence = 0;
                    }
                    CultivationRequestSequence.Complete(characterDbId,
                        operationSessionId, requestType,
                        result.requestSequence, result.clientRequestId);
                    Refresh();
                    FindObjectOfType<InventoryModuleRuntime>()
                        ?.RequestReconciliation();
                }
                if (result.snapshot != null)
                    ApplySnapshotJson(json, result.snapshot, result.serverTime);
                if (isStart)
                    StartResultReceived?.Invoke(result);
                else
                    LearnRejected?.Invoke(result.errorCode);
                return;
            }

            CultivationRequestSequence.Complete(characterDbId, operationSessionId,
                requestType, result.requestSequence,
                result.clientRequestId);
            if (isStart && Snapshot?.activeTask == null)
                awaitingStartRevision = Snapshot?.revision ?? 0;
            InventoryModuleRuntime inventory = FindObjectOfType<InventoryModuleRuntime>();
            if (inventory != null)
            {
                if (result.idempotentReplay || result.requiresInventorySnapshot
                    || result.inventoryRevision <= 0
                    || result.stackItemChanges == null
                    || result.stackItemChanges.Length == 0
                    || !(isStart
                        ? inventory.ApplyCultivationStartInventoryChanges(
                            result.inventoryRevision, false, result.stackItemChanges)
                        : inventory.ApplyCultivationInventoryChanges(
                            result.inventoryRevision, false, result.stackItemChanges)))
                    inventory.RequestReconciliation();
            }
            if (!result.idempotentReplay)
                ApplySnapshotJson(json, result.snapshot, result.serverTime);
            if (result.idempotentReplay || result.snapshot == null
                || (isStart && awaitingStartRevision >= 0))
            {
                pendingSnapshotSequence = 0;
                RequestSnapshot();
            }
            session.RequestProgressionSnapshot();
            if (isStart)
                StartResultReceived?.Invoke(result);
            else
                LearnResultReceived?.Invoke(result);
        }

        private void HandleCatalog(string json)
        {
            if (!CanUseSession() || pendingCatalogSequence == 0)
            {
                Debug.LogWarning("[Cultivation][Catalog][RX] ignored sessionReady="
                    + CanUseSession() + " pendingSequence=" + pendingCatalogSequence);
                return;
            }
            CultivationCatalogResultDto result;
            try { result = JsonUtility.FromJson<CultivationCatalogResultDto>(json); }
            catch (Exception exception)
            {
                Debug.LogWarning("[Cultivation][Catalog][RX] parse failed: "
                    + exception.Message);
                ClearPendingCatalog();
                ScheduleRefresh();
                return;
            }
            if (result == null || result.msgId != CultivationMessageIds.CatalogResult
                || result.type != CultivationMessageIds.CatalogResult
                || result.requestSequence != pendingCatalogSequence)
            {
                Debug.LogWarning("[Cultivation][Catalog][RX] envelope mismatch expected="
                    + pendingCatalogSequence + " received="
                    + (result == null ? "<null>" : result.requestSequence.ToString()));
                return;
            }
            if (!result.success)
            {
                Debug.LogWarning("[Cultivation][Catalog][RX] rejected errorCode="
                    + result.errorCode);
                ClearPendingCatalog();
                ScheduleRefresh();
                return;
            }
            if (result.configurationRevision <= 0 || result.serverTime <= 0
                || result.grades == null
                || result.techniques == null
                || !CultivationCatalogWireState.TryRestoreNullableReferences(
                    json, result) || !IsValidCatalog(result))
            {
                Debug.LogWarning("[Cultivation][Catalog][RX] invalid revision="
                    + result.configurationRevision + " serverTimeValid="
                    + (result.serverTime > 0) + " gradesPresent="
                    + (result.grades != null) + " techniquesPresent="
                    + (result.techniques != null));
                ClearPendingCatalog();
                ScheduleRefresh();
                return;
            }
            if (Catalog != null
                && result.configurationRevision < Catalog.configurationRevision)
            {
                Debug.LogWarning("[Cultivation][Catalog][RX] stale revision="
                    + result.configurationRevision + " current="
                    + Catalog.configurationRevision);
                ClearPendingCatalog();
                ScheduleRefresh();
                return;
            }
            CultivationCatalogBatchResult batchStatus = catalogBatches.Apply(result, json);
            if (batchStatus == CultivationCatalogBatchResult.Pending)
            {
                catalogLastProgressAt = Time.unscaledTime;
                return;
            }
            if (batchStatus == CultivationCatalogBatchResult.Duplicate)
                return;
            ClearPendingCatalog();
            if (batchStatus == CultivationCatalogBatchResult.Invalid
                || !CultivationCatalogBatches.TryResolveScope(result, Catalog))
            {
                Debug.LogWarning("[Cultivation][Catalog][RX] invalid batch or scope sequence="
                    + result.requestSequence + " index=" + result.batchIndex);
                ScheduleRefresh();
                return;
            }
            if (HasNewerCatalogScope(result,
                FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot))
            {
                ScheduleRefresh();
                return;
            }
            Catalog = result;
            Debug.Log("[Cultivation][Catalog][RX] accepted revision="
                + result.configurationRevision + " techniqueCount="
                + result.techniques.Length);
            if (HasAcceptedSnapshot
                && pendingCatalogForConfiguration == Snapshot.configurationRevision
                && result.configurationRevision == Snapshot.configurationRevision)
                catalogSyncedConfiguration = Snapshot.configurationRevision;
            CatalogChanged?.Invoke(result);
            if (HasAcceptedSnapshot && catalogSyncedConfiguration
                != Snapshot.configurationRevision)
                ScheduleRefresh();
        }

        private bool HasNewerCatalogScope(CultivationCatalogResultDto catalog,
            InventoryOwnershipSnapshot inventory)
        {
            return catalog?.characterTechniqueIds != null
                && ((inventory != null && inventory.CharacterDbId == characterDbId
                        && inventory.InventoryRevision > catalog.inventoryRevision)
                    || (HasAcceptedSnapshot
                        && (Snapshot.revision > catalog.cultivationRevision
                            || Snapshot.configurationRevision > catalog.configurationRevision)));
        }

        private void HandleItemCatalog(string json)
        {
            if (!CanUseSession() || pendingItemCatalogSequence == 0)
            {
                Debug.LogWarning("[Cultivation][ItemCatalog][RX] ignored sessionReady="
                    + CanUseSession() + " pendingSequence="
                    + pendingItemCatalogSequence);
                return;
            }
            CultivationItemCatalogResultDto result;
            try { result = JsonUtility.FromJson<CultivationItemCatalogResultDto>(json); }
            catch (Exception exception)
            {
                Debug.LogWarning("[Cultivation][ItemCatalog][RX] parse failed: "
                    + exception.Message);
                FailPendingItemCatalog();
                return;
            }
            if (result == null || result.msgId != CultivationItemCatalogMessageIds.Result
                || result.type != CultivationItemCatalogMessageIds.Result
                || result.requestSequence != pendingItemCatalogSequence)
            {
                Debug.LogWarning("[Cultivation][ItemCatalog][RX] envelope mismatch expected="
                    + pendingItemCatalogSequence + " received="
                    + (result == null ? "<null>" : result.requestSequence.ToString()));
                return;
            }
            if (!result.success || result.catalogVersion < 0 || result.serverTime <= 0
                || result.items == null)
            {
                Debug.LogWarning("[Cultivation][ItemCatalog][RX] rejected success="
                    + result.success + " errorCode=" + result.errorCode
                    + " version=" + result.catalogVersion + " serverTimeValid="
                    + (result.serverTime > 0) + " itemsPresent="
                    + (result.items != null));
                FailPendingItemCatalog();
                return;
            }
            if (ItemCatalog != null
                && result.catalogVersion < ItemCatalog.catalogVersion)
            {
                Debug.LogWarning("[Cultivation][ItemCatalog][RX] stale version="
                    + result.catalogVersion + " current=" + ItemCatalog.catalogVersion);
                FailPendingItemCatalog();
                return;
            }
            ItemCatalogBatchResult batchStatus = itemCatalogBatches.Apply(
                result, json, out CultivationItemCatalogResultDto complete);
            if (batchStatus == ItemCatalogBatchResult.Pending)
            {
                itemCatalogLastProgressAt = Time.unscaledTime;
                return;
            }
            if (batchStatus == ItemCatalogBatchResult.Duplicate)
                return;

            if (batchStatus == ItemCatalogBatchResult.Invalid)
            {
                Debug.LogWarning("[Cultivation][ItemCatalog][RX] invalid batch sequence="
                    + result.requestSequence + " index=" + result.batchIndex
                    + " count=" + result.batchCount);
                FailPendingItemCatalog();
                return;
            }
            result = complete;
            var itemsById = new Dictionary<string, CultivationItemDefinitionDto>(
                StringComparer.Ordinal);
            var itemsByEquipmentId = new Dictionary<string, CultivationItemDefinitionDto>(
                StringComparer.Ordinal);
            foreach (CultivationItemDefinitionDto item in result.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.itemId)
                    || string.IsNullOrWhiteSpace(item.kind) || item.maxStack < 0
                    || (item.maxStack == 0 && item.kind != "currency")
                    || !itemsById.TryAdd(item.itemId, item))
                {
                    Debug.LogWarning("[Cultivation][ItemCatalog][RX] invalid itemId="
                        + (item?.itemId ?? "<null>") + " kind="
                        + (item?.kind ?? "<null>") + " maxStack="
                        + (item == null ? 0 : item.maxStack));
                    FailPendingItemCatalog();
                    return;
                }

                if (item.kind == "equipment" && !string.IsNullOrWhiteSpace(item.equipmentId)
                    && !itemsByEquipmentId.TryAdd(item.equipmentId, item))
                {
                    Debug.LogWarning("[Cultivation][ItemCatalog][RX] duplicate equipmentId="
                        + item.equipmentId);
                    FailPendingItemCatalog();
                    return;
                }
            }
            ClearPendingItemCatalog();
            itemById = itemsById;
            itemByEquipmentId = itemsByEquipmentId;
            ItemCatalog = result;
            Debug.Log("[Cultivation][ItemCatalog][RX] accepted sequence="
                + result.requestSequence + " version=" + result.catalogVersion
                + " itemCount=" + result.items.Length);
            if (HasAcceptedSnapshot
                && pendingItemForConfiguration == Snapshot.configurationRevision)
                itemSyncedConfiguration = Snapshot.configurationRevision;
            ItemCatalogChanged?.Invoke(result);
            if (HasAcceptedSnapshot
                && itemSyncedConfiguration != Snapshot.configurationRevision)
                ScheduleItemCatalogRefresh();
        }

        private void HandleSnapshot(string json)
        {
            if (!CanUseSession())
            {
                Debug.LogWarning("[Cultivation][Snapshot][RX] ignored sessionReady=false");
                return;
            }
            CultivationSnapshotResultDto result;
            try { result = JsonUtility.FromJson<CultivationSnapshotResultDto>(json); }
            catch (Exception exception)
            {
                Debug.LogWarning("[Cultivation][Snapshot][RX] parse failed: "
                    + exception.Message);
                pendingSnapshotSequence = 0;
                ScheduleRefresh();
                return;
            }
            if (result == null
                || result.msgId != CultivationMessageIds.SnapshotResult
                || result.type != CultivationMessageIds.SnapshotResult)
            {
                Debug.LogWarning("[Cultivation][Snapshot][RX] invalid envelope");
                return;
            }
            bool automatic = result.requestSequence == 0;
            if (!automatic && (pendingSnapshotSequence == 0
                || result.requestSequence != pendingSnapshotSequence))
            {
                Debug.LogWarning("[Cultivation][Snapshot][RX] sequence mismatch expected="
                    + pendingSnapshotSequence + " received=" + result.requestSequence);
                return;
            }
            if (!automatic)
                pendingSnapshotSequence = 0;
            if (!result.success)
            {
                Debug.LogWarning("[Cultivation][Snapshot][RX] rejected errorCode="
                    + result.errorCode);
                ScheduleRefresh();
                return;
            }
            ApplySnapshotJson(json, result.snapshot, result.serverTime);
        }

        private void HandleChanged(string json)
        {
            if (!CanUseSession())
                return;
            CultivationChangedDto result;
            try { result = JsonUtility.FromJson<CultivationChangedDto>(json); }
            catch (Exception) { ScheduleRefresh(); return; }
            if (result == null || result.msgId != CultivationMessageIds.Changed
                || result.type != CultivationMessageIds.Changed
                || !string.Equals(result.cultivationOperationSessionId,
                    operationSessionId, StringComparison.Ordinal))
                return;
            ApplySnapshotJson(json, result.snapshot, result.serverTime);
        }

        private void ApplySnapshotJson(string json, CultivationSnapshotDto value,
            long serverTime)
        {
            if (serverTime <= 0 || !RefiningSnapshotWireState.TryRead(json,
                out bool hasSnapshot, out bool snapshotIsNull,
                out bool hasActiveTask, out bool activeTaskIsNull)
                || !hasSnapshot || snapshotIsNull)
            {
                Debug.LogWarning("[Cultivation][Snapshot][RX] invalid snapshot envelope"
                    + " serverTimeValid=" + (serverTime > 0));
                ScheduleRefresh();
                return;
            }
            if (!hasActiveTask || value == null
                || (!activeTaskIsNull && value.activeTask == null))
            {
                Debug.LogWarning("[Cultivation][Snapshot][RX] activeTask state invalid"
                    + " hasActiveTask=" + hasActiveTask
                    + " snapshotPresent=" + (value != null)
                    + " activeTaskIsNull=" + activeTaskIsNull);
                ScheduleRefresh();
                return;
            }
            if (activeTaskIsNull)
                value.activeTask = null;
            if (!IsValidSnapshot(value))
            {
                Debug.LogWarning("[Cultivation][Snapshot][RX] invalid data characterDbId="
                    + value.characterDbId + " revision=" + value.revision
                    + " configurationRevision=" + value.configurationRevision
                    + " techniquesPresent=" + (value.techniques != null));
                ScheduleRefresh();
                return;
            }
            if (HasAcceptedSnapshot)
            {
                long configurationDifference =
                    value.configurationRevision - Snapshot.configurationRevision;
                long revisionDifference = value.revision - Snapshot.revision;
                if (configurationDifference < 0 && revisionDifference <= 0
                    || revisionDifference < 0 && configurationDifference <= 0)
                    return;
                if (configurationDifference < 0 || revisionDifference < 0)
                {
                    ScheduleRefresh();
                    return;
                }
            }
            bool configurationChanged = !HasAcceptedSnapshot
                || value.configurationRevision != Snapshot.configurationRevision;
            if (configurationChanged)
            {
                catalogSyncedConfiguration = 0;
                itemSyncedConfiguration = 0;
            }
            ApplyProfessionProgress(json, value);
            Snapshot = value;
            HasAcceptedSnapshot = true;
            Debug.Log("[Cultivation][Snapshot][RX] accepted revision="
                + value.revision + " configurationRevision="
                + value.configurationRevision + " learnedCount="
                + value.techniques.Length + " activeTaskPresent="
                + (value.activeTask != null));
            LastSnapshotServerTime = serverTime;
            if (awaitingStartRevision >= 0
                && (value.revision > awaitingStartRevision
                    || value.activeTask != null))
                awaitingStartRevision = -1;
            SnapshotChanged?.Invoke(value);
            if (configurationChanged)
            {
                ScheduleRefresh();
                if (!itemCatalogRefreshOnReady && (pendingItemCatalogSequence == 0
                    || pendingItemForConfiguration != value.configurationRevision))
                    ScheduleItemCatalogRefresh();
            }
            else if (!refreshOnReady && pendingCatalogSequence == 0
                && HasNewerCatalogScope(Catalog, null))
                ScheduleRefresh();
        }

        private void ApplyProfessionProgress(string json, CultivationSnapshotDto value)
        {
            JObject wire = null;
            try { wire = JObject.Parse(json)["snapshot"] as JObject; }
            catch (Newtonsoft.Json.JsonException) { }

            value.alchemyProfession = ReadProfession(wire?["alchemyProfession"], value.alchemyProfession)
                ?? Snapshot?.alchemyProfession;
            value.forgingProfession = ReadProfession(wire?["forgingProfession"], value.forgingProfession)
                ?? Snapshot?.forgingProfession;
        }

        private static CultivationProfessionProgressDto ReadProfession(
            JToken wire, CultivationProfessionProgressDto value)
        {
            JToken rank = (wire as JObject)?["rank"];
            if (value == null || rank?.Type != JTokenType.Integer
                || !int.TryParse(rank.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out int wireRank)
                || wireRank != value.rank || wireRank < -1 || wireRank > 9)
                return null;
            return value;
        }

        private bool IsValidSnapshot(CultivationSnapshotDto value)
        {
            if (value.characterDbId != characterDbId || value.revision < 0
                || value.configurationRevision <= 0 || value.techniques == null
                || value.baseComprehension < 0 || value.techniqueComprehension < 0
                || value.totalComprehension < 0)
                return false;
            foreach (CultivationLearnedTechniqueDto technique in value.techniques)
                if (technique == null || string.IsNullOrWhiteSpace(technique.techniqueId)
                    || technique.level < 0 || technique.learnedAt < 0)
                    return false;
            CultivationTaskDto task = value.activeTask;
            return task == null
                || (!string.IsNullOrWhiteSpace(task.taskId)
                    && !string.IsNullOrWhiteSpace(task.techniqueId)
                    && task.startedAt > 0 && task.endsAt > task.startedAt
                    && task.paidMaterials != null);
        }

        private static bool IsValidCatalog(CultivationCatalogResultDto value)
        {
            foreach (CultivationGradeDto grade in value.grades)
                if (grade == null || string.IsNullOrWhiteSpace(grade.gradeId)
                    || string.IsNullOrWhiteSpace(grade.name))
                    return false;
            foreach (CultivationTechniqueDto technique in value.techniques)
            {
                if (technique == null
                    || string.IsNullOrWhiteSpace(technique.techniqueId)
                    || string.IsNullOrWhiteSpace(technique.gradeId)
                    || string.IsNullOrWhiteSpace(technique.name)
                    || technique.levels == null)
                    return false;
                foreach (CultivationLevelDto level in technique.levels)
                {
                    if (level == null || level.level < 0
                        || level.comprehension < 0 || level.durationSeconds < 0
                        || level.materials == null)
                        return false;
                    foreach (CultivationMaterialDto material in level.materials)
                        if (material == null || string.IsNullOrWhiteSpace(material.itemId)
                            || material.quantity <= 0)
                            return false;
                }
            }
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
            if (error.requestMsgId == CultivationMessageIds.Catalog)
                ClearPendingCatalog();
            else if (error.requestMsgId == CultivationMessageIds.Snapshot)
                pendingSnapshotSequence = 0;
            else if (error.requestMsgId == CultivationItemCatalogMessageIds.Query)
            {
                if (pendingItemCatalogSequence == 0)
                    return;
                Debug.LogWarning("[Cultivation][ItemCatalog][RX] server errorCode="
                    + error.errorCode);
                FailPendingItemCatalog();
                return;
            }
            else if ((error.requestMsgId == CultivationMessageIds.Learn
                    || error.requestMsgId == CultivationMessageIds.Start)
                && CultivationRequestSequence.TryGetPending(
                    out NetMessageBase pending)
                && (pending is CultivationLearnRequestDto
                    || pending is CultivationStartRequestDto)
                && pending.msgId == error.requestMsgId)
            {
                if (pending is CultivationStartRequestDto
                    && error.errorCode == "cultivation_task_active"
                    && Snapshot?.activeTask == null)
                {
                    awaitingStartRevision = Snapshot?.revision ?? 0;
                    pendingSnapshotSequence = 0;
                }
                if (error.errorCode == "cultivation_persist_failed")
                    nextLearnRetryAt = Time.unscaledTime + 5f;
                else if (error.errorCode == "invalid_cultivation_request"
                    || error.errorCode == "cultivation_operation_conflict")
                    CultivationRequestSequence.MarkConflict();
                else if (error.errorCode == "session_lease_required"
                    || error.errorCode == "session_lease_lost"
                    || error.errorCode == "not_authenticated")
                    learnRetryBlockedForSession = true;
                else
                    CultivationRequestSequence.Complete(characterDbId,
                        operationSessionId, pending.msgId,
                        pending is CultivationLearnRequestDto learn
                            ? learn.requestSequence
                            : ((CultivationStartRequestDto)pending).requestSequence,
                        pending is CultivationLearnRequestDto learnRequest
                            ? learnRequest.clientRequestId
                            : ((CultivationStartRequestDto)pending).clientRequestId);
                if (pending is CultivationStartRequestDto)
                    StartRejected?.Invoke(error.errorCode);
                else
                    LearnRejected?.Invoke(error.errorCode);
                RequestSnapshot();
                FindObjectOfType<InventoryModuleRuntime>()
                    ?.RequestReconciliation();
            }
            else
                return;
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            refreshOnReady = true;
            nextRefreshAt = Time.unscaledTime + 2f;
        }

        private void ScheduleItemCatalogRefresh()
        {
            itemCatalogRefreshOnReady = true;
            nextItemCatalogRefreshAt = Time.unscaledTime + 2f;
        }

        private void RefreshItemCatalogIfNeeded()
        {
            if (itemCatalogRefreshOnReady && pendingItemCatalogSequence == 0
                && Time.unscaledTime >= nextItemCatalogRefreshAt && !RequestItemCatalog())
                ScheduleItemCatalogRefresh();
        }

        private void RefreshMissingForgingBlueprints()
        {
            if (pendingItemCatalogSequence != 0 || itemCatalogRefreshOnReady
                || queuedBlueprintRecipes.Count == 0 || ItemCatalog == null)
                return;

            // A completed regular sync may already have supplied the association.
            foreach (CultivationItemDefinitionDto item in ItemCatalog.items)
                if (item.kind == "forgingblueprint")
                    queuedBlueprintRecipes.Remove(item.learnForgingRecipeId);
            if (queuedBlueprintRecipes.Count == 0)
                return;

            attemptedBlueprintRecipes.UnionWith(queuedBlueprintRecipes);
            queuedBlueprintRecipes.Clear();
            RequestItemCatalog(true);
        }

        private void ClearPendingCatalog()
        {
            pendingCatalogSequence = 0;
            catalogBatches.Clear();
        }

        private void ClearPendingItemCatalog()
        {
            pendingItemCatalogSequence = 0;
            pendingBlueprintCatalogRefresh = false;
            itemCatalogBatches.Clear();
        }

        private void FailPendingItemCatalog()
        {
            bool shouldRetry = !pendingBlueprintCatalogRefresh;
            ClearPendingItemCatalog();
            if (shouldRetry)
                ScheduleItemCatalogRefresh();
        }

        private void HandleDisconnected(long epoch, string reason)
        {
            if (enteredEpoch == epoch)
                Suspend();
        }

        private void Suspend()
        {
            bool hadSnapshot = HasAcceptedSnapshot;
            enteredEpoch = 0;
            characterDbId = 0;
            operationSessionId = null;
            ClearPendingCatalog();
            pendingSnapshotSequence = 0;
            ClearPendingItemCatalog();
            nextItemCatalogSequence = 1;
            pendingCatalogForConfiguration = 0;
            pendingItemForConfiguration = 0;
            catalogSyncedConfiguration = 0;
            itemSyncedConfiguration = 0;
            nextInventoryCheckAt = 0;
            nextUnknownItemRefreshAt = 0;
            nextLearnRetryAt = 0;
            learnRetryBlockedForSession = false;
            awaitingStartRevision = -1;
            refreshOnReady = false;
            nextRefreshAt = 0;
            itemCatalogRefreshOnReady = false;
            nextItemCatalogRefreshAt = 0;
            Catalog = null;
            ItemCatalog = null;
            itemById.Clear();
            itemByEquipmentId.Clear();
            queuedBlueprintRecipes.Clear();
            attemptedBlueprintRecipes.Clear();
            Snapshot = null;
            HasAcceptedSnapshot = false;
            LastSnapshotServerTime = 0;
            CultivationRequestSequence.Suspend();
            if (hadSnapshot)
                SnapshotChanged?.Invoke(null);
        }

        private void Unregister()
        {
            if (dispatcher == null)
                return;
            dispatcher.Unregister(CultivationMessageIds.CatalogResult, HandleCatalog);
            dispatcher.Unregister(CultivationMessageIds.SnapshotResult, HandleSnapshot);
            dispatcher.Unregister(CultivationMessageIds.Changed, HandleChanged);
            dispatcher.Unregister(CultivationItemCatalogMessageIds.Result,
                HandleItemCatalog);
            dispatcher.Unregister(CultivationMessageIds.LearnResult,
                HandleLearnResult);
            dispatcher.Unregister(CultivationMessageIds.StartResult,
                HandleStartResult);
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
