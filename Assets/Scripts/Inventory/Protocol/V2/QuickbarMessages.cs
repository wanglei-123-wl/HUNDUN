using System;

namespace Game.Inventory.Protocol.V2
{
    public static class QuickbarProtocol
    {
        public const int Version = 1;
        public const int SlotCount = 8;
        public const string Snapshot = "quickbar_snapshot";
        public const string SnapshotResult = "quickbar_snapshot_result";
        public const string Assign = "quickbar_assign";
        public const string AssignResult = "quickbar_assign_result";
        public const string Clear = "quickbar_clear";
        public const string ClearResult = "quickbar_clear_result";
        public const string Move = "quickbar_move";
        public const string MoveResult = "quickbar_move_result";
    }

    [Serializable]
    public sealed class QuickbarSnapshotRequestDto : NetMessageBase
    {
        public long requestSequence;

        public QuickbarSnapshotRequestDto(long sequence)
        {
            msgId = type = QuickbarProtocol.Snapshot;
            requestSequence = sequence;
        }
    }

    [Serializable]
    public sealed class QuickbarAssignRequestDto : NetMessageBase
    {
        public string quickbarOperationSessionId;
        public long requestSequence;
        public long expectedQuickbarRevision;
        public long expectedInventoryRevision;
        public int slotIndex;
        public long stackId;

        public QuickbarAssignRequestDto(string sessionId, long sequence,
            long quickbarRevision, long inventoryRevision, int slot, long stack)
        {
            msgId = type = QuickbarProtocol.Assign;
            quickbarOperationSessionId = sessionId;
            requestSequence = sequence;
            expectedQuickbarRevision = quickbarRevision;
            expectedInventoryRevision = inventoryRevision;
            slotIndex = slot;
            stackId = stack;
        }
    }

    [Serializable]
    public sealed class QuickbarClearRequestDto : NetMessageBase
    {
        public string quickbarOperationSessionId;
        public long requestSequence;
        public long expectedQuickbarRevision;
        public long expectedInventoryRevision;
        public int slotIndex;

        public QuickbarClearRequestDto(string sessionId, long sequence,
            long quickbarRevision, long inventoryRevision, int slot)
        {
            msgId = type = QuickbarProtocol.Clear;
            quickbarOperationSessionId = sessionId;
            requestSequence = sequence;
            expectedQuickbarRevision = quickbarRevision;
            expectedInventoryRevision = inventoryRevision;
            slotIndex = slot;
        }
    }

    [Serializable]
    public sealed class QuickbarMoveRequestDto : NetMessageBase
    {
        public string quickbarOperationSessionId;
        public long requestSequence;
        public long expectedQuickbarRevision;
        public long expectedInventoryRevision;
        public int sourceSlotIndex;
        public int targetSlotIndex;

        public QuickbarMoveRequestDto(string sessionId, long sequence,
            long quickbarRevision, long inventoryRevision, int source, int target)
        {
            msgId = type = QuickbarProtocol.Move;
            quickbarOperationSessionId = sessionId;
            requestSequence = sequence;
            expectedQuickbarRevision = quickbarRevision;
            expectedInventoryRevision = inventoryRevision;
            sourceSlotIndex = source;
            targetSlotIndex = target;
        }
    }

    [Serializable]
    public sealed class QuickbarSlotDto
    {
        public int slotIndex;
        public long stackId;
        public string itemId;
        public int count;
        public bool isBound;
    }

    [Serializable]
    public sealed class QuickbarSnapshotResultDto : NetMessageBase
    {
        public long characterDbId;
        public long requestSequence;
        public bool success;
        public string errorCode;
        public int protocolVersion;
        public int slotCount;
        public long quickbarRevision;
        public long inventoryRevision;
        public QuickbarSlotDto[] slots;
        public long serverTime;
    }

    [Serializable]
    public sealed class QuickbarWriteResultDto : NetMessageBase
    {
        public long characterDbId;
        public string quickbarOperationSessionId;
        public long requestSequence;
        public bool success;
        public string errorCode;
        public int protocolVersion;
        public int slotCount;
        public long quickbarRevision;
        public long inventoryRevision;
        public QuickbarSlotDto[] slots;
        public bool idempotentReplay;
        public bool requiresSnapshot;
        public long serverTime;
    }
}
