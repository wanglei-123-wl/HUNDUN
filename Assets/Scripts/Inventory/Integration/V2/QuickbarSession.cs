using System;
using System.Collections.Generic;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;
using Game.Items;
using UnityEngine;

namespace Game.Inventory.Integration.V2
{
    public sealed class QuickbarSession : IDisposable
    {
        private const float RequestTimeoutSeconds = 10f;
        private const float SnapshotIntervalSeconds = 0.5f;
        private const float WriteIntervalSeconds = 0.1f;

        private sealed class WriteIntent
        {
            public string MessageId;
            public int Source;
            public int Target;
            public long StackId;
        }

        private readonly NetworkManager network;
        private readonly NetMessageDispatcher dispatcher;
        private readonly Func<InventoryOwnershipSnapshot> getInventory;
        private readonly Action requestInventorySnapshot;
        private readonly ItemDefinitionTable catalog;
        private readonly Func<string, bool> canUseItem;
        private readonly Queue<WriteIntent> writeQueue = new Queue<WriteIntent>();

        private long characterDbId;
        private string sessionId;
        private long nextSnapshotSequence;
        private long nextWriteSequence;
        private QuickbarSnapshotRequestDto pendingSnapshot;
        private NetMessageBase pendingWrite;
        private string pendingResultId;
        private long pendingWriteSequence;
        private float pendingStartedAt;
        private bool retried;
        private float retryAfterAt;
        private float lastSnapshotAt = float.NegativeInfinity;
        private float lastWriteAt = float.NegativeInfinity;
        private bool needsSnapshot;
        private QuickbarSlotDto[] serverSlots;
        private QuickbarSlotDto[] visibleSlots;
        private long serverInventoryRevision;
        private InventoryOwnershipSnapshot lastInventory;

        public long QuickbarRevision { get; private set; }
        public long InventoryRevision => serverInventoryRevision;
        public bool IsReady { get; private set; }
        public IReadOnlyList<QuickbarSlotDto> Slots =>
            Array.AsReadOnly(visibleSlots ?? new QuickbarSlotDto[0]);
        public event Action StateChanged;
        public event Action<string> OperationFailed;

        public QuickbarSession(NetworkManager network, NetMessageDispatcher dispatcher,
            Func<InventoryOwnershipSnapshot> getInventory,
            Action requestInventorySnapshot, ItemDefinitionTable catalog,
            Func<string, bool> canUseItem = null)
        {
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.getInventory = getInventory ?? throw new ArgumentNullException(nameof(getInventory));
            this.requestInventorySnapshot = requestInventorySnapshot;
            this.catalog = catalog;
            this.canUseItem = canUseItem;
            dispatcher.Register(QuickbarProtocol.SnapshotResult, HandleSnapshotResult);
            dispatcher.Register(QuickbarProtocol.AssignResult, HandleWriteResult);
            dispatcher.Register(QuickbarProtocol.ClearResult, HandleWriteResult);
            dispatcher.Register(QuickbarProtocol.MoveResult, HandleWriteResult);
            dispatcher.Register("error", HandleError);
        }

        public void BeginSession(long activeCharacterDbId, string operationSessionId,
            int protocolVersion, int slotCount)
        {
            Reset();
            if (activeCharacterDbId <= 0 || string.IsNullOrWhiteSpace(operationSessionId)
                || protocolVersion != QuickbarProtocol.Version
                || slotCount != QuickbarProtocol.SlotCount)
            {
                Debug.LogWarning("[Quickbar] Enter-world quickbar session or protocol is unavailable.");
                return;
            }

            characterDbId = activeCharacterDbId;
            sessionId = operationSessionId;
            needsSnapshot = true;
        }

