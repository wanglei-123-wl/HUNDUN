using System;

namespace Game.Client.BattleSync.Protocol
{
    [Serializable]
    public abstract class SnapshotMessageDtoBase
    {
        public string msgId;
        public string type;
        public string sceneId;
        public long serverTick;
        public long serverTime;
        public long snapshotRevision;
        public string reason;
    }

    [Serializable]
    public sealed class WorldSnapshotMessageDto : SnapshotMessageDtoBase
    {
        public PlayerStateDto[] players;
    }

    [Serializable]
    public sealed class MonsterSnapshotMessageDto : SnapshotMessageDtoBase
    {
        public MonsterStateDto[] monsters;
    }

    [Serializable]
    public sealed class LootSnapshotMessageDto : SnapshotMessageDtoBase
    {
        public LootStateDto[] loots;
    }

    [Serializable]
    public sealed class CombatSnapshotMessageDto : SnapshotMessageDtoBase
    {
        public CombatStateDto[] states;
    }

    [Serializable]
    public sealed class InventorySnapshotResultMessageDto : SnapshotMessageDtoBase
    {
        public int equipmentProtocolVersion;
        public long characterDbId;
        public long inventoryRevision;
        public InventorySlotDto[] slots = Array.Empty<InventorySlotDto>();
        public InventoryWalletDto[] wallets = Array.Empty<InventoryWalletDto>();
        public string errorCode;
        public int capacity;
        public string message;
    }

    [Serializable]
    public sealed class PlayerStateDto
    {
        public string userId;
        public string username;
        public long characterDbId;
        public string characterId;
        public string roleId;
        public string nickname;
        public string sceneId;
        public float x;
        public float y;
        public float z;
        public float rotY;
        public float moveSpeed;
        public string animation;
        public long sequence;
        public long time;
        public long serverTime;
        public float velocityX;
        public float velocityY;
        public float velocityZ;
        public bool grounded;
        public int movementState;
        public long serverTick;
        public long lastProcessedInputSequence;
        public int hp;
        public int maxHp;
        public string lifeState;
        public long combatRevision;
        public long visibilityRevision;
        public int netId;
    }

    [Serializable]
    public sealed class MonsterStateDto
    {
        public string monsterInstanceId;
        public string spawnPointId;
        public string monsterId;
        public string prefabId;
        public string name;
        public int level;
        public string sceneId;
        public string mapId;
        public string instanceId;
        public string spawnGroupId;
        public float x;
        public float y;
        public float z;
        public float rotY;
        public int hp;
        public int maxHp;
        public string state;
        public float collisionRadius;
        public float desiredGap;
        public float attackReach;
        public long aoiRevision;
        public long combatRevision;
        public long spawnGeneration;
        public string lifeState;
        public long visibilityRevision;
        public int netId;
    }

