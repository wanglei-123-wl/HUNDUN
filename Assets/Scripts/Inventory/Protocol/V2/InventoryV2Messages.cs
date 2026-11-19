using System;

namespace Game.Inventory.Protocol.V2
{
    public static class InventoryV2Protocol
    {
        public const int EquipmentProtocolVersion = 3;
        public const int EquipmentCatalogVersion = 4;
        public const int EquipmentSlotCount = 12;
        // The snapshot validator checks the required wire fields for every positive version.
        public static bool SupportsSnapshotVersion(int version) => version > 0;
        public static bool IsCatalogCompatible(int clientVersion, int serverVersion) =>
            clientVersion == serverVersion
            || (clientVersion == EquipmentCatalogVersion && serverVersion == 2);
        public const string Snapshot = "inventory_snapshot";
        public const string SnapshotResult = "inventory_snapshot_result";
        public const string Equip = "equipment_equip";
        public const string Unequip = "equipment_unequip";
        public const string SlotMove = "equipment_slot_move";
        public const string OperationResult = "equipment_operation_result";
        public const string ItemUse = "item_use";
        public const string ItemUseResult = "item_use_result";
        public const string InventoryDiscard = "inventory_discard";
        public const string InventoryWorldDrop = "inventory_world_drop";
        public const string InventoryOperationResult = "inventory_operation_result";
        public const string EquipmentDiscard = "equipment_discard";
        public const string EquipmentWorldDrop = "equipment_world_drop";
        public const string WorldDropResult = "world_drop_result";
    }

    [Serializable]
    public sealed class InventoryV2StackDto
    {
        public long stackId;
        public string itemId;
        public int count;
        public bool isBound;
    }

    [Serializable]
    public sealed class InventoryV2StackChangeDto
    {
        public long stackId;
        public string itemId;
        public int previousCount;
        public int newCount;
        public bool isBound;
    }

    [Serializable]
    public sealed class InventoryV2WalletChangeDto
    {
        public string currencyId;
        public long previousBalance;
        public long newBalance;
    }

    [Serializable]
    public sealed class InventoryV2EquipmentDto
    {
        public string itemInstanceId;
        public string equipmentId;
        public bool isBound;
        public int slotCode;
    }

    [Serializable]
    public sealed class InventoryV2WalletDto
    {
        public string currencyId;
        public long balance;
    }

    [Serializable]
    public sealed class InventoryV2AttributeBonusDto
    {
        public int maxHp;
        public int attack;
        public int defense;
        public int maxMp;
        public int critRateBps;
        public int critDamageBps;
        public int magicAttack;
        public int magicDefense;
    }

    [Serializable]
    public sealed class InventoryV2SnapshotRequestDto : NetMessageBase
    {
        public long requestSequence;

        public InventoryV2SnapshotRequestDto(long sequence)
        {
            msgId = type = InventoryV2Protocol.Snapshot;
            requestSequence = sequence;
        }
    }

    [Serializable]
    public sealed class InventoryV2SnapshotResultDto : NetMessageBase
    {
        public string sceneId;
        public long serverTick;
        public long snapshotRevision;
        public string reason;
        public int equipmentProtocolVersion;
        public int equipmentCatalogVersion;
        public long requestSequence;
        public bool success;
        public long characterDbId;
        public int capacity;
        public long inventoryRevision;
        public long equipmentRevision;
        public InventoryV2StackDto[] stackItems;
        public InventoryV2EquipmentDto[] equipmentItems;
        public InventoryV2WalletDto[] wallets;
        public InventoryV2AttributeBonusDto attributeBonus;
        public string errorCode;
        public string message;
        public long serverTime;
    }

    [Serializable]
    public sealed class InventoryV2EquipRequestDto : NetMessageBase
    {
        public string equipmentOperationSessionId;
        public long requestSequence;
        public string itemInstanceId;
        public int targetSlotCode;

        public InventoryV2EquipRequestDto(string sessionId, long sequence,
            string instanceId, int slotCode)
        {
            msgId = type = InventoryV2Protocol.Equip;
            equipmentOperationSessionId = sessionId;
            requestSequence = sequence;
            itemInstanceId = instanceId;
            targetSlotCode = slotCode;
        }
    }

    [Serializable]
    public sealed class InventoryV2UnequipRequestDto : NetMessageBase
    {
        public string equipmentOperationSessionId;
        public long requestSequence;
        public int slotCode;

        public InventoryV2UnequipRequestDto(string sessionId, long sequence,
            int sourceSlotCode)
        {
            msgId = type = InventoryV2Protocol.Unequip;
            equipmentOperationSessionId = sessionId;
            requestSequence = sequence;
            slotCode = sourceSlotCode;
        }
    }

    [Serializable]
    public sealed class InventoryV2SlotMoveRequestDto : NetMessageBase
    {
        public string equipmentOperationSessionId;
        public long requestSequence;
        public int sourceSlotCode;
        public int targetSlotCode;

        public InventoryV2SlotMoveRequestDto(string sessionId, long sequence,
            int source, int target)
        {
            msgId = type = InventoryV2Protocol.SlotMove;
            equipmentOperationSessionId = sessionId;
            requestSequence = sequence;
            sourceSlotCode = source;
            targetSlotCode = target;
        }
    }

