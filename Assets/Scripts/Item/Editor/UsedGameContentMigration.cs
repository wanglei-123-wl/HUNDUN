using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Items;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.Layout;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Game.Items.Editor
{
    public static class UsedGameContentMigration
    {
        private const string StartupScene = "Assets/Scenes/启动场景.unity";
        private const string LoginScene = "Assets/Scenes/SampleScene.unity";
        private const string WorldScene = "Assets/Scenes/WorldShell.unity";
        private const string DemoMapScene = "Assets/Scenes/Demo 1 Map.unity";
        private const string Map001Scene = "Assets/Scenes/Map001 Gameplay.unity";
        private const string ItemTable = "Assets/Data/Items/ItemDefinitionTable.asset";
        private const string ChineseFont = "Assets/字体/Fonts/NotoSansSC-Regular SDF.asset";
        private const string SharedTextShader = "Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader";
        private const string RemoteGroupName = "RemoteGameContent";
        private const string LocalGroupName = "BootstrapShared";

        [Serializable]
        private sealed class Inventory
        {
            public string[] builtInStartupDependencies;
            public string[] downloadedGameDependencies;
            public string[] unreferencedProjectFiles;
        }

        [MenuItem("Tools/Resource Update/Migrate Used Game Content")]
        public static void Migrate()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Leave Play mode before building content.");

                var settings = AddressableAssetSettingsDefaultObject.Settings;
                var iconGroup = settings == null ? null : settings.FindGroup("RemoteTrialOnly");
                if (iconGroup == null || settings.DefaultGroup == null)
                    throw new InvalidOperationException("Existing Addressables groups are missing.");

                var scenes = EditorBuildSettings.scenes;
                bool originalScenes = scenes.Length == 3
                    && scenes[0].enabled && scenes[0].path == StartupScene
                    && scenes[1].enabled && scenes[1].path == LoginScene
                    && scenes[2].enabled && scenes[2].path == WorldScene;
                bool migratedScenes = scenes.Length == 1
                    && scenes[0].enabled && scenes[0].path == StartupScene;
                if (!originalScenes && !migratedScenes)
                    throw new InvalidOperationException(
                        "Build Settings scenes changed; review entry scenes before migration.");

                var table = AssetDatabase.LoadAssetAtPath<ItemDefinitionTable>(ItemTable);
                if (table == null || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ChineseFont) == null
                    || AssetDatabase.LoadAssetAtPath<Shader>(SharedTextShader) == null)
                    throw new InvalidOperationException("Item table, startup font, or shared shader is missing.");

                var requiredIcons = new HashSet<string>(
                    table.GetRequiredRemoteIconAddresses(), StringComparer.Ordinal);
                var registeredIcons = new HashSet<string>(
                    iconGroup.entries.Select(entry => entry.address), StringComparer.Ordinal);
                if (requiredIcons.Count == 0 || !requiredIcons.SetEquals(registeredIcons))
                    throw new InvalidOperationException(
                        "Remote icon group must match item table; unused icons must not be downloaded.");

                var bootstrap = new HashSet<string>(
                    AssetDatabase.GetDependencies(StartupScene, true), StringComparer.Ordinal);
                if (bootstrap.Contains(ItemTable) || bootstrap.Contains(ChineseFont))
                    throw new InvalidOperationException(
                        "Startup scene still references the remote item table or shared font directly.");

                string[] iconPaths = registeredIcons.Select(AssetPathForIcon).ToArray();
                var remoteRoots = new[] { LoginScene, WorldScene, DemoMapScene, Map001Scene, ItemTable };
                if (remoteRoots.Concat(iconPaths).Any(bootstrap.Contains))
                    throw new InvalidOperationException(
                        "Startup scene directly references a remote asset.");
                WriteInventory(bootstrap, remoteRoots, iconPaths, ChineseFont, SharedTextShader);

                var localGroup = EnsureGroup(settings, LocalGroupName, settings.DefaultGroup);
                var remoteGroup = EnsureGroup(settings, RemoteGroupName, iconGroup);
                var localBundle = localGroup.GetSchema<BundledAssetGroupSchema>();
                var remoteBundle = remoteGroup.GetSchema<BundledAssetGroupSchema>();
                if (localBundle == null || remoteBundle == null
                    || localBundle.BuildPath.Id != settings.DefaultGroup
                        .GetSchema<BundledAssetGroupSchema>().BuildPath.Id
                    || localBundle.LoadPath.Id != settings.DefaultGroup
                        .GetSchema<BundledAssetGroupSchema>().LoadPath.Id
                    || remoteBundle.BuildPath.Id != iconGroup
                        .GetSchema<BundledAssetGroupSchema>().BuildPath.Id
                    || remoteBundle.LoadPath.Id != iconGroup
                        .GetSchema<BundledAssetGroupSchema>().LoadPath.Id)
                    throw new InvalidOperationException("Migration group paths must remain local/remote.");
                EnsureEntry(settings, localGroup, ChineseFont, RemoteGameContent.BootstrapFontAddress);
                EnsureEntry(settings, localGroup, SharedTextShader, "bootstrap/shader/text");
                settings.AddLabel(RemoteGameContent.RequiredContentLabel);
                EnsureEntry(settings, remoteGroup, LoginScene, RemoteGameContent.LoginSceneAddress);
                EnsureEntry(settings, remoteGroup, WorldScene, RemoteGameContent.WorldSceneAddress);
                EnsureEntry(settings, remoteGroup, DemoMapScene, RemoteGameContent.DemoMapSceneAddress);
                EnsureEntry(settings, remoteGroup, Map001Scene, RemoteGameContent.Map001SceneAddress);
                EnsureEntry(settings, remoteGroup, ItemTable, RemoteGameContent.ItemTableAddress);
                foreach (var entry in remoteGroup.entries)
                    entry.SetLabel(RemoteGameContent.RequiredContentLabel, true);
                if (remoteGroup.entries.Count != 5 || localGroup.entries.Count != 2)
                    throw new InvalidOperationException("Unexpected assets in migration groups.");

                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(StartupScene, true)
                };
                AssetDatabase.SaveAssets();
                AddressablesPlayerBuildResult result;
                bool previousLayoutSetting = ProjectConfigData.GenerateBuildLayout;
                bool buildSucceeded = false;
                try
                {
                    ProjectConfigData.GenerateBuildLayout = true;
                    AddressableAssetSettings.BuildPlayerContent(out result);
                    if (result == null || !string.IsNullOrEmpty(result.Error))
                        throw new InvalidOperationException(
                            "Addressables build failed: " + result?.Error);
                    VerifyBuiltContent();
                    buildSucceeded = true;
                }
                finally
                {
                    ProjectConfigData.GenerateBuildLayout = previousLayoutSetting;
                    if (!buildSucceeded)
                        EditorBuildSettings.scenes = scenes;
                }

                Debug.Log("[ResourceMigration] Used game content built. Player scenes: startup only; "
                    + "remote roots: login, world, both maps, item table; startup font: local shared group. "
                    + "Inspect the generated Addressables build layout before publishing.");
            }
            catch (Exception error)
            {
                Debug.LogError("[ResourceMigration] Migration failed: " + error);
            }
        }

        private static AddressableAssetGroup EnsureGroup(
            AddressableAssetSettings settings, string name, AddressableAssetGroup template)
        {
            var existing = settings.FindGroup(name);
            if (existing != null)
                return existing;

            var bundle = template.GetSchema<BundledAssetGroupSchema>();
            var update = template.GetSchema<ContentUpdateGroupSchema>();
            if (bundle == null || update == null)
                throw new InvalidOperationException("Group schema missing: " + template.Name);
            return settings.CreateGroup(name, false, false, true,
                new List<AddressableAssetGroupSchema> { update, bundle });
        }

        private static void EnsureEntry(AddressableAssetSettings settings,
            AddressableAssetGroup group, string path, string address)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException("Asset not found: " + path);
            var existing = settings.FindAssetEntry(guid);
            if (existing != null && existing.parentGroup != group)
                throw new InvalidOperationException("Asset already belongs to another group: " + path);
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = address;
        }

        private static string AssetPathForIcon(string address)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var entry = settings.FindGroup("RemoteTrialOnly").entries
                .FirstOrDefault(candidate => candidate.address == address);
            if (entry == null)
                throw new InvalidOperationException("Missing remote icon: " + address);
            return entry.AssetPath;
        }

        private static void WriteInventory(HashSet<string> bootstrap,
            string[] remoteRoots, string[] iconPaths, params string[] sharedPaths)
        {
            var local = new HashSet<string>(bootstrap, StringComparer.Ordinal);
            local.UnionWith(sharedPaths);
            local.Add("Assets/Resources/NetworkEndpointSettings.asset");
            var remote = new HashSet<string>(
                AssetDatabase.GetDependencies(remoteRoots, true), StringComparer.Ordinal);
            foreach (string path in iconPaths)
                remote.UnionWith(AssetDatabase.GetDependencies(path, true));

            var allFiles = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)
                    && !AssetDatabase.IsValidFolder(path) && !path.EndsWith(".cs",
                        StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var inventory = new Inventory
            {
                builtInStartupDependencies = local.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                downloadedGameDependencies = remote
                    .Where(path => !local.Contains(path))
                    .OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                unreferencedProjectFiles = allFiles
                    .Where(path => !local.Contains(path) && !remote.Contains(path))
                    .OrderBy(path => path, StringComparer.Ordinal).ToArray()
            };
            string directory = Path.Combine("Build", "RemoteMigrationAudit");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "asset-inventory.json"),
                JsonUtility.ToJson(inventory, true));
            Debug.Log("[ResourceMigration] Inventory: startup=" + local.Count
                + ", remote dependencies=" + inventory.downloadedGameDependencies.Length
                + ", not selected for download=" + inventory.unreferencedProjectFiles.Length);
        }

        private static void VerifyBuiltContent()
        {
            const string layoutPath = "Library/com.unity.addressables/buildlayout.json";
            var layout = BuildLayout.Open(layoutPath, true, true);
            if (layout == null || !string.IsNullOrEmpty(layout.BuildError))
                throw new InvalidOperationException("Addressables build layout is missing or invalid.");
            if (layout.DuplicatedAssets.Count != 0)
                throw new InvalidOperationException(
                    "Assets are duplicated across bundles: "
                    + string.Join(", ", layout.DuplicatedAssets.Select(asset => asset.AssetGuid)));

            var inventory = JsonUtility.FromJson<Inventory>(
                File.ReadAllText(Path.Combine("Build", "RemoteMigrationAudit", "asset-inventory.json")));
            var expected = new HashSet<string>(
                inventory.downloadedGameDependencies, StringComparer.Ordinal);
            var remoteGroups = layout.Groups.Where(group =>
                group.Name == RemoteGroupName || group.Name == "RemoteTrialOnly").ToArray();
            if (remoteGroups.Length != 2)
                throw new InvalidOperationException("Remote groups missing from build layout.");

            int packed = 0;
            foreach (var group in remoteGroups)
            foreach (var bundle in group.Bundles)
            foreach (var file in bundle.Files)
            foreach (string path in file.Assets.Select(asset => asset.AssetPath)
                .Concat(file.OtherAssets.Select(asset => asset.AssetPath)))
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;
                if (!expected.Contains(path))
                    throw new InvalidOperationException(
                        "Unexpected asset in remote bundle: " + path);
                packed++;
            }
            if (packed == 0)
                throw new InvalidOperationException("Remote bundles contain no game assets.");
            Debug.Log("[ResourceMigration] Build layout verified: remote asset entries="
                + packed + ", duplicated assets=0, no unselected project assets.");
        }
    }
}
