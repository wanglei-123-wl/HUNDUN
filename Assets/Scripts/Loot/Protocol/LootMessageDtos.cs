using System;

[Serializable]
public sealed class LootDataDto
{
    public string lootId;
    public string sceneId;
    public string itemId;
    public int count;

    public float x;
    public float y;
    public float z;

    public string ownerType;
    public string ownerId;

    public long ownerExpireAtMs;
    public long expireAtMs;
    public long aoiRevision;

    public string state;
}

[Serializable]
public sealed class LootSnapshotMessageDto : NetMessageBase
{
    public string sceneId;
    public long serverTime;
    public LootDataDto[] loots;
}

[Serializable]
public sealed class LootUpsertMessageDto : NetMessageBase
{
    public long reliableMessageId;
    public string sceneId;
    public long serverTime;
    public long aoiRevision;
    public LootDataDto loot;
}

[Serializable]
public sealed class LootRemoveMessageDto : NetMessageBase
{
    public long reliableMessageId;
    public string sceneId;
    public string lootId;
    public string reason;
    public long aoiRevision;
    public long serverTime;
}

[Serializable]
public sealed class LootPickupMessageDto : MovementReferencedRequest
{
    public long requestSequence;
    public long clientTick;
    public string lootId;

    public LootPickupMessageDto()
    {
        msgId = LootProtocolConstants.Pickup;
        type = LootProtocolConstants.Pickup;
    }
}

[Serializable]
public sealed class LootPickupResultMessageDto : NetMessageBase
{
    public long requestSequence;
    public bool success;
    public string status;
    public string lootId;
    public string grantId;
    public string itemId;
    public int count;
    public int grantedCount;
    public long inventoryRevision;
    public Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[] stackItemChanges;
    public Game.Inventory.Protocol.V2.InventoryV2EquipmentDto[] equipmentItemChanges;
    public InventorySlotChangeDto[] slotChanges;
    public WalletChangeDto[] walletChanges;
    public string errorCode;
    public string message;
    public long serverTime;
}

[Serializable]
public sealed class InventorySlotChangeDto
{
    public long slotId;
    public int slotIndex;
    public string itemId;
    public int previousCount;
    public int newCount;
    public bool isBound;
}

[Serializable]
public sealed class WalletChangeDto
{
    public string currencyId;
    public long previousBalance;
    public long newBalance;
}

[Serializable]
public sealed class InventorySnapshotMessageDto : NetMessageBase
{
    public InventorySnapshotMessageDto()
    {
        msgId = LootProtocolConstants.InventorySnapshot;
        type = LootProtocolConstants.InventorySnapshot;
    }
}

[Serializable]
public sealed class InventorySlotDto
{
    public long slotId;
    public int slotIndex;
    public string itemId;
    public int count;
    public bool isBound;
}

[Serializable]
public sealed class WalletDto
{
    public string currencyId;
    public long balance;
}

[Serializable]
public sealed class InventorySnapshotResultMessageDto : NetMessageBase
{
    public long characterDbId;
    public long inventoryRevision;
    public InventorySlotDto[] slots;
    public WalletDto[] wallets;
    public string errorCode;
    public long serverTime;
}

[Serializable]
public sealed class NetErrorMessageDto : NetMessageBase
{
    public long requestSequence;
    public string requestMsgId;
    public string errorCode;
    public string message;
    public long serverTime;
}

[Serializable]
public sealed class BattleOutboxAckMessageDto : NetMessageBase
{
    public long reliableMessageId;

    public BattleOutboxAckMessageDto()
    {
        msgId = LootProtocolConstants.BattleOutboxAck;
        type = LootProtocolConstants.BattleOutboxAck;
    }
}
