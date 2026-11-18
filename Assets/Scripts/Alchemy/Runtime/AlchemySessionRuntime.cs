using System;
using Game.Alchemy.Model;
using Game.Alchemy.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.UI.Notice;
using UnityEngine;

namespace Game.Alchemy.Runtime
{
    [DisallowMultipleComponent]
    public sealed class AlchemySessionRuntime : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float queryTimeoutSeconds = 10f;

        private readonly AlchemyCatalogState catalogState =
            new AlchemyCatalogState();
        private readonly AlchemySnapshotState snapshotState =
            new AlchemySnapshotState();
        private readonly AlchemyLearnOperationState learnOperation =
            new AlchemyLearnOperationState();
        private readonly AlchemyStartOperationState startOperation =
            new AlchemyStartOperationState();
        private AlchemyMaterialSelection heldStartSelection;
        private bool? replayStartSucceeded;
        private InventoryOwnershipSnapshot replayStartInventoryBaseline;
        private readonly AlchemyClaimOperationState claimOperation =
            new AlchemyClaimOperationState();
        [SerializeField, Min(1f)] private float requestTimeoutSeconds = 10f;
        private NetworkManager network;
        private NetMessageDispatcher dispatcher;
        private GameSession session;
        private long enteredEpoch;
        private long characterDbId;
        private string operationSessionId;
        private long nextRequestSequence = 1;
        private long pendingSnapshotSequence;
        private bool initialCatalogQueryPending;
        private bool initialSnapshotQueryPending;
        private float initialQueryRetryAt;
        private bool refreshAfterSnapshot;
        private bool recoveryAttempted;
        private bool catalogRecoveryAttempted;
        private bool itemCatalogRefreshPending;
        private float catalogSentAt;
        private float snapshotSentAt;
        private long lastStartAttemptAt;
        private long lastLearnAttemptAt;
        private int learnRetryAttempts;
        private bool learnTimeoutReported;
        private bool learnRetryPaused;
        private int startRetryAttempts;
        private bool startTimeoutReported;
        private bool startRetryPaused;
        private long lastClaimAttemptAt;
        private int claimRetryAttempts;
        private bool claimTimeoutReported;
        private bool claimRetryPaused;
        private bool inventoryRecoveryPending;
        private InventoryOwnershipSnapshot inventoryRecoveryBaseline;
        private long inventoryRecoveryRevision;
        private float nextInventoryRepairAt;

        public static AlchemySessionRuntime Instance { get; private set; }
        public AlchemyRecipeDto[] Catalog => catalogState.Recipes;
        public long ConfigurationRevision => catalogState.ConfigurationRevision;
        public bool IsCatalogCurrent => CanUseSession() && catalogState.IsCurrent;
        public bool IsSessionActive => CanUseSession();
        public AlchemySnapshotDto Snapshot => snapshotState.Snapshot;
        public long LastServerTime => snapshotState.LastServerTime;
        public bool IsSnapshotCurrent { get; private set; }
        public bool HasUnresolvedStart => startOperation.HasPending;
        public AlchemyMaterialSelection StartSelection =>
            heldStartSelection != null
                && heldStartSelection.CharacterDbId == characterDbId
                ? heldStartSelection : RestoreStartSelection();
        public bool HasUnresolvedLearn => learnOperation.HasPending;
        public bool HasUnresolvedClaim => claimOperation.HasPending;
        public bool IsInventoryRecovering => inventoryRecoveryPending;
        public event Action<AlchemyRecipeDto[]> CatalogChanged;
        public event Action<AlchemySnapshotDto> SnapshotChanged;
        public event Action<AlchemyResponseDto> StartResultReceived;
        public event Action StartSelectionResolved;
        public event Action<string, string> StartRequestRejected;
        public event Action<AlchemyResponseDto> LearnResultReceived;
        public event Action LearnTimedOut;
        public event Action StartTimedOut;
        public event Action<AlchemyResponseDto> ClaimResultReceived;
        public event Action ClaimTimedOut;
        public event Action<string> RequestRejected;

        public static AlchemySessionRuntime Ensure(NetworkManager activeNetwork,
            GameSession activeSession)
        {
            if (activeNetwork == null || activeSession == null)
                throw new ArgumentNullException(activeNetwork == null
                    ? nameof(activeNetwork) : nameof(activeSession));
            AlchemySessionRuntime runtime =
                activeNetwork.GetComponent<AlchemySessionRuntime>();
            if (runtime == null)
                runtime = activeNetwork.gameObject
                    .AddComponent<AlchemySessionRuntime>();
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
                dispatcher?.Register(AlchemyMessageIds.CatalogResult,
                    HandleCatalog);
                dispatcher?.Register(AlchemyMessageIds.SnapshotResult,
                    HandleSnapshot);
                dispatcher?.Register(AlchemyMessageIds.Changed,
                    HandleChanged);
                dispatcher?.Register(AlchemyMessageIds.StartResult,
                    HandleStartResult);
                dispatcher?.Register(AlchemyMessageIds.LearnResult,
                    HandleLearnResult);
                dispatcher?.Register(AlchemyMessageIds.ClaimResult,
                    HandleClaimResult);
                dispatcher?.Register("error", HandleServerError);
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
            initialCatalogQueryPending = true;
            initialSnapshotQueryPending = true;
            initialQueryRetryAt = 0f;
            nextRequestSequence = startOperation.EnterWorld(characterDbId,
                operationSessionId, nextRequestSequence);
            if (heldStartSelection?.CharacterDbId != characterDbId
                || !startOperation.HasPending)
                heldStartSelection = null;
            replayStartSucceeded = null;
            replayStartInventoryBaseline = null;
            if (heldStartSelection == null)
                RestoreStartSelection();
            nextRequestSequence = claimOperation.EnterWorld(characterDbId,
                operationSessionId, nextRequestSequence);
            nextRequestSequence = learnOperation.EnterWorld(characterDbId,
                operationSessionId, nextRequestSequence);
            learnRetryAttempts = 0;
            learnTimeoutReported = false;
            learnRetryPaused = false;
            startRetryAttempts = 0;
            startTimeoutReported = false;
            startRetryPaused = false;
            claimRetryAttempts = 0;
            claimTimeoutReported = false;
            claimRetryPaused = false;
            inventoryRecoveryPending = startOperation.HasPending
                || claimOperation.HasPending || learnOperation.HasPending;
            inventoryRecoveryBaseline = FindObjectOfType<InventoryModuleRuntime>()
                ?.CurrentSnapshot;
            inventoryRecoveryRevision = 0;
        }