    [Serializable]
    public sealed class LootStateDto
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
        public long visibilityRevision;
        public int netId;
    }

    [Serializable]
    public sealed class CombatEntityDto
    {
        public string entityType;
        public string entityId;
    }

    [Serializable]
    public sealed class CombatStateDto
    {
        public CombatEntityDto entity;
        public int hp;
        public int maxHp;
        public string lifeState;
        public long combatRevision;
        public long spawnGeneration;
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
    public sealed class InventoryWalletDto
    {
        public string currencyId;
        public long balance;
    }

    public static class BattleSyncSnapshotValidator
    {
        public static bool TryValidate(WorldSnapshotMessageDto message, out string error)
        {
            if (!TryValidateRoot(message, BattleSyncProtocolConstants.MessageIds.WorldSnapshot, out error)) return false;
            message.players = message.players ?? Array.Empty<PlayerStateDto>();
            for (int i = 0; i < message.players.Length; i++)
            {
                PlayerStateDto item = message.players[i];
                if (item == null || item.netId <= 0 || item.characterDbId <= 0
                    || string.IsNullOrWhiteSpace(item.characterId)
                    || string.IsNullOrWhiteSpace(item.sceneId)
                    || !string.Equals(item.sceneId, message.sceneId, StringComparison.Ordinal)
                    || item.visibilityRevision < 0)
                    return Fail("Invalid player snapshot entry at index " + i + ".", out error);
            }
            error = string.Empty; return true;
        }

        public static bool TryValidate(MonsterSnapshotMessageDto message, out string error)
        {
            if (!TryValidateRoot(message, BattleSyncProtocolConstants.MessageIds.MonsterSnapshot, out error)) return false;
            message.monsters = message.monsters ?? Array.Empty<MonsterStateDto>();
            for (int i = 0; i < message.monsters.Length; i++)
            {
                MonsterStateDto item = message.monsters[i];
                if (item == null || item.netId <= 0 || string.IsNullOrWhiteSpace(item.monsterInstanceId)
                    || string.IsNullOrWhiteSpace(item.monsterId)
                    || !string.Equals(item.sceneId, message.sceneId, StringComparison.Ordinal)
                    || item.spawnGeneration < 0 || item.visibilityRevision < 0)
                    return Fail("Invalid monster snapshot entry at index " + i
                        + ", netId=" + (item == null ? 0 : item.netId)
                        + ", spawnGeneration=" + (item == null ? -1 : item.spawnGeneration)
                        + ".", out error);
            }
            error = string.Empty; return true;
        }

        public static bool TryValidate(LootSnapshotMessageDto message, out string error)
        {
            if (!TryValidateRoot(message, BattleSyncProtocolConstants.MessageIds.LootSnapshot, out error)) return false;
            message.loots = message.loots ?? Array.Empty<LootStateDto>();
            for (int i = 0; i < message.loots.Length; i++)
            {
                LootStateDto item = message.loots[i];
                if (item == null || item.netId <= 0 || string.IsNullOrWhiteSpace(item.lootId)
                    || string.IsNullOrWhiteSpace(item.itemId) || item.count <= 0
                    || !string.Equals(item.sceneId, message.sceneId, StringComparison.Ordinal)
                    || item.visibilityRevision < 0)
                    return Fail("Invalid loot snapshot entry at index " + i + ".", out error);
            }
            error = string.Empty; return true;
        }

        public static bool TryValidate(CombatSnapshotMessageDto message, out string error)
        {
            if (!TryValidateRoot(message, BattleSyncProtocolConstants.MessageIds.CombatSnapshot, out error)) return false;
            message.states = message.states ?? Array.Empty<CombatStateDto>();
            for (int i = 0; i < message.states.Length; i++)
            {
                CombatStateDto item = message.states[i];
                if (item == null || item.entity == null || string.IsNullOrWhiteSpace(item.entity.entityType)
                    || string.IsNullOrWhiteSpace(item.entity.entityId) || item.maxHp < 0
                    || item.hp < 0 || item.hp > item.maxHp || item.combatRevision < 0)
                    return Fail("Invalid combat snapshot entry at index " + i + ".", out error);
            }
            error = string.Empty; return true;
        }

        public static bool TryValidate(InventorySnapshotResultMessageDto message, out string error)
        {
            if (!TryValidateRoot(message, BattleSyncProtocolConstants.MessageIds.InventorySnapshotResult, out error)
                || message.equipmentProtocolVersion != 0
                || message.characterDbId <= 0 || message.inventoryRevision < 0)
            {
                if (string.IsNullOrEmpty(error)) error = "Inventory protocol is not legacy.";
                return false;
            }
            message.slots = message.slots ?? Array.Empty<InventorySlotDto>();
            message.wallets = message.wallets ?? Array.Empty<InventoryWalletDto>();
            for (int i = 0; i < message.slots.Length; i++)
            {
                InventorySlotDto item = message.slots[i];
                if (item == null || item.slotId <= 0 || item.slotIndex < 0
                    || string.IsNullOrWhiteSpace(item.itemId) || item.count <= 0)
                    return Fail("Invalid inventory slot at index " + i + ".", out error);
            }
            for (int i = 0; i < message.wallets.Length; i++)
            {
                InventoryWalletDto item = message.wallets[i];
                if (item == null || string.IsNullOrWhiteSpace(item.currencyId) || item.balance < 0)
                    return Fail("Invalid inventory wallet at index " + i + ".", out error);
            }
            error = string.Empty; return true;
        }

        private static bool TryValidateRoot(SnapshotMessageDtoBase message, string expectedId, out string error)
        {
            if (message == null) return Fail("Snapshot is null.", out error);
            if (!string.Equals(message.msgId, expectedId, StringComparison.Ordinal)) return Fail("Snapshot msgId mismatch.", out error);
            if (!string.IsNullOrEmpty(message.type) && !string.Equals(message.type, expectedId, StringComparison.Ordinal)) return Fail("Snapshot type mismatch.", out error);
            if (string.IsNullOrWhiteSpace(message.sceneId) || message.serverTick <= 0
                || message.serverTime <= 0 || message.snapshotRevision <= 0
                || string.IsNullOrWhiteSpace(message.reason)) return Fail("Snapshot root metadata is invalid.", out error);
            error = string.Empty; return true;
        }

        private static bool Fail(string value, out string error) { error = value; return false; }
    }
}
