using System;

namespace Game.Inventory
{
    [Serializable]
    public sealed class InventorySnapshotRequestDto : NetMessageBase
    {
        public InventorySnapshotRequestDto()
        {
            msgId = InventoryProtocolConstants.SnapshotRequest;
            type = InventoryProtocolConstants.SnapshotRequest;
        }
    }

    [Serializable]
    public sealed class InventoryServerSlotDto
    {
        public long slotId;
        public int slotIndex;
        public string itemId;
        public int count;
        public bool isBound;
    }

    [Serializable]
    public sealed class InventoryServerWalletDto
    {
        public string currencyId;
        public long balance;
    }

    [Serializable]
    public sealed class InventorySnapshotResultDto : NetMessageBase
    {
        public long characterDbId;
        public string sceneId;
        public long serverTick;
        public long inventoryRevision;
        public long snapshotRevision;
        public string reason;
        public int capacity;
        public InventoryServerSlotDto[] slots;
        public InventoryServerWalletDto[] wallets;
        public string errorCode;
        public string message;
        public long serverTime;
    }

    [Serializable]
    public sealed class InventoryServerErrorDto : NetMessageBase
    {
        public string requestMsgId;
        public string errorCode;
        public string message;
        public long serverTime;
    }
}