        public void Reset()
        {
            bool hadState = IsReady || serverSlots != null;
            characterDbId = 0;
            sessionId = null;
            nextSnapshotSequence = 0;
            nextWriteSequence = 0;
            pendingSnapshot = null;
            pendingWrite = null;
            pendingResultId = null;
            pendingWriteSequence = 0;
            retried = false;
            retryAfterAt = 0f;
            lastSnapshotAt = float.NegativeInfinity;
            lastWriteAt = float.NegativeInfinity;
            needsSnapshot = false;
            serverSlots = null;
            visibleSlots = null;
            lastInventory = null;
            serverInventoryRevision = 0;
            QuickbarRevision = 0;
            IsReady = false;
            writeQueue.Clear();
            if (hadState) StateChanged?.Invoke();
        }

        public void Tick()
        {
            if (characterDbId <= 0 || !network.IsConnected) return;
            InventoryOwnershipSnapshot inventory = getInventory();
            if (inventory == null || inventory.CharacterDbId != characterDbId) return;

            if (!ReferenceEquals(lastInventory, inventory))
            {
                lastInventory = inventory;
                if (serverSlots != null) ReconcileInventory(inventory);
            }

            if (pendingSnapshot != null && TimedOut())
            {
                if (!retried)
                {
                    retried = true;
                    pendingStartedAt = Time.unscaledTime;
                    if (!network.Send(pendingSnapshot))
                        Debug.LogWarning("[Quickbar] Snapshot retry could not be sent.");
                }
                else
                {
                    pendingSnapshot = null;
                    needsSnapshot = true;
                    Debug.LogWarning("[Quickbar] Snapshot timed out; retrying after interval.");
                }
            }

            if (pendingWrite != null && retryAfterAt > 0f
                && Time.unscaledTime >= retryAfterAt)
            {
                retryAfterAt = 0f;
                pendingStartedAt = Time.unscaledTime;
                if (!SendPendingWrite())
                    Debug.LogWarning("[Quickbar] Rate-limited write retry could not be sent.");
            }

            if (pendingWrite != null && TimedOut())
            {
                if (!retried)
                {
                    retried = true;
                    pendingStartedAt = Time.unscaledTime;
                    if (!SendPendingWrite())
                        Debug.LogWarning("[Quickbar] Write retry could not be sent.");
                }
                else
                {
                    pendingWrite = null;
                    pendingResultId = null;
                    writeQueue.Clear();
                    needsSnapshot = true;
                    OperationFailed?.Invoke("quickbar_timeout");
                }
            }

            if (pendingSnapshot == null && pendingWrite == null && needsSnapshot
                && Time.unscaledTime - lastSnapshotAt >= SnapshotIntervalSeconds)
            {
                SendSnapshot();
            }
            else if (pendingSnapshot == null && pendingWrite == null && IsReady
                && !needsSnapshot && writeQueue.Count > 0
                && Time.unscaledTime - lastWriteAt >= WriteIntervalSeconds)
            {
                SendNextWrite(inventory);
            }
        }

        public bool TryAssign(int slotIndex, long stackId)
        {
            if (!CanQueue() || !ValidSlot(slotIndex) || stackId <= 0
                || !HasConsumableStack(getInventory(), stackId)) return false;
            writeQueue.Enqueue(new WriteIntent
            {
                MessageId = QuickbarProtocol.Assign,
                Target = slotIndex,
                StackId = stackId
            });
            return true;
        }

        public bool TryClear(int slotIndex)
        {
            if (!CanQueue() || !ValidSlot(slotIndex)) return false;
            writeQueue.Enqueue(new WriteIntent
            {
                MessageId = QuickbarProtocol.Clear,
                Source = slotIndex
            });
            return true;
        }

        public bool TryMove(int sourceSlotIndex, int targetSlotIndex)
        {
            if (!CanQueue() || !ValidSlot(sourceSlotIndex)
                || !ValidSlot(targetSlotIndex)
                || sourceSlotIndex == targetSlotIndex) return false;
            writeQueue.Enqueue(new WriteIntent
            {
                MessageId = QuickbarProtocol.Move,
                Source = sourceSlotIndex,
                Target = targetSlotIndex
            });
            return true;
        }

