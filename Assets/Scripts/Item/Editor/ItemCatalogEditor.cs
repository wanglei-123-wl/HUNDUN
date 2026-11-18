using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.Items.Editor
{
    internal static class ItemCatalogEditor
    {
        private const string TablePath = "Assets/Data/Items/ItemDefinitionTable.asset";

        [MenuItem("Tools/Items/Validate Item Catalog")]
        private static void Validate()
        {
            Process(refresh: false);
        }

        [MenuItem("Tools/Items/Refresh Item Catalog")]
        private static void Refresh()
        {
            Process(refresh: true);
        }

        internal static bool TryRegister(ItemDefinitionAsset asset, out string message)
        {
            string path = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)
                || !path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                message = "请先把物品配置保存为项目 Assets 下的资源。";
                return false;
            }

            ItemDefinitionTable table =
                AssetDatabase.LoadAssetAtPath<ItemDefinitionTable>(TablePath);
            if (table == null)
            {
                message = "找不到物品总表：" + TablePath;
                return false;
            }

            var serialized = new SerializedObject(table);
            serialized.Update();
            SerializedProperty legacy = serialized.FindProperty("items");
            SerializedProperty references = serialized.FindProperty("itemAssets");
            if (legacy == null || references == null)
            {
                message = "物品总表缺少物品列表字段。";
                return false;
            }

            bool alreadyRegistered = false;
            for (int index = 0; index < references.arraySize; index++)
            {
                if (references.GetArrayElementAtIndex(index).objectReferenceValue == asset)
                {
                    alreadyRegistered = true;
                    break;
                }
            }

            var entries = new List<ItemCatalogId>();
            for (int index = 0; index < legacy.arraySize; index++)
            {
                ItemDefinition definition = table.LegacyDefinitions[index];
                entries.Add(new ItemCatalogId(definition, "原有物品[" + index + "]",
                    allowUnmigratedEquipment: true));
            }
            for (int index = 0; index < references.arraySize; index++)
            {
                var existing = references.GetArrayElementAtIndex(index)
                    .objectReferenceValue as ItemDefinitionAsset;
                if (existing != null)
                    entries.Add(new ItemCatalogId(existing.Definition,
                        AssetDatabase.GetAssetPath(existing),
                        allowUnmigratedEquipment: existing != asset));
            }
            if (!alreadyRegistered)
                entries.Add(new ItemCatalogId(asset.Definition, path));
            if (!ItemCatalogValidation.TryValidate(
                entries, ResolveSkillValidator(), out string report))
            {
                message = "物品 ID 校验失败：\n" + report;
                return false;
            }

            if (!alreadyRegistered)
            {
                Undo.RecordObject(table, "发布物品到总表");
                references.arraySize++;
                references.GetArrayElementAtIndex(references.arraySize - 1)
                    .objectReferenceValue = asset;
                serialized.ApplyModifiedProperties();
            }
            AssetDatabase.SaveAssetIfDirty(asset);
            if (!alreadyRegistered)
                AssetDatabase.SaveAssetIfDirty(table);
            table.RefreshLookup();
            message = alreadyRegistered
                ? "已更新总表中的物品资料：" + asset.Definition.ItemId
                : "已发布到物品总表：" + asset.Definition.ItemId;
            return true;
        }

        private static void Process(bool refresh)
        {
            ItemDefinitionTable table =
                AssetDatabase.LoadAssetAtPath<ItemDefinitionTable>(TablePath);
            if (table == null)
            {
                Debug.LogError("Item catalog table not found: " + TablePath);
                return;
            }

            string[] guids = AssetDatabase.FindAssets(
                "t:ItemDefinitionAsset", new[] { "Assets" });
            var paths = new List<string>(guids.Length);
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && seenPaths.Add(path))
                    paths.Add(path);
            }
            paths.Sort(StringComparer.Ordinal);

            var assets = new List<ItemDefinitionAsset>(paths.Count);
            var entries = new List<ItemCatalogId>();
            var serialized = new SerializedObject(table);
            serialized.Update();
            SerializedProperty legacy = serialized.FindProperty("items");
            SerializedProperty references = serialized.FindProperty("itemAssets");
            if (legacy == null || references == null)
            {
                Debug.LogError("Item catalog fields are missing.", table);
                return;
            }

            for (int index = 0; index < legacy.arraySize; index++)
            {
                entries.Add(new ItemCatalogId(
                    table.LegacyDefinitions[index], "Legacy Items[" + index + "]",
                    allowUnmigratedEquipment: true));
            }

            var referencedAssets = new HashSet<ItemDefinitionAsset>();
            for (int index = 0; index < references.arraySize; index++)
            {
                var existing = references.GetArrayElementAtIndex(index)
                    .objectReferenceValue as ItemDefinitionAsset;
                if (existing != null) referencedAssets.Add(existing);
            }

            foreach (string path in paths)
            {
                ItemDefinitionAsset asset =
                    AssetDatabase.LoadAssetAtPath<ItemDefinitionAsset>(path);
                if (asset == null)
                {
                    Debug.LogError("Item asset could not be loaded: " + path, table);
                    return;
                }

                assets.Add(asset);
                entries.Add(new ItemCatalogId(asset.Definition, path,
                    allowUnmigratedEquipment: referencedAssets.Contains(asset)));
            }

            if (!ItemCatalogValidation.TryValidate(
                entries, ResolveSkillValidator(), out string report))
            {
                Debug.LogError("Item catalog validation failed:\n" + report, table);
                EditorUtility.DisplayDialog(
                    "Item Catalog", "Validation failed. See Console for details.", "OK");
                return;
            }

            if (!refresh)
            {
                EditorUtility.DisplayDialog(
                    "Item Catalog", "Item IDs are valid.", "OK");
                return;
            }

            var scanned = new HashSet<ItemDefinitionAsset>(assets);
            for (int index = 0; index < references.arraySize; index++)
            {
                var existing = references.GetArrayElementAtIndex(index)
                    .objectReferenceValue as ItemDefinitionAsset;
                if (existing != null && !scanned.Contains(existing))
                {
                    Debug.LogError(
                        "Item catalog has a referenced asset outside Assets: "
                        + AssetDatabase.GetAssetPath(existing), table);
                    return;
                }
            }

            int addedCount = 0;
            var existingReferences = new HashSet<ItemDefinitionAsset>();
            for (int index = 0; index < references.arraySize; index++)
            {
                var existing = references.GetArrayElementAtIndex(index)
                    .objectReferenceValue as ItemDefinitionAsset;
                if (existing != null)
                    existingReferences.Add(existing);
            }

            for (int index = 0; index < assets.Count; index++)
            {
                ItemDefinitionAsset asset = assets[index];
                if (asset == null || existingReferences.Contains(asset))
                    continue;

                references.arraySize++;
                references.GetArrayElementAtIndex(references.arraySize - 1)
                    .objectReferenceValue = asset;
                existingReferences.Add(asset);
                addedCount++;
            }

            if (addedCount > 0)
            {
                serialized.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(table);
            }

            table.RefreshLookup();
            Debug.Log("Item catalog "
                + (addedCount == 0 ? "already current" : "appended")
                + ": scanned " + assets.Count + " item assets, added "
                + addedCount + " new references. Existing references were preserved.",
                table);
        }

        private static System.Func<string, bool> ResolveSkillValidator()
        {
            技能注册表[] registries =
                UnityEngine.Object.FindObjectsOfType<技能注册表>(true);
            for (int index = 0; index < registries.Length; index++)
            {
                技能注册表 registry = registries[index];
                if (registry != null)
                    return registry.IsRegisteredAndValid;
            }

            return null;
        }
    }
}
