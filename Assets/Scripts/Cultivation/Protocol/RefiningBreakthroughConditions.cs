using System;
using Game.Inventory.Model.V2;

namespace Game.Cultivation.Protocol
{
    public static class RefiningBreakthroughConditions
    {
        public static bool MatchesNextRealm(RefiningBreakthroughDto path,
            ProgressionSnapshotResult progression)
        {
            return path != null && progression != null
                && progression.hasNextJingjie
                && string.Equals(path.toJingjieId,
                    progression.nextJingjieId, StringComparison.Ordinal)
                && path.toJingjieStage == progression.nextJingjieStage;
        }

        public static bool TryFindPath(RefiningResultDto catalog,
            ProgressionSnapshotResult progression, long characterDbId,
            out RefiningBreakthroughDto path)
        {
            path = null;
            if (catalog == null || !catalog.success
                || catalog.breakthroughs == null
                || progression == null || !progression.success
                || progression.characterDbId != characterDbId
                || string.IsNullOrWhiteSpace(progression.jingjieId)
                || progression.jingjieStage < 0 || progression.jingjieExp < 0)
                return false;
            foreach (RefiningBreakthroughDto entry in catalog.breakthroughs)
            {
                if (entry != null
                    && entry.fromJingjieStage == progression.jingjieStage
                    && string.Equals(entry.fromJingjieId,
                        progression.jingjieId, StringComparison.Ordinal)
                    && entry.requiredCultivation >= 0
                    && entry.materials != null)
                {
                    path = entry;
                    return true;
                }
            }
            return false;
        }

        public static bool CanAttempt(RefiningBreakthroughDto path,
            long availableCultivation, InventoryOwnershipSnapshot inventory,
            long characterDbId)
        {
            if (path == null || path.materials == null
                || availableCultivation < path.requiredCultivation)
                return false;
            if (path.materials.Length == 0)
                return true;
            if (inventory == null || inventory.CharacterDbId != characterDbId)
                return false;
            foreach (RefiningMaterialDto material in path.materials)
            {
                if (material == null || string.IsNullOrWhiteSpace(material.itemId)
                    || material.count <= 0
                    || !inventory.TryGetItemCount(material.itemId,
                        out long available) || available < material.count)
                    return false;
            }
            return true;
        }
    }
}