        public QuickbarSlotDto GetSlot(int slotIndex)
        {
            if (!IsReady || !ValidSlot(slotIndex)) return null;
            return Clone(visibleSlots[slotIndex - 1]);
        }

        public void RequestSnapshot()
        {
            if (characterDbId > 0) needsSnapshot = true;
        }

        public void Dispose()
        {
            dispatcher.Unregister(QuickbarProtocol.SnapshotResult, HandleSnapshotResult);
            dispatcher.Unregister(QuickbarProtocol.AssignResult, HandleWriteResult);
            dispatcher.Unregister(QuickbarProtocol.ClearResult, HandleWriteResult);
            dispatcher.Unregister(QuickbarProtocol.MoveResult, HandleWriteResult);
            dispatcher.Unregister("error", HandleError);
            Reset();
        }

        private bool CanQueue() =>
            characterDbId > 0 && network.IsConnected && IsReady && !needsSnapshot;

        private static bool ValidSlot(int index) =>
            index >= 1 && index <= QuickbarProtocol.SlotCount;

        private bool HasConsumableStack(InventoryOwnershipSnapshot inventory, long stackId)
        {
            if (inventory == null || catalog == null) return false;
            foreach (InventoryStackState stack in inventory.Stacks)
            {
                if (stack.StackId == stackId && stack.Count > 0
                    && (canUseItem != null
                        ? canUseItem(stack.ItemId)
                        : catalog.TryGet(stack.ItemId, out ItemDefinition definition)
                            && definition != null
                            && definition.Category == ItemCategory.Consumable))
                    return true;
            }
            return false;
        }

        private bool TimedOut() =>
            Time.unscaledTime - pendingStartedAt >= RequestTimeoutSeconds;

        private void SendSnapshot()
        {
            if (pendingSnapshot != null || pendingWrite != null) return;
            var request = new QuickbarSnapshotRequestDto(nextSnapshotSequence + 1);
            if (!network.Send(request)) return;
            nextSnapshotSequence = request.requestSequence;
            pendingSnapshot = request;
            pendingStartedAt = Time.unscaledTime;
            lastSnapshotAt = Time.unscaledTime;
            retried = false;
            needsSnapshot = false;
        }

        private void SendNextWrite(InventoryOwnershipSnapshot inventory)
        {
            WriteIntent intent = writeQueue.Dequeue();
            long sequence = nextWriteSequence + 1;
            if (intent.MessageId == QuickbarProtocol.Assign)
            {
                if (!HasConsumableStack(inventory, intent.StackId)) return;
                pendingWrite = new QuickbarAssignRequestDto(sessionId, sequence,
                    QuickbarRevision, inventory.InventoryRevision, intent.Target, intent.StackId);
                pendingResultId = QuickbarProtocol.AssignResult;
            }
            else if (intent.MessageId == QuickbarProtocol.Clear)
            {
                pendingWrite = new QuickbarClearRequestDto(sessionId, sequence,
                    QuickbarRevision, inventory.InventoryRevision, intent.Source);
                pendingResultId = QuickbarProtocol.ClearResult;
            }
            else
            {
                pendingWrite = new QuickbarMoveRequestDto(sessionId, sequence,
                    QuickbarRevision, inventory.InventoryRevision, intent.Source, intent.Target);
                pendingResultId = QuickbarProtocol.MoveResult;
            }

            pendingWriteSequence = sequence;
            nextWriteSequence = sequence;
            pendingStartedAt = Time.unscaledTime;
            lastWriteAt = Time.unscaledTime;
            retried = false;
            retryAfterAt = 0f;
            if (!SendPendingWrite())
                Debug.LogWarning("[Quickbar] Write could not be sent; retaining the same request for retry.");
        }

