using System;
using Game.Inventory.Protocol.V2;

[Serializable]
public sealed class SkillLearnRequest : NetMessageBase
{
    public string learnSessionId;
    public long requestSequence;
    public long expectedInventoryRevision;
    public long stackId;
    public long clientTick;

    public SkillLearnRequest(string sessionId, long sequence, long inventoryRevision,
        long stackId, long clientTick)
    {
        msgId = type = "skill_learn";
        learnSessionId = sessionId;
        requestSequence = sequence;
        expectedInventoryRevision = inventoryRevision;
        this.stackId = stackId;
        this.clientTick = clientTick;
    }
}

public sealed class SkillLearnResult : NetMessageBase
{
    public string learnSessionId;
    public long requestSequence;
    public bool success;
    public string status;
    public string itemId;
    public string skillId;
    public int consumedCount;
    public long inventoryRevision;
    public InventoryV2StackChangeDto[] stackItemChanges;
    public SkillSnapshotEntry learnedSkill;
    public bool requiresInventorySnapshot;
    public string errorCode;
    public string message;
    public long serverTime;
}

[Serializable]
public sealed class SkillSnapshotRequest : NetMessageBase
{
    public long requestSequence;

    public SkillSnapshotRequest(long sequence)
    {
        msgId = type = "skill_snapshot";
        requestSequence = sequence;
    }
}

public sealed class SkillSnapshotResult : NetMessageBase
{
    public long requestSequence;
    public bool success;
    public long characterDbId;
    public long skillRevision;
    public long? skillBarRevision;
    public SkillSnapshotEntry[] skills;
    public string snapshotId;
    public int? batchIndex;
    public int? batchCount;
    public int? totalCount;
    public string errorCode;
    public string message;
    public long serverTime;
}

public sealed class SkillSnapshotEntry
{
    public string skillId;
    public int skillLevel;
    public int? equippedSlot;
    public long unlockedAtUnixMilliseconds;
    public SkillProgressData progress;
}

[Serializable]
public sealed class SkillBarSaveRequest : NetMessageBase
{
    public string learnSessionId;
    public long requestSequence;
    public long expectedSkillBarRevision;
    public SkillBarSlot[] slots;

    public SkillBarSaveRequest(string sessionId, long sequence, long revision, SkillBarSlot[] layout)
    {
        msgId = type = "skill_bar_save";
        learnSessionId = sessionId;
        requestSequence = sequence;
        expectedSkillBarRevision = revision;
        slots = layout;
    }
}

public sealed class SkillBarSaveResult : NetMessageBase
{
    public string learnSessionId;
    public long requestSequence;
    public long characterDbId;
    public bool success;
    public long? skillBarRevision;
    public SkillBarSlot[] slots;
    public string errorCode;
}

[Serializable]
public sealed class SkillBarSlot
{
    public int slotIndex;
    public string skillId;
}

public sealed class SkillProgressResult : NetMessageBase
{
    public long characterDbId;
    public SkillProgressData skill;
    public long serverTime;
}

public sealed class SkillProgressData
{
    public string skillId;
    public int skillLevel;
    public int maxLevel;
    public long practiceCount;
    public long? requiredCasts;
    public long practiceRevision;
    public SkillProgressAttributes attributes;
}

public sealed class SkillProgressAttributes
{
    public string name;
    public string targetType;
    public string effectType;
    public string damageType;
    public double range;
    public int cooldownTicks;
    public int hitDelayTicks;
    public int mpCost;
    public int damageCoefficientPermille;
    public int flatDamage;
    public int healAmount;
    public double areaRadius;
    public int durationTicks;
    public int tickIntervalTicks;
    public int maxTargets;
    public string buffId;
}
