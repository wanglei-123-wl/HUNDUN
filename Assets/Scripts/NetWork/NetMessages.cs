using System;
using UnityEngine;

[Serializable]
public class NetMessageBase
{
    public string msgId;
    public string type;
}

[Serializable]
public class EnterWorldRequest : NetMessageBase
{
    public long characterDbId;
    public string characterId;
    public string token;
    public long time;
    public int sessionLifecycleVersion = 1;
    public int movementProtocolVersion = MovementEventPolicy.Version;
    public int movementBroadcastVersion = MovementEventPolicy.BroadcastVersion;

    public EnterWorldRequest()
    {
        msgId = "enter_world";
        type = "enter_world";
    }
}

[Serializable]
public class EnterWorldResult : NetMessageBase
{
    public int movementProtocolVersion;
    public int movementBroadcastVersion;
    public MovementEventControl movementControl;
    public MovementEventPolicy movementPolicy;
    public bool success;
    public string message;
    public string sceneSessionId;
    public string equipmentOperationSessionId;
    public string worldDropOperationSessionId;
    public string itemUseOperationSessionId;
    public string learnSessionId;
    public string quickbarOperationSessionId;
    public int quickbarSlotCount;
    public int quickbarProtocolVersion;
    public string playerTradeOperationSessionId;
    public string jingjieOperationSessionId;
    public int cultivationProtocolVersion;
    public string cultivationOperationSessionId;
    public int alchemyProtocolVersion;
    public string alchemyOperationSessionId;
    public int forgingProtocolVersion;
    public string forgingOperationSessionId;
    public string userId;
    public string username;
    public long characterDbId;
    public string characterId;
    public string roleId;
    public string nickname;
    public string sceneId;
    public string mapId;
    public float x;
    public float y;
    public float z;
    public float rotY;
    public long serverTime;
    public long snapshotRevision;
    public string reason;
    public int heartbeatIntervalSeconds;
    public long gameSessionExpiresAt;
    public int reconnectWindowSeconds;
    public PlayerNetworkState self;
    public PlayerNetworkState[] visiblePlayers;

    public Vector3 GetPosition()
    {
        return new Vector3(x, y, z);
    }

    public Quaternion GetRotation()
    {
        return Quaternion.Euler(0f, rotY, 0f);
    }
}

[Serializable]
public sealed class AttributeSnapshotRequest : NetMessageBase
{
    public long requestSequence;

    public AttributeSnapshotRequest(long sequence)
    {
        msgId = type = "attribute_snapshot";
        requestSequence = sequence;
    }
}

[Serializable]
public sealed class AttributeSnapshotResult : NetMessageBase
{
    public long requestSequence;
    public bool success;
    public long characterDbId;
    public long attributeRevision;
    public long combatRevision;
    public AttributeSnapshotFinal final;
    public string errorCode;
    public string message;
}

[Serializable]
public sealed class AttributeSnapshotFinal
{
    public int maxHp;
    public int attack;
    public int defense;
    public int critRateBps;
    public int critDamageBps;
    public int magicAttack;
    public int magicDefense;
}

[Serializable]
public sealed class ProgressionSnapshotRequest : NetMessageBase
{
    public long requestSequence;

    public ProgressionSnapshotRequest(long sequence)
    {
        msgId = type = "progression_snapshot";
        requestSequence = sequence;
    }
}

[Serializable]
public sealed class ProgressionSnapshotResult : NetMessageBase
{
    public long requestSequence;
    public bool success;
    public long characterDbId;
    public int currentMp;
    public int maxMp;
    public string jingjieId;
    public int jingjieStage;
    public string jingjieName;
    public bool hasNextJingjie;
    public string nextJingjieId;
    public int nextJingjieStage;
    public string nextJingjieName;
    public long jingjieExp;
    public long requiredJingjieExp;
    public long totalJingjieExp;
    public long baseComprehension;
    public long techniqueComprehension;
    public long totalComprehension;
    public long cultivationConfigurationRevision;
    public long cultivationRevision;
    public string errorCode;
    public string message;
}

