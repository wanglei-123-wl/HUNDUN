using System;
using UnityEngine;

[Flags]
public enum MovementEventFlags
{
    None = 0, Forward = 1, Backward = 2, StrafeLeft = 4, StrafeRight = 8,
    TurnLeft = 16, TurnRight = 32, Walking = 64, Falling = 128
}

[Serializable]
public class MovementReferencedRequest : NetMessageBase
{
    public string sceneSessionId;
    public long movementEpoch;
    public long movementSequence;
}

[Serializable]
public sealed class MovementEventPolicy
{
    public const int Version = 2;
    public const int BroadcastVersion = 2;
    public const int MonsterSyncVersion = 2;
    public const string IncompatibleServerMessage =
        "游戏服务器的移动或怪物同步协议版本不匹配，请确认客户端与服务端版本配套。";
    public int version, heartbeatIntervalMs, maxEventsPerSecond, movingTimeoutMs, controlTimeoutMs;

    public bool IsValid => version == Version && heartbeatIntervalMs > 0
        && maxEventsPerSecond > 0 && movingTimeoutMs > heartbeatIntervalMs && controlTimeoutMs > 0;

    public static bool Accepts(EnterWorldResult entry) => entry != null
        && entry.movementProtocolVersion == Version
        && entry.movementBroadcastVersion == BroadcastVersion
        && (entry.monsterSyncVersion == 0 || entry.monsterSyncVersion == MonsterSyncVersion)
        && entry.movementPolicy?.IsValid == true
        && entry.movementControl?.IsValidFor(entry.sceneSessionId) == true;

    public static bool IsAdmissionError(ServerErrorMessage error) =>
        error?.requestMsgId == "enter_world" && (error.errorCode == "movement_client_update_required"
            || error.errorCode == "monster_sync_client_update_required");
}

[Serializable]
public sealed class MovementEventControl
{
    public string sceneSessionId, mode, reason;
    public long epoch, nextSequence, serverTime;
    public float x, y, z, yaw, walkSpeed, runSpeed, jumpSpeed, gravity, radius, height, velocityY, turnSpeed;
    public bool grounded;
    public MovementEventFlags flags;

    public Vector3 Position => new Vector3(x, y, z);
    public Quaternion Rotation => Quaternion.Euler(0f, yaw, 0f);
    public bool IsValidFor(string scene) => !string.IsNullOrEmpty(scene) && sceneSessionId == scene
        && epoch > 0 && nextSequence > 0 && walkSpeed > 0 && runSpeed >= walkSpeed
        && jumpSpeed > 0 && gravity < 0 && radius > 0 && height >= 2f * radius
        && (mode == "normal" || mode == "controlled" || mode == "recovery");
}

[Serializable]
public sealed class MovementEventInfo
{
    public long clientTimeMs, fallTimeMs;
    public float x, y, z, yaw, velocityX, velocityY, velocityZ, jumpSpeed;
    public MovementEventFlags flags;

    public Vector3 Position => new Vector3(x, y, z);
    public Vector3 Velocity => new Vector3(velocityX, velocityY, velocityZ);
}

[Serializable]
public sealed class MovementEventRequest : NetMessageBase
{
    public string sceneSessionId, @event;
    public long movementEpoch, sequence;
    public MovementEventInfo info;

    public MovementEventRequest() { msgId = type = "movement_event"; }
}

[Serializable]
public sealed class MovementControlAckRequest : NetMessageBase
{
    public string sceneSessionId;
    public long movementEpoch, clientTimeMs;

    public MovementControlAckRequest() { msgId = type = "movement_control_ack"; }
}

[Serializable]
public sealed class MovementControlNotification
{
    public MovementEventControl control;
}

[Serializable]
public sealed class MovementControlEvaluation
{
    public bool success, needsControlAck;
    public string errorCode;
    public long acceptedSequence, revision;
    public MovementEventControl control;
}

[Serializable]
public sealed class MovementControlReply
{
    public MovementControlEvaluation result;
}

[Serializable]
public sealed class RemoteMovementSnapshot
{
    public long epoch, revision, serverTime;
    public string mode;
    public MovementEventInfo info;
    public RemoteRollSnapshot roll;

    // JsonUtility initializes absent nested objects with all-zero fields.
    public RemoteRollSnapshot ActiveRoll => roll != null && (roll.id != 0 || roll.elapsedMs != 0) ? roll : null;

    public bool IsValid => revision > 0 && epoch > 0 && info != null
        && (mode == "normal" || mode == "controlled" || mode == "recovery")
        && (ActiveRoll == null || (mode == "normal" && (info.flags & MovementEventFlags.Falling) == 0
            && roll.IsValidFor(revision)));
}

[Serializable]
public sealed class RemoteRollSnapshot
{
    public const int MaxDurationMs = 2000;
    public long id;
    public int elapsedMs;

    internal bool IsValidFor(long revision) =>
        id > 0 && id <= revision && elapsedMs >= 0 && elapsedMs < MaxDurationMs;
}

[Serializable]
public sealed class PlayerMovementEventMessage
{
    public long characterDbId, visibilityRevision;
    public string sceneId, sceneSessionId, @event;
    public RemoteMovementSnapshot movement;
}

public static class MovementReferenceError
{
    public static bool DidNotExecute(string code) =>
        code == "stale_movement_epoch" || code == "movement_reference_mismatch"
        || code == "movement_control_required" || code == "movement_locked";
}
