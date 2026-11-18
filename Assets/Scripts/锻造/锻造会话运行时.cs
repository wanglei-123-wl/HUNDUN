using System;
using System.Collections.Generic;
using Game.Forging;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;
using Game.Forging.Protocol;
using Game.UI.Notice;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Forging.Runtime
{
    [DisallowMultipleComponent]
    public sealed class 锻造会话运行时 : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float queryTimeoutSeconds = 10f;
        [SerializeField, Min(1f)] private float requestTimeoutSeconds = 10f;
        private readonly 锻造目录状态 catalog = new 锻造目录状态();
        private readonly 锻造任务状态 tasks = new 锻造任务状态();
        private readonly 锻造学习状态 learn = new 锻造学习状态();
        private readonly 锻造开工状态 start = new 锻造开工状态();
        private readonly 锻造领取状态 claim = new 锻造领取状态();
        private NetworkManager network;
        private NetMessageDispatcher dispatcher;
        private GameSession session;
        private long enteredEpoch;
        private long characterDbId;
        private int activeProtocolVersion;
        private string operationSessionId;
        private long nextRequestSequence = 1;
        private long pendingSnapshotSequence;
        private float catalogSentAt;
        private float snapshotSentAt;
        private float retryAt;
        private float clockAt;
        private bool catalogRefreshNeeded;
        private bool snapshotRefreshNeeded;
        private bool inventoryRefreshNeeded;
        private string[] learnedRecipeIds;
        private bool learnRetryPaused;
        private bool learnTimeoutReported;
        private int learnRetryAttempts;
        private long lastLearnAttemptAt;
        private string completionQueriedTaskId;
        private bool startConfirmed;
        private bool startSnapshotVerified;
        private bool startRetryPaused;
        private bool startTimeoutReported;
        private int startRetryAttempts;
        private long lastStartAttemptAt;
        private float nextRepairAt;
        private bool claimConfirmed;
        private bool claimSnapshotVerified;
        private bool claimSucceeded;
        private bool claimRetryPaused;
        private bool claimTimeoutReported;
        private int claimRetryAttempts;
        private long lastClaimAttemptAt;
        private long claimTargetInventoryRevision;
        private float nextClaimRepairAt;

        public static 锻造会话运行时 Instance { get; private set; }
        public 锻造配方[] Catalog => catalog.Recipes;
        public long ConfigurationRevision => catalog.ConfigurationRevision;
        public bool IsCatalogCurrent => IsSessionActive && catalog.IsCurrent;
        public 锻造快照 Snapshot => tasks.Snapshot;
        public bool IsSnapshotCurrent { get; private set; }
        public bool HasUnresolvedStart => start.HasPending;
        public bool HasUnresolvedClaim => claim.HasPending;
        public bool HasUnresolvedLearn => learn.HasPending;
        public IReadOnlyList<string> LearnedRecipeIds => learnedRecipeIds;
        public bool UsesBlueprintLearning => activeProtocolVersion == 锻造消息.兼容版本;
        public long EstimatedServerTime => tasks.LastServerTime <= 0 ? 0
            : tasks.LastServerTime + (long)(Mathf.Max(0f,
                Time.realtimeSinceStartup - clockAt) * 1000f);
        public bool IsSessionActive => network != null && network.IsConnected
            && enteredEpoch > 0 && network.ConnectionEpoch == enteredEpoch
            && characterDbId > 0 && session != null
            && session.LocalCharacterDbId == characterDbId
            && 锻造消息.IsSupportedVersion(activeProtocolVersion)
            && session.ForgingProtocolVersion == activeProtocolVersion
            && string.Equals(session.ForgingOperationSessionId,
                operationSessionId, StringComparison.Ordinal);
        public event Action<锻造响应> MessageReceived;
        public event Action<锻造配方[]> CatalogChanged;
        public event Action<锻造快照> SnapshotChanged;
        public event Action LearnedRecipesChanged;
        public event Action StartStateChanged;
        public event Action ClaimStateChanged;

        public static 锻造会话运行时 Ensure(NetworkManager activeNetwork,
            GameSession activeSession)
        {
            if (activeNetwork == null || activeSession == null)
                throw new ArgumentNullException(activeNetwork == null
                    ? nameof(activeNetwork) : nameof(activeSession));
            锻造会话运行时 runtime =
                activeNetwork.GetComponent<锻造会话运行时>();
            if (runtime == null)
                runtime = activeNetwork.gameObject.AddComponent<锻造会话运行时>();
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
                dispatcher?.Register(锻造消息.目录结果, HandleCatalog);
                dispatcher?.Register(锻造消息.快照结果, HandleSnapshot);
                dispatcher?.Register(锻造消息.学习结果, HandleLearn);
                dispatcher?.Register(锻造消息.开工结果, HandleStart);
                dispatcher?.Register(锻造消息.领取结果, HandleClaim);
                dispatcher?.Register(锻造消息.变化, HandleChanged);
                dispatcher?.Register("error", HandleError);
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
            int protocolVersion, string sessionId)
        {
            Suspend();
            if (session != owner || network == null || !network.IsConnected
                || activeCharacterDbId <= 0
                || !锻造消息.IsSupportedVersion(protocolVersion)
                || !Guid.TryParseExact(sessionId, "D", out _))
            {
                Debug.LogWarning("[Forging][Session] entry rejected"
                    + " ownerMatches=" + (session == owner)
                    + " networkPresent=" + (network != null)
                    + " connected=" + (network != null && network.IsConnected)
                    + " characterValid=" + (activeCharacterDbId > 0)
                    + " protocolVersion=" + protocolVersion
                    + " sessionIdValid=" + Guid.TryParseExact(sessionId, "D", out _));
                return;
            }
            characterDbId = activeCharacterDbId;
            activeProtocolVersion = protocolVersion;
            operationSessionId = sessionId;
            enteredEpoch = network.ConnectionEpoch;
            nextRequestSequence = start.EnterWorld(characterDbId,
                operationSessionId, nextRequestSequence);
            nextRequestSequence = claim.EnterWorld(characterDbId,
                operationSessionId, nextRequestSequence);
            nextRequestSequence = learn.EnterWorld(characterDbId,
                operationSessionId, nextRequestSequence);
            catalogRefreshNeeded = true;
            snapshotRefreshNeeded = true;
            inventoryRefreshNeeded = true;
            retryAt = 0f;
        }

        public void SuspendSession(GameSession owner)
        {
            if (session == owner)
                Suspend();
        }

        public bool RequestCatalog()
        {
            if (!IsSessionActive || catalog.PendingSequence != 0
                || nextRequestSequence <= 0
                || nextRequestSequence == long.MaxValue)
                return false;
            var request = new 锻造目录请求(nextRequestSequence++);
            if (!network.Send(request))
                return false;
            catalog.BeginQuery(request.requestSequence);
            catalogSentAt = Time.unscaledTime;
            catalogRefreshNeeded = false;
            return true;
        }

        public bool RequestSnapshot()
        {
            if (!IsSessionActive || pendingSnapshotSequence != 0
                || nextRequestSequence <= 0
                || nextRequestSequence == long.MaxValue)
                return false;
            var request = new 锻造快照请求(nextRequestSequence++);
            if (!network.Send(request))
                return false;
            pendingSnapshotSequence = request.requestSequence;
            snapshotSentAt = Time.unscaledTime;
            snapshotRefreshNeeded = false;
            return true;
        }

        public bool TryStart(锻造开始意向 intent,
            InventoryOwnershipSnapshot ownership)
        {
            if (!CanStart(intent, ownership)
                || nextRequestSequence <= 0
                || nextRequestSequence == long.MaxValue)
                return false;
            var materials = new 锻造所选材料[intent.Materials.Count];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = new 锻造所选材料(intent.Materials[i].StackId,
                    intent.Materials[i].Count);
            if (!start.Prepare(nextRequestSequence++, ConfigurationRevision,
                ownership.InventoryRevision, intent.RecipeId, materials,
                out 锻造开工请求 request))
                return false;
            startRetryAttempts = 0;
            startTimeoutReported = false;
            lastStartAttemptAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool sent = network.Send(request);
            start.MarkSent(lastStartAttemptAt);
            StartStateChanged?.Invoke();
            if (!sent)
                Debug.LogWarning("[Forging] Start send is uncertain; "
                    + "the saved request will be retried.");
            return true;
        }

        public bool TryLearn(long stackId, InventoryOwnershipSnapshot ownership)
        {
            if (!IsSessionActive)
                return RejectLearn(InactiveSessionReason(), stackId, ownership);
            if (learn.HasPending)
                return RejectLearn(learn.HasConflict
                    ? "learn_pending_conflict" : "learn_pending", stackId, ownership);
            if (start.HasPending)
                return RejectLearn("start_pending", stackId, ownership);
            if (claim.HasPending)
                return RejectLearn("claim_pending", stackId, ownership);
            if (stackId <= 0)
                return RejectLearn("invalid_stack_id", stackId, ownership);
            if (ownership == null)
                return RejectLearn("inventory_snapshot_missing", stackId, ownership);
            if (ownership.CharacterDbId != characterDbId)
                return RejectLearn("inventory_character_mismatch", stackId, ownership);
            if (ownership.InventoryRevision < 0)
                return RejectLearn("inventory_revision_invalid", stackId, ownership);
            if (!ReferenceEquals(FindObjectOfType<InventoryModuleRuntime>()
                ?.CurrentSnapshot, ownership))
                return RejectLearn("inventory_snapshot_stale", stackId, ownership);
            if (nextRequestSequence <= 0 || nextRequestSequence == long.MaxValue)
                return RejectLearn("request_sequence_invalid", stackId, ownership);
            bool owned = false;
            foreach (InventoryStackState stack in ownership.Stacks)
                if (stack.StackId == stackId && stack.Count > 0)
                {
                    owned = true;
                    break;
                }
            if (!owned)
                return RejectLearn("stack_not_owned", stackId, ownership);
            if (!learn.Prepare(nextRequestSequence++,
                ownership.InventoryRevision, stackId,
                out 锻造学习请求 request))
                return RejectLearn("request_prepare_failed", stackId, ownership);
            learnRetryAttempts = 0;
            learnTimeoutReported = false;
            lastLearnAttemptAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool sent = network.Send(request);
            learn.MarkSent(lastLearnAttemptAt);
            StartStateChanged?.Invoke();
            if (!sent)
                Debug.LogWarning("[Forging] Learn send is uncertain; "
                    + "the saved request will be retried.");
            return true;
        }

        private string InactiveSessionReason()
        {
            if (network == null) return "network_missing";
            if (!network.IsConnected) return "network_disconnected";
            if (enteredEpoch <= 0) return "session_not_started";
            if (network.ConnectionEpoch != enteredEpoch) return "connection_epoch_mismatch";
            if (characterDbId <= 0) return "session_character_invalid";
            if (session == null) return "game_session_missing";
            if (session.LocalCharacterDbId != characterDbId)
                return "session_character_mismatch";
            if (session.ForgingProtocolVersion != activeProtocolVersion)
                return "protocol_version_mismatch";
            if (!string.Equals(session.ForgingOperationSessionId,
                operationSessionId, StringComparison.Ordinal))
                return "operation_session_mismatch";
            return "session_inactive";
        }

        private bool RejectLearn(string reason, long stackId,
            InventoryOwnershipSnapshot ownership)
        {
            InventoryOwnershipSnapshot current =
                FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot;
            Debug.LogWarning("[Forging][Learn] rejected reason=" + reason
                + " stackId=" + stackId
                + " inventoryRevision=" + (ownership?.InventoryRevision ?? -1)
                + " currentInventoryRevision=" + (current?.InventoryRevision ?? -1)
                + " inventoryCharacterId=" + (ownership?.CharacterDbId ?? 0)
                + " sessionCharacterId=" + characterDbId
                + " snapshotSame=" + ReferenceEquals(ownership, current)
                + " enteredEpoch=" + enteredEpoch
                + " connectionEpoch=" + (network?.ConnectionEpoch ?? 0)
                + " ownerProtocolVersion=" + (session?.ForgingProtocolVersion ?? 0)
                + " ownerSessionIdValid=" + Guid.TryParseExact(
                    session?.ForgingOperationSessionId, "D", out _));
            return false;
        }

        public bool IsRecipeLearned(string recipeId)
        {
            return !string.IsNullOrWhiteSpace(recipeId)
                && (learnedRecipeIds == null
                    ? activeProtocolVersion == 锻造消息.支持版本
                    : Array.IndexOf(learnedRecipeIds, recipeId) >= 0);
        }

        public bool CanClaim(string taskId, InventoryOwnershipSnapshot ownership)
        {
            锻造任务 task = tasks.Snapshot?.currentTask;
            return IsSessionActive && IsSnapshotCurrent
                && !start.HasPending && !claim.HasPending && !learn.HasPending
                && task != null && task.status == "ready"
                && task.succeeded.HasValue && task.taskId == taskId
                && ownership != null && ownership.CharacterDbId == characterDbId
                && ownership.InventoryRevision >= 0
                && ReferenceEquals(FindObjectOfType<InventoryModuleRuntime>()
                    ?.CurrentSnapshot, ownership);
        }

        public bool TryClaim(string taskId, InventoryOwnershipSnapshot ownership)
        {
            if (!CanClaim(taskId, ownership)
                || nextRequestSequence <= 0
                || nextRequestSequence == long.MaxValue
                || !claim.Prepare(nextRequestSequence++, ownership.InventoryRevision,
                    tasks.Snapshot.revision, taskId, out 锻造领取请求 request))
                return false;
            claimRetryAttempts = 0;
            claimTimeoutReported = false;
            lastClaimAttemptAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool sent = network.Send(request);
            claim.MarkSent(lastClaimAttemptAt);
            ClaimStateChanged?.Invoke();
            if (!sent)
                Debug.LogWarning("[Forging] Claim send is uncertain; "
                    + "the saved request will be retried.");
            return true;
        }

        public bool CanStart(锻造开始意向 intent,
            InventoryOwnershipSnapshot ownership)
        {
            if (!IsCatalogCurrent || ConfigurationRevision <= 0
                || !IsSnapshotCurrent || tasks.Snapshot == null
                || tasks.Snapshot.currentTask != null || start.HasPending
                || claim.HasPending || learn.HasPending
                || intent == null || ownership == null
                || ownership.CharacterDbId != characterDbId
                || intent.CharacterDbId != characterDbId
                || intent.InventoryRevision != ownership.InventoryRevision
                || ownership.InventoryRevision < 0
                || !ReferenceEquals(FindObjectOfType<InventoryModuleRuntime>()
                    ?.CurrentSnapshot, ownership)
                || intent.Materials == null || intent.Materials.Count > 500)
                return false;
            锻造配方 recipe = Array.Find(catalog.Recipes,
                value => value != null && string.Equals(value.recipeId,
                    intent.RecipeId, StringComparison.Ordinal));
            if (recipe == null)
                return false;
            if (!IsRecipeLearned(intent.RecipeId))
                return false;
            var required = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (锻造配方材料 material in recipe.materials)
            {
                if (material.isCurrency)
                {
                    if (!ownership.Wallets.TryGetValue(material.itemId,
                            out long balance) || balance < material.count)
                        return false;
                }
                else
                    required.Add(material.itemId, material.count);
            }
            var seen = new HashSet<long>();
            foreach (锻造已选材料 selected in intent.Materials)
            {
                if (selected == null || selected.StackId <= 0
                    || selected.Count <= 0 || !seen.Add(selected.StackId)
                    || !required.TryGetValue(selected.ItemId,
                        out long remaining))
                    return false;
                bool owned = false;
                foreach (InventoryStackState stack in ownership.Stacks)
                    if (stack.StackId == selected.StackId
                        && stack.ItemId == selected.ItemId
                        && stack.Count >= selected.Count)
                    {
                        owned = true;
                        break;
                    }
                if (!owned || remaining < selected.Count)
                    return false;
                required[selected.ItemId] = remaining - selected.Count;
            }
            foreach (long remaining in required.Values)
                if (remaining != 0)
                    return false;
            return true;
        }

        private void Update()
        {
            if (!IsSessionActive)
                return;
            if (inventoryRefreshNeeded)
            {
                InventoryModuleRuntime inventory =
                    FindObjectOfType<InventoryModuleRuntime>();
                if (inventory != null)
                {
                    inventory.RequestSnapshot();
                    inventoryRefreshNeeded = false;
                }
            }
            if (catalog.PendingSequence != 0
                && Time.unscaledTime - catalogSentAt >= queryTimeoutSeconds)
            {
                catalog.Invalidate();
                catalogRefreshNeeded = true;
                retryAt = Time.unscaledTime + 1f;
            }
            if (pendingSnapshotSequence != 0
                && Time.unscaledTime - snapshotSentAt >= queryTimeoutSeconds)
            {
                pendingSnapshotSequence = 0;
                IsSnapshotCurrent = false;
                snapshotRefreshNeeded = true;
                completionQueriedTaskId = null;
                retryAt = Time.unscaledTime + 1f;
            }
            if (Time.unscaledTime >= retryAt)
            {
                if (catalogRefreshNeeded && !RequestCatalog())
                    retryAt = Time.unscaledTime + 1f;
                if (snapshotRefreshNeeded && !RequestSnapshot())
                    retryAt = Time.unscaledTime + 1f;
            }
            RetryStart();
            ResolveStart();
            RetryClaim();
            ResolveClaim();
            RetryLearn();
            锻造任务 task = tasks.Snapshot?.currentTask;
            if (task != null && task.status == "active"
                && EstimatedServerTime >= task.endsAtUnixMilliseconds
                && completionQueriedTaskId != task.taskId
                && pendingSnapshotSequence == 0
                && Time.unscaledTime >= retryAt
                && RequestSnapshot())
                completionQueriedTaskId = task.taskId;
        }

        private void RetryLearn()
        {
            if (!learn.HasPending || learn.HasConflict || learnRetryPaused
                || start.HasPending || claim.HasPending)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (learn.NeedsRecovery)
            {
                InventoryOwnershipSnapshot ownership =
                    FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot;
                if (ownership?.CharacterDbId != characterDbId
                    || now - lastLearnAttemptAt < 1000
                    || !learn.TryGetRetry(out 锻造学习请求 recovered))
                    return;
                lastLearnAttemptAt = now;
                if (network.Send(recovered))
                    learn.MarkSent(now);
                return;
            }
            if (learnTimeoutReported || now - learn.LastSentAt
                < requestTimeoutSeconds * 1000f)
                return;
            if (learnRetryAttempts < 2
                && learn.TryGetSameSession(out 锻造学习请求 retry)
                && network.Send(retry))
            {
                learnRetryAttempts++;
                learn.MarkSent(now);
                return;
            }
            learnTimeoutReported = true;
            GameNoticeService.Show("图谱学习结果未确认，请重连后恢复");
        }

        private void RetryStart()
        {
            if (!start.HasPending || start.HasConflict || startConfirmed
                || startRetryPaused)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (start.NeedsRecovery)
            {
                InventoryOwnershipSnapshot ownership =
                    FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot;
                if (!IsSnapshotCurrent
                    || ownership?.CharacterDbId != characterDbId
                    || now - lastStartAttemptAt < 1000
                    || !start.TryGetRetry(out 锻造开工请求 recovered))
                    return;
                lastStartAttemptAt = now;
                if (network.Send(recovered))
                    start.MarkSent(now);
                return;
            }
            if (startTimeoutReported || now - start.LastSentAt
                < requestTimeoutSeconds * 1000f)
                return;
            if (startRetryAttempts < 2
                && start.TryGetSameSession(out 锻造开工请求 retry)
                && network.Send(retry))
            {
                startRetryAttempts++;
                start.MarkSent(now);
                return;
            }
            startTimeoutReported = true;
            GameNoticeService.Show("锻造开工结果未确认，请重连后恢复");
        }

        private void ResolveStart()
        {
            if (!startConfirmed || !start.HasPending)
                return;
            InventoryModuleRuntime inventory =
                FindObjectOfType<InventoryModuleRuntime>();
            InventoryOwnershipSnapshot ownership = inventory?.CurrentSnapshot;
            锻造任务 task = tasks.Snapshot?.currentTask;
            bool assetReady = ownership?.CharacterDbId == characterDbId
                && start.ExpectedInventoryRevision < long.MaxValue
                && ownership.InventoryRevision
                    >= start.ExpectedInventoryRevision + 1;
            bool taskReady = IsSnapshotCurrent && startSnapshotVerified
                && task != null
                && string.Equals(task.recipeId, start.RecipeId,
                    StringComparison.Ordinal);
            if (IsSnapshotCurrent && startSnapshotVerified
                && tasks.Snapshot != null
                && tasks.Snapshot.revision > 0 && task == null)
                taskReady = true;
            if (assetReady && taskReady)
            {
                start.Complete();
                startConfirmed = false;
                StartStateChanged?.Invoke();
                return;
            }
            if (Time.unscaledTime < nextRepairAt)
                return;
            nextRepairAt = Time.unscaledTime + 2f;
            if (!assetReady)
                inventory?.RequestSnapshot();
            if (!taskReady && pendingSnapshotSequence == 0)
                RequestSnapshot();
        }

        private void RetryClaim()
        {
            if (!claim.HasPending || claim.HasConflict || claimConfirmed
                || claimRetryPaused || start.HasPending)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (claim.NeedsRecovery)
            {
                InventoryOwnershipSnapshot ownership =
                    FindObjectOfType<InventoryModuleRuntime>()?.CurrentSnapshot;
                if (!IsSnapshotCurrent
                    || ownership?.CharacterDbId != characterDbId
                    || now - lastClaimAttemptAt < 1000
                    || !claim.TryGetRetry(out 锻造领取请求 recovered))
                    return;
                lastClaimAttemptAt = now;
                if (network.Send(recovered))
                    claim.MarkSent(now);
                return;
            }
            if (claimTimeoutReported || now - claim.LastSentAt
                < requestTimeoutSeconds * 1000f)
                return;
            if (claimRetryAttempts < 2
                && claim.TryGetSameSession(out 锻造领取请求 retry)
                && network.Send(retry))
            {
                claimRetryAttempts++;
                claim.MarkSent(now);
                return;
            }
            claimTimeoutReported = true;
            GameNoticeService.Show("锻造领取结果未确认，请重连后恢复");
        }

        private void ResolveClaim()
        {
            if (!claimConfirmed || !claim.HasPending)
                return;
            InventoryModuleRuntime inventory =
                FindObjectOfType<InventoryModuleRuntime>();
            InventoryOwnershipSnapshot ownership = inventory?.CurrentSnapshot;
            bool assetReady = !claimSucceeded
                || (ownership?.CharacterDbId == characterDbId
                    && claim.ExpectedInventoryRevision < long.MaxValue
                    && ownership.InventoryRevision >= Math.Max(
                        claim.ExpectedInventoryRevision + 1,
                        claimTargetInventoryRevision));
            bool taskReady = IsSnapshotCurrent && claimSnapshotVerified
                && tasks.Snapshot != null
                && tasks.Snapshot.revision > claim.TaskRevision
                && tasks.Snapshot.currentTask == null;
            if (assetReady && taskReady)
            {
                claim.Complete();
                claimConfirmed = false;
                ClaimStateChanged?.Invoke();
                return;
            }
            if (Time.unscaledTime < nextClaimRepairAt)
                return;
            nextClaimRepairAt = Time.unscaledTime + 2f;
            if (!assetReady)
                inventory?.RequestSnapshot();
            if (!taskReady && pendingSnapshotSequence == 0)
                RequestSnapshot();
        }

        private void HandleCatalog(string json) =>
            HandleResponse(json, 锻造消息.目录结果);

        private void HandleSnapshot(string json) =>
            HandleResponse(json, 锻造消息.快照结果);

        private void HandleLearn(string json) =>
            HandleResponse(json, 锻造消息.学习结果);

        private void HandleStart(string json) =>
            HandleResponse(json, 锻造消息.开工结果);

        private void HandleClaim(string json) =>
            HandleResponse(json, 锻造消息.领取结果);

        private void HandleChanged(string json) =>
            HandleResponse(json, 锻造消息.变化);

        private void HandleResponse(string json, string expectedId)
        {
            if (!IsSessionActive)
                return;
            锻造响应 response;
            try { response = JsonConvert.DeserializeObject<锻造响应>(json); }
            catch (JsonException) { return; }
            if (response == null || response.msgId != expectedId
                || response.type != expectedId
                || !string.Equals(response.forgingOperationSessionId,
                    operationSessionId, StringComparison.Ordinal)
                || (expectedId == 锻造消息.变化
                    ? response.requestSequence != 0
                        || !string.IsNullOrEmpty(response.clientRequestId)
                    : response.requestSequence <= 0)
                || (expectedId != 锻造消息.学习结果
                    && response.snapshot != null
                    && response.snapshot.characterDbId != characterDbId))
                return;
            if (expectedId == 锻造消息.目录结果)
            {
                锻造目录接收结果 applied = catalog.Apply(response);
                if (applied == 锻造目录接收结果.完成)
                    CatalogChanged?.Invoke(catalog.Recipes);
                else if (applied == 锻造目录接收结果.无效
                    || applied == 锻造目录接收结果.过期
                    || applied == 锻造目录接收结果.拒绝)
                {
                    catalogRefreshNeeded = applied != 锻造目录接收结果.拒绝
                        || (response.errorCode != "forging_catalog_upgrade_required"
                            && response.errorCode != "forging_catalog_entry_too_large");
                    retryAt = Time.unscaledTime + 1f;
                }
            }
            else if (expectedId == 锻造消息.快照结果
                && response.requestSequence == pendingSnapshotSequence)
            {
                pendingSnapshotSequence = 0;
                if (response.success)
                    ApplySnapshot(json, response);
                else
                {
                    IsSnapshotCurrent = false;
                    snapshotRefreshNeeded = true;
                    retryAt = Time.unscaledTime + 1f;
                }
            }
            else if (expectedId == 锻造消息.变化)
            {
                if (catalog.NoteRevision(response.configurationRevision))
                {
                    catalog.Invalidate();
                    catalogRefreshNeeded = true;
                }
                if (response.snapshot == null
                    || ApplySnapshot(json, response) == 锻造任务接收结果.无效)
                    snapshotRefreshNeeded = true;
            }
            else if (expectedId == 锻造消息.开工结果)
                HandleStartResult(json, response);
            else if (expectedId == 锻造消息.学习结果)
            {
                if (!learn.Matches(response.forgingOperationSessionId,
                    response.requestSequence, response.clientRequestId))
                    return;
                HandleLearnResult(response);
            }
            else if (expectedId == 锻造消息.领取结果)
            {
                if (!claim.Matches(response.forgingOperationSessionId,
                    response.requestSequence, response.clientRequestId))
                    return;
                HandleClaimResult(json, response);
            }
            MessageReceived?.Invoke(response);
        }

        private void HandleLearnResult(锻造响应 result)
        {
            if (!result.success && result.errorCode == "forging_persist_failed")
            {
                learn.MarkSent(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                learnRetryAttempts = 0;
                GameNoticeService.Show("图谱学习结果待确认，正在保留原请求");
                return;
            }
            if (!result.success && result.errorCode == "forging_request_conflict")
            {
                learn.MarkConflict();
                learnRetryPaused = true;
                snapshotRefreshNeeded = true;
                inventoryRefreshNeeded = true;
                StartStateChanged?.Invoke();
                GameNoticeService.Show("图谱学习请求身份冲突，已停止自动重试");
                return;
            }

            bool learnedAccepted = result.snapshot?.learnedRecipeIds != null
                && (result.snapshot.characterDbId == 0
                    || result.snapshot.characterDbId == characterDbId)
                && (result.snapshot.revision == 0
                    || tasks.Snapshot == null
                    || result.snapshot.revision >= tasks.Snapshot.revision)
                && UpdateLearnedRecipes(result.snapshot.learnedRecipeIds);
            if (result.success && !learnedAccepted)
                Debug.LogWarning("[Forging] Learn result has no valid learned "
                    + "recipes; requesting authoritative snapshots.");
            if (result.success && !result.idempotentReplay
                && !result.requiresInventorySnapshot && learnedAccepted)
                ApplyLearnInventory(result);

            learn.Complete();
            learnTimeoutReported = false;
            learnRetryAttempts = 0;
            if (result.success || result.idempotentReplay
                || result.errorCode == "forging_already_learned")
            {
                pendingSnapshotSequence = 0;
                IsSnapshotCurrent = false;
                snapshotRefreshNeeded = true;
            }
            if (result.success && !learnedAccepted
                || result.idempotentReplay
                || result.requiresInventorySnapshot
                || result.errorCode == "forging_inventory_conflict")
                inventoryRefreshNeeded = true;
            if (result.errorCode == "forging_recipe_unavailable")
            {
                catalog.Invalidate();
                catalogRefreshNeeded = true;
            }
            retryAt = 0f;
            StartStateChanged?.Invoke();
            GameNoticeService.Show(result.success
                ? "图谱学习成功，正在同步背包与配方"
                : result.errorCode == "forging_already_learned"
                    ? "该图谱已学会"
                    : result.errorCode == "forging_recipe_unavailable"
                        ? "图谱对应的配方不可用，图谱未消耗"
                        : "图谱学习失败（" + (result.errorCode ?? "unknown") + "）");
        }

        private void ApplyLearnInventory(锻造响应 result)
        {
            InventoryModuleRuntime inventory =
                FindObjectOfType<InventoryModuleRuntime>();
            InventoryOwnershipSnapshot ownership = inventory?.CurrentSnapshot;
            InventoryV2StackChangeDto[] changes = result.stackItemChanges;
            InventoryV2StackChangeDto change = changes?.Length == 1
                ? changes[0] : null;
            InventoryStackState owned = null;
            if (ownership != null)
                foreach (InventoryStackState stack in ownership.Stacks)
                    if (stack.StackId == learn.StackId)
                    {
                        owned = stack;
                        break;
                    }
            if (inventory == null || ownership?.CharacterDbId != characterDbId
                || learn.ExpectedInventoryRevision < 0
                || learn.ExpectedInventoryRevision == long.MaxValue
                || ownership.InventoryRevision
                    != learn.ExpectedInventoryRevision
                || result.inventoryRevision
                    != learn.ExpectedInventoryRevision + 1
                || owned == null || change == null
                || change.stackId != learn.StackId
                || change.newCount != owned.Count - 1
                || (change.newCount > 0
                    && (change.itemId != owned.ItemId
                        || change.isBound != owned.IsBound))
                || (change.newCount == 0
                    && !string.IsNullOrEmpty(change.itemId)
                    && change.itemId != owned.ItemId)
                || !inventory.ApplyForgingLearnInventoryChanges(
                    result.inventoryRevision, changes))
                inventoryRefreshNeeded = true;
        }

        private bool UpdateLearnedRecipes(string[] ids)
        {
            if (ids == null)
                return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                    return false;
            bool changed = learnedRecipeIds == null
                || learnedRecipeIds.Length != ids.Length;
            if (!changed)
                for (int index = 0; index < ids.Length; index++)
                    if (learnedRecipeIds[index] != ids[index])
                    {
                        changed = true;
                        break;
                    }
            if (changed)
            {
                learnedRecipeIds = (string[])ids.Clone();
                LearnedRecipesChanged?.Invoke();
            }
            return true;
        }

        private void HandleClaimResult(string json, 锻造响应 result)
        {
            if (claimConfirmed)
                return;
            if (result.configurationRevision > ConfigurationRevision
                && catalog.NoteRevision(result.configurationRevision))
            {
                catalog.Invalidate();
                catalogRefreshNeeded = true;
            }
            if (!result.success && result.errorCode == "forging_persist_failed")
            {
                claimRetryAttempts = 0;
                claim.MarkSent(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                GameNoticeService.Show("锻造领取结果尚未确认，正在保留原请求");
                return;
            }
            if (!result.success && result.errorCode == "forging_request_conflict")
            {
                claim.MarkConflict();
                claimRetryPaused = true;
                RequestSnapshot();
                GameNoticeService.Show("锻造领取请求身份冲突，已停止自动重试");
                return;
            }
            InventoryModuleRuntime inventory =
                FindObjectOfType<InventoryModuleRuntime>();
            if (!result.success)
            {
                claim.Complete();
                claimTimeoutReported = false;
                ClaimStateChanged?.Invoke();
                if (!result.idempotentReplay && result.snapshot != null)
                    ApplySnapshot(json, result);
                if (result.idempotentReplay)
                {
                    inventory?.RequestSnapshot();
                    snapshotRefreshNeeded = true;
                }
                if (result.errorCode == "forging_inventory_full"
                    || result.errorCode == "forging_inventory_conflict")
                    inventory?.RequestSnapshot();
                if (result.requiresInventorySnapshot)
                    inventory?.RequestSnapshot();
                if (result.errorCode == "forging_task_missing"
                    || result.errorCode == "forging_not_ready"
                    || result.errorCode == "forging_inventory_full")
                    RequestSnapshot();
                GameNoticeService.Show(ClaimErrorText(result.errorCode));
                return;
            }
            if (!result.forgingSucceeded.HasValue)
            {
                snapshotRefreshNeeded = true;
                inventory?.RequestSnapshot();
                GameNoticeService.Show("锻造领取回包缺少结果，请重连恢复");
                claimTimeoutReported = true;
                return;
            }
            claimConfirmed = true;
            claimSucceeded = result.forgingSucceeded.Value;
            claimSnapshotVerified = false;
            claimTargetInventoryRevision = result.inventoryRevision;
            claimTimeoutReported = false;
            if (!result.idempotentReplay && result.snapshot != null)
                ApplySnapshot(json, result);
            else
                snapshotRefreshNeeded = true;
            if (result.idempotentReplay || result.requiresInventorySnapshot)
            {
                inventory?.RequestSnapshot();
                snapshotRefreshNeeded = true;
            }
            else if (claimSucceeded)
            {
                InventoryOwnershipSnapshot ownership = inventory?.CurrentSnapshot;
                bool canApply = result.inventoryRevision > 0
                    && claim.ExpectedInventoryRevision < long.MaxValue
                    && result.inventoryRevision
                        == claim.ExpectedInventoryRevision + 1
                    && ownership?.CharacterDbId == characterDbId
                    && ownership.InventoryRevision
                        == claim.ExpectedInventoryRevision
                    && result.equipmentItems?.Length == 1
                    && result.stackItemChanges != null && result.wallets != null;
                if (!canApply || !inventory.ApplyForgingClaimInventoryChanges(
                    result.inventoryRevision, result.stackItemChanges,
                    result.equipmentItems, result.wallets))
                    inventory?.RequestSnapshot();
            }
            else if (result.inventoryRevision > 0
                || result.equipmentItems?.Length > 0
                || result.stackItemChanges?.Length > 0
                || result.wallets?.Length > 0)
                inventory?.RequestSnapshot();
            if (!result.idempotentReplay)
                GameNoticeService.Show(claimSucceeded
                    ? "锻造装备已领取" : "本次锻造失败，材料已消耗");
            ResolveClaim();
        }

        private static string ClaimErrorText(string code)
        {
            switch (code)
            {
                case "forging_inventory_full":
                    return "背包空间不足，锻造成品仍待领取";
                case "forging_not_ready":
                    return "锻造尚未完成";
                case "forging_task_missing":
                    return "锻造任务已变化，请刷新";
                case "forging_inventory_conflict":
                    return "背包已变化，请刷新后重新领取";
                default:
                    return "锻造领取失败（" + (code ?? "unknown") + "）";
            }
        }

        private void HandleStartResult(string json, 锻造响应 result)
        {
            if (!start.Matches(result.forgingOperationSessionId,
                result.requestSequence, result.clientRequestId)
                || startConfirmed)
                return;
            if (result.configurationRevision > ConfigurationRevision
                && catalog.NoteRevision(result.configurationRevision))
            {
                catalog.Invalidate();
                catalogRefreshNeeded = true;
            }
            if (!result.success && result.errorCode == "forging_persist_failed")
            {
                startRetryAttempts = 0;
                start.MarkSent(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                GameNoticeService.Show("锻造结果尚未确认，正在保留原请求重试");
                return;
            }
            if (!result.success && result.errorCode == "forging_request_conflict")
            {
                start.MarkConflict();
                startRetryPaused = true;
                RequestSnapshot();
                GameNoticeService.Show("锻造请求身份冲突，已停止自动重试");
                return;
            }
            if (!result.success)
            {
                start.Complete();
                startTimeoutReported = false;
                startRetryAttempts = 0;
                StartStateChanged?.Invoke();
                if (result.errorCode == "forging_configuration_changed"
                    || result.errorCode == "forging_recipe_unavailable"
                    || result.errorCode == "item_off_shelf")
                {
                    catalog.Invalidate();
                    catalogRefreshNeeded = true;
                }
                if (result.errorCode == "forging_occupied")
                    RequestSnapshot();
                if (result.errorCode
                    == "forging_cultivation_configuration_changed")
                    Game.Cultivation.Runtime.CultivationSessionRuntime.Instance
                        ?.Refresh();
                if (result.errorCode == "forging_inventory_conflict"
                    || result.errorCode == "forging_material_missing"
                    || result.errorCode == "forging_material_insufficient")
                    FindObjectOfType<InventoryModuleRuntime>()
                        ?.RequestReconciliation();
                if (result.requiresInventorySnapshot)
                    FindObjectOfType<InventoryModuleRuntime>()
                        ?.RequestSnapshot();
                GameNoticeService.Show(StartErrorText(result.errorCode));
                return;
            }

            startConfirmed = true;
            startSnapshotVerified = false;
            startTimeoutReported = false;
            if (!result.idempotentReplay && result.snapshot != null)
            {
                锻造任务接收结果 applied = ApplySnapshot(json, result);
                if (applied != 锻造任务接收结果.已更新
                    && applied != 锻造任务接收结果.未变化)
                    snapshotRefreshNeeded = true;
            }
            else
                snapshotRefreshNeeded = true;

            InventoryModuleRuntime inventory =
                FindObjectOfType<InventoryModuleRuntime>();
            InventoryOwnershipSnapshot ownership = inventory?.CurrentSnapshot;
            bool canApply = !result.idempotentReplay
                && !result.requiresInventorySnapshot
                && result.inventoryRevision > 0
                && start.ExpectedInventoryRevision < long.MaxValue
                && result.inventoryRevision
                    == start.ExpectedInventoryRevision + 1
                && ownership?.CharacterDbId == characterDbId
                && ownership.InventoryRevision
                    == start.ExpectedInventoryRevision
                && (result.equipmentItems == null
                    || result.equipmentItems.Length == 0)
                && result.stackItemChanges != null
                && result.wallets != null;
            if (!canApply || !inventory.ApplyForgingStartInventoryChanges(
                result.inventoryRevision, result.stackItemChanges,
                result.wallets))
                inventory?.RequestSnapshot();
            if (result.idempotentReplay || result.requiresInventorySnapshot)
                snapshotRefreshNeeded = true;
            ResolveStart();
        }

        private static string StartErrorText(string code)
        {
            switch (code)
            {
                case "forging_technique_required":
                    return "请先学习锻造类功法";
                case "forging_configuration_changed":
                case "forging_recipe_unavailable":
                case "item_off_shelf":
                    return "锻造配方已变化，请刷新后重新选材";
                case "forging_cultivation_configuration_changed":
                    return "功法配置已变化，请等待同步后重新确认";
                case "forging_occupied":
                    return "已有锻造任务，请先查看任务";
                case "forging_inventory_conflict":
                case "forging_material_missing":
                case "forging_material_insufficient":
                    return "背包材料已变化，请重新选材";
                case "forging_material_mismatch":
                case "forging_material_selection_invalid":
                case "forging_material_quantity_mismatch":
                    return "所选材料与配方不符";
                case "forging_currency_insufficient":
                    return "钱包余额不足";
                default:
                    return "锻造开工失败（" + (code ?? "unknown") + "）";
            }
        }

        private 锻造任务接收结果 ApplySnapshot(string json, 锻造响应 response)
        {
            bool wasCurrent = IsSnapshotCurrent;
            锻造任务接收结果 applied = tasks.Apply(json, response.snapshot,
                characterDbId, response.serverTime);
            if (applied == 锻造任务接收结果.已更新
                || applied == 锻造任务接收结果.未变化)
            {
                if (tasks.Snapshot?.learnedRecipeIds != null)
                    UpdateLearnedRecipes(tasks.Snapshot.learnedRecipeIds);
                IsSnapshotCurrent = true;
                if (startConfirmed)
                    startSnapshotVerified = true;
                if (claimConfirmed && tasks.Snapshot != null
                    && tasks.Snapshot.revision > claim.TaskRevision
                    && tasks.Snapshot.currentTask == null)
                    claimSnapshotVerified = true;
                clockAt = Time.realtimeSinceStartup;
                if (tasks.Snapshot?.currentTask?.status != "active")
                    completionQueriedTaskId = null;
                else if (completionQueriedTaskId
                    == tasks.Snapshot.currentTask.taskId)
                {
                    completionQueriedTaskId = null;
                    retryAt = Time.unscaledTime + 1f;
                }
                if (applied == 锻造任务接收结果.已更新 || !wasCurrent)
                    SnapshotChanged?.Invoke(tasks.Snapshot);
            }
            else if (applied == 锻造任务接收结果.无快照
                || applied == 锻造任务接收结果.无效)
            {
                IsSnapshotCurrent = false;
                snapshotRefreshNeeded = true;
                retryAt = Time.unscaledTime + 1f;
            }
            return applied;
        }

        private void HandleError(string json)
        {
            if (!IsSessionActive)
                return;
            ServerErrorMessage error;
            try { error = JsonConvert.DeserializeObject<ServerErrorMessage>(json); }
            catch (JsonException) { return; }
            if (error == null || error.msgId != "error"
                || (error.requestMsgId != 锻造消息.目录
                    && error.requestMsgId != 锻造消息.快照
                    && error.requestMsgId != 锻造消息.学习
                    && error.requestMsgId != 锻造消息.开工
                    && error.requestMsgId != 锻造消息.领取))
                return;
            if (error.requestMsgId == 锻造消息.目录
                && catalog.PendingSequence != 0)
            {
                catalog.Invalidate();
                catalogRefreshNeeded = error.errorCode
                    != "forging_catalog_upgrade_required"
                    && error.errorCode != "forging_catalog_entry_too_large";
                retryAt = Time.unscaledTime + 1f;
            }
            else if (error.requestMsgId == 锻造消息.快照
                && pendingSnapshotSequence != 0)
            {
                pendingSnapshotSequence = 0;
                IsSnapshotCurrent = false;
                snapshotRefreshNeeded = true;
                completionQueriedTaskId = null;
                retryAt = Time.unscaledTime + 1f;
            }
            else if (error.requestMsgId == 锻造消息.开工
                && start.HasPending)
            {
                startRetryPaused = true;
                snapshotRefreshNeeded = true;
                GameNoticeService.Show("锻造操作被拒绝，请重新进场确认");
            }
            else if (error.requestMsgId == 锻造消息.学习
                && learn.HasPending)
            {
                learnRetryPaused = true;
                snapshotRefreshNeeded = true;
                inventoryRefreshNeeded = true;
                GameNoticeService.Show("图谱学习请求被拒绝，请重新进场确认");
            }
            else if (error.requestMsgId == 锻造消息.领取
                && claim.HasPending)
            {
                claimRetryPaused = true;
                snapshotRefreshNeeded = true;
                GameNoticeService.Show("锻造领取被拒绝，请重新进场确认");
            }
            else
                Debug.LogWarning("[Forging] Uncorrelated error for "
                    + error.requestMsgId + ": " + error.errorCode);
        }

        private void HandleDisconnected(long epoch, string reason)
        {
            if (enteredEpoch == epoch)
                Suspend();
        }

        private void Suspend()
        {
            learn.Suspend();
            start.Suspend();
            claim.Suspend();
            enteredEpoch = 0;
            characterDbId = 0;
            activeProtocolVersion = 0;
            operationSessionId = null;
            catalog.Reset();
            tasks.Reset();
            learnedRecipeIds = null;
            pendingSnapshotSequence = 0;
            IsSnapshotCurrent = false;
            catalogRefreshNeeded = snapshotRefreshNeeded =
                inventoryRefreshNeeded = false;
            completionQueriedTaskId = null;
            learnRetryPaused = false;
            learnTimeoutReported = false;
            learnRetryAttempts = 0;
            lastLearnAttemptAt = 0;
            startConfirmed = false;
            startSnapshotVerified = false;
            startRetryPaused = false;
            startTimeoutReported = false;
            startRetryAttempts = 0;
            lastStartAttemptAt = 0;
            nextRepairAt = 0f;
            claimConfirmed = false;
            claimSnapshotVerified = false;
            claimSucceeded = false;
            claimRetryPaused = false;
            claimTimeoutReported = false;
            claimRetryAttempts = 0;
            lastClaimAttemptAt = 0;
            claimTargetInventoryRevision = 0;
            nextClaimRepairAt = 0f;
        }

        private void Unregister()
        {
            if (dispatcher == null)
                return;
            dispatcher.Unregister(锻造消息.目录结果, HandleCatalog);
            dispatcher.Unregister(锻造消息.快照结果, HandleSnapshot);
            dispatcher.Unregister(锻造消息.学习结果, HandleLearn);
            dispatcher.Unregister(锻造消息.开工结果, HandleStart);
            dispatcher.Unregister(锻造消息.领取结果, HandleClaim);
            dispatcher.Unregister(锻造消息.变化, HandleChanged);
            dispatcher.Unregister("error", HandleError);
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
