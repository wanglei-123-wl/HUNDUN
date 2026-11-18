using System;
using Game.Inventory.Protocol.V2;

namespace Game.Cultivation.Protocol
{
    public static class CultivationMessageIds
    {
        public const string Catalog = "cultivation_catalog";
        public const string CatalogResult = "cultivation_catalog_result";
        public const string Snapshot = "cultivation_snapshot";
        public const string SnapshotResult = "cultivation_snapshot_result";
        public const string Learn = "cultivation_learn";
        public const string LearnResult = "cultivation_learn_result";
        public const string Start = "cultivation_start";
        public const string StartResult = "cultivation_start_result";
        public const string Changed = "cultivation_changed";
    }

    [Serializable]
    public sealed class CultivationQueryRequestDto : NetMessageBase
    {
        public long requestSequence;

        public CultivationQueryRequestDto(string requestType, long sequence)
        {
            msgId = type = requestType;
            requestSequence = sequence;
        }
    }

    [Serializable]
    public sealed class CultivationCatalogRequestDto : NetMessageBase
    {
        public long requestSequence;
        public bool characterScoped = true;
        public bool supportsCatalogBatches = true;
        public long knownConfigurationRevision;
        public string[] knownTechniqueIds;

        public CultivationCatalogRequestDto(long sequence, long knownRevision,
            string[] knownIds)
        {
            msgId = type = CultivationMessageIds.Catalog;
            requestSequence = sequence;
            knownConfigurationRevision = knownRevision;
            knownTechniqueIds = knownIds;
        }
    }

    [Serializable]
    public sealed class CultivationLearnRequestDto : NetMessageBase
    {
        public string cultivationOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public long configurationRevision;
        public long expectedInventoryRevision;
        public long stackId;

        public CultivationLearnRequestDto(string session, long sequence, string requestId,
            long configuration, long inventory, long stack)
        {
            msgId = type = CultivationMessageIds.Learn;
            cultivationOperationSessionId = session;
            requestSequence = sequence;
            clientRequestId = requestId;
            configurationRevision = configuration;
            expectedInventoryRevision = inventory;
            stackId = stack;
        }
    }

    [Serializable]
    public sealed class CultivationStartRequestDto : NetMessageBase
    {
        public string cultivationOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public long configurationRevision;
        public long expectedInventoryRevision;
        public string techniqueId;

        public CultivationStartRequestDto(string session, long sequence, string requestId,
            long configuration, long inventory, string technique)
        {
            msgId = type = CultivationMessageIds.Start;
            cultivationOperationSessionId = session;
            requestSequence = sequence;
            clientRequestId = requestId;
            configurationRevision = configuration;
            expectedInventoryRevision = inventory;
            techniqueId = technique;
        }
    }

    [Serializable]
    public sealed class CultivationGradeDto
    {
        public string gradeId;
        public string name;
        public int order;
    }

    [Serializable]
    public sealed class CultivationMaterialDto
    {
        public string itemId;
        public int quantity;
    }

    [Serializable]
    public sealed class CultivationLevelDto
    {
        public int level;
        public int comprehension;
        public long durationSeconds;
        [NonSerialized] public int referenceComprehension;
        public CultivationMaterialDto[] materials;

        // JsonUtility cannot represent int-or-null; the wire reader restores
        // this value together with its presence flag.
        [NonSerialized] public bool hasReferenceComprehension;
    }

    [Serializable]
    public sealed class CultivationTechniqueDto
    {
        public string techniqueId;
        public string name;
        public string gradeId;
        public int requiredLevel;
        public bool enabled;
        public CultivationLevelDto[] levels;
    }

    [Serializable]
    public sealed class CultivationCatalogResultDto : NetMessageBase
    {
        public long requestSequence;
        public bool success;
        public long configurationRevision;
        public string catalogSyncId;
        public int batchIndex;
        public int batchCount;
        public int totalCount;
        public int totalGradeCount;
        public int totalCharacterTechniqueCount;
        public string[] characterTechniqueIds;
        public long inventoryRevision;
        public long cultivationRevision;
        public CultivationGradeDto[] grades;
        public CultivationTechniqueDto[] techniques;
        public string errorCode;
        public long serverTime;
    }

    [Serializable]
    public sealed class CultivationLearnedTechniqueDto
    {
        public string techniqueId;
        public int level;
        public long learnedAt;
    }

    [Serializable]
    public sealed class CultivationTaskDto
    {
        public string taskId;
        public string techniqueId;
        public int fromLevel;
        public int targetLevel;
        public long startedAt;
        public long endsAt;
        public long configurationRevision;
        public CultivationMaterialDto[] paidMaterials;
        public string status;
    }

    [Serializable]
    public sealed class CultivationProfessionProgressDto
    {
        public int rank;
        public int experience;
        public int requiredExperience;
    }

    [Serializable]
    public sealed class CultivationSnapshotDto
    {
        public long characterDbId;
        public long revision;
        public long configurationRevision;
        public long baseComprehension;
        public long techniqueComprehension;
        public long totalComprehension;
        public CultivationProfessionProgressDto alchemyProfession;
        public CultivationProfessionProgressDto forgingProfession;
        public CultivationLearnedTechniqueDto[] techniques;
        public CultivationTaskDto activeTask;
    }

    [Serializable]
    public sealed class CultivationSnapshotResultDto : NetMessageBase
    {
        public long requestSequence;
        public bool success;
        public CultivationSnapshotDto snapshot;
        public string errorCode;
        public long serverTime;
    }

    [Serializable]
    public sealed class CultivationChangedDto : NetMessageBase
    {
        public string cultivationOperationSessionId;
        public CultivationSnapshotDto snapshot;
        public long serverTime;
    }

    [Serializable]
    public sealed class CultivationWriteResultDto : NetMessageBase
    {
        public string cultivationOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public bool success;
        public string errorCode;
        public string message;
        public bool idempotentReplay;
        public bool requiresInventorySnapshot;
        public long inventoryRevision;
        public InventoryV2StackChangeDto[] stackItemChanges;
        public CultivationMaterialDto[] missingMaterials;
        public CultivationSnapshotDto snapshot;
        public long serverTime;
    }
}