[Serializable]
public class PlayerNetworkState
{
    public RemoteMovementSnapshot movement;
    public string userId;
    public string username;
    public long characterDbId;
    public string characterId;
    public string roleId;
    public string sceneId;
    public string nickname;
    public float x;
    public float y;
    public float z;
    public float rotY;
    public long sequence;
    public long time;
    public float moveSpeed;
    public string animation;
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

    public Vector3 GetPosition()
    {
        return new Vector3(x, y, z);
    }

    public Quaternion GetRotation()
    {
        return Quaternion.Euler(0f, rotY, 0f);
    }

    public Vector3 GetVelocity()
    {
        return new Vector3(velocityX, velocityY, velocityZ);
    }
}

[Serializable]
public class AttackModeSetRequest : MovementReferencedRequest
{
    public const string FreeMode = "free";

    public long requestSequence;
    public string mode;

    public AttackModeSetRequest(long sequence, string mode)
    {
        msgId = type = "attack_mode_set";
        requestSequence = sequence;
        this.mode = mode;
    }
}

[Serializable]
public class AttackModeResult : NetMessageBase
{
    public long requestSequence;
    public bool success;
    public string mode;
    public string errorCode;
}

[Serializable]
public class CombatActionRequest : MovementReferencedRequest
{
    public long actionSequence;
    public long clientTick;
    public string actionId;
    public string targetType;
    public string targetId;
    public float aimYaw;

    public CombatActionRequest()
    {
        msgId = "combat_action";
        type = "combat_action";
    }
}

[Serializable]
public class SkillCastRequest : MovementReferencedRequest
{
    public long requestSequence;
    public long clientTick;
    public string skillId;
    public string targetType;
    public string targetId;

    public SkillCastRequest()
    {
        msgId = "cast_skill";
        type = "cast_skill";
    }
}

[Serializable]
public class SkillCastStarted : NetMessageBase
{
    public long requestSequence;
    public string skillId;
    public string casterId;
    public string targetType;
    public string targetId;
    public double targetX;
    public double targetY;
    public double targetZ;
    public long hitTick;
    public long cooldownUntilTick;
    public int currentMp;
    public int maxMp;
    public long serverTick;
    public long serverTime;
}

[Serializable]
public class SkillCastEvent : NetMessageBase
{
    public string castId;
    public string sceneId;
    public string casterId;
    public string skillId;
    public double casterX;
    public double casterY;
    public double casterZ;
    public double casterRotY;
    public string targetType;
    public string targetId;
    public double targetX;
    public double targetY;
    public double targetZ;
    public long serverTick;
    public long serverTime;
    public long hitTick;
}

[Serializable]
public class SkillCastResult : NetMessageBase
{
    public long requestSequence;
    public bool success;
    public string skillId;
    public string casterId;
    public string targetType;
    public string targetId;
    public int damage;
    public int heal;
    public int currentMp;
    public int maxMp;
    public string[] appliedBuffs;
    public long cooldownUntilTick;
    public string errorCode;
    public string message;
    public long serverTick;
    public long serverTime;
}

[Serializable]
public class PlayerAuthoritativeState : NetMessageBase
{
    public long movementEpoch;
    public string movementMode;
    public long serverTick;
    public long serverTimeMs;
    public long lastProcessedInputSequence;
    public float x;
    public float y;
    public float z;
    public float rotY;
    public float velocityX;
    public float velocityY;
    public float velocityZ;
    public bool grounded;
    public int movementState;
    public int actionState;
    public long actionSequence;

    public Vector3 GetPosition()
    {
        return new Vector3(x, y, z);
    }

    public Quaternion GetRotation()
    {
        return Quaternion.Euler(0f, rotY, 0f);
    }

    public Vector3 GetVelocity()
    {
        return new Vector3(velocityX, velocityY, velocityZ);
    }
}

[Serializable]
public class PlayerStateBroadcast : NetMessageBase
{
    public PlayerNetworkState player;
}

[Serializable]
public class PlayerEnterBroadcast : NetMessageBase
{
    public PlayerNetworkState player;
    public long serverTime;
}

[Serializable]
public class WorldSnapshotMessage : NetMessageBase
{
    public PlayerNetworkState[] players;
}

[Serializable]
public class PlayerLeaveMessage : NetMessageBase
{
    public string userId;
    public string username;
    public long characterDbId;
    public string characterId;
    public string nickname;
    public string reason;
    public long serverTime;
}

