using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Game.Cultivation.Protocol
{
    internal enum CultivationCatalogBatchResult
    {
        Pending,
        Duplicate,
        Complete,
        Invalid
    }

    internal sealed class CultivationCatalogBatches
    {
        private readonly SortedDictionary<int, CultivationCatalogResultDto> batches =
            new SortedDictionary<int, CultivationCatalogResultDto>();
        private readonly Dictionary<int, JObject> packets = new Dictionary<int, JObject>();
        private CultivationCatalogResultDto header;

        public void Clear()
        {
            batches.Clear();
            packets.Clear();
            header = null;
        }

        public CultivationCatalogBatchResult Apply(CultivationCatalogResultDto result,
            string json)
        {
            JObject packet;
            try { packet = JObject.Parse(json); }
            catch (Newtonsoft.Json.JsonException)
            {
                return CultivationCatalogBatchResult.Invalid;
            }

            if (packet.Property("characterTechniqueIds") == null)
                result.characterTechniqueIds = null;
            else if (packet["characterTechniqueIds"]?.Type != JTokenType.Array
                || result.characterTechniqueIds == null
                || packet["inventoryRevision"]?.Type != JTokenType.Integer
                || packet["cultivationRevision"]?.Type != JTokenType.Integer
                || result.inventoryRevision < 0 || result.cultivationRevision < 0)
                return CultivationCatalogBatchResult.Invalid;

            bool scoped = result.characterTechniqueIds != null;
            if (packet.Property("catalogSyncId") == null
                && packet.Property("batchIndex") == null && packet.Property("batchCount") == null
                && packet.Property("totalCount") == null && packet.Property("totalGradeCount") == null
                && packet.Property("totalCharacterTechniqueCount") == null)
                return header == null ? CultivationCatalogBatchResult.Complete
                    : CultivationCatalogBatchResult.Invalid;

            long totalEntries = (long)result.totalCount + result.totalGradeCount
                + result.totalCharacterTechniqueCount;
            int scopeCount = result.characterTechniqueIds?.Length ?? 0;
            if (packet["catalogSyncId"]?.Type != JTokenType.String
                || !Guid.TryParseExact(result.catalogSyncId, "D", out _)
                || packet["batchIndex"]?.Type != JTokenType.Integer
                || packet["batchCount"]?.Type != JTokenType.Integer
                || packet["totalCount"]?.Type != JTokenType.Integer
                || packet["totalGradeCount"]?.Type != JTokenType.Integer
                || (scoped && packet["totalCharacterTechniqueCount"]?.Type != JTokenType.Integer)
                || (!scoped && packet.Property("totalCharacterTechniqueCount") != null)
                || result.batchCount < 1 || result.batchCount > Math.Max(1, totalEntries)
                || result.batchIndex < 1 || result.batchIndex > result.batchCount
                || result.techniques.Length > result.totalCount
                || result.grades.Length > result.totalGradeCount
                || scopeCount > result.totalCharacterTechniqueCount
                || (result.batchCount > 1 && result.techniques.Length == 0
                    && result.grades.Length == 0 && scopeCount == 0))
                return CultivationCatalogBatchResult.Invalid;

            if (header == null)
                header = result;
            else if (result.catalogSyncId != header.catalogSyncId
                || result.requestSequence != header.requestSequence
                || result.configurationRevision != header.configurationRevision
                || result.inventoryRevision != header.inventoryRevision
                || result.cultivationRevision != header.cultivationRevision
                || result.serverTime != header.serverTime
                || scoped != (header.characterTechniqueIds != null)
                || result.batchCount != header.batchCount
                || result.totalCount != header.totalCount
                || result.totalGradeCount != header.totalGradeCount
                || result.totalCharacterTechniqueCount != header.totalCharacterTechniqueCount)
                return CultivationCatalogBatchResult.Invalid;

            if (packets.TryGetValue(result.batchIndex, out JObject previous))
                return JToken.DeepEquals(previous, packet) ? CultivationCatalogBatchResult.Duplicate
                    : CultivationCatalogBatchResult.Invalid;
            packets.Add(result.batchIndex, packet);
            batches.Add(result.batchIndex, result);
            if (batches.Count != result.batchCount)
                return CultivationCatalogBatchResult.Pending;

            var grades = new List<CultivationGradeDto>();
            var techniques = new List<CultivationTechniqueDto>();
            var ids = new List<string>();
            foreach (CultivationCatalogResultDto batch in batches.Values)
            {
                grades.AddRange(batch.grades);
                techniques.AddRange(batch.techniques);
                if (scoped)
                    ids.AddRange(batch.characterTechniqueIds);
            }
            if (grades.Count != result.totalGradeCount || techniques.Count != result.totalCount
                || ids.Count != result.totalCharacterTechniqueCount)
                return CultivationCatalogBatchResult.Invalid;
            result.grades = grades.ToArray();
            result.techniques = techniques.ToArray();
            result.characterTechniqueIds = scoped ? ids.ToArray() : null;
            return CultivationCatalogBatchResult.Complete;
        }

        public static bool TryResolveScope(CultivationCatalogResultDto result,
            CultivationCatalogResultDto cached)
        {
            if (result.characterTechniqueIds == null)
                return true;
            var grades = new Dictionary<string, CultivationGradeDto>(StringComparer.Ordinal);
            var techniques = new Dictionary<string, CultivationTechniqueDto>(StringComparer.Ordinal);
            foreach (CultivationGradeDto grade in result.grades)
                if (!grades.TryAdd(grade.gradeId, grade))
                    return false;
            foreach (CultivationTechniqueDto technique in result.techniques)
                if (!techniques.TryAdd(technique.techniqueId, technique))
                    return false;
            if (cached?.characterTechniqueIds != null
                && cached.configurationRevision == result.configurationRevision)
            {
                foreach (CultivationGradeDto grade in cached.grades)
                    grades.TryAdd(grade.gradeId, grade);
                foreach (CultivationTechniqueDto technique in cached.techniques)
                    techniques.TryAdd(technique.techniqueId, technique);
            }

            var visible = new List<CultivationTechniqueDto>();
            var visibleGrades = new List<CultivationGradeDto>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenGrades = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in result.characterTechniqueIds)
            {
                if (string.IsNullOrWhiteSpace(id) || !seenIds.Add(id)
                    || !techniques.TryGetValue(id, out CultivationTechniqueDto technique)
                    || !grades.TryGetValue(technique.gradeId, out CultivationGradeDto grade))
                    return false;
                visible.Add(technique);
                if (seenGrades.Add(grade.gradeId))
                    visibleGrades.Add(grade);
            }
            result.techniques = visible.ToArray();
            result.grades = visibleGrades.ToArray();
            return true;
        }
    }
}
