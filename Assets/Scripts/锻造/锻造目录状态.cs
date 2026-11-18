using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Game.Forging.Protocol
{
    public enum 锻造目录接收结果
    {
        忽略,
        等待,
        完成,
        拒绝,
        无效,
        过期
    }

    public sealed class 锻造目录状态
    {
        private readonly Dictionary<int, 锻造配方[]> batches =
            new Dictionary<int, 锻造配方[]>();
        private readonly Dictionary<int, string> fingerprints =
            new Dictionary<int, string>();
        private long pendingSequence;
        private long minimumRevision;
        private string syncId;
        private long batchRevision;
        private int batchCount;
        private int totalCount;

        public 锻造配方[] Recipes { get; private set; }
        public long ConfigurationRevision { get; private set; }
        public bool IsCurrent { get; private set; }
        public long PendingSequence => pendingSequence;

        public void Reset()
        {
            ClearPending();
            Recipes = null;
            ConfigurationRevision = 0;
            minimumRevision = 0;
            IsCurrent = false;
        }

        public void BeginQuery(long sequence)
        {
            ClearPending();
            pendingSequence = sequence;
        }

        public void Invalidate()
        {
            ClearPending();
            IsCurrent = false;
        }

        public bool NoteRevision(long revision)
        {
            if (revision <= minimumRevision)
                return false;
            minimumRevision = revision;
            if (Recipes != null && ConfigurationRevision < revision)
                IsCurrent = false;
            return Recipes == null || ConfigurationRevision < revision;
        }

        public 锻造目录接收结果 Apply(锻造响应 result)
        {
            if (result == null || pendingSequence == 0
                || result.requestSequence != pendingSequence)
                return 锻造目录接收结果.忽略;
            if (!result.success)
            {
                Invalidate();
                return 锻造目录接收结果.拒绝;
            }
            if (result.recipes == null || result.configurationRevision < 0
                || result.serverTime <= 0)
                return Fail();
            if (result.configurationRevision < minimumRevision
                || (Recipes != null
                    && result.configurationRevision < ConfigurationRevision))
            {
                Invalidate();
                return 锻造目录接收结果.过期;
            }

            bool legacy = string.IsNullOrEmpty(result.catalogSyncId)
                && result.batchIndex == 0 && result.batchCount == 0
                && result.totalCount == 0;
            if (legacy)
            {
                if (syncId != null)
                    return Fail();
                return Commit(result.recipes, result.configurationRevision);
            }
            if (!Guid.TryParseExact(result.catalogSyncId, "D", out _)
                || result.batchCount <= 0 || result.totalCount < 0
                || result.batchIndex < 1
                || result.batchIndex > result.batchCount
                || (result.totalCount == 0 && result.batchCount != 1)
                || (result.totalCount > 0
                    && (result.batchCount > result.totalCount
                        || result.recipes.Length == 0))
                || result.recipes.Length > result.totalCount)
                return Fail();
            foreach (锻造配方 recipe in result.recipes)
                if (!IsValid(recipe))
                    return Fail();

            if (syncId == null)
            {
                syncId = result.catalogSyncId;
                batchRevision = result.configurationRevision;
                batchCount = result.batchCount;
                totalCount = result.totalCount;
            }
            else if (!string.Equals(syncId, result.catalogSyncId,
                    StringComparison.Ordinal)
                || batchRevision != result.configurationRevision
                || batchCount != result.batchCount
                || totalCount != result.totalCount)
                return Fail();

            string fingerprint = JsonConvert.SerializeObject(result.recipes);
            if (fingerprints.TryGetValue(result.batchIndex, out string previous))
                return previous == fingerprint ? 锻造目录接收结果.等待 : Fail();
            batches.Add(result.batchIndex, result.recipes);
            fingerprints.Add(result.batchIndex, fingerprint);
            if (batches.Count < batchCount)
                return 锻造目录接收结果.等待;

            var merged = new List<锻造配方>();
            for (int index = 1; index <= batchCount; index++)
            {
                if (!batches.TryGetValue(index, out 锻造配方[] part))
                    return Fail();
                merged.AddRange(part);
                if (merged.Count > totalCount)
                    return Fail();
            }
            return merged.Count == totalCount
                ? Commit(merged.ToArray(), batchRevision) : Fail();
        }

        private 锻造目录接收结果 Commit(锻造配方[] recipes, long revision)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (锻造配方 recipe in recipes)
                if (!IsValid(recipe) || !ids.Add(recipe.recipeId))
                    return Fail();
            Recipes = recipes;
            ConfigurationRevision = revision;
            IsCurrent = revision >= minimumRevision;
            ClearPending();
            return 锻造目录接收结果.完成;
        }

        private static bool IsValid(锻造配方 recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.recipeId)
                || string.IsNullOrWhiteSpace(recipe.name)
                || recipe.durationSeconds < 1 || recipe.durationSeconds > 31536000
                || recipe.successRateBps < 0 || recipe.successRateBps > 10000
                || recipe.materials == null || recipe.materials.Length < 1
                || recipe.materials.Length > 6)
                return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (锻造配方材料 material in recipe.materials)
                if (material == null || string.IsNullOrWhiteSpace(material.itemId)
                    || material.count <= 0 || !ids.Add(material.itemId))
                    return false;
            return true;
        }

        private 锻造目录接收结果 Fail()
        {
            Invalidate();
            return 锻造目录接收结果.无效;
        }

        private void ClearPending()
        {
            pendingSequence = 0;
            syncId = null;
            batchRevision = 0;
            batchCount = totalCount = 0;
            batches.Clear();
            fingerprints.Clear();
        }
    }
}
