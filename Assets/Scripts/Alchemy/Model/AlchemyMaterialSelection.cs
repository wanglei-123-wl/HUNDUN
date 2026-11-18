using System;
using System.Collections.Generic;
using Game.Alchemy.Protocol;
using Game.Inventory.Model.V2;

namespace Game.Alchemy.Model
{
    public enum AlchemySelectionError
    {
        None,
        NoRecipe,
        Locked,
        ConfigurationChanged,
        InventoryUnavailable,
        WrongCharacter,
        StackMissing,
        MaterialMismatch,
        InvalidQuantity,
        QuantityMismatch,
        EmptySelection,
        UnsupportedItem
    }

    // Preselection owns no items. Only a confirmed alchemy_start may change inventory.
    public sealed class AlchemyMaterialSelection
    {
        private sealed class Entry
        {
            public string ItemId;
            public int Count;
        }

        private readonly Dictionary<string, int> required =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<long, Entry> selected =
            new Dictionary<long, Entry>();
        private readonly List<long> selectedOrder = new List<long>();
        private readonly List<string> unsupported =
            new List<string>();
        private long characterDbId;
        private long configurationRevision;
        private string recipeId;

        public bool IsLocked { get; private set; }
        public string RecipeId => recipeId;
        public long CharacterDbId => characterDbId;
        public long ConfigurationRevision => configurationRevision;
        public int SelectedStackCount => selected.Count;
        public IReadOnlyList<long> SelectedStackIds =>
            new List<long>(selectedOrder).AsReadOnly();
        public IReadOnlyList<string> UnsupportedIdentities => unsupported.AsReadOnly();

