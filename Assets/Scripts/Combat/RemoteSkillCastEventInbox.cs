using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class RemoteSkillCastEventInbox
{
    private const int MaxSeenCastIds = 4096;
    private const float SeenCastLifetimeSeconds = 120f;

    private struct SeenCast
    {
        public string Id;
        public float ReceivedAt;
    }

    private readonly HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly Queue<SeenCast> seenOrder = new Queue<SeenCast>();
    private long activeEpoch;
    private long localCharacterDbId;
    private string activeSceneId;

    public void BeginSession(long connectionEpoch, string sceneId, long characterDbId)
    {
        Clear();
        if (connectionEpoch <= 0 || string.IsNullOrWhiteSpace(sceneId)
            || characterDbId <= 0)
            return;

        activeEpoch = connectionEpoch;
        activeSceneId = sceneId;
        localCharacterDbId = characterDbId;
    }

    public void EndSession(long connectionEpoch)
    {
        if (connectionEpoch == activeEpoch)
            Clear();
    }

    public bool TryAccept(SkillCastEvent message, long connectionEpoch,
        bool isConnected, float now, out string reason)
    {
        if (activeEpoch <= 0 || !isConnected || connectionEpoch != activeEpoch)
        {
            reason = "inactive_session";
            return false;
        }

        if (message == null || message.msgId != "skill_cast_event"
            || message.type != "skill_cast_event"
            || !string.Equals(message.sceneId, activeSceneId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(message.castId)
            || string.IsNullOrWhiteSpace(message.skillId)
            || string.IsNullOrWhiteSpace(message.targetId)
            || (message.targetType != "player" && message.targetType != "monster")
            || !long.TryParse(message.casterId, NumberStyles.None,
                CultureInfo.InvariantCulture, out long casterId)
            || casterId <= 0
            || !IsPosition(message.casterX, message.casterY, message.casterZ)
            || !IsPosition(message.targetX, message.targetY, message.targetZ)
            || !IsFiniteFloat(message.casterRotY)
            || message.serverTime <= 0)
        {
            reason = "invalid_event";
            return false;
        }

        if (casterId == localCharacterDbId)
        {
            reason = "local_caster";
            return false;
        }

        ExpireSeen(now);
        if (!seenIds.Add(message.castId))
        {
            reason = "duplicate_cast";
            return false;
        }

        if (seenOrder.Count >= MaxSeenCastIds)
            seenIds.Remove(seenOrder.Dequeue().Id);
        seenOrder.Enqueue(new SeenCast { Id = message.castId, ReceivedAt = now });
        reason = "accepted";
        return true;
    }

    private void ExpireSeen(float now)
    {
        while (seenOrder.Count > 0
            && now - seenOrder.Peek().ReceivedAt >= SeenCastLifetimeSeconds)
            seenIds.Remove(seenOrder.Dequeue().Id);
    }

    private static bool IsPosition(double x, double y, double z)
    {
        return IsFiniteFloat(x) && IsFiniteFloat(y) && IsFiniteFloat(z);
    }

    private static bool IsFiniteFloat(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value)
            && value >= -float.MaxValue && value <= float.MaxValue;
    }

    private void Clear()
    {
        seenIds.Clear();
        seenOrder.Clear();
        activeEpoch = 0;
        localCharacterDbId = 0;
        activeSceneId = null;
    }
}
