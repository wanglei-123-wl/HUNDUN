using System;
using System.Collections.Generic;
using Game.Cultivation.Protocol;
using Game.Forging.Protocol;
using Game.Items;

namespace Game.Forging
{
    public static class 锻造图谱列表
    {
        public static IReadOnlyList<锻造图谱> Build(锻造配方[] catalog,
            IReadOnlyList<string> learnedRecipeIds, bool usesBlueprintLearning,
            CultivationItemDefinitionDto[] items, ItemDefinitionTable definitions)
        {
            var rows = new List<锻造图谱>();
            if (catalog == null || (usesBlueprintLearning && learnedRecipeIds == null))
                return rows;

            var learned = usesBlueprintLearning
                ? new HashSet<string>(learnedRecipeIds, StringComparer.Ordinal) : null;
            var blueprintIds = new Dictionary<string, string>(StringComparer.Ordinal);
            if (usesBlueprintLearning && items != null)
                foreach (CultivationItemDefinitionDto item in items)
                    if (item?.kind == "forgingblueprint"
                        && !string.IsNullOrWhiteSpace(item.itemId)
                        && !string.IsNullOrWhiteSpace(item.learnForgingRecipeId)
                        && learned.Contains(item.learnForgingRecipeId)
                        && (!blueprintIds.TryGetValue(item.learnForgingRecipeId, out string existing)
                            || string.CompareOrdinal(item.itemId, existing) < 0))
                        blueprintIds[item.learnForgingRecipeId] = item.itemId;

            foreach (锻造配方 recipe in catalog)
                if (learned == null || learned.Remove(recipe.recipeId))
                    rows.Add(BuildRow(recipe.recipeId, recipe, blueprintIds,
                        usesBlueprintLearning, definitions));

            if (learned != null)
                foreach (string recipeId in learnedRecipeIds)
                    if (learned.Remove(recipeId))
                        rows.Add(BuildRow(recipeId, null, blueprintIds, true, definitions));
            return rows;
        }

        private static 锻造图谱 BuildRow(string recipeId, 锻造配方 recipe,
            Dictionary<string, string> blueprintIds, bool usesBlueprintLearning,
            ItemDefinitionTable definitions)
        {
            blueprintIds.TryGetValue(recipeId, out string blueprintId);
            string displayId = usesBlueprintLearning ? blueprintId : recipeId;
            ItemDefinition resource = null;
            if (displayId != null && definitions != null
                && definitions.TryGet(displayId, out ItemDefinition resolved))
                resource = resolved;

            string name = recipe?.name ?? blueprintId ?? recipeId;
            if (usesBlueprintLearning)
            {
                if (!string.IsNullOrWhiteSpace(resource?.DisplayName))
                    name = resource.DisplayName;
                else if (!name.EndsWith("图谱", StringComparison.Ordinal))
                    name += "图谱";
            }

            var materials = new 锻造材料需求[recipe?.materials.Length ?? 0];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = new 锻造材料需求
                {
                    itemId = recipe.materials[i].itemId,
                    count = recipe.materials[i].count,
                    isCurrency = recipe.materials[i].isCurrency
                };
            return new 锻造图谱
            {
                recipeId = recipeId,
                name = recipe == null ? name + "（不可锻造）" : name,
                icon = resource?.Icon,
                materials = materials,
                canForge = recipe != null,
                hasBlueprintAssociation = blueprintId != null
            };
        }
    }
}
