using System;
using Game.Cultivation.Protocol;
using Game.Inventory.Protocol.V2;

namespace Game.Alchemy.Protocol
{
    public static class AlchemyMessageIds
    {
        public const string Catalog = "alchemy_catalog";
        public const string CatalogResult = "alchemy_catalog_result";
        public const string Snapshot = "alchemy_snapshot";
        public const string SnapshotResult = "alchemy_snapshot_result";
        public const string Learn = "alchemy_learn";
        public const string LearnResult = "alchemy_learn_result";
        public const string Start = "alchemy_start";
        public const string StartResult = "alchemy_start_result";
        public const string Claim = "alchemy_claim";
        public const string ClaimResult = "alchemy_claim_result";
        public const string Changed = "alchemy_changed";
    }

    [Serializable]
    public sealed class AlchemyCatalogRequestDto : NetMessageBase
    {
        public long requestSequence;
        public bool supportsCatalogBatches;

        public AlchemyCatalogRequestDto(long sequence)
        {
            msgId = type = AlchemyMessageIds.Catalog;
            requestSequence = sequence;
            supportsCatalogBatches = true;
        }
    }

    [Serializable]
    public sealed class AlchemySnapshotRequestDto : NetMessageBase
    {
        public long requestSequence;

        public AlchemySnapshotRequestDto(long sequence)
        {
            msgId = type = AlchemyMessageIds.Snapshot;
            requestSequence = sequence;
        }
    }

    [Serializable]
    public sealed class AlchemyLearnRequestDto : NetMessageBase
    {
        public string alchemyOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public long configurationRevision;
        public long expectedInventoryRevision;
        public string recipeId;
        public long stackId;

        public AlchemyLearnRequestDto(string sessionId, long sequence,
            string requestId, long configuration, long inventory,
            string selectedRecipeId, long bookStackId)
        {
            msgId = type = AlchemyMessageIds.Learn;
            alchemyOperationSessionId = sessionId;
            requestSequence = sequence;
            clientRequestId = requestId;
            configurationRevision = configuration;
            expectedInventoryRevision = inventory;
            recipeId = selectedRecipeId;
            stackId = bookStackId;
        }
    }

    [Serializable]
    public sealed class AlchemyRecipeMaterialDto
    {
        public string itemId;
        public int count;
    }

    [Serializable]
    public sealed class AlchemyRecipeOutputDto
    {
        public int quality;
        public string itemId;
        public int count;
        public int weight;
    }

    [Serializable]
    public sealed class AlchemyRecipeDto
    {
        public string recipeId;
        public string name;
        public string iconKey;
        public string description;
        public int tier;
        public int requiredLevel;
        public string requiredJingjieId;
        public int requiredJingjieStage;
        public int durationSeconds;
        public int successRateBps;
        public bool bindOutput;
        public AlchemyRecipeMaterialDto[] materials;
        public AlchemyRecipeOutputDto[] outputs;
    }

    [Serializable]
    public sealed class AlchemySelectedMaterialDto
    {
        public long stackId;
        public int count;

        public AlchemySelectedMaterialDto(long stack, int amount)
        {
            stackId = stack;
            count = amount;
        }
    }

    [Serializable]
    public sealed class AlchemyStartRequestDto : NetMessageBase
    {
        public string alchemyOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public long configurationRevision;
        public long expectedInventoryRevision;
        public string recipeId;
        public AlchemySelectedMaterialDto[] materials;

        public AlchemyStartRequestDto(string sessionId, long sequence,
            string requestId, long configuration, long inventory,
            string selectedRecipeId, AlchemySelectedMaterialDto[] selectedMaterials)
        {
            msgId = type = AlchemyMessageIds.Start;
            alchemyOperationSessionId = sessionId;
            requestSequence = sequence;
            clientRequestId = requestId;
            configurationRevision = configuration;
            expectedInventoryRevision = inventory;
            recipeId = selectedRecipeId;
            materials = selectedMaterials;
        }
    }

    [Serializable]
    public sealed class AlchemyClaimRequestDto : NetMessageBase
    {
        public string alchemyOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public long expectedInventoryRevision;
        public string taskId;

        public AlchemyClaimRequestDto(string sessionId, long sequence,
            string requestId, long inventory, string selectedTaskId)
        {
            msgId = type = AlchemyMessageIds.Claim;
            alchemyOperationSessionId = sessionId;
            requestSequence = sequence;
            clientRequestId = requestId;
            expectedInventoryRevision = inventory;
            taskId = selectedTaskId;
        }
    }

    [Serializable]
    public sealed class AlchemyTaskDto
    {
        public string taskId;
        public string recipeId;
        public string recipeName;
        public int tier;
        public long configurationRevision;
        public long startedAtUnixMilliseconds;
        public long endsAtUnixMilliseconds;
        public string status;
        public bool succeeded;
        public int quality;
        public string outputItemId;
        public int outputCount;
        public bool outputBound;

        [NonSerialized] private bool? parsedSucceeded;
        public bool? ParsedSucceeded => parsedSucceeded;
        internal void SetSucceeded(bool? value) { parsedSucceeded = value; }
    }

    [Serializable]
    public sealed class AlchemySnapshotDto
    {
        public long characterDbId;
        public long revision;
        public string[] learnedRecipeIds;
        public AlchemyTaskDto currentTask;
    }

    [Serializable]
    public class AlchemyResponseDto : NetMessageBase
    {
        public long requestSequence;
        public string alchemyOperationSessionId;
        public string clientRequestId;
        public bool success;
        public string errorCode;
        public string message;
        public long configurationRevision;
        public AlchemyRecipeDto[] recipes;
        public string catalogSyncId;
        public int batchIndex;
        public int batchCount;
        public int totalCount;
        public AlchemySnapshotDto snapshot;
        public bool idempotentReplay;
        public bool requiresInventorySnapshot;
        public long inventoryRevision;
        public InventoryV2StackChangeDto[] stackItemChanges;
        public long serverTime;
    }

    [Serializable]
    public sealed class AlchemySnapshotResultDto : AlchemyResponseDto { }

    public static class AlchemySnapshotWireState
    {
        public static bool TryRead(string json, out bool hasSnapshot,
            out bool snapshotIsNull, out bool hasCurrentTask,
            out bool currentTaskIsNull)
        {
            return RefiningSnapshotWireState.TryRead(json, "currentTask",
                out hasSnapshot, out snapshotIsNull, out hasCurrentTask,
                out currentTaskIsNull);
        }

        public static bool TryReadSucceeded(string json, out bool present,
            out bool? succeeded)
        {
            return RefiningSnapshotWireState.TryReadTaskBoolean(json,
                "currentTask", "succeeded", out present, out succeeded);
        }
    }
}