    [Serializable]
    public sealed class InventoryV2OperationResultDto : NetMessageBase
    {
        public string equipmentOperationSessionId;
        public long requestSequence;
        public string operation;
        public bool success;
        public long characterDbId;
        public long inventoryRevision;
        public long equipmentRevision;
        public InventoryV2EquipmentDto[] equipmentItemChanges;
        public InventoryV2AttributeBonusDto attributeBonus;
        public bool idempotentReplay;
        public bool requiresSnapshot;
        public string[] removedItemInstanceIds;
        public string errorCode;
        public string message;
        public long serverTime;
    }

    [Serializable]
    internal sealed class InventorySnapshotEnvelope : NetMessageBase
    {
        public int equipmentProtocolVersion;
        public InventoryV2StackDto[] stackItems;
        public InventoryV2EquipmentDto[] equipmentItems;
    }

    [Serializable]
    public sealed class InventoryV2ItemUseRequestDto : NetMessageBase
    {
        public string sceneSessionId;
        public string itemUseOperationSessionId;
        public long requestSequence;
        public long expectedInventoryRevision;
        public long stackId;
        public int count;
        public long clientTick;

        public InventoryV2ItemUseRequestDto(string sessionId, long sequence,
            long revision, long id, long tick)
        {
            msgId = type = InventoryV2Protocol.ItemUse;
            itemUseOperationSessionId = sessionId;
            requestSequence = sequence;
            expectedInventoryRevision = revision;
            stackId = id;
            count = 1;
            clientTick = tick;
        }
    }

    [Serializable]
    public sealed class InventoryV2ItemUseResultDto : NetMessageBase
    {
        public string itemUseOperationSessionId;
        public long requestSequence;
        public string useId;
        public bool success;
        public string status;
        public string itemId;
        public int consumedCount;
        public long inventoryRevision;
        public InventoryV2StackChangeDto[] stackItemChanges;
        public int currentHp;
        public int maxHp;
        public int currentMp;
        public int maxMp;
        public InventoryV2ItemUseEffectDto[] effects;
        public long gainedJingjieExp;
        public long cooldownAvailableAt;
        public bool requiresInventorySnapshot;
        public bool requiresSnapshot;
        public string errorCode;
        public string message;
        public long serverTime;
    }

    [Serializable]
    public sealed class InventoryV2ItemUseEffectDto
    {
        public string type;
        public int amount;
    }

    [Serializable]
    public sealed class InventoryV2DiscardRequestDto : NetMessageBase
    {
        public long requestSequence;
        public long expectedInventoryRevision;
        public long stackId;
        public int count;
        public bool confirmationAccepted;

        public InventoryV2DiscardRequestDto(long sequence, long revision,
            long id, int amount)
        {
            msgId = type = InventoryV2Protocol.InventoryDiscard;
            requestSequence = sequence;
            expectedInventoryRevision = revision;
            stackId = id;
            count = amount;
            confirmationAccepted = true;
        }
    }

    [Serializable]
    public sealed class InventoryV2DiscardResultDto : NetMessageBase
    {
        public long requestSequence;
        public string operationType;
        public bool success;
        public long inventoryRevision;
        public long serverInventoryRevision;
        public bool requiresSnapshot;
        public InventoryV2StackChangeDto[] stackItemChanges;
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class InventoryV2EquipmentDiscardRequestDto : NetMessageBase
    {
        public string equipmentOperationSessionId;
        public long requestSequence;
        public string itemInstanceId;
        public bool confirmationAccepted;

        public InventoryV2EquipmentDiscardRequestDto(string sessionId,
            long sequence, string instanceId)
        {
            msgId = type = InventoryV2Protocol.EquipmentDiscard;
            equipmentOperationSessionId = sessionId;
            requestSequence = sequence;
            itemInstanceId = instanceId;
            confirmationAccepted = true;
        }
    }

    [Serializable]
    public sealed class InventoryV2StackWorldDropRequestDto : MovementReferencedRequest
    {
        public string worldDropOperationSessionId;
        public long requestSequence;
        public long expectedInventoryRevision;
        public long stackId;
        public int count;
        public bool confirmationAccepted;

        public InventoryV2StackWorldDropRequestDto(string sessionId,
            long sequence, long revision, long id, int amount)
        {
            msgId = type = InventoryV2Protocol.InventoryWorldDrop;
            worldDropOperationSessionId = sessionId;
            requestSequence = sequence;
            expectedInventoryRevision = revision;
            stackId = id;
            count = amount;
            confirmationAccepted = true;
        }
    }

    [Serializable]
    public sealed class InventoryV2WorldDropRequestDto : MovementReferencedRequest
    {
        public string worldDropOperationSessionId;
        public long requestSequence;
        public string itemInstanceId;
        public bool confirmationAccepted;

        public InventoryV2WorldDropRequestDto(string sessionId,
            long sequence, string instanceId)
        {
            msgId = type = InventoryV2Protocol.EquipmentWorldDrop;
            worldDropOperationSessionId = sessionId;
            requestSequence = sequence;
            itemInstanceId = instanceId;
            confirmationAccepted = true;
        }
    }

    [Serializable]
    public sealed class InventoryV2WorldDropResultDto : NetMessageBase
    {
        public string worldDropOperationSessionId;
        public long requestSequence;
        public string assetKind;
        public bool success;
        public string status;
        public long characterDbId;
        public long inventoryRevision;
        public long equipmentRevision;
        public InventoryV2StackChangeDto[] stackItemChanges;
        public InventoryV2EquipmentDto[] equipmentItemChanges;
        public string[] removedItemInstanceIds;
        public string lootId;
        public long expireAtMs;
        public bool requiresSnapshot;
        public string errorCode;
        public string message;
        public long serverTime;
    }
}
