using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Items.Editor
{
    internal static class RemoteItemIconBuildAudit
    {
        private const string TablePath = "Assets/Data/Items/ItemDefinitionTable.asset";
        private const string GroupName = "RemoteTrialOnly";

        [MenuItem("Tools/Resource Update/Verify Remote Item Icons In Player Build")]
        private static void Verify()
        {
            try
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                var group = settings == null ? null : settings.FindGroup(GroupName);
                var table = AssetDatabase.LoadAssetAtPath<ItemDefinitionTable>(TablePath);
                if (group == null || table == null)
                    throw new InvalidOperationException("Item table or remote group is missing.");

                var remoteGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var addresses = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in group.entries)
                {
                    string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null
                        || !addresses.Add(entry.address))
                        throw new InvalidOperationException("Invalid remote Sprite entry: " + entry.address);
                    remoteGuids.Add(entry.guid);
                }

                var assets = new SerializedObject(table).FindProperty("itemAssets");
                if (assets == null || assets.arraySize == 0)
                    throw new InvalidOperationException("Item table has no registered item assets.");
                var required = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < assets.arraySize; index++)
                {
                    var asset = assets.GetArrayElementAtIndex(index).objectReferenceValue
                        as ItemDefinitionAsset;
                    ItemDefinition definition = asset == null ? null : asset.Definition;
                    if (definition == null || definition.Icon != null
                        || string.IsNullOrEmpty(definition.RemoteIconAddress)
                        || definition.RemoteIconAddress != RemoteItemIconCache.AddressFor(definition.ItemId))
                        throw new InvalidOperationException("Item is not remote-only: "
                            + (asset == null ? index.ToString() : asset.name));
                    required.Add(definition.RemoteIconAddress);
                }
                if (!required.SetEquals(addresses))
                    throw new InvalidOperationException("Item addresses and remote group differ.");

                BuildReport report = BuildReport.GetLatestReport();
                if (report == null || report.summary.result != BuildResult.Succeeded
                    || !report.summary.outputPath.EndsWith(".exe",
                        StringComparison.OrdinalIgnoreCase)
                    || report.packedAssets == null || report.packedAssets.Length == 0)
                    throw new InvalidOperationException(
                        "Build a Windows Player first; the latest detailed Player report is required.");

                // Unity 2022 returns UTC build ticks without setting DateTimeKind.
                DateTime buildStart = report.summary.buildStartedAt;
                DateTime builtAt = buildStart.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(buildStart, DateTimeKind.Utc)
                    : buildStart.ToUniversalTime();
                string[] inputs = { TablePath,
                    "Assets/AddressableAssetsData/AssetGroups/RemoteTrialOnly.asset",
                    "Assets/Scripts/Item/Config/ItemDefinition.cs",
                    "Assets/Scripts/Item/Config/RemoteItemIconCache.cs",
                    "Assets/Bootstrap/Runtime/StartupContentUpdate.cs",
                    "Assets/Scenes/启动场景.unity" };
                foreach (string input in inputs)
                {
                    if (File.GetLastWriteTimeUtc(Path.GetFullPath(input)) > builtAt)
                        throw new InvalidOperationException(
                            "Player report predates the remote-only configuration: " + input);
                }
                for (int index = 0; index < assets.arraySize; index++)
                {
                    var asset = assets.GetArrayElementAtIndex(index).objectReferenceValue
                        as ItemDefinitionAsset;
                    if (File.GetLastWriteTimeUtc(Path.GetFullPath(
                        AssetDatabase.GetAssetPath(asset))) > builtAt)
                        throw new InvalidOperationException(
                            "Player report predates item definition: " + asset.name);
                }

                foreach (PackedAssets packed in report.packedAssets)
                {
                    foreach (PackedAssetInfo item in packed.contents)
                    {
                        if (remoteGuids.Contains(item.sourceAssetGUID.ToString()))
                            throw new InvalidOperationException(
                                "Remote icon is also packed into the Player: "
                                + item.sourceAssetPath);
                    }
                }
                Debug.Log("[ResourceTrial] Player build contains no remote item icon GUIDs. "
                    + "Verified " + remoteGuids.Count + " remote Sprites in "
                    + report.summary.outputPath);
            }
            catch (Exception error)
            {
                Debug.LogError("[ResourceTrial] Player icon isolation NOT verified: "
                    + error.Message);
            }
        }
    }
}