        public bool SelectRecipe(AlchemyRecipeDto recipe, long revision,
            long ownerCharacterDbId)
        {
            if (IsLocked)
                return false;
            Reset();
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.recipeId)
                || recipe.materials == null || recipe.materials.Length == 0
                || revision < 0 || ownerCharacterDbId <= 0)
                return false;
            foreach (AlchemyRecipeMaterialDto material in recipe.materials)
            {
                if (material == null || string.IsNullOrWhiteSpace(material.itemId)
                    || material.count <= 0
                    || required.ContainsKey(material.itemId))
                {
                    Reset();
                    return false;
                }
                required.Add(material.itemId, material.count);
            }
            characterDbId = ownerCharacterDbId;
            configurationRevision = revision;
            recipeId = recipe.recipeId;
            return true;
        }

        public bool RestorePending(string selectedRecipeId, long revision,
            long ownerCharacterDbId, AlchemySelectedMaterialDto[] materials,
            InventoryOwnershipSnapshot inventory)
        {
            if (IsLocked || recipeId != null || selected.Count != 0
                || string.IsNullOrWhiteSpace(selectedRecipeId) || revision < 0
                || ownerCharacterDbId <= 0 || materials == null
                || materials.Length == 0)
                return false;
            var seen = new HashSet<long>();
            foreach (AlchemySelectedMaterialDto material in materials)
                if (material == null || material.stackId <= 0
                    || material.count <= 0 || !seen.Add(material.stackId))
                    return false;
            characterDbId = ownerCharacterDbId;
            configurationRevision = revision;
            recipeId = selectedRecipeId;
            foreach (AlchemySelectedMaterialDto material in materials)
            {
                selectedOrder.Add(material.stackId);
                selected.Add(material.stackId, new Entry { Count = material.count });
            }
            SyncRestoredStackItems(inventory);
            IsLocked = true;
            return true;
        }

        public void SyncRestoredStackItems(InventoryOwnershipSnapshot inventory)
        {
            if (inventory == null || inventory.CharacterDbId != characterDbId
                || inventory.Stacks == null)
                return;
            foreach (long stackId in selectedOrder)
                if (selected.TryGetValue(stackId, out Entry entry)
                    && string.IsNullOrEmpty(entry.ItemId))
                {
                    InventoryStackState stack = FindStack(inventory, stackId);
                    if (stack != null)
                        entry.ItemId = stack.ItemId;
                }
        }

        public AlchemySelectionError SetStack(
            InventoryOwnershipSnapshot inventory, long stackId, int count)
        {
            if (IsLocked)
                return AlchemySelectionError.Locked;
            if (recipeId == null)
                return AlchemySelectionError.NoRecipe;
            AlchemySelectionError inventoryError = CheckInventory(inventory);
            if (inventoryError != AlchemySelectionError.None)
                return inventoryError;
            if (stackId <= 0 || count <= 0)
                return AlchemySelectionError.InvalidQuantity;

            InventoryStackState stack = FindStack(inventory, stackId);
            if (stack == null || stack.Count <= 0)
                return AlchemySelectionError.StackMissing;
            if (string.IsNullOrWhiteSpace(stack.ItemId))
                return AlchemySelectionError.MaterialMismatch;
            if (count > stack.Count)
                return AlchemySelectionError.InvalidQuantity;

            if (!selected.ContainsKey(stackId))
                selectedOrder.Add(stackId);
            selected[stackId] = new Entry { ItemId = stack.ItemId, Count = count };
            return AlchemySelectionError.None;
        }

        public AlchemySelectionError SetUnsupportedItem(
            InventoryOwnershipSnapshot inventory, string identity)
        {
            if (IsLocked)
                return AlchemySelectionError.Locked;
            if (recipeId == null)
                return AlchemySelectionError.NoRecipe;
            AlchemySelectionError inventoryError = CheckInventory(inventory);
            if (inventoryError != AlchemySelectionError.None)
                return inventoryError;
            if (string.IsNullOrWhiteSpace(identity) || inventory.Equipment == null)
                return AlchemySelectionError.StackMissing;
            foreach (InventoryEquipmentState equipment in inventory.Equipment)
                if (equipment != null && equipment.Identity == identity)
                {
                    if (!unsupported.Contains(identity))
                        unsupported.Add(identity);
                    return AlchemySelectionError.None;
                }
            return AlchemySelectionError.StackMissing;
        }

        public bool RemoveUnsupportedItem(string identity)
        {
            return !IsLocked && unsupported.Remove(identity);
        }

        public bool RemoveStack(long stackId)
        {
            if (IsLocked || !selected.Remove(stackId))
                return false;
            selectedOrder.Remove(stackId);
            return true;
        }

        public bool TryGetSelectedStack(long stackId, out string itemId,
            out int count)
        {
            itemId = null;
            count = 0;
            if (!selected.TryGetValue(stackId, out Entry entry))
                return false;
            itemId = entry.ItemId;
            count = entry.Count;
            return true;
        }

        public long SelectedCount(string itemId)
        {
            long count = 0;
            foreach (Entry entry in selected.Values)
                if (string.Equals(entry.ItemId, itemId, StringComparison.Ordinal))
                    count += entry.Count;
            return count;
        }

        public int SelectedCountForStack(long stackId)
        {
            return selected.TryGetValue(stackId, out Entry entry)
                ? entry.Count : 0;
        }

        public int RemoveItem(string itemId)
        {
            if (IsLocked || string.IsNullOrEmpty(itemId))
                return 0;
            var removed = new List<long>();
            foreach (KeyValuePair<long, Entry> pair in selected)
                if (string.Equals(pair.Value.ItemId, itemId,
                    StringComparison.Ordinal))
                    removed.Add(pair.Key);
            foreach (long stackId in removed)
            {
                selected.Remove(stackId);
                selectedOrder.Remove(stackId);
            }
            return removed.Count;
        }

        public int RemoveInvalidStacks(InventoryOwnershipSnapshot inventory)
        {
            if (IsLocked || recipeId == null
                || CheckInventory(inventory) != AlchemySelectionError.None)
                return 0;
            var invalid = new List<long>();
            foreach (KeyValuePair<long, Entry> pair in selected)
            {
                InventoryStackState stack = FindStack(inventory, pair.Key);
                if (stack == null || stack.ItemId != pair.Value.ItemId
                    || stack.Count < pair.Value.Count)
                    invalid.Add(pair.Key);
            }
            foreach (long stackId in invalid)
            {
                selected.Remove(stackId);
                selectedOrder.Remove(stackId);
            }
            return invalid.Count;
        }

        public AlchemySelectionError CheckReady(
            InventoryOwnershipSnapshot inventory, long currentConfigurationRevision)
        {
            AlchemySelectionError eligibility = CheckEligibility(inventory,
                currentConfigurationRevision);
            if (eligibility != AlchemySelectionError.None)
                return eligibility;
            if (unsupported.Count > 0)
                return AlchemySelectionError.UnsupportedItem;
            if (selected.Count == 0)
                return AlchemySelectionError.EmptySelection;

            foreach (KeyValuePair<long, Entry> pair in selected)
            {
                InventoryStackState stack = FindStack(inventory, pair.Key);
                if (stack == null || stack.Count <= 0)
                    return AlchemySelectionError.StackMissing;
                Entry entry = pair.Value;
                if (stack.ItemId != entry.ItemId
                    || string.IsNullOrWhiteSpace(stack.ItemId))
                    return AlchemySelectionError.MaterialMismatch;
                if (entry.Count <= 0 || entry.Count > stack.Count)
                    return AlchemySelectionError.InvalidQuantity;
            }
            return AlchemySelectionError.None;
        }

        public AlchemySelectionError CheckEligibility(
            InventoryOwnershipSnapshot inventory, long currentConfigurationRevision)
        {
            if (IsLocked)
                return AlchemySelectionError.Locked;
            if (recipeId == null)
                return AlchemySelectionError.NoRecipe;
            if (configurationRevision != currentConfigurationRevision)
                return AlchemySelectionError.ConfigurationChanged;
            AlchemySelectionError inventoryError = CheckInventory(inventory);
            if (inventoryError != AlchemySelectionError.None)
                return inventoryError;
            return AlchemySelectionError.None;
        }

        public AlchemySelectionError PrepareStart(
            InventoryOwnershipSnapshot inventory, long currentConfigurationRevision,
            out AlchemySelectedMaterialDto[] materials,
            out long expectedInventoryRevision)
        {
            materials = null;
            expectedInventoryRevision = 0;
            AlchemySelectionError error = CheckReady(inventory,
                currentConfigurationRevision);
            if (error != AlchemySelectionError.None)
                return error;

            var stackIds = new List<long>(selected.Keys);
            stackIds.Sort();
            materials = new AlchemySelectedMaterialDto[stackIds.Count];
            for (int i = 0; i < stackIds.Count; i++)
                materials[i] = new AlchemySelectedMaterialDto(stackIds[i],
                    selected[stackIds[i]].Count);
            expectedInventoryRevision = inventory.InventoryRevision;
            IsLocked = true;
            return AlchemySelectionError.None;
        }

        public void ResolveOperation(bool clearSelection)
        {
            IsLocked = false;
            if (clearSelection)
            {
                selected.Clear();
                selectedOrder.Clear();
                unsupported.Clear();
            }
        }

        public bool Clear()
        {
            if (IsLocked)
                return false;
            Reset();
            return true;
        }

        private AlchemySelectionError CheckInventory(
            InventoryOwnershipSnapshot inventory)
        {
            if (inventory == null || inventory.InventoryRevision < 0
                || inventory.Stacks == null)
                return AlchemySelectionError.InventoryUnavailable;
            return inventory.CharacterDbId == characterDbId
                ? AlchemySelectionError.None
                : AlchemySelectionError.WrongCharacter;
        }

        private static InventoryStackState FindStack(
            InventoryOwnershipSnapshot inventory, long stackId)
        {
            foreach (InventoryStackState stack in inventory.Stacks)
                if (stack != null && stack.StackId == stackId)
                    return stack;
            return null;
        }

        private void Reset()
        {
            selected.Clear();
            selectedOrder.Clear();
            unsupported.Clear();
            required.Clear();
            recipeId = null;
            characterDbId = 0;
            configurationRevision = 0;
        }
    }
}
