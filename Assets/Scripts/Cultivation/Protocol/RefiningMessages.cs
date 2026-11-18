using System;
using Game.Inventory.Protocol.V2;

namespace Game.Cultivation.Protocol
{
    public static class RefiningMessageIds
    {
        public const string Catalog = "refining_catalog";
        public const string CatalogResult = "refining_catalog_result";
        public const string Snapshot = "refining_snapshot";
        public const string SnapshotResult = "refining_snapshot_result";
        public const string Start = "refining_start";
        public const string StartResult = "refining_start_result";
        public const string Changed = "refining_changed";
    }

    [Serializable]
    public sealed class RefiningQueryRequestDto : NetMessageBase
    {
        public long requestSequence;

        public RefiningQueryRequestDto(string requestType, long sequence)
        {
            msgId = type = requestType;
            requestSequence = sequence;
        }
    }

    [Serializable]
    public sealed class RefiningStartRequestDto : NetMessageBase
    {
        public string jingjieOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public long configurationRevision;
        public long expectedInventoryRevision;
        public long stackId;
        public int count;

        public RefiningStartRequestDto(string sessionId, long sequence,
            string requestId, long configuration, long inventory, long stack,
            int amount)
        {
            msgId = type = RefiningMessageIds.Start;
            jingjieOperationSessionId = sessionId;
            requestSequence = sequence;
            clientRequestId = requestId;
            configurationRevision = configuration;
            expectedInventoryRevision = inventory;
            stackId = stack;
            count = amount;
        }
    }

    [Serializable]
    public sealed class RefiningRuleDto
    {
        public string itemId;
        public long secondsPerItem;
        public long cultivationPerItem;
    }

    [Serializable]
    public sealed class RefiningMaterialDto
    {
        public string itemId;
        public int count;
    }

    [Serializable]
    public sealed class RefiningBreakthroughDto
    {
        public string fromJingjieId;
        public int fromJingjieStage;
        public string toJingjieId;
        public int toJingjieStage;
        public long requiredCultivation;
        public RefiningMaterialDto[] materials;
    }

    [Serializable]
    public sealed class RefiningTaskDto
    {
        public string taskId;
        public string itemId;
        public int count;
        public long secondsPerItem;
        public long cultivationPerItem;
        public long totalCultivation;
        public long configurationRevision;
        public long startedAtUnixMilliseconds;
        public long endsAtUnixMilliseconds;
        public string status;
    }

    [Serializable]
    public sealed class RefiningSnapshotDto
    {
        public long characterDbId;
        public long revision;
        public long cultivation;
        public long totalCultivation;
        public RefiningTaskDto activeTask;
        public RefiningTaskDto lastCompletedTask;
    }

    [Serializable]
    public sealed class RefiningResultDto : NetMessageBase
    {
        public bool success;
        public string errorCode;
        public string message;
        public long requestSequence;
        public string clientRequestId;
        public string jingjieOperationSessionId;
        public long configurationRevision;
        public long serverTime;
        public RefiningRuleDto[] rules;
        public RefiningBreakthroughDto[] breakthroughs;
        public RefiningSnapshotDto snapshot;
        public long inventoryRevision;
        public InventoryV2StackChangeDto[] stackItemChanges;
        public bool requiresInventorySnapshot;
        public bool idempotentReplay;
    }
}
