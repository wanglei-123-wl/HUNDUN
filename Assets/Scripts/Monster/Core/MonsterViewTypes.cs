using System;
using UnityEngine;

public enum MonsterLocomotionMode
{
    Grounded = 0,
    TakingOff = 1,
    Flying = 2,
    Landing = 3
}

[Serializable]
public struct MonsterViewState
{
    public string entityId;
    public long stateSequence;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
    public bool preserveTransform;
    [Range(0f, 1f)] public float moveSpeed;
    public MonsterLocomotionMode locomotionMode;
    public int hp;
    public int maxHp;
}

[Serializable]
public struct MonsterActionData
{
    public string entityId;
    public long eventSequence;
    public int actionSlot;
    public float actionSpeed;
}

[Serializable]
public class MonsterSnapshotMessage
{
    public string msgId;
    public string sceneId;
    public long serverTime;
    public MonsterSnapshotItem[] monsters;
}

[Serializable]
public class MonsterSnapshotItem
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
    public long aoiRevision;
}

[Serializable]
public class MonsterUpsertMessage
{
    public string msgId;
    public string type;
    public string sceneId;
    public MonsterSnapshotItem monster;
    public long aoiRevision;
    public long serverTime;
}

[Serializable]
public class MonsterRemoveMessage
{
    public string msgId;
    public string type;
    public string sceneId;
    public string monsterInstanceId;
    public long aoiRevision;
    public string reason;
    public long serverTime;
}
