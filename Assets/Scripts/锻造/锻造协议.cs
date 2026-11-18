using System;
using Game.Inventory.Protocol.V2;

namespace Game.Forging.Protocol
{
    public static class 锻造消息
    {
        public const int 支持版本 = 1;
        public const int 兼容版本 = 2;
        public static bool IsSupportedVersion(int version) =>
            version == 支持版本 || version == 兼容版本;
        public const string 目录 = "forging_catalog";
        public const string 目录结果 = "forging_catalog_result";
        public const string 快照 = "forging_snapshot";
        public const string 快照结果 = "forging_snapshot_result";
        public const string 学习 = "forging_learn";
        public const string 学习结果 = "forging_learn_result";
        public const string 开工 = "forging_start";
        public const string 开工结果 = "forging_start_result";
        public const string 领取 = "forging_claim";
        public const string 领取结果 = "forging_claim_result";
        public const string 变化 = "forging_changed";
    }

    [Serializable]
    public sealed class 锻造目录请求 : NetMessageBase
    {
        public long requestSequence;
        public bool supportsCatalogBatches;

        public 锻造目录请求(long sequence)
        {
            msgId = type = 锻造消息.目录;
            requestSequence = sequence;
            supportsCatalogBatches = true;
        }
    }

    [Serializable]
    public sealed class 锻造快照请求 : NetMessageBase
    {
        public long requestSequence;

        public 锻造快照请求(long sequence)
        {
            msgId = type = 锻造消息.快照;
            requestSequence = sequence;
        }
    }

    [Serializable]
    public sealed class 锻造学习请求 : NetMessageBase
    {
        public string forgingOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public long expectedInventoryRevision;
        public long stackId;

        public 锻造学习请求(string sessionId, long sequence, string requestId,
            long inventoryRevision, long selectedStackId)
        {
            msgId = type = 锻造消息.学习;
            forgingOperationSessionId = sessionId;
            requestSequence = sequence;
            clientRequestId = requestId;
            expectedInventoryRevision = inventoryRevision;
            stackId = selectedStackId;
        }
    }

    [Serializable]
    public sealed class 锻造所选材料
    {
        public long stackId;
        public int count;

        public 锻造所选材料(long id, int amount)
        {
            stackId = id;
            count = amount;
        }
    }

    [Serializable]
    public sealed class 锻造开工请求 : NetMessageBase
    {
        public string forgingOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public long configurationRevision;
        public long expectedInventoryRevision;
        public string recipeId;
        public 锻造所选材料[] materials;

        public 锻造开工请求(string sessionId, long sequence, string requestId,
            long configuration, long inventory, string selectedRecipeId,
            锻造所选材料[] selectedMaterials)
        {
            msgId = type = 锻造消息.开工;
            forgingOperationSessionId = sessionId;
            requestSequence = sequence;
            clientRequestId = requestId;
            configurationRevision = configuration;
            expectedInventoryRevision = inventory;
            recipeId = selectedRecipeId;
            materials = selectedMaterials;
        }
    }

    [Serializable]
    public sealed class 锻造领取请求 : NetMessageBase
    {
        public string forgingOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public long configurationRevision;
        public long expectedInventoryRevision;
        public string taskId;

        public 锻造领取请求(string sessionId, long sequence, string requestId,
            long inventory, string selectedTaskId)
        {
            msgId = type = 锻造消息.领取;
            forgingOperationSessionId = sessionId;
            requestSequence = sequence;
            clientRequestId = requestId;
            configurationRevision = 0;
            expectedInventoryRevision = inventory;
            taskId = selectedTaskId;
        }
    }

    [Serializable]
    public sealed class 锻造配方材料
    {
        public string itemId;
        public int count;
        public bool isCurrency;
    }

    [Serializable]
    public sealed class 锻造配方
    {
        public string recipeId;
        public string name;
        public long durationSeconds;
        public int successRateBps;
        public 锻造配方材料[] materials;
    }

    [Serializable]
    public sealed class 锻造任务
    {
        public string taskId;
        public string recipeId;
        public string recipeName;
        public long configurationRevision;
        public long startedAtUnixMilliseconds;
        public long endsAtUnixMilliseconds;
        public string status;
        public bool? succeeded;
        public string outputItemId;
        public int outputCount;
        public bool outputBound;
    }

    [Serializable]
    public sealed class 锻造快照
    {
        public long characterDbId;
        public long revision;
        public string[] learnedRecipeIds;
        public 锻造任务 currentTask;
    }

    [Serializable]
    public sealed class 锻造装备变化
    {
        public string itemInstanceId;
        public string itemId;
        public string equipmentId;
        public bool isBound;
        public int enhanceLevel;
        public int durability;
    }

    [Serializable]
    public sealed class 锻造响应 : NetMessageBase
    {
        public string forgingOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public bool success;
        public bool? forgingSucceeded;
        public string errorCode;
        public string message;
        public long configurationRevision;
        public 锻造配方[] recipes;
        public string catalogSyncId;
        public int batchIndex;
        public int batchCount;
        public int totalCount;
        public 锻造快照 snapshot;
        public bool idempotentReplay;
        public bool requiresInventorySnapshot;
        public long inventoryRevision;
        public InventoryV2StackChangeDto[] stackItemChanges;
        public 锻造装备变化[] equipmentItems;
        public InventoryV2WalletDto[] wallets;
        public long serverTime;
    }
}
