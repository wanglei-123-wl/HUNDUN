using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Game.Cultivation.Protocol
{
    internal enum ItemCatalogBatchResult
    {
        Pending,
        Duplicate,
        Complete,
        Invalid
    }

    internal sealed class CultivationItemCatalogBatches
    {
        private sealed class Batch
        {
            public CultivationItemCatalogResultDto Result;
            public JObject Json;
            public JArray Items;
        }

        private readonly SortedDictionary<int, Batch> batches =
            new SortedDictionary<int, Batch>();
        private CultivationItemCatalogResultDto header;

        public void Clear()
        {
            batches.Clear();
            header = null;
        }

        public ItemCatalogBatchResult Apply(CultivationItemCatalogResultDto result,
            string json, out CultivationItemCatalogResultDto complete)
        {
            complete = null;
            if (result.lifecycleRevision < 0 || result.batchCount < 0
                || result.batchIndex < 0)
                return ItemCatalogBatchResult.Invalid;

            // Older servers omitted batchCount.
            if (result.batchCount == 0)
            {
                if (header != null || result.batchIndex != 0)
                    return ItemCatalogBatchResult.Invalid;
                result.rawJson = json;
                complete = result;
                return ItemCatalogBatchResult.Complete;
            }

            if (result.batchIndex >= result.batchCount)
                return ItemCatalogBatchResult.Invalid;

            if (header != null && (result.catalogVersion != header.catalogVersion
                || result.lifecycleRevision != header.lifecycleRevision
                || result.batchCount != header.batchCount))
                return ItemCatalogBatchResult.Invalid;

            JObject packet;
            try { packet = JObject.Parse(json); }
            catch (Newtonsoft.Json.JsonException)
            {
                return ItemCatalogBatchResult.Invalid;
            }
            if (packet["catalogVersion"]?.Type != JTokenType.Integer
                || packet["lifecycleRevision"]?.Type != JTokenType.Integer
                || packet["batchIndex"]?.Type != JTokenType.Integer
                || packet["batchCount"]?.Type != JTokenType.Integer
                || !(packet["items"] is JArray rawItems)
                || rawItems.Count != result.items.Length)
                return ItemCatalogBatchResult.Invalid;

            if (result.batchCount == 1)
            {
                result.rawJson = json;
                complete = result;
                return ItemCatalogBatchResult.Complete;
            }

            if (batches.TryGetValue(result.batchIndex, out Batch previous))
                return JToken.DeepEquals(previous.Items, rawItems)
                    ? ItemCatalogBatchResult.Duplicate
                    : ItemCatalogBatchResult.Invalid;
            if (header == null)
                header = result;
            batches.Add(result.batchIndex, new Batch
            {
                Result = result,
                Json = packet,
                Items = rawItems
            });
            if (batches.Count < result.batchCount)
                return ItemCatalogBatchResult.Pending;

            var items = new List<CultivationItemDefinitionDto>();
            var combinedItems = new JArray();
            foreach (Batch batch in batches.Values)
            {
                items.AddRange(batch.Result.items);
                foreach (JToken item in batch.Items)
                    combinedItems.Add(item.DeepClone());
            }

            // Use the first packet's envelope and keep fields outside the typed DTO.
            Batch first = batches[0];
            first.Json["items"] = combinedItems;
            first.Json["batchCount"] = 1;
            complete = first.Result;
            complete.items = items.ToArray();
            complete.batchCount = 1;
            complete.rawJson = first.Json.ToString(Newtonsoft.Json.Formatting.None);
            return ItemCatalogBatchResult.Complete;
        }
    }
}
