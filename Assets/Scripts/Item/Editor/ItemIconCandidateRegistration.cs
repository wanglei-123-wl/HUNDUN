using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Game.Items.Editor
{
    internal static class ItemIconCandidateRegistration
    {
        private const string TablePath = "Assets/Data/Items/ItemDefinitionTable.asset";

        [MenuItem("Tools/Items/Register Remote Item Icons")]
        public static void Register()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings == null ? null : settings.FindGroup("RemoteTrialOnly");
            var table = AssetDatabase.LoadAssetAtPath<ItemDefinitionTable>(TablePath);
            if (group == null || table == null)
                throw new InvalidOperationException("Item table or remote trial group is missing.");
            if (table.LegacyDefinitions.Count != 0)
                throw new InvalidOperationException("Legacy inline item definitions require a separate audit.");

            var assets = new SerializedObject(table).FindProperty("itemAssets");
            if (assets == null)
                throw new InvalidOperationException("Item table references are missing.");

            var addresses = new Dictionary<string, string>(StringComparer.Ordinal);
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < assets.arraySize; index++)
            {
                var asset = assets.GetArrayElementAtIndex(index).objectReferenceValue
                    as ItemDefinitionAsset;
                ItemDefinition definition = asset == null ? null : asset.Definition;
                if (definition == null || !definition.IsValid
                    || !itemIds.Add(definition.ItemId))
                    throw new InvalidOperationException("Invalid or duplicate item icon at index " + index);

                string address = RemoteItemIconCache.AddressFor(definition.ItemId);
                if (!string.IsNullOrEmpty(definition.RemoteIconAddress))
                {
                    if (definition.Icon != null || definition.RemoteIconAddress != address)
                        throw new InvalidOperationException("Remote-only icon configuration is invalid: "
                            + definition.ItemId);
                    bool found = false;
                    foreach (AddressableAssetEntry entry in group.entries)
                    {
                        if (entry.address != address)
                            continue;
                        if (found || AssetDatabase.LoadAssetAtPath<Sprite>(
                            AssetDatabase.GUIDToAssetPath(entry.guid)) == null)
                            throw new InvalidOperationException("Remote icon address is ambiguous or invalid: "
                                + definition.ItemId);
                        found = true;
                    }
                    if (!found)
                        throw new InvalidOperationException("Remote icon address is missing: "
                            + definition.ItemId);
                    continue;
                }
                if (definition.Icon == null)
                    throw new InvalidOperationException("Item icon is missing: " + definition.ItemId);
                string path = AssetDatabase.GetAssetPath(definition.Icon);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)
                    || AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
                    throw new InvalidOperationException("Icon is not an asset Sprite: " + definition.ItemId);

                if (addresses.TryGetValue(guid, out string previous))
                {
                    if (previous != address)
                        throw new InvalidOperationException("Shared icon has no alias mapping: "
                            + definition.ItemId);
                    continue;
                }

                var existing = settings.FindAssetEntry(guid);
                if (existing != null
                    && (existing.parentGroup != group || existing.address != address))
                    throw new InvalidOperationException("Existing address conflicts with item: "
                        + definition.ItemId);
                addresses.Add(guid, address);
            }

            int added = 0;
            foreach (var candidate in addresses)
            {
                if (settings.FindAssetEntry(candidate.Key) != null)
                    continue;
                settings.CreateOrMoveEntry(candidate.Key, group).address = candidate.Value;
                added++;
            }
            if (added > 0)
            {
                EditorUtility.SetDirty(group);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("[ItemIcons] Registered " + added
                + " new Sprite addresses for " + itemIds.Count + " items.");
        }
    }
}
