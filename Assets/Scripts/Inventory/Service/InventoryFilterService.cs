using System;
using System.Collections.Generic;
using Game.Items;

namespace Game.Inventory
{
    public sealed class InventoryFilterService
    {
        public List<InventoryItemState> FilterAndSort(
            IReadOnlyList<InventoryItemState> items,
            InventoryItemCategory category,
            ItemDefinitionTable itemDefinitions)
        {
            List<InventoryItemState> result =
                new List<InventoryItemState>();

            if (items == null)
            {
                return result;
            }

            for (int index = 0; index < items.Count; index++)
            {
                InventoryItemState item = items[index];
                if (item == null)
                {
                    continue;
                }

                if (category == InventoryItemCategory.All)
                {
                    result.Add(item);
                    continue;
                }

                if (itemDefinitions == null)
                {
                    continue;
                }

                ItemDefinition definition =
                    itemDefinitions.GetOrDefault(item.ItemId);
                if (definition == null
                    || definition.IsCurrency
                    || !MatchesCategory(definition.Category, category))
                {
                    continue;
                }

                result.Add(item);
            }

            result.Sort(CompareSlotIndex);
            return result;
        }

        private static bool MatchesCategory(
            ItemCategory itemCategory,
            InventoryItemCategory filterCategory)
        {
            switch (filterCategory)
            {
                case InventoryItemCategory.Equipment:
                    return itemCategory == ItemCategory.Equipment;
                case InventoryItemCategory.Consumable:
                    return itemCategory == ItemCategory.Consumable;
                case InventoryItemCategory.Material:
                    return itemCategory == ItemCategory.Material;
                case InventoryItemCategory.Quest:
                    return itemCategory == ItemCategory.Quest;
                case InventoryItemCategory.Other:
                    return itemCategory == ItemCategory.Other
                        || itemCategory == ItemCategory.ForgingBlueprint;
                default:
                    return false;
            }
        }

        private static int CompareSlotIndex(
            InventoryItemState left,
            InventoryItemState right)
        {
            int slotComparison = left.SlotIndex.CompareTo(right.SlotIndex);
            if (slotComparison != 0)
            {
                return slotComparison;
            }

            return string.Compare(
                left.ItemId,
                right.ItemId,
                StringComparison.Ordinal);
        }
    }
}
