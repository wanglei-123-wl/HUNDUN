using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Items
{
    public enum ItemDetailCategory
    {
        [InspectorName("自动")]
        Auto = 0,
        [InspectorName("装备")]
        Equipment = 1,
        [InspectorName("功法")]
        Cultivation = 2,
        [InspectorName("金币")]
        Currency = 3,
        [InspectorName("丹药")]
        Consumable = 4,
        [InspectorName("材料")]
        Material = 5,
        [InspectorName("任务物品")]
        Quest = 6,
        [InspectorName("其他")]
        Other = 7,
        [InspectorName("丹药")]
        Pill = 8,
        [InspectorName("锻造图纸")]
        ForgingBlueprint = 9
    }

    [Serializable]
    public sealed class ItemDetailEntry
    {
        [Tooltip("详情字段名称。")]
        public string Label = string.Empty;

        [Tooltip("详情字段内容。")]
        public string Value = string.Empty;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(Label)
            || !string.IsNullOrWhiteSpace(Value);
    }

    [Serializable]
    public sealed class ItemDetailData
    {
        public string Identity = string.Empty;
        public string DisplayName = string.Empty;
        public Sprite Icon;
        public ItemDetailCategory Category = ItemDetailCategory.Other;
        public string CategoryLabel = string.Empty;
        public string Description = string.Empty;
        public string DetailText = string.Empty;
        public List<ItemDetailEntry> Entries = new List<ItemDetailEntry>();

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(Identity)
            || !string.IsNullOrWhiteSpace(DisplayName)
            || Icon != null;
    }

    public static class ItemDetailCategoryResolver
    {
        public static ItemDetailCategory Resolve(ItemDefinition definition)
        {
            if (definition == null)
                return ItemDetailCategory.Other;

            if (definition.DisplayCategory != ItemDetailCategory.Auto)
                return definition.DisplayCategory;

            if (definition.Category == ItemCategory.SkillBook)
                return ItemDetailCategory.Cultivation;
            if (definition.Category == ItemCategory.ForgingBlueprint)
                return ItemDetailCategory.ForgingBlueprint;
            if (definition.IsCurrency
                || definition.Category == ItemCategory.Currency)
                return ItemDetailCategory.Currency;
            if (definition.Category == ItemCategory.Equipment)
                return ItemDetailCategory.Equipment;
            if (definition.Category == ItemCategory.Consumable)
                return ItemDetailCategory.Consumable;
            if (definition.Category == ItemCategory.Material)
                return ItemDetailCategory.Material;
            if (definition.Category == ItemCategory.Quest)
                return ItemDetailCategory.Quest;
            return ItemDetailCategory.Other;
        }

        public static string GetLabel(ItemDetailCategory category)
        {
            switch (category)
            {
                case ItemDetailCategory.Equipment:
                    return "装备";
                case ItemDetailCategory.Cultivation:
                    return "功法";
                case ItemDetailCategory.Currency:
                    return "金币";
                case ItemDetailCategory.Consumable:
                    return "消耗品";
                case ItemDetailCategory.Pill:
                    return "丹药";
                case ItemDetailCategory.Material:
                    return "材料";
                case ItemDetailCategory.Quest:
                    return "任务物品";
                case ItemDetailCategory.ForgingBlueprint:
                    return "锻造图纸";
                default:
                    return "其他";
            }
        }
    }

    public static class ItemDetailDataResolver
    {
        public static ItemDetailData FromItem(ItemDefinition definition)
        {
            if (definition == null)
                return null;

            ItemDetailCategory category =
                ItemDetailCategoryResolver.Resolve(definition);
            var data = new ItemDetailData
            {
                Identity = definition.ItemId ?? string.Empty,
                DisplayName = definition.DisplayName ?? string.Empty,
                Icon = definition.Icon,
                Category = category,
                CategoryLabel = ItemDetailCategoryResolver.GetLabel(category),
                Description = definition.Description ?? string.Empty,
                DetailText = definition.DetailText ?? string.Empty
            };

            if (definition.DetailEntries != null)
            {
                for (int index = 0; index < definition.DetailEntries.Count; index++)
                {
                    ItemDetailEntry entry = definition.DetailEntries[index];
                    if (entry == null || !entry.IsValid)
                        continue;

                    data.Entries.Add(new ItemDetailEntry
                    {
                        Label = entry.Label ?? string.Empty,
                        Value = entry.Value ?? string.Empty
                    });
                }
            }

            return data;
        }

    }
}