[Serializable]
public class LeaveWorldRequest : NetMessageBase
{
    public string userId;
    public string username;

    public LeaveWorldRequest()
    {
        msgId = "leave_world";
        type = "leave_world";
    }
}
[Serializable]
public class HeartbeatMessage : NetMessageBase
{
    public long time;
    public string clientPingId;

    public HeartbeatMessage()
    {
        msgId = "heartbeat";
        type = "heartbeat";
    }
}

[Serializable]
public class HeartbeatAckMessage : NetMessageBase
{
    public bool success;
    public long time;
    public long serverTime;
    public string clientPingId;
    public long gameSessionExpiresAt;
    public int reconnectWindowSeconds;
}

[Serializable]
public class AccountKickedMessage : NetMessageBase
{
    public string reason;
}

[Serializable]
public class GameSessionTransportError : NetMessageBase
{
    public string requestMsgId;
    public string errorCode;
}

[Serializable]
public class CombatEntityMessage
{
    public string entityType;
    public string entityId;
}

[Serializable]
public class CombatStateMessage
{
    public CombatEntityMessage entity;
    public int hp;
    public int maxHp;
    public string lifeState;
    public long combatRevision;
    public long spawnGeneration;
}

[Serializable]
public class CombatSnapshotMessage : NetMessageBase
{
    public string sceneId;
    public long serverTick;
    public long serverTime;
    public CombatStateMessage[] states;
}

[Serializable]
public class CombatStateUpsertMessage : NetMessageBase
{
    public string sceneId;
    public long serverTick;
    public long serverTime;
    public CombatStateMessage[] states;
}

[Serializable]
public class CombatEventMessage : NetMessageBase
{
    public string sceneId;
    public long eventId;
    public string eventType;
    public string actionId;
    public long actionSequence;
    public CombatEntityMessage source;
    public CombatEntityMessage target;
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
    public long serverTime;
}

[Serializable]
public class DeathTimeoutKickMessage : NetMessageBase
{
    public string errorCode;
    public string message;
    public long serverTime;
}

[Serializable]
public class RespawnRequest : NetMessageBase
{
    public string sceneSessionId;
    public long requestSequence;
    public long clientTick;

    public RespawnRequest()
    {
        msgId = "respawn";
        type = "respawn";
    }
}

[Serializable]
public sealed class MapTransferOptionsRequest : MovementReferencedRequest
{

    public MapTransferOptionsRequest()
    {
        msgId = "map_transfer_options";
    }
}

[Serializable]
public sealed class MapTransferRoute
{
    public string routeId;
    public string sceneId;
    public string mapId;
}

[Serializable]
public sealed class MapTransferOptionsResult : NetMessageBase
{
    public string sceneSessionId;
    public MapTransferRoute[] routes;
}

[Serializable]
public sealed class MapTransferRequest : MovementReferencedRequest
{
    public string requestId;
    public string routeId;

    public MapTransferRequest()
    {
        msgId = "map_transfer";
    }
}

[Serializable]
public sealed class MapTransferPrepare : NetMessageBase
{
    public string requestId;
    public string transferId;
    public string sceneId;
    public string mapId;
    public long expiresAt;
}

[Serializable]
public sealed class MapTransferReadyRequest : NetMessageBase
{
    public string transferId;
    public string sceneSessionId;
    public bool loaded;

    public MapTransferReadyRequest()
    {
        msgId = "map_transfer_ready";
    }
}

[Serializable]
public sealed class MapTransferResult : NetMessageBase
{
    public bool success;
    public string errorCode;
    public string requestId;
    public string transferId;
    public string sceneSessionId;
    public string sceneId;
    public string mapId;
    public float x;
    public float y;
    public float z;
    public float rotY;
    public MovementEventControl movementControl;
    public long snapshotRevision;
    public PlayerNetworkState self;
    public MapTransferSafeZone safeZone;
    public bool pvpEnabled;
    public long serverTime;
}

[Serializable]
public sealed class MapTransferSafeZone
{
    public float x;
    public float z;
    public float radius;
}

[Serializable]
public class ServerErrorMessage : NetMessageBase
{
    public string requestMsgId;
    public string errorCode;
    public string message;
    public long expectedRequestSequence;
    public long requestSequence;
}
