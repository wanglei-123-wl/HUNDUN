using System;
using System.Collections.Generic;
using Game.Items;

namespace Game.Items.Editor
{
    internal readonly struct ItemCatalogId
    {
        public readonly string Id;
        public readonly string Source;
        public readonly ItemDefinition Definition;
        public readonly bool AllowUnmigratedEquipment;

        public ItemCatalogId(string id, string source)
        {
            Id = id;
            Source = source;
            Definition = null;
            AllowUnmigratedEquipment = false;
        }

        public ItemCatalogId(ItemDefinition definition, string source,
            bool allowUnmigratedEquipment = false)
        {
            Id = definition == null ? null : definition.ItemId;
            Source = source;
            Definition = definition;
            AllowUnmigratedEquipment = allowUnmigratedEquipment;
        }
    }

    internal static class ItemCatalogValidation
    {
        public static bool TryValidate(IReadOnlyList<ItemCatalogId> entries, out string report)
        {
            return TryValidate(entries, null, out report);
        }

        public static bool TryValidate(IReadOnlyList<ItemCatalogId> entries,
            Func<string, bool> isSkillRegistered, out string report)
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            var equipmentSources = new Dictionary<string, string>(StringComparer.Ordinal);
            var errors = new List<string>();
            foreach (ItemCatalogId entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    errors.Add(entry.Source + ": ItemId is empty.");
                    continue;
                }

                if (!string.Equals(entry.Id, entry.Id.Trim(), StringComparison.Ordinal))
                {
                    errors.Add(entry.Source + ": ItemId has leading or trailing spaces.");
                    continue;
                }

                if (sources.TryGetValue(entry.Id, out string firstSource))
                {
                    errors.Add(entry.Source + ": duplicate ItemId '" + entry.Id
                        + "' (first in " + firstSource + ").");
                    continue;
                }

                sources.Add(entry.Id, entry.Source);
                ItemDefinition definition = entry.Definition;
                if (definition == null) continue;

                if (!string.IsNullOrEmpty(definition.RemoteIconAddress))
                {
                    if (definition.Icon != null)
                        errors.Add(entry.Source + ": remote icon must not also reference a local Sprite.");
                    if (!string.Equals(definition.RemoteIconAddress,
                        RemoteItemIconCache.AddressFor(definition.ItemId), StringComparison.Ordinal))
                        errors.Add(entry.Source + ": remote icon address does not match ItemId.");
                }

                if (!Enum.IsDefined(typeof(ItemDetailCategory),
                    definition.DisplayCategory))
                {
                    errors.Add(entry.Source
                        + ": detail display category is invalid.");
                }

                ValidateDetailEntries(entry.Source, definition, errors);

                if (definition.Category != ItemCategory.Equipment)
                {
                    if (!string.IsNullOrWhiteSpace(definition.EquipmentId)
                        || definition.EquipmentCategory != EquipmentCategoryCode.None)
                        errors.Add(entry.Source + ": only equipment may set equipment fields.");

                    if (definition.Category == ItemCategory.SkillBook)
                    {
                        string linkedSkillId = definition.LinkedSkillId;
                        if (string.IsNullOrWhiteSpace(linkedSkillId))
                            errors.Add(entry.Source + ": LinkedSkillId is empty.");
                        else if (!string.Equals(linkedSkillId, linkedSkillId.Trim(),
                            StringComparison.Ordinal))
                            errors.Add(entry.Source
                                + ": LinkedSkillId has leading or trailing spaces.");
                        else if (isSkillRegistered != null
                            && !isSkillRegistered(linkedSkillId))
                            errors.Add(entry.Source
                                + ": LinkedSkillId is not registered or invalid.");
                    }
                    else if (!string.IsNullOrWhiteSpace(definition.LinkedSkillId))
                    {
                        errors.Add(entry.Source
                            + ": only skill books may set LinkedSkillId.");
                    }

                    continue;
                }

                if (!string.IsNullOrWhiteSpace(definition.LinkedSkillId))
                    errors.Add(entry.Source + ": only skill books may set LinkedSkillId.");

                string equipmentId = definition.EquipmentId;
                if (entry.AllowUnmigratedEquipment
                    && string.IsNullOrWhiteSpace(equipmentId))
                    continue;
                if (string.IsNullOrWhiteSpace(equipmentId))
                    errors.Add(entry.Source + ": EquipmentId is empty.");
                else if (!string.Equals(equipmentId, equipmentId.Trim(),
                    StringComparison.Ordinal))
                    errors.Add(entry.Source + ": EquipmentId has leading or trailing spaces.");
                else if (equipmentSources.TryGetValue(equipmentId, out string firstEquipment))
                    errors.Add(entry.Source + ": duplicate EquipmentId '" + equipmentId
                        + "' (first in " + firstEquipment + ").");
                else
                    equipmentSources.Add(equipmentId, entry.Source);

                if (definition.EquipmentCategory < EquipmentCategoryCode.Weapon
                    || definition.EquipmentCategory > EquipmentCategoryCode.Formation)
                    errors.Add(entry.Source + ": equipment category is missing or invalid.");
                if (definition.Icon == null
                    && string.IsNullOrEmpty(definition.RemoteIconAddress))
                    errors.Add(entry.Source + ": equipment icon is missing.");
            }

            report = string.Join("\n", errors);
            return errors.Count == 0;
        }

        private static void ValidateDetailEntries(
            string source,
            ItemDefinition definition,
            List<string> errors)
        {
            if (definition.DetailEntries == null)
                return;

            var labels = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < definition.DetailEntries.Count; index++)
            {
                ItemDetailEntry entry = definition.DetailEntries[index];
                if (entry == null)
                {
                    errors.Add(source + ": detail entry[" + index
                        + "] is null.");
                    continue;
                }

                if (!entry.IsValid)
                {
                    errors.Add(source + ": detail entry[" + index
                        + "] is empty.");
                    continue;
                }

                string label = entry.Label == null
                    ? string.Empty : entry.Label.Trim();
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                if (!labels.Add(label))
                {
                    errors.Add(source + ": duplicate detail entry label '"
                        + label + "'.");
                }
            }
        }
    }
}