        private bool SendPendingWrite()
        {
            if (pendingWrite is QuickbarAssignRequestDto assign) return network.Send(assign);
            if (pendingWrite is QuickbarClearRequestDto clear) return network.Send(clear);
            if (pendingWrite is QuickbarMoveRequestDto move) return network.Send(move);
            return false;
        }

        private void HandleSnapshotResult(string json)
        {
            QuickbarSnapshotResultDto result;
            try { result = JsonUtility.FromJson<QuickbarSnapshotResultDto>(json); }
            catch (Exception) { return; }
            if (result == null || pendingSnapshot == null
                || result.requestSequence != pendingSnapshot.requestSequence
                || result.characterDbId != characterDbId) return;

            pendingSnapshot = null;
            if (!result.success || result.quickbarRevision < QuickbarRevision
                || !AcceptLayout(result.protocolVersion, result.slotCount,
                    result.quickbarRevision, result.inventoryRevision, result.slots))
            {
                needsSnapshot = true;
                if (result.errorCode == "quickbar_rate_limited")
                    lastSnapshotAt = Time.unscaledTime + SnapshotIntervalSeconds;
                OperationFailed?.Invoke(string.IsNullOrWhiteSpace(result.errorCode)
                    ? "quickbar_snapshot_failed" : result.errorCode);
                Debug.LogWarning("[Quickbar] Snapshot rejected: " + result.errorCode);
            }
        }

        private void HandleWriteResult(string json)
        {
            QuickbarWriteResultDto result;
            try { result = JsonUtility.FromJson<QuickbarWriteResultDto>(json); }
            catch (Exception) { return; }
            if (result == null || pendingWrite == null
                || !string.Equals(result.msgId, pendingResultId, StringComparison.Ordinal)
                || result.characterDbId != characterDbId
                || !string.Equals(result.quickbarOperationSessionId, sessionId,
                    StringComparison.Ordinal)
                || result.requestSequence != pendingWriteSequence) return;

            if (!result.success && result.errorCode == "quickbar_rate_limited")
            {
                retryAfterAt = Time.unscaledTime + 1f;
                pendingStartedAt = Time.unscaledTime;
                retried = false;
                return;
            }

            if (!result.success
                && result.errorCode == "quickbar_operation_session_invalid")
            {
                Reset();
                OperationFailed?.Invoke(result.errorCode);
                return;
            }

            pendingWrite = null;
            pendingResultId = null;
            retryAfterAt = 0f;
            bool refreshLayout = result.requiresSnapshot || result.idempotentReplay
                || result.quickbarRevision < QuickbarRevision;
            bool applied = result.success && !refreshLayout
                && AcceptLayout(result.protocolVersion, result.slotCount,
                    result.quickbarRevision, result.inventoryRevision, result.slots);
            if (!applied)
            {
                writeQueue.Clear();
                if (refreshLayout || result.success
                    || result.errorCode == "quickbar_revision_conflict"
                    || result.errorCode == "quickbar_inventory_revision_conflict"
                    || result.errorCode == "inventory_revision_conflict")
                    needsSnapshot = true;
                if (result.inventoryRevision > (getInventory()?.InventoryRevision ?? -1))
                    requestInventorySnapshot?.Invoke();
                if (!result.success) OperationFailed?.Invoke(result.errorCode ?? string.Empty);
            }
        }

        private bool AcceptLayout(int protocolVersion, int slotCount,
            long revision, long inventoryRevision, QuickbarSlotDto[] slots)
        {
            if (protocolVersion != QuickbarProtocol.Version
                || slotCount != QuickbarProtocol.SlotCount || slots == null
                || slots.Length != QuickbarProtocol.SlotCount
                || revision < QuickbarRevision || inventoryRevision < 0)
                return false;

            var accepted = new QuickbarSlotDto[QuickbarProtocol.SlotCount];
            foreach (QuickbarSlotDto slot in slots)
            {
                if (slot == null || !ValidSlot(slot.slotIndex)
                    || slot.stackId < 0 || slot.count < 0
                    || (slot.stackId > 0 && string.IsNullOrWhiteSpace(slot.itemId))
                    || accepted[slot.slotIndex - 1] != null)
                    return false;
                accepted[slot.slotIndex - 1] = Clone(slot);
                if (slot.stackId == 0)
                {
                    accepted[slot.slotIndex - 1].itemId = null;
                    accepted[slot.slotIndex - 1].count = 0;
                    accepted[slot.slotIndex - 1].isBound = false;
                }
            }

            serverSlots = accepted;
            QuickbarRevision = revision;
            serverInventoryRevision = inventoryRevision;
            ReconcileInventory(getInventory());
            return true;
        }

