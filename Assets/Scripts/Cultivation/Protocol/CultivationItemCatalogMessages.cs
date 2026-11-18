using System;

namespace Game.Cultivation.Protocol
{
    public static class CultivationItemCatalogMessageIds
    {
        public const string Query = "item_catalog";
        public const string Result = "item_catalog_result";
    }

    [Serializable]
    public sealed class CultivationItemCatalogRequestDto : NetMessageBase
    {
        public long requestSequence;
        public int knownCatalogVersion;
        public bool supportsLifecycleNotifications;
        public bool supportsBatches;

        public CultivationItemCatalogRequestDto(long sequence, int knownVersion)
        {
            msgId = type = CultivationItemCatalogMessageIds.Query;
            requestSequence = sequence;
            knownCatalogVersion = knownVersion;
            supportsLifecycleNotifications = false;
            supportsBatches = true;
        }
    }

    [Serializable]
    public sealed class CultivationItemDefinitionDto
    {
        public string itemId;
        public string kind;
        public int version;
        public string nameKey;
        public string descriptionKey;
        public string iconKey;
        public string appearanceKey;
        public int maxStack;
        public bool bindOnAcquire;
        public bool tradable;
        public bool canDiscard;
        public bool requireDiscardConfirmation;
        public long sellPrice;
        public int requiredLevel;
        public string requiredRoleId;
        public string cooldownGroup;
        public int cooldownMilliseconds;
        public string equipmentId;
        public string equipmentCategory;
        public string equipmentSubType;
        public int quality;
        public int maxDurability;
        public string learnSkillId;
        public string learnTechniqueId;
        public string learnRecipeId;
        public string learnForgingRecipeId;
        public string availability;
        public string requiredJingjieId;
        public int requiredJingjieStage;
        public int requiredAttack;
        public int requiredMagicAttack;
        public CultivationItemEffectDto[] effects;
        public CultivationItemAttributeDto[] attributes;

        public bool IsSkillBook => kind == "skillbook" && !string.IsNullOrWhiteSpace(learnSkillId);

        public bool HasUsablePillEffect
        {
            get
            {
                if (kind != "pill" || effects == null)
                    return false;
                foreach (CultivationItemEffectDto effect in effects)
                    if (effect != null && effect.valueMode == "flat"
                        && effect.amount > 0
                        && (effect.type == "restore_hp"
                            || effect.type == "restore_mp"
                            || effect.type == "add_jingjie_exp"))
                        return true;
                return false;
            }
        }
    }

    [Serializable]
    public sealed class CultivationItemEffectDto
    {
        public string type;
        public string valueMode;
        public long amount;
    }

    [Serializable]
    public sealed class CultivationItemAttributeDto
    {
        public string attributeId;
        public int value;
    }

    [Serializable]
    public sealed class CultivationItemCatalogResultDto : NetMessageBase
    {
        public long requestSequence;
        public bool success;
        public int catalogVersion;
        public long lifecycleRevision;
        public int batchIndex;
        public int batchCount;
        public CultivationItemDefinitionDto[] items;
        public string errorCode;
        public long serverTime;

        // Retain metadata of other item categories that this module does not interpret.
        [NonSerialized] public string rawJson;
    }
}