        public void SuspendSession(GameSession owner)
        {
            if (session == owner)
                Suspend();
        }

        public bool RequestSnapshot()
        {
            IsSnapshotCurrent = false;
            if (!SendSnapshotRequest())
                return false;
            initialSnapshotQueryPending = false;
            recoveryAttempted = false;
            return true;
        }

        public bool RequestCatalog()
        {
            catalogRecoveryAttempted = false;
            if (!SendCatalogRequest())
                return false;
            initialCatalogQueryPending = false;
            return true;
        }

        public void EnsureLearningData()
        {
            if (!IsCatalogCurrent && catalogState.PendingSequence == 0)
                RequestCatalog();
            if (!IsSnapshotCurrent && pendingSnapshotSequence == 0)
                RequestSnapshot();
        }

        public bool CanStart(AlchemyMaterialSelection selection,
            InventoryOwnershipSnapshot ownership)
        {
            if (!CanUseSession() || !IsCatalogCurrent || !IsSnapshotCurrent
                || startOperation.HasPending || claimOperation.HasPending
                || learnOperation.HasPending
                || inventoryRecoveryPending
                || snapshotState.Snapshot?.currentTask != null
                || selection == null || ownership == null
                || ownership.CharacterDbId != characterDbId
                || selection.RecipeId == null
                || selection.CheckEligibility(ownership, ConfigurationRevision)
                    != AlchemySelectionError.None)
                return false;
            AlchemyRecipeDto recipe = Array.Find(Catalog,
                entry => entry != null && entry.recipeId == selection.RecipeId);
            if (recipe == null || snapshotState.Snapshot.learnedRecipeIds == null)
                return false;
            foreach (string learned in snapshotState.Snapshot.learnedRecipeIds)
                if (string.Equals(learned, recipe.recipeId,
                    StringComparison.Ordinal))
                    return true;
            return false;
        }

        public bool CanLearn(long stackId, string itemId,
            InventoryOwnershipSnapshot ownership)
        {
            if (!CanUseSession() || !IsCatalogCurrent || !IsSnapshotCurrent
                || learnOperation.HasPending || startOperation.HasPending
                || claimOperation.HasPending || inventoryRecoveryPending
                || stackId <= 0 || ownership == null
                || ownership.CharacterDbId != characterDbId
                || ownership.InventoryRevision < 0
                || !ReferenceEquals(FindObjectOfType<InventoryModuleRuntime>()
                    ?.CurrentSnapshot, ownership))
                return false;
            CultivationSessionRuntime items = CultivationSessionRuntime.Instance;
            if (items == null || !items.TryGetItem(itemId, out var book)
                || book == null || book.kind != "recipebook"
                || string.IsNullOrWhiteSpace(book.learnRecipeId)
                || Array.Find(Catalog, recipe => recipe != null
                    && recipe.recipeId == book.learnRecipeId) == null)
                return false;
            foreach (InventoryStackState stack in ownership.Stacks)
                if (stack != null && stack.StackId == stackId
                    && stack.ItemId == itemId && stack.Count > 0)
                {
                    foreach (string learned in Snapshot.learnedRecipeIds)
                        if (string.Equals(learned, book.learnRecipeId,
                            StringComparison.Ordinal))
                            return false;
                    return true;
                }
            return false;
        }

