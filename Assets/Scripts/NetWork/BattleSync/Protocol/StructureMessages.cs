using System;

namespace Game.Client.BattleSync.Protocol
{
    [Serializable]
    public abstract class ReliableStructureMessageDtoBase
    {
        public string msgId;
        public string type;
        public long reliableMessageId;
        public string sceneId;
        public long serverTime;
    }

    [Serializable]
    public sealed class PlayerEnterMessageDto : ReliableStructureMessageDtoBase
    {
        public int netId;
        public long visibilityRevision;
        public long serverTick;
        public PlayerStateDto player;
    }

    [Serializable]
    public sealed class PlayerLeaveMessageDto : ReliableStructureMessageDtoBase
    {
        public int netId;
        public long visibilityRevision;
        public long serverTick;
        public string userId;
        public string username;
        public long characterDbId;
        public string characterId;
        public string nickname;
        public string reason;
    }

    [Serializable]
    public sealed class MonsterEnterMessageDto : ReliableStructureMessageDtoBase
    {
        public int netId;
        public MonsterStateDto monster;
        public long visibilityRevision;
        public long aoiRevision;
        public long serverTick;
    }

    [Serializable]
    public sealed class MonsterRemoveMessageDto : ReliableStructureMessageDtoBase
    {
        public int netId;
        public string monsterInstanceId;
        public long aoiRevision;
        public long visibilityRevision;
        public long combatRevision;
        public long spawnGeneration;
        public string reason;
        public long serverTick;
    }

    [Serializable]
    public sealed class MonsterUpsertMessageDto : ReliableStructureMessageDtoBase
    {
        public MonsterStateDto monster;
        public long aoiRevision;
    }

    [Serializable]
    public sealed class LootUpsertMessageDto : ReliableStructureMessageDtoBase
    {
        public int netId;
        public LootStateDto loot;
        public long visibilityRevision;
        public long aoiRevision;
    }

    [Serializable]
    public sealed class LootRemoveMessageDto : ReliableStructureMessageDtoBase
    {
        public int netId;
        public string lootId;
        public long visibilityRevision;
        public long aoiRevision;
        public string reason;
    }

    [Serializable]
    public sealed class CombatEventMessageDto : ReliableStructureMessageDtoBase
    {
        public long eventId;
        public string eventType;
        public string actionId;
        public long actionSequence;
        public CombatEntityDto source;
        public CombatEntityDto target;
        public int damage;
        public bool critical;
        public int hpAfter;
        public int maxHp;
        public bool lethal;
        public string killerType;
        public string killerId;
        public string killerName;
        public long respawnAvailableAt;
        public long deathKickAt;
        public long combatRevision;
        public long serverTick;
    }

    [Serializable]
    public sealed class CombatEventBatchMessageDto : ReliableStructureMessageDtoBase
    {
        public long serverTick;
        public CombatEventMessageDto[] events;
    }
}
