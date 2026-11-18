using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Alchemy.Protocol
{
    public enum AlchemyCatalogApplyResult
    {
        Ignored,
        Pending,
        Applied,
        Rejected,
        Invalid,
        Stale
    }

    public sealed class AlchemyCatalogState
    {
        private readonly Dictionary<int, AlchemyRecipeDto[]> batches =
            new Dictionary<int, AlchemyRecipeDto[]>();
        private readonly Dictionary<int, string> batchContents =
            new Dictionary<int, string>();
        private long pendingSequence;
        private string syncId;
        private int batchCount;
        private int totalCount;
        private long batchRevision;
        private long batchServerTime;
        private long minimumRevision;

        public AlchemyRecipeDto[] Recipes { get; private set; }
        public long ConfigurationRevision { get; private set; }
        public long ServerTime { get; private set; }
        public bool IsCurrent { get; private set; }
        public long PendingSequence => pendingSequence;

        public void Reset()
        {
            ClearPending();
            Recipes = null;
            ConfigurationRevision = 0;
            ServerTime = 0;
            minimumRevision = 0;
            IsCurrent = false;
        }

        public void BeginQuery(long sequence)
        {
            ClearPending();
            IsCurrent = false;
            pendingSequence = sequence;
        }

        public void Invalidate()
        {
            ClearPending();
            IsCurrent = false;
        }

        public bool NoteConfigurationRevision(long revision)
        {
            if (revision <= minimumRevision)
                return false;
            minimumRevision = revision;
            if (Recipes != null && ConfigurationRevision < minimumRevision)
                IsCurrent = false;
            return Recipes == null || ConfigurationRevision < minimumRevision;
        }

        public AlchemyCatalogApplyResult Apply(AlchemyResponseDto result)
        {
            if (result == null || pendingSequence <= 0
                || result.requestSequence != pendingSequence)
                return AlchemyCatalogApplyResult.Ignored;
            if (!result.success)
            {
                Invalidate();
                return AlchemyCatalogApplyResult.Rejected;
            }
            if (result.recipes == null || result.configurationRevision < 0
                || result.serverTime <= 0)
                return Fail();
            if (result.configurationRevision < minimumRevision
                || (Recipes != null
                    && result.configurationRevision < ConfigurationRevision))
            {
                Invalidate();
                return AlchemyCatalogApplyResult.Stale;
            }

            bool legacy = string.IsNullOrEmpty(result.catalogSyncId)
                && result.batchIndex == 0 && result.batchCount == 0
                && result.totalCount == 0;
            if (legacy)
            {
                if (syncId != null)
                    return Fail();
                return Commit(result.recipes, result.configurationRevision,
                    result.serverTime);
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
            foreach (AlchemyRecipeDto recipe in result.recipes)
                if (!IsValidRecipe(recipe))
                    return Fail();

            if (syncId == null)
            {
                syncId = result.catalogSyncId;
                batchCount = result.batchCount;
                totalCount = result.totalCount;
                batchRevision = result.configurationRevision;
                batchServerTime = result.serverTime;
            }
            else if (!string.Equals(syncId, result.catalogSyncId,
                    StringComparison.Ordinal)
                || batchCount != result.batchCount
                || totalCount != result.totalCount
                || batchRevision != result.configurationRevision
                || batchServerTime != result.serverTime)
                return Fail();

            string content = Fingerprint(result.recipes);
            if (batchContents.TryGetValue(result.batchIndex,
                out string previous))
                return string.Equals(previous, content, StringComparison.Ordinal)
                    ? AlchemyCatalogApplyResult.Pending : Fail();

            batches.Add(result.batchIndex, result.recipes);
            batchContents.Add(result.batchIndex, content);
            if (batches.Count != batchCount)
                return AlchemyCatalogApplyResult.Pending;

            var merged = new List<AlchemyRecipeDto>();
            for (int index = 1; index <= batchCount; index++)
            {
                if (!batches.TryGetValue(index, out AlchemyRecipeDto[] part))
                    return Fail();
                merged.AddRange(part);
                if (merged.Count > totalCount)
                    return Fail();
            }
            if (merged.Count != totalCount)
                return Fail();
            return Commit(merged.ToArray(), batchRevision, batchServerTime);
        }

        private AlchemyCatalogApplyResult Commit(AlchemyRecipeDto[] recipes,
            long revision, long serverTime)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (AlchemyRecipeDto recipe in recipes)
            {
                if (!IsValidRecipe(recipe) || !ids.Add(recipe.recipeId))
                    return Fail();
            }
            Recipes = recipes;
            ConfigurationRevision = revision;
            ServerTime = serverTime;
            ClearPending();
            IsCurrent = true;
            return AlchemyCatalogApplyResult.Applied;
        }

        private static bool IsValidRecipe(AlchemyRecipeDto recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.recipeId)
                || string.IsNullOrWhiteSpace(recipe.name)
                || recipe.durationSeconds <= 0 || recipe.successRateBps < 0
                || recipe.successRateBps > 10000
                || recipe.materials == null || recipe.outputs == null)
                return false;
            var materialIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AlchemyRecipeMaterialDto material in recipe.materials)
                if (material == null || string.IsNullOrWhiteSpace(material.itemId)
                    || material.count <= 0 || !materialIds.Add(material.itemId))
                    return false;
            var qualities = new HashSet<int>();
            foreach (AlchemyRecipeOutputDto output in recipe.outputs)
                if (output == null || output.quality < 1 || output.quality > 4
                    || !qualities.Add(output.quality)
                    || string.IsNullOrWhiteSpace(output.itemId)
                    || output.count <= 0 || output.weight < 0
                    || output.weight > 10000)
                    return false;
            return true;
        }

        private static string Fingerprint(AlchemyRecipeDto[] recipes)
        {
            var parts = new string[recipes.Length];
            for (int i = 0; i < recipes.Length; i++)
                parts[i] = JsonUtility.ToJson(recipes[i]);
            return string.Join("\n", parts);
        }

        private AlchemyCatalogApplyResult Fail()
        {
            Invalidate();
            return AlchemyCatalogApplyResult.Invalid;
        }

        private void ClearPending()
        {
            pendingSequence = 0;
            syncId = null;
            batchCount = totalCount = 0;
            batchRevision = batchServerTime = 0;
            batches.Clear();
            batchContents.Clear();
        }
    }
}