        public bool TryLearn(long stackId, string itemId,
            InventoryOwnershipSnapshot ownership)
        {
            if (!CanLearn(stackId, itemId, ownership)
                || nextRequestSequence <= 0
                || nextRequestSequence == long.MaxValue
                || !CultivationSessionRuntime.Instance.TryGetItem(itemId,
                    out var book)
                || !learnOperation.Prepare(nextRequestSequence,
                    ConfigurationRevision, ownership.InventoryRevision,
                    book.learnRecipeId, stackId,
                    out AlchemyLearnRequestDto request))
                return false;
            nextRequestSequence++;
            bool sent = network.Send(request);
            learnOperation.MarkSent(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            lastLearnAttemptAt = learnOperation.LastSentAt;
            if (!sent)
                Debug.LogWarning("[Alchemy] Learn send is uncertain; "
                    + "the original request is retained.");
            return true;
        }

        public bool TryStart(AlchemyMaterialSelection selection,
            InventoryOwnershipSnapshot ownership)
        {
            if (!CanStart(selection, ownership) || nextRequestSequence <= 0
                || nextRequestSequence == long.MaxValue
                || !ReferenceEquals(FindObjectOfType<InventoryModuleRuntime>()
                    ?.CurrentSnapshot, ownership)
                || selection.PrepareStart(ownership, ConfigurationRevision,
                    out AlchemySelectedMaterialDto[] materials,
                    out long inventoryRevision) != AlchemySelectionError.None)
                return false;
            if (!startOperation.Prepare(nextRequestSequence,
                ConfigurationRevision, inventoryRevision, selection.RecipeId,
                materials, out AlchemyStartRequestDto request))
            {
                selection.ResolveOperation(false);
                return false;
            }
            heldStartSelection = selection;
            nextRequestSequence++;
            bool sent = network.Send(request);
            startOperation.MarkSent(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            lastStartAttemptAt = startOperation.LastSentAt;
            if (!sent)
                Debug.LogWarning("[Alchemy] Start send is uncertain; "
                    + "the original request is retained.");
            return true;
        }

        public bool CanClaim(InventoryOwnershipSnapshot ownership)
        {
            AlchemyTaskDto task = snapshotState.Snapshot?.currentTask;
            if (!CanUseSession() || !IsSnapshotCurrent
                || startOperation.HasPending || claimOperation.HasPending
                || learnOperation.HasPending
                || task == null || task.status != "ready"
                || string.IsNullOrWhiteSpace(task.taskId))
                return false;
            if (task.ParsedSucceeded == false)
                return true;
            return task.ParsedSucceeded == true && !inventoryRecoveryPending
                && ownership != null && ownership.CharacterDbId == characterDbId
                && ownership.InventoryRevision >= 0
                && ReferenceEquals(FindObjectOfType<InventoryModuleRuntime>()
                    ?.CurrentSnapshot, ownership);
        }

        public bool TryClaim(InventoryOwnershipSnapshot ownership)
        {
            if (!CanClaim(ownership) || nextRequestSequence <= 0
                || nextRequestSequence == long.MaxValue)
                return false;
            AlchemyTaskDto task = snapshotState.Snapshot.currentTask;
            bool expectsOutput = task.ParsedSucceeded == true;
            long revision = ownership != null
                && ownership.CharacterDbId == characterDbId
                && ownership.InventoryRevision >= 0
                ? ownership.InventoryRevision : 0;
            if (!claimOperation.Prepare(nextRequestSequence, revision,
                task.taskId, expectsOutput, out AlchemyClaimRequestDto request))
                return false;
            nextRequestSequence++;
            bool sent = network.Send(request);
            claimOperation.MarkSent(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            lastClaimAttemptAt = claimOperation.LastSentAt;
            if (!sent)
                Debug.LogWarning("[Alchemy] Claim send is uncertain; "
                    + "the original request is retained.");
            return true;
        }

        private bool SendCatalogRequest()
        {
            if (!CanUseSession() || nextRequestSequence <= 0
                || nextRequestSequence == long.MaxValue)
                return false;
            var request = new AlchemyCatalogRequestDto(nextRequestSequence++);
            if (!network.Send(request))
                return false;
            catalogState.BeginQuery(request.requestSequence);
            catalogSentAt = Time.unscaledTime;
            return true;
        }

        private bool SendSnapshotRequest()
        {
            if (!CanUseSession() || nextRequestSequence <= 0
                || nextRequestSequence == long.MaxValue)
                return false;
            var request = new AlchemySnapshotRequestDto(nextRequestSequence++);
            if (!network.Send(request))
                return false;
            pendingSnapshotSequence = request.requestSequence;
            snapshotSentAt = Time.unscaledTime;
            return true;
        }

        private void Update()
        {
            if (!CanUseSession())
                return;
            if (Time.unscaledTime >= initialQueryRetryAt)
            {
                bool catalogFailed = initialCatalogQueryPending
                    && !RequestCatalog();
                bool snapshotFailed = initialSnapshotQueryPending
                    && !RequestSnapshot();
                if (catalogFailed || snapshotFailed)
                    initialQueryRetryAt = Time.unscaledTime + 1f;
            }
            RecoverInventory();
            ResolveReplayedStart();
            RetryLearn();
            RetryStart();
            RetryClaim();
            if (catalogState.PendingSequence > 0
                && Time.unscaledTime - catalogSentAt >= queryTimeoutSeconds)
            {
                catalogState.Invalidate();
                Debug.LogWarning("[Alchemy] Catalog query timed out.");
                RetryCatalogOnce();
            }
            if (pendingSnapshotSequence > 0
                && Time.unscaledTime - snapshotSentAt >= queryTimeoutSeconds)
            {
                pendingSnapshotSequence = 0;
                IsSnapshotCurrent = false;
                Debug.LogWarning("[Alchemy] Snapshot query timed out.");
                if (refreshAfterSnapshot)
                    FlushQueuedRefresh();
                else if (!recoveryAttempted)
                {
                    recoveryAttempted = true;
                    SendSnapshotRequest();
                }
            }
            if (itemCatalogRefreshPending
                && CultivationSessionRuntime.Instance != null
                && CultivationSessionRuntime.Instance.RequestItemCatalog())
                itemCatalogRefreshPending = false;
        }

        private void RetryStart()
        {
            if (!startOperation.HasPending || startOperation.HasConflict
                || replayStartSucceeded.HasValue || startRetryPaused
                || claimOperation.HasPending
                || learnOperation.HasPending)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (startOperation.NeedsRecovery)
            {
                if (now - lastStartAttemptAt < 1000
                    || !startOperation.TryGetRetry(out AlchemyStartRequestDto request))
                    return;
                lastStartAttemptAt = now;
                if (network.Send(request))
                    startOperation.MarkSent(now);
                return;
            }
            if (startTimeoutReported || now - startOperation.LastSentAt
                < requestTimeoutSeconds * 1000f)
                return;
            if (startRetryAttempts < 2
                && startOperation.TryGetSameSession(out AlchemyStartRequestDto retry)
                && network.Send(retry))
            {
                startRetryAttempts++;
                startOperation.MarkSent(now);
                return;
            }
            startTimeoutReported = true;
            StartTimedOut?.Invoke();
        }

        private void RetryClaim()
        {
            if (!claimOperation.HasPending || claimOperation.HasConflict
                || claimRetryPaused || startOperation.HasPending
                || learnOperation.HasPending)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (claimOperation.NeedsRecovery)
            {
                if (now - lastClaimAttemptAt < 1000
                    || !claimOperation.TryGetRetry(out AlchemyClaimRequestDto request))
                    return;
                lastClaimAttemptAt = now;
                if (network.Send(request))
                    claimOperation.MarkSent(now);
                return;
            }
            if (claimTimeoutReported || now - claimOperation.LastSentAt
                < requestTimeoutSeconds * 1000f)
                return;
            if (claimRetryAttempts < 2
                && claimOperation.TryGetSameSession(out AlchemyClaimRequestDto retry)
                && network.Send(retry))
            {
                claimRetryAttempts++;
                claimOperation.MarkSent(now);
                return;
            }
            claimTimeoutReported = true;
            ClaimTimedOut?.Invoke();
        }

        private void RetryLearn()
        {
            if (!learnOperation.HasPending || learnOperation.HasConflict
                || learnRetryPaused || startOperation.HasPending
                || claimOperation.HasPending)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (learnOperation.NeedsRecovery)
            {
                if (now - lastLearnAttemptAt < 1000
                    || !learnOperation.TryGetRetry(
                        out AlchemyLearnRequestDto request))
                    return;
                lastLearnAttemptAt = now;
                if (network.Send(request))
                    learnOperation.MarkSent(now);
                return;
            }
            if (learnTimeoutReported || now - learnOperation.LastSentAt
                < requestTimeoutSeconds * 1000f)
                return;
            if (learnRetryAttempts < 2
                && learnOperation.TryGetSameSession(
                    out AlchemyLearnRequestDto retry)
                && network.Send(retry))
            {
                learnRetryAttempts++;
                learnOperation.MarkSent(now);
                return;
            }
            learnTimeoutReported = true;
            LearnTimedOut?.Invoke();
        }

        private void RecoverInventory()
        {
            if (!inventoryRecoveryPending)
                return;
            InventoryModuleRuntime inventory = FindObjectOfType<InventoryModuleRuntime>();
            InventoryOwnershipSnapshot current = inventory?.CurrentSnapshot;
            if (current != null && current.CharacterDbId == characterDbId
                && !ReferenceEquals(current, inventoryRecoveryBaseline)
                && current.InventoryRevision >= inventoryRecoveryRevision)
            {
                inventoryRecoveryPending = false;
                inventoryRecoveryBaseline = null;
                return;
            }
            if (inventory != null && Time.unscaledTime >= nextInventoryRepairAt)
            {
                nextInventoryRepairAt = Time.unscaledTime + 2f;
                inventory.RequestReconciliation();
            }
        }

        private AlchemyMaterialSelection RestoreStartSelection()
        {
            if (characterDbId <= 0
                || !startOperation.TryGetPending(out AlchemyStartRequestDto request))
                return null;
            var restored = new AlchemyMaterialSelection();
            InventoryOwnershipSnapshot ownership =
                FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot;
            if (!restored.RestorePending(request.recipeId,
                request.configurationRevision, characterDbId,
                request.materials, ownership))
                return null;
            heldStartSelection = restored;
            return restored;
        }

        private void ResolveReplayedStart()
        {
            InventoryOwnershipSnapshot ownership =
                FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot;
            if (!replayStartSucceeded.HasValue || heldStartSelection == null
                || !startOperation.HasPending || !IsSnapshotCurrent
                || inventoryRecoveryPending || ownership == null
                || ownership.CharacterDbId != characterDbId
                || ReferenceEquals(ownership, replayStartInventoryBaseline)
                || snapshotState.Snapshot?.characterDbId != characterDbId)
                return;
            startOperation.Complete();
            heldStartSelection.ResolveOperation(replayStartSucceeded.Value);
            replayStartSucceeded = null;
            replayStartInventoryBaseline = null;
            StartSelectionResolved?.Invoke();
        }

        private void RequireInventoryRecovery(long revision)
        {
            InventoryModuleRuntime inventory = FindObjectOfType<InventoryModuleRuntime>();
            inventoryRecoveryPending = true;
            inventoryRecoveryBaseline = inventory?.CurrentSnapshot;
            inventoryRecoveryRevision = revision;
            nextInventoryRepairAt = 0f;
            inventory?.RequestReconciliation();
        }

        private void RetryCatalogOnce()
        {
            if (catalogRecoveryAttempted)
                return;
            catalogRecoveryAttempted = true;
            SendCatalogRequest();
        }

        private bool CanUseSession()
        {
            return session != null && session.IsWorldReady && network != null
                && network.IsConnected && enteredEpoch > 0
                && enteredEpoch == network.ConnectionEpoch && characterDbId > 0
                && session.LocalCharacterDbId == characterDbId
                && string.Equals(session.AlchemyOperationSessionId,
                    operationSessionId, StringComparison.Ordinal);
        }

        private void HandleCatalog(string json)
        {
            if (!CanUseSession() || catalogState.PendingSequence == 0)
                return;
            AlchemyResponseDto result;
            try { result = JsonUtility.FromJson<AlchemyResponseDto>(json); }
            catch (Exception) { return; }
            if (result == null || result.msgId != AlchemyMessageIds.CatalogResult
                || result.type != AlchemyMessageIds.CatalogResult
                || result.requestSequence != catalogState.PendingSequence
                || !string.Equals(result.alchemyOperationSessionId,
                    operationSessionId, StringComparison.Ordinal))
                return;
            AlchemyCatalogApplyResult applied = catalogState.Apply(result);
            if (applied == AlchemyCatalogApplyResult.Applied)
                CatalogChanged?.Invoke(catalogState.Recipes);
            else if (applied == AlchemyCatalogApplyResult.Rejected)
            {
                RequestRejected?.Invoke(result.errorCode);
                if (result.errorCode != "alchemy_catalog_upgrade_required"
                    && result.errorCode != "alchemy_catalog_entry_too_large")
                    RetryCatalogOnce();
            }
            else if (applied == AlchemyCatalogApplyResult.Invalid
                || applied == AlchemyCatalogApplyResult.Stale)
            {
                Debug.LogWarning("[Alchemy] Catalog not applied: " + applied);
                RetryCatalogOnce();
            }
        }

        private void HandleSnapshot(string json)
        {
            if (!CanUseSession() || pendingSnapshotSequence == 0)
                return;
            AlchemySnapshotResultDto result;
            try { result = JsonUtility.FromJson<AlchemySnapshotResultDto>(json); }
            catch (Exception) { return; }
            if (result == null || result.msgId != AlchemyMessageIds.SnapshotResult
                || result.type != AlchemyMessageIds.SnapshotResult
                || result.requestSequence != pendingSnapshotSequence
                || !string.Equals(result.alchemyOperationSessionId,
                    operationSessionId, StringComparison.Ordinal))
                return;
            pendingSnapshotSequence = 0;
            if (result.success)
                NoteConfiguration(result.configurationRevision);
            if (!result.success)
            {
                IsSnapshotCurrent = false;
                RequestRejected?.Invoke(result.errorCode);
                FlushQueuedRefresh();
                return;
            }
            AlchemySnapshotApplyResult applied = snapshotState.Apply(json,
                result.snapshot, characterDbId, result.serverTime);
            if (applied == AlchemySnapshotApplyResult.Applied
                || applied == AlchemySnapshotApplyResult.Unchanged)
            {
                IsSnapshotCurrent = true;
                SnapshotChanged?.Invoke(snapshotState.Snapshot);
            }
            else if (applied == AlchemySnapshotApplyResult.Invalid
                || applied == AlchemySnapshotApplyResult.NotProvided)
            {
                IsSnapshotCurrent = false;
                Debug.LogWarning("[Alchemy] Snapshot not applied: " + applied);
                if (!refreshAfterSnapshot && !recoveryAttempted)
                {
                    recoveryAttempted = true;
                    SendSnapshotRequest();
                }
            }
            else if (applied == AlchemySnapshotApplyResult.Stale
                && !IsSnapshotCurrent && !recoveryAttempted)
            {
                recoveryAttempted = true;
                SendSnapshotRequest();
            }
            FlushQueuedRefresh();
        }

        private void HandleChanged(string json)
        {
            if (!CanUseSession())
                return;
            AlchemyResponseDto changed;
            try { changed = JsonUtility.FromJson<AlchemyResponseDto>(json); }
            catch (Exception) { return; }
            if (changed == null || changed.msgId != AlchemyMessageIds.Changed
                || changed.type != AlchemyMessageIds.Changed
                || !string.Equals(changed.alchemyOperationSessionId,
                    operationSessionId, StringComparison.Ordinal)
                || changed.requestSequence != 0
                || !string.IsNullOrEmpty(changed.clientRequestId)
                || !changed.success
                || (changed.snapshot != null
                    && changed.snapshot.characterDbId != characterDbId))
                return;
            NoteConfiguration(changed.configurationRevision);
            if (changed.snapshot != null)
            {
                AlchemySnapshotApplyResult applied = snapshotState.Apply(json,
                    changed.snapshot, characterDbId, changed.serverTime);
                if (applied == AlchemySnapshotApplyResult.Applied
                    || applied == AlchemySnapshotApplyResult.Unchanged)
                {
                    IsSnapshotCurrent = true;
                    SnapshotChanged?.Invoke(snapshotState.Snapshot);
                    return;
                }
                if (applied == AlchemySnapshotApplyResult.Stale)
                    return;
            }
            IsSnapshotCurrent = false;
            refreshAfterSnapshot = true;
            FlushQueuedRefresh();
        }

        private void HandleStartResult(string json)
        {
            if (!CanUseSession() || !startOperation.HasPending)
                return;
            AlchemyResponseDto result;
            try { result = JsonUtility.FromJson<AlchemyResponseDto>(json); }
            catch (Exception) { return; }
            if (result == null || result.msgId != AlchemyMessageIds.StartResult
                || result.type != AlchemyMessageIds.StartResult
                || !startOperation.Matches(result.alchemyOperationSessionId,
                    result.requestSequence, result.clientRequestId))
                return;
            if (replayStartSucceeded.HasValue)
                return;
            NoteConfiguration(result.configurationRevision);
            InventoryModuleRuntime inventory =
                FindObjectOfType<InventoryModuleRuntime>();
            if (result.idempotentReplay)
            {
                replayStartSucceeded = result.success;
                replayStartInventoryBaseline = inventory?.CurrentSnapshot;
                RequireInventoryRecovery(0);
                RequestSnapshot();
                if (!result.success
                    && (result.errorCode == "alchemy_configuration_changed"
                        || result.errorCode == "alchemy_recipe_unavailable"))
                    RequestCatalog();
                startTimeoutReported = false;
                StartResultReceived?.Invoke(result);
                return;
            }
            if (result.requiresInventorySnapshot)
                RequireInventoryRecovery(0);
            if (!result.success
                && result.errorCode == "alchemy_request_conflict")
            {
                startOperation.MarkConflict();
                startRetryPaused = true;
                RequestSnapshot();
                RequestRejected?.Invoke(result.errorCode);
                StartRequestRejected?.Invoke(result.errorCode, result.message);
                return;
            }
            if (!result.success
                && result.errorCode == "alchemy_persist_failed")
            {
                startOperation.MarkSent(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                startRetryAttempts = 0;
                RequestRejected?.Invoke(result.errorCode);
                StartRequestRejected?.Invoke(result.errorCode, result.message);
                return;
            }
            if (!result.success)
            {
                startOperation.Complete();
                startTimeoutReported = false;
                heldStartSelection?.ResolveOperation(false);
                if (result.errorCode == "alchemy_configuration_changed"
                    || result.errorCode == "alchemy_recipe_unavailable")
                    RequestCatalog();
                if (result.errorCode == "alchemy_inventory_conflict"
                    || result.errorCode == "alchemy_material_missing"
                    || result.errorCode == "alchemy_material_insufficient")
                    RequireInventoryRecovery(0);
                if (result.errorCode == "alchemy_furnace_occupied")
                    RequestSnapshot();
                StartResultReceived?.Invoke(result);
                return;
            }
            if (result.snapshot?.currentTask == null)
            {
                RequireInventoryRecovery(0);
                RequestSnapshot();
                Debug.LogWarning("[Alchemy] Start succeeded without a task; "
                    + "the original request remains unresolved.");
                return;
            }
            AlchemySnapshotApplyResult appliedStart = snapshotState.Apply(json,
                result.snapshot, characterDbId, result.serverTime);
            if (appliedStart != AlchemySnapshotApplyResult.Applied
                && appliedStart != AlchemySnapshotApplyResult.Unchanged
                && appliedStart != AlchemySnapshotApplyResult.Stale)
            {
                RequireInventoryRecovery(0);
                RequestSnapshot();
                Debug.LogWarning("[Alchemy] Invalid start state; "
                    + "the original request remains unresolved.");
                return;
            }
            if (appliedStart != AlchemySnapshotApplyResult.Stale)
            {
                IsSnapshotCurrent = true;
                SnapshotChanged?.Invoke(snapshotState.Snapshot);
            }
            if (!result.requiresInventorySnapshot)
            {
                if (result.inventoryRevision <= 0
                    || result.stackItemChanges == null
                    || result.stackItemChanges.Length == 0
                    || inventory == null
                    || !inventory.ApplyAlchemyInventoryChanges(
                        result.inventoryRevision, result.stackItemChanges))
                    RequireInventoryRecovery(result.inventoryRevision);
            }
            startOperation.Complete();
            startTimeoutReported = false;
            heldStartSelection?.ResolveOperation(true);
            StartResultReceived?.Invoke(result);
        }

        private void HandleLearnResult(string json)
        {
            if (!CanUseSession() || !learnOperation.HasPending)
                return;
            AlchemyResponseDto result;
            try { result = JsonUtility.FromJson<AlchemyResponseDto>(json); }
            catch (Exception) { return; }
            if (result == null || result.msgId != AlchemyMessageIds.LearnResult
                || result.type != AlchemyMessageIds.LearnResult
                || !learnOperation.Matches(result.alchemyOperationSessionId,
                    result.requestSequence, result.clientRequestId))
                return;
            NoteConfiguration(result.configurationRevision);
            if (result.requiresInventorySnapshot)
                RequireInventoryRecovery(0);
            if (!result.success && result.errorCode == "alchemy_request_conflict")
            {
                learnOperation.MarkConflict();
                learnRetryPaused = true;
                RequestSnapshot();
                RequestRejected?.Invoke(result.errorCode);
                return;
            }
            if (!result.success && result.errorCode == "alchemy_persist_failed")
            {
                learnOperation.MarkSent(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                learnRetryAttempts = 0;
                RequestRejected?.Invoke(result.errorCode);
                return;
            }
            if (result.idempotentReplay)
            {
                RequireInventoryRecovery(0);
                RequestSnapshot();
            }
            else if (result.success)
            {
                AlchemySnapshotApplyResult applied = snapshotState.Apply(json,
                    result.snapshot, characterDbId, result.serverTime);
                if (applied != AlchemySnapshotApplyResult.Applied
                    && applied != AlchemySnapshotApplyResult.Unchanged
                    && applied != AlchemySnapshotApplyResult.Stale)
                {
                    RequireInventoryRecovery(0);
                    RequestSnapshot();
                    Debug.LogWarning("[Alchemy] Invalid learn snapshot; "
                        + "the original request remains unresolved.");
                    return;
                }
                if (applied == AlchemySnapshotApplyResult.Stale)
                    RequestSnapshot();
                else
                {
                    if (Array.IndexOf(snapshotState.Snapshot.learnedRecipeIds,
                            learnOperation.RecipeId) < 0)
                    {
                        RequireInventoryRecovery(0);
                        RequestSnapshot();
                        Debug.LogWarning("[Alchemy] Learn result missing learned "
                            + "recipe; the original request remains unresolved.");
                        return;
                    }
                    IsSnapshotCurrent = true;
                    SnapshotChanged?.Invoke(snapshotState.Snapshot);
                }
                if (!result.requiresInventorySnapshot)
                {
                    InventoryModuleRuntime inventory =
                        FindObjectOfType<InventoryModuleRuntime>();
                    if (result.inventoryRevision <= 0
                        || result.stackItemChanges == null
                        || result.stackItemChanges.Length == 0
                        || inventory == null
                        || !inventory.ApplyAlchemyLearnInventoryChanges(
                            result.inventoryRevision, result.stackItemChanges))
                        RequireInventoryRecovery(result.inventoryRevision);
                }
            }
            else
            {
                if (result.errorCode == "alchemy_already_learned")
                    RequestSnapshot();
                if (result.errorCode == "alchemy_configuration_changed"
                    || result.errorCode == "alchemy_recipe_unavailable")
                    RequestCatalog();
                if (result.errorCode == "alchemy_book_missing")
                {
                    itemCatalogRefreshPending = true;
                    RequireInventoryRecovery(0);
                    RequestCatalog();
                }
                if (result.errorCode == "alchemy_inventory_conflict")
                    RequireInventoryRecovery(0);
            }
            learnOperation.Complete();
            learnTimeoutReported = false;
            LearnResultReceived?.Invoke(result);
        }

        private void HandleClaimResult(string json)
        {
            if (!CanUseSession() || !claimOperation.HasPending)
                return;
            AlchemyResponseDto result;
            try { result = JsonUtility.FromJson<AlchemyResponseDto>(json); }
            catch (Exception) { return; }
            if (result == null || result.msgId != AlchemyMessageIds.ClaimResult
                || result.type != AlchemyMessageIds.ClaimResult
                || !claimOperation.Matches(result.alchemyOperationSessionId,
                    result.requestSequence, result.clientRequestId))
                return;
            NoteConfiguration(result.configurationRevision);
            if (result.requiresInventorySnapshot)
                RequireInventoryRecovery(0);
            if (!result.success
                && result.errorCode == "alchemy_request_conflict")
            {
                claimOperation.MarkConflict();
                claimRetryPaused = true;
                RequestSnapshot();
                RequestRejected?.Invoke(result.errorCode);
                return;
            }
            if (!result.success
                && result.errorCode == "alchemy_persist_failed")
            {
                claimOperation.MarkSent(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                claimRetryAttempts = 0;
                RequestRejected?.Invoke(result.errorCode);
                return;
            }
            if (result.idempotentReplay)
            {
                RequireInventoryRecovery(0);
                RequestSnapshot();
            }
            else if (result.success)
            {
                if (!AlchemySnapshotWireState.TryRead(json,
                        out bool hasSnapshot, out bool snapshotIsNull,
                        out bool hasTask, out bool taskIsNull)
                    || !hasSnapshot || snapshotIsNull || !hasTask
                    || !taskIsNull || result.snapshot == null)
                {
                    RequireInventoryRecovery(0);
                    RequestSnapshot();
                    Debug.LogWarning("[Alchemy] Claim succeeded without an empty "
                        + "furnace; the original request remains unresolved.");
                    return;
                }
                AlchemySnapshotApplyResult applied = snapshotState.Apply(json,
                    result.snapshot, characterDbId, result.serverTime);
                if (applied != AlchemySnapshotApplyResult.Applied
                    && applied != AlchemySnapshotApplyResult.Unchanged
                    && applied != AlchemySnapshotApplyResult.Stale)
                {
                    RequireInventoryRecovery(0);
                    RequestSnapshot();
                    Debug.LogWarning("[Alchemy] Invalid claim snapshot; "
                        + "the original request remains unresolved.");
                    return;
                }
                if (applied == AlchemySnapshotApplyResult.Unchanged
                    && snapshotState.HasCurrentTask)
                {
                    RequireInventoryRecovery(0);
                    RequestSnapshot();
                    return;
                }
                if (applied == AlchemySnapshotApplyResult.Stale
                    && snapshotState.HasCurrentTask)
                    RequestSnapshot();
                if (applied != AlchemySnapshotApplyResult.Stale)
                {
                    IsSnapshotCurrent = true;
                    SnapshotChanged?.Invoke(snapshotState.Snapshot);
                }
                if (!result.requiresInventorySnapshot
                    && (result.inventoryRevision > 0
                        || claimOperation.ExpectsOutput))
                {
                    InventoryModuleRuntime inventory =
                        FindObjectOfType<InventoryModuleRuntime>();
                    if (result.inventoryRevision <= 0
                        || result.stackItemChanges == null
                        || result.stackItemChanges.Length == 0
                        || inventory == null
                        || !inventory.ApplyAlchemyClaimInventoryChanges(
                            result.inventoryRevision, result.stackItemChanges))
                        RequireInventoryRecovery(result.inventoryRevision);
                }
            }
            else
            {
                if (result.errorCode == "alchemy_inventory_conflict"
                    || result.errorCode == "alchemy_inventory_full")
                    RequireInventoryRecovery(0);
                if (result.errorCode == "alchemy_task_missing"
                    || result.errorCode == "alchemy_not_ready"
                    || result.errorCode == "alchemy_inventory_full")
                    RequestSnapshot();
            }
            claimOperation.Complete();
            claimTimeoutReported = false;
            ClaimResultReceived?.Invoke(result);
        }

        private void NoteConfiguration(long revision)
        {
            if (revision <= 0
                || !catalogState.NoteConfigurationRevision(revision))
                return;
            itemCatalogRefreshPending = true;
            if (catalogState.PendingSequence == 0)
                RequestCatalog();
        }

        private void FlushQueuedRefresh()
        {
            if (!refreshAfterSnapshot || pendingSnapshotSequence != 0)
                return;
            if (RequestSnapshot())
                refreshAfterSnapshot = false;
        }

        private void HandleServerError(string json)
        {
            if (!CanUseSession())
                return;
            ServerErrorMessage error;
            try { error = JsonUtility.FromJson<ServerErrorMessage>(json); }
            catch (Exception) { return; }
            if (error == null
                || (error.requestMsgId != AlchemyMessageIds.Snapshot
                    && error.requestMsgId != AlchemyMessageIds.Catalog
                    && error.requestMsgId != AlchemyMessageIds.Learn
                    && error.requestMsgId != AlchemyMessageIds.Start
                    && error.requestMsgId != AlchemyMessageIds.Claim))
                return;
            if (error.requestMsgId == AlchemyMessageIds.Learn)
            {
                if (learnOperation.HasPending)
                {
                    if (error.errorCode == "alchemy_book_missing")
                        learnOperation.Complete();
                    else
                        learnRetryPaused = true;
                }
                if (error.errorCode != "alchemy_book_missing")
                {
                    RequestSnapshot();
                    RequestRejected?.Invoke(error.errorCode);
                    return;
                }
                itemCatalogRefreshPending = true;
                if (CultivationSessionRuntime.Instance != null
                    && CultivationSessionRuntime.Instance.RequestItemCatalog())
                    itemCatalogRefreshPending = false;
                FindObjectOfType<InventoryModuleRuntime>()
                    ?.RequestReconciliation();
                RequestSnapshot();
                RequestRejected?.Invoke(error.errorCode);
                GameNoticeService.Show("该丹方书当前不可学习，请等待目录同步");
                return;
            }
            if (error.requestMsgId == AlchemyMessageIds.Claim)
            {
                if (!claimOperation.HasPending)
                    return;
                claimRetryPaused = true;
                RequestSnapshot();
                RequestRejected?.Invoke(error.errorCode);
                return;
            }
            if (error.requestMsgId == AlchemyMessageIds.Start)
            {
                if (!startOperation.HasPending)
                    return;
                startRetryPaused = true;
                RequestSnapshot();
                RequestRejected?.Invoke(error.errorCode);
                StartRequestRejected?.Invoke(error.errorCode, error.message);
                return;
            }
            if (error.requestMsgId == AlchemyMessageIds.Catalog)
            {
                if (catalogState.PendingSequence == 0)
                    return;
                catalogState.Invalidate();
                RequestRejected?.Invoke(error.errorCode);
                if (error.errorCode != "alchemy_catalog_upgrade_required"
                    && error.errorCode != "alchemy_catalog_entry_too_large")
                    RetryCatalogOnce();
                return;
            }
            if (pendingSnapshotSequence == 0)
                return;
            pendingSnapshotSequence = 0;
            IsSnapshotCurrent = false;
            RequestRejected?.Invoke(error.errorCode);
            FlushQueuedRefresh();
        }

        private void HandleDisconnected(long epoch, string reason)
        {
            if (enteredEpoch == epoch)
                Suspend();
        }

        private void Suspend()
        {
            learnOperation.Suspend();
            startOperation.Suspend();
            claimOperation.Suspend();
            enteredEpoch = 0;
            characterDbId = 0;
            operationSessionId = null;
            pendingSnapshotSequence = 0;
            initialCatalogQueryPending = false;
            initialSnapshotQueryPending = false;
            initialQueryRetryAt = 0f;
            refreshAfterSnapshot = false;
            recoveryAttempted = false;
            catalogRecoveryAttempted = false;
            itemCatalogRefreshPending = false;
            catalogState.Reset();
            catalogSentAt = snapshotSentAt = 0f;
            IsSnapshotCurrent = false;
            snapshotState.Reset();
            inventoryRecoveryPending = false;
            inventoryRecoveryBaseline = null;
            inventoryRecoveryRevision = 0;
            nextInventoryRepairAt = 0f;
        }

        private void Unregister()
        {
            if (dispatcher == null)
                return;
            dispatcher.Unregister(AlchemyMessageIds.CatalogResult,
                HandleCatalog);
            dispatcher.Unregister(AlchemyMessageIds.SnapshotResult,
                HandleSnapshot);
            dispatcher.Unregister(AlchemyMessageIds.Changed,
                HandleChanged);
            dispatcher.Unregister(AlchemyMessageIds.StartResult,
                HandleStartResult);
            dispatcher.Unregister(AlchemyMessageIds.LearnResult,
                HandleLearnResult);
            dispatcher.Unregister(AlchemyMessageIds.ClaimResult,
                HandleClaimResult);
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
