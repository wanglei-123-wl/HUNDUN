using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Build.Layout;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

public static class ResourceReleaseVerifier
{
    public static JObject Verify(string release, JObject inputs)
    {
        string remote = (string)inputs["remoteDirectory"];
        string player = Path.Combine(release, "PlayerContent");
        string catalogPath = Path.Combine(remote, (string)inputs["catalogFileName"]);
        string hashPath = Path.ChangeExtension(catalogPath, ".hash");
        var errors = new JArray();
        var warnings = new JArray();
        var result = new JObject { ["errors"] = errors, ["warnings"] = warnings,
            ["catalog"] = ResourceReleaseBuildStage4.Record(catalogPath),
            ["catalogHash"] = ResourceReleaseBuildStage4.Record(hashPath),
            ["buildLayout"] = ResourceReleaseBuildStage4.Record(Path.Combine(release, "buildlayout.json")) };
        Require(new FileInfo(hashPath).Length > 0 && File.Exists(Path.Combine(player, "settings.json")), "Catalog hash or player runtime settings missing.");
        var outputFiles = Directory.GetFiles(remote, "*.bundle", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(player, "*.bundle", SearchOption.AllDirectories))
            .Select(Path.GetFullPath).ToArray();
        BuildLayout layout = BuildLayout.Open(Path.Combine(release, "buildlayout.json"), true, true);
        Require(layout != null && string.IsNullOrEmpty(layout.BuildError)
            && layout.BuildTarget == BuildTarget.StandaloneWindows64, "Fresh successful Windows Build Layout missing.");
        try
        {
            var bundles = layout.Groups.SelectMany(group => group.Bundles).Concat(layout.BuiltInBundles)
                .GroupBy(bundle => bundle.Name, StringComparer.Ordinal).Select(group => group.First()).ToArray();
            var files = bundles.ToDictionary(bundle => bundle.Name,
                bundle => ResourceReleaseFiles.FindBundleFile(bundle.Name, outputFiles), StringComparer.Ordinal);
            var verifiedCopies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var bundle in bundles)
            {
                Require(files.TryGetValue(bundle.Name, out string path), "Built bundle file missing: " + bundle.Name);
                Require((ulong)new FileInfo(path).Length == bundle.FileSize, "Bundle size differs from Build Layout: " + bundle.Name);
            }
            var packed = bundles.SelectMany(bundle => bundle.Files).SelectMany(file => file.Assets).ToArray();
            var records = new JArray();
            foreach (JObject entry in (JArray)inputs["entries"])
            {
                var matches = packed.Where(asset => asset.Guid == (string)entry["guid"]).ToArray();
                if (matches.Length != 1 || matches[0].AssetPath != (string)entry["path"]
                    || matches[0].AddressableName != (string)entry["address"])
                    errors.Add("Explicit root missing, changed or packed more than once: " + entry["address"]);
                else
                {
                    var asset = matches[0];
                    if (asset.Bundle.Group?.Name != (string)entry["group"])
                        errors.Add("Wrong packed group: " + entry["address"]);
                    records.Add(new JObject { ["address"] = (string)entry["address"], ["path"] = asset.AssetPath,
                        ["guid"] = asset.Guid, ["bundle"] = asset.Bundle.Name, ["group"] = asset.Bundle.Group?.Name });
                }
            }
            result["explicitRoots"] = records;
            var maps = records.OfType<JObject>().Where(item => WorldMapSceneCatalog.TryGetMapByAddress((string)item["address"], out _, out _)).ToArray();
            if (maps.Length != 8 || maps.Select(item => (string)item["bundle"]).Distinct(StringComparer.Ordinal).Count() != maps.Length)
                errors.Add("All registered map scenes must have separate bundles.");
            result["mapScenesHaveSeparateBundles"] = maps.Length == 8
                && maps.Select(item => (string)item["bundle"]).Distinct(StringComparer.Ordinal).Count() == maps.Length;

            var duplicates = new JArray();
            foreach (var duplicate in layout.DuplicatedAssets)
            {
                ulong extra = 0;
                var objects = new JArray();
                foreach (var item in duplicate.DuplicatedObjects)
                {
                    var sizes = item.IncludedInBundleFiles.Select(file => file.OtherAssets
                        .Where(asset => asset.AssetGuid == duplicate.AssetGuid).SelectMany(asset => asset.Objects)
                        .Where(asset => asset.LocalIdentifierInFile == item.LocalIdentifierInFile)
                        .Aggregate(0UL, (total, asset) => total + asset.SerializedSize + asset.StreamedSize)).ToArray();
                    if (sizes.Length > 1) extra += sizes.Aggregate(0UL, (sum, size) => sum + size) - sizes.Max();
                    objects.Add(new JObject { ["localId"] = item.LocalIdentifierInFile,
                        ["bundles"] = JArray.FromObject(item.IncludedInBundleFiles.Select(file => file.Bundle.Name)) });
                }
                duplicates.Add(new JObject { ["guid"] = duplicate.AssetGuid, ["path"] = AssetDatabase.GUIDToAssetPath(duplicate.AssetGuid),
                    ["extraUncompressedObjectBytes"] = extra, ["objects"] = objects,
                    ["smallEngineOrBakeMetadata"] = ReleaseDuplicatePolicy.IsSmallMetadata(duplicate.AssetGuid,
                        duplicate.DuplicatedObjects.Select(item => item.LocalIdentifierInFile).ToArray(), extra) });
            }
            result["duplicatedAssets"] = duplicates;
            var businessDuplicates = new JArray(duplicates.OfType<JObject>().Where(item => !(bool)item["smallEngineOrBakeMetadata"]).Select(item => item.DeepClone()));
            result["businessDuplicatedAssets"] = businessDuplicates;
            result["allowedMetadataDuplicates"] = new JArray(duplicates.OfType<JObject>().Where(item => (bool)item["smallEngineOrBakeMetadata"]).Select(item => item.DeepClone()));
            if (businessDuplicates.Count > 0) errors.Add("Actual Build Layout contains duplicated business assets; inspect businessDuplicatedAssets.");

            var localGameAssets = new JArray();
            foreach (var bundle in bundles.Where(bundle => !IsInside(remote, files[bundle.Name])))
            foreach (var file in bundle.Files)
            foreach (var item in file.Assets.Select(asset => new { Path = asset.AssetPath, Type = asset.MainAssetType.ToString() })
                .Concat(file.OtherAssets.Select(asset => new { Path = asset.AssetPath, Type = asset.MainAssetType.ToString() })))
                if (item.Path.StartsWith("Assets/", StringComparison.Ordinal) && !item.Path.StartsWith("Assets/Resources/Startup/", StringComparison.Ordinal)
                    && item.Type != "Shader" && item.Type != "ComputeShader" && item.Type != "MonoScript"
                    && !item.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    localGameAssets.Add(new JObject { ["path"] = item.Path, ["bundle"] = bundle.Name });
            result["gameArtInLocalAddressableBundles"] = localGameAssets;
            if (localGameAssets.Count > 0) errors.Add("Game art was packed into local Addressables bundles.");

            var locator = JsonUtility.FromJson<ContentCatalogData>(File.ReadAllText(catalogPath)).CreateLocator();
            // Use the exact runtime startup planner, including newly added unlabelled dependencies.
            var startupPlan = StartupResourceDownloadPlan.Create(new[] { locator });
            var startupBundles = new HashSet<string>(startupPlan.RemoteBundles
                .Select(location => ResourceReleaseFiles.FindBundleName(location.InternalId, files.Keys)), StringComparer.Ordinal);
            var requiredStartupAssets = new JArray();
            foreach (string path in ((JArray)inputs["requiredStartupAssets"] ?? new JArray()).Values<string>())
            {
                var containing = bundles.Where(bundle => bundle.Files.Any(file =>
                    file.Assets.Any(asset => asset.AssetPath == path)
                    || file.OtherAssets.Any(asset => asset.AssetPath == path))).ToArray();
                if (containing.Length != 1 || !IsInside(remote, files[containing[0].Name])
                    || !startupBundles.Contains(containing[0].Name))
                    errors.Add("Required new resource is missing, duplicated, local or outside startup download: " + path);
                else requiredStartupAssets.Add(new JObject { ["path"] = path, ["bundle"] = containing[0].Name,
                    ["downloadedAtStartup"] = true });
            }
            result["requiredStartupAssets"] = requiredStartupAssets;
            result["runtimeStartupDownloadBundleCount"] = startupBundles.Count;
            var rootLocations = new Dictionary<string, IList<IResourceLocation>>(StringComparer.Ordinal);
            var bundleLocations = new Dictionary<string, IResourceLocation>(StringComparer.Ordinal);
            var crcChecked = new HashSet<string>(StringComparer.Ordinal);
            var crcResults = new JArray();
            var initial = new HashSet<string>(startupBundles, StringComparer.Ordinal);
            foreach (JObject entry in (JArray)inputs["entries"])
            {
                string address = (string)entry["address"];
                Type resourceType = (string)entry["group"] == "RemoteTrialOnly" ? typeof(Sprite)
                    : ((string)entry["path"]).EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? typeof(SceneInstance)
                    : AssetDatabase.GetMainAssetTypeAtPath((string)entry["path"]);
                // AnimatorController is an editor authoring type; bundles expose
                // its RuntimeAnimatorController base to the player catalog.
                if (typeof(RuntimeAnimatorController).IsAssignableFrom(resourceType)) resourceType = typeof(RuntimeAnimatorController);
                if (!locator.Locate(address, resourceType, out var locations) || locations.Count != 1)
                {
                    errors.Add("Catalog root did not resolve uniquely: " + address);
                    continue;
                }
                rootLocations[address] = locations;
                bool startup = ((JArray)entry["labels"]).Values<string>().Contains(RemoteGameContent.RequiredContentLabel)
                    || address == RemoteGameContent.BootstrapFontAddress || (string)entry["group"] == "RemoteTrialOnly";
                foreach (var location in Walk(locations))
                {
                    if (!(location.Data is AssetBundleRequestOptions options)) continue;
                    string name = ResourceReleaseFiles.FindBundleName(location.InternalId, files.Keys);
                    bundleLocations[name] = location;
                    if (startup && IsInside(remote, files[name])) initial.Add(name);
                    if (IsInside(remote, files[name]) && !location.InternalId.StartsWith(((string)inputs["remoteLoadPath"]).TrimEnd('/') + "/", StringComparison.Ordinal))
                        errors.Add("Remote catalog bundle has an unexpected server URL: " + location.InternalId);
                    if (options.BundleSize > 0 && options.BundleSize != new FileInfo(files[name]).Length)
                        errors.Add("Catalog bundle size mismatch: " + name);
                    if (options.Crc != 0 && crcChecked.Add(name))
                    {
                        // Validate with the same runtime loader contract used by
                        // the client, rather than the legacy editor CRC reader.
                        string probePath = files[name];
                        if (inputs["verificationCopyDirectory"] != null)
                        {
                            string copyRoot = (string)inputs["verificationCopyDirectory"];
                            string copyPath = ResourceReleaseFiles.Inside(copyRoot, Path.Combine(copyRoot, name));
                            string sourceHash = ResourceReleaseFiles.Hash(probePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(copyPath));
                            if (!File.Exists(copyPath)) File.Copy(probePath, copyPath, false);
                            Require(ResourceReleaseFiles.Hash(copyPath) == sourceHash
                                && ResourceReleaseFiles.Hash(probePath) == sourceHash,
                                "Independent verification copy differs: " + name);
                            probePath = copyPath;
                            verifiedCopies[name] = copyPath;
                        }
                        var probe = AssetBundle.LoadFromFile(probePath.Replace('\\', '/'), options.Crc);
                        bool valid = probe != null;
                        if (probe != null) probe.Unload(true);
                        crcResults.Add(new JObject { ["bundle"] = name, ["catalogCrc"] = options.Crc,
                            ["verifiedFile"] = probePath,
                            ["runtimeLoaderAcceptedCrc"] = valid });
                        if (!valid) errors.Add("Runtime bundle CRC validation failed: " + name);
                    }
                }
            }
            if (!locator.Locate(RemoteGameContent.RequiredContentLabel, null, out var required) || required.Count != 5)
                errors.Add("Required-content catalog label changed.");
            foreach (var bundle in bundles)
                if (!bundleLocations.ContainsKey(bundle.Name)) errors.Add("Built bundle is not reachable from registered catalog roots: " + bundle.Name);
            var bundleRecords = new JArray();
            foreach (var bundle in bundles)
            {
                JObject record = ResourceReleaseBuildStage4.Record(verifiedCopies.TryGetValue(bundle.Name, out string verified)
                    ? verified : files[bundle.Name]);
                record["name"] = bundle.Name; record["group"] = bundle.Group?.Name ?? "BuiltIn";
                record["remote"] = IsInside(remote, files[bundle.Name]);
                record["initialDownload"] = initial.Contains(bundle.Name);
                record["dependencies"] = JArray.FromObject((bundle.Dependencies ?? new List<BuildLayout.Bundle>()).Select(item => item.Name));
                bundleRecords.Add(record);
            }
            result["bundles"] = bundleRecords;
            result["bundleCrcChecks"] = crcResults;
            result["remoteBundleBytes"] = bundleRecords.OfType<JObject>().Where(item => (bool)item["remote"]).Sum(item => (long)item["bytes"]);
            result["initialRemoteBundleBytes"] = initial.Sum(name => new FileInfo(files[name]).Length);
            result["localAddressableBundleBytes"] = bundleRecords.OfType<JObject>().Where(item => !(bool)item["remote"]).Sum(item => (long)item["bytes"]);
            result["shaderSmoke"] = ShaderSmoke(bundles, files, errors);
            warnings.Add("Shader objects were checked in the Windows Unity Editor; final player rendering and clean-machine startup remain later-stage checks.");
            if (errors.Count == 0)
            {
                string payload = (string)inputs["payloadDirectory"] ?? Path.Combine(release, "RemotePayload");
                Directory.CreateDirectory(payload);
                var payloadRecords = new JArray();
                foreach (string source in files.Values.Where(path => IsInside(remote, path)).Concat(new[] { catalogPath, hashPath }))
                {
                    string relative = source.Substring(Path.GetFullPath(remote).TrimEnd('\\', '/').Length + 1);
                    string target = ResourceReleaseFiles.Inside(payload, Path.Combine(payload, relative));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string name = files.Where(pair => pair.Value == source).Select(pair => pair.Key).FirstOrDefault();
                    string verifiedSource = name != null && verifiedCopies.TryGetValue(name, out string copy) ? copy : source;
                    if (!File.Exists(target)) File.Copy(verifiedSource, target, false);
                    Require(ResourceReleaseFiles.Hash(verifiedSource) == ResourceReleaseFiles.Hash(target), "Payload copy changed: " + target);
                    JObject record = ResourceReleaseBuildStage4.Record(target);
                    record["relativePath"] = relative.Replace('\\', '/');
                    payloadRecords.Add(record);
                }
                var manifest = new JObject { ["releaseDirectory"] = release, ["buildTarget"] = "StandaloneWindows64",
                    ["playerVersion"] = inputs["playerVersion"], ["remoteLoadPath"] = inputs["remoteLoadPath"],
                    ["remotePayloadDirectory"] = payload, ["files"] = payloadRecords,
                    ["playerContentDirectory"] = player, ["inputs"] = ResourceReleaseBuildStage4.Record(Path.Combine(release, "inputs.json")),
                    ["buildLayout"] = result["buildLayout"].DeepClone(), ["verifiedAtUtc"] = DateTime.UtcNow.ToString("o"),
                    ["published"] = false };
                ResourceReleaseBuildStage4.WriteFile(Path.Combine(release, "release-manifest.json"), manifest);
                result["manifest"] = ResourceReleaseBuildStage4.Record(Path.Combine(release, "release-manifest.json"));
            }
            return result;
        }
        finally { layout.Close(); }
    }