        private void ReconcileInventory(InventoryOwnershipSnapshot inventory)
        {
            if (serverSlots == null || inventory == null
                || inventory.CharacterDbId != characterDbId) return;
            if (inventory.InventoryRevision < serverInventoryRevision)
            {
                if (IsReady)
                {
                    IsReady = false;
                    StateChanged?.Invoke();
                }
                requestInventorySnapshot?.Invoke();
                return;
            }

            var projected = new QuickbarSlotDto[QuickbarProtocol.SlotCount];
            bool missingStack = false;
            for (int index = 0; index < projected.Length; index++)
            {
                QuickbarSlotDto slot = Clone(serverSlots[index]);
                if (slot.stackId > 0)
                {
                    InventoryStackState stack = null;
                    foreach (InventoryStackState candidate in inventory.Stacks)
                        if (candidate.StackId == slot.stackId) { stack = candidate; break; }
                    if (stack == null || stack.Count <= 0 || stack.ItemId != slot.itemId)
                    {
                        slot.stackId = 0;
                        slot.itemId = null;
                        slot.count = 0;
                        slot.isBound = false;
                        missingStack = true;
                    }
                    else
                    {
                        slot.count = stack.Count;
                        slot.isBound = stack.IsBound;
                    }
                }
                projected[index] = slot;
            }

            visibleSlots = projected;
            IsReady = true;
            StateChanged?.Invoke();
            if (missingStack)
            {
                needsSnapshot = true;
                if (inventory.InventoryRevision == serverInventoryRevision)
                    requestInventorySnapshot?.Invoke();
            }
        }

        private void HandleError(string json)
        {
            ServerErrorMessage error;
            try { error = JsonUtility.FromJson<ServerErrorMessage>(json); }
            catch (Exception) { return; }
            if (error == null) return;

            if (error.requestMsgId == QuickbarProtocol.Snapshot
                && pendingSnapshot != null
                && (error.expectedRequestSequence <= 0
                    || error.expectedRequestSequence == pendingSnapshot.requestSequence))
            {
                pendingSnapshot = null;
                needsSnapshot = true;
                OperationFailed?.Invoke(string.IsNullOrWhiteSpace(error.errorCode)
                    ? "quickbar_snapshot_failed" : error.errorCode);
                return;
            }

            if (pendingWrite == null || error.requestMsgId != pendingWrite.msgId
                || (error.expectedRequestSequence > 0
                    && error.expectedRequestSequence != pendingWriteSequence)) return;
            pendingWrite = null;
            pendingResultId = null;
            retryAfterAt = 0f;
            writeQueue.Clear();
            if (error.errorCode == "quickbar_operation_session_invalid")
            {
                Reset();
            }
            else
            {
                needsSnapshot = true;
                if (error.errorCode != null
                    && error.errorCode.IndexOf("inventory", StringComparison.Ordinal) >= 0)
                    requestInventorySnapshot?.Invoke();
            }
            OperationFailed?.Invoke(error.errorCode ?? string.Empty);
        }

        private static QuickbarSlotDto Clone(QuickbarSlotDto slot) =>
            new QuickbarSlotDto
            {
                slotIndex = slot.slotIndex,
                stackId = slot.stackId,
                itemId = slot.itemId,
                count = slot.count,
                isBound = slot.isBound
            };
    }
}
