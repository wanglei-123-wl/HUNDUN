using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Inventory.Model.V2;
using UnityEngine;

namespace Game.Forging
{
    [Serializable]
    public sealed class 锻造材料需求
    {
        public string itemId;
        public int count;
        public bool isCurrency;
    }

    [Serializable]
    public sealed class 锻造图谱
    {
        public string recipeId;
        public string name;
        public Sprite icon;
        public 锻造材料需求[] materials;
        public bool canForge = true;
        public bool hasBlueprintAssociation;
    }

    public sealed class 锻造已选材料
    {
        public long StackId { get; }
        public string ItemId { get; }
        public int Count { get; }
        public string Identity => "s:"
            + StackId.ToString(CultureInfo.InvariantCulture);

        public 锻造已选材料(long stackId, string itemId, int count)
        {
            StackId = stackId;
            ItemId = itemId;
            Count = count;
        }
    }

    public sealed class 锻造开始意向
    {
        public long CharacterDbId { get; }
        public long InventoryRevision { get; }
        public string RecipeId { get; }
        public IReadOnlyList<锻造已选材料> Materials { get; }

        public 锻造开始意向(long characterDbId, long inventoryRevision,
            string recipeId, IReadOnlyList<锻造已选材料> materials)
        {
            CharacterDbId = characterDbId;
            InventoryRevision = inventoryRevision;
            RecipeId = recipeId;
            Materials = materials;
        }
    }

    [Serializable]
    public sealed class 锻造任务展示
    {
        public string taskId;
        public string status;
        public string resultName;
        public Sprite resultIcon;
        public float progress;
        public bool ready;
        public bool succeeded;
        public int outputCount;
    }

    public sealed class 锻造本地选择
    {
        private readonly Dictionary<long, 锻造已选材料> entries =
            new Dictionary<long, 锻造已选材料>();
        private readonly List<long> order = new List<long>();

        public IReadOnlyList<锻造已选材料> Materials
        {
            get
            {
                var result = new List<锻造已选材料>(order.Count);
                foreach (long id in order)
                    result.Add(entries[id]);
                return result;
            }
        }

        public void Clear()
        {
            entries.Clear();
            order.Clear();
        }

        public bool TrySet(InventoryOwnershipSnapshot snapshot, long characterDbId,
            string identity, string itemId, int count)
        {
            if (snapshot == null || snapshot.CharacterDbId != characterDbId
                || snapshot.InventoryRevision < 0 || count <= 0
                || string.IsNullOrEmpty(identity)
                || string.IsNullOrEmpty(itemId))
                return false;
            foreach (InventoryStackState stack in snapshot.Stacks)
            {
                if (stack == null || stack.Identity != identity
                    || stack.ItemId != itemId || stack.Count < count)
                    continue;
                if (!entries.ContainsKey(stack.StackId))
                    order.Add(stack.StackId);
                entries[stack.StackId] =
                    new 锻造已选材料(stack.StackId, itemId, count);
                return true;
            }
            return false;
        }

        public bool Remove(string identity)
        {
            for (int index = 0; index < order.Count; index++)
            {
                long id = order[index];
                if (entries[id].Identity != identity)
                    continue;
                entries.Remove(id);
                order.RemoveAt(index);
                return true;
            }
            return false;
        }

        public bool Sync(InventoryOwnershipSnapshot snapshot, long characterDbId)
        {
            if (snapshot == null || snapshot.CharacterDbId != characterDbId)
            {
                bool hadEntries = entries.Count != 0;
                Clear();
                return hadEntries;
            }
            bool changed = false;
            for (int index = order.Count - 1; index >= 0; index--)
            {
                long id = order[index];
                锻造已选材料 selected = entries[id];
                bool valid = false;
                foreach (InventoryStackState stack in snapshot.Stacks)
                    if (stack != null && stack.StackId == id
                        && stack.ItemId == selected.ItemId
                        && stack.Count >= selected.Count)
                    {
                        valid = true;
                        break;
                    }
                if (valid)
                    continue;
                order.RemoveAt(index);
                entries.Remove(id);
                changed = true;
            }
            return changed;
        }

        public bool IsCurrent(InventoryOwnershipSnapshot snapshot,
            long characterDbId)
        {
            if (snapshot == null || snapshot.CharacterDbId != characterDbId
                || snapshot.InventoryRevision < 0)
                return false;
            foreach (锻造已选材料 selected in entries.Values)
            {
                bool found = false;
                foreach (InventoryStackState stack in snapshot.Stacks)
                    if (stack != null && stack.StackId == selected.StackId
                        && stack.ItemId == selected.ItemId
                        && stack.Count >= selected.Count)
                    {
                        found = true;
                        break;
                    }
                if (!found)
                    return false;
            }
            return true;
        }

        public bool Matches(锻造图谱 recipe,
            InventoryOwnershipSnapshot snapshot = null)
        {
            if (recipe?.materials == null || recipe.materials.Length == 0
                || recipe.materials.Length > 6)
                return false;
            var requirements = new Dictionary<string, long>(StringComparer.Ordinal);
            var materialIds = new HashSet<string>(StringComparer.Ordinal);
            bool needsStacks = false;
            foreach (锻造材料需求 material in recipe.materials)
            {
                if (material == null || string.IsNullOrWhiteSpace(material.itemId)
                    || material.count <= 0
                    || !materialIds.Add(material.itemId))
                    return false;
                if (material.isCurrency)
                {
                    if (snapshot == null || snapshot.Wallets == null
                        || !snapshot.Wallets.TryGetValue(material.itemId,
                            out long balance)
                        || balance < material.count)
                        return false;
                }
                else
                {
                    needsStacks = true;
                    requirements.Add(material.itemId, material.count);
                }
            }
            if (needsStacks && entries.Count == 0)
                return false;
            foreach (锻造已选材料 selected in entries.Values)
            {
                if (!requirements.TryGetValue(selected.ItemId, out long remaining))
                    return false;
                requirements[selected.ItemId] = remaining - selected.Count;
            }
            foreach (long remaining in requirements.Values)
                if (remaining != 0)
                    return false;
            return true;
        }
    }
}