    private static JObject ShaderSmoke(BuildLayout.Bundle[] bundles, Dictionary<string, string> files, JArray errors)
    {
        var rows = new JArray();
        var loaded = new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
        var unsupported = new JArray();
        try
        {
            var targets = bundles.Where(bundle => bundle.Files.Any(file => file.Assets.Any(asset => asset.MainAssetType.ToString() == "Shader")
                || file.OtherAssets.Any(asset => asset.MainAssetType.ToString() == "Shader"))).ToArray();
            Action<BuildLayout.Bundle> load = null;
            load = bundle => {
                if (loaded.ContainsKey(bundle.Name)) return;
                loaded[bundle.Name] = null; // Cycle guard.
                foreach (var dependency in bundle.Dependencies ?? new List<BuildLayout.Bundle>()) load(dependency);
                var handle = AssetBundle.LoadFromFile(files[bundle.Name]);
                Require(handle != null, "Built shader/dependency bundle cannot be loaded: " + bundle.Name);
                loaded[bundle.Name] = handle;
            };
            foreach (var target in targets) load(target);
            foreach (var target in targets)
            {
                var handle = loaded[target.Name];
                if (handle.isStreamedSceneAssetBundle) continue;
                foreach (Shader shader in handle.LoadAllAssets<Shader>())
                {
                    bool hasError = ShaderUtil.ShaderHasError(shader);
                    rows.Add(new JObject { ["name"] = shader.name, ["bundle"] = target.Name, ["supportedInEditor"] = shader.isSupported,
                        ["hasCompilerError"] = hasError });
                    if (hasError) errors.Add("Compiled shader reports an error: " + shader.name);
                    if (!shader.isSupported) unsupported.Add(shader.name);
                }
            }
            if (rows.Count == 0) errors.Add("No compiled shader objects were found in the generated shader bundles.");
            var alwaysIncluded = new JArray();
            var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (graphics != null)
            {
                var shaders = new SerializedObject(graphics).FindProperty("m_AlwaysIncludedShaders");
                if (shaders != null)
                    for (int index = 0; index < shaders.arraySize; index++)
                    {
                        var shader = shaders.GetArrayElementAtIndex(index).objectReferenceValue as Shader;
                        if (shader != null) alwaysIncluded.Add(shader.name);
                    }
            }
            var available = new HashSet<string>(rows.OfType<JObject>().Where(row => (bool)row["supportedInEditor"] && !(bool)row["hasCompilerError"])
                .Select(row => (string)row["name"]).Concat(alwaysIncluded.Values<string>()), StringComparer.Ordinal);
            var localShaderReferences = new JArray();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Resources/Startup" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null && material.shader != null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader))
                {
                    available.Add(material.shader.name);
                    localShaderReferences.Add(new JObject { ["path"] = path, ["shader"] = material.shader.name,
                        ["keywords"] = JArray.FromObject(material.shaderKeywords) });
                }
            }
            var dynamicCoverage = new JArray();
            foreach (var use in new[] {
                new[] { "LootHoverBeam", "Sprites/Default" },
                new[] { "SafeZoneBoundaryRenderer", "Legacy Shaders/Particles/Additive", "Particles/Standard Unlit", "Sprites/Default" },
                new[] { "节能技能特效", "Universal Render Pipeline/Unlit", "Unlit/Color" }
            })
            {
                string[] matches = use.Skip(1).Where(available.Contains).ToArray();
                dynamicCoverage.Add(new JObject { ["usage"] = use[0], ["availableAlternatives"] = JArray.FromObject(matches) });
                if (matches.Length == 0) errors.Add("Dynamic Shader.Find has no packaged or always-included alternative: " + use[0]);
            }
            return new JObject { ["graphicsDevice"] = SystemInfo.graphicsDeviceType.ToString(), ["shaders"] = rows,
                ["unsupportedInEditor"] = unsupported, ["alwaysIncludedShaders"] = alwaysIncluded,
                ["localResourceShaderReferences"] = localShaderReferences,
                ["dynamicShaderCoverage"] = dynamicCoverage, ["playerRenderingVerified"] = false };
        }
        finally
        {
            foreach (var handle in loaded.Values.Reverse()) if (handle != null) handle.Unload(true);
        }
    }

    private static IEnumerable<IResourceLocation> Walk(IEnumerable<IResourceLocation> roots)
    {
        var visited = new HashSet<IResourceLocation>(); var queue = new Stack<IResourceLocation>(roots);
        while (queue.Count > 0)
        {
            var item = queue.Pop(); if (!visited.Add(item)) continue; yield return item;
            if (item.HasDependencies) foreach (var child in item.Dependencies) queue.Push(child);
        }
    }
    private static bool IsInside(string parent, string path) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(parent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
