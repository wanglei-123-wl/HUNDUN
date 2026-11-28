using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using QiDao.Bootstrap.CodeUpdate;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceProviders;

public static class HotUpdateResourceBuild
{
    public static void BuildFromRequest()
    {
        if (!Path.GetFullPath(Application.dataPath).StartsWith(@"C:\QiDaoBuild\", StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("Resource migration builds must use an isolated project.");
        var request = JObject.Parse(File.ReadAllText(HotUpdateCodeBuild.Argument("-hotupdate-request")));
        HotUpdateResourceWorkflow.VerifyInputs(request);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new BuildFailedException("Addressables settings are missing.");
        string output = Path.GetFullPath((string)request["outputDirectory"]);
        string id = (string)request["resourceReleaseId"];
        if (!CodeUpdateProtocol.IsHex(id, 32)) throw new BuildFailedException("Invalid resource release ID.");
        string root = "http://47.93.78.106:18761" + CodeUpdateProtocol.ChannelPath + "/resources/" + id;
        string mode=(string)request["resourceBuildMode"] ?? "baseline";
        if(mode!="baseline" && mode!="update") throw new BuildFailedException("Invalid resource build mode.");
        if(mode=="update" && request.Value<bool>("cleanBuild")) throw new BuildFailedException("Do not purge content-update caches.");
        string nativeLoadRoot=mode=="update" ? (string)request["nativeLoadRoot"] : root;
        string remote = Path.Combine(output, "NativeRemote", "[BuildTarget]").Replace('\\', '/');
        Directory.CreateDirectory(output);
        string profile = settings.profileSettings.GetProfileId("HotUpdateMigration");
        if (string.IsNullOrEmpty(profile)) profile = settings.profileSettings.AddProfile("HotUpdateMigration", settings.activeProfileId);
        settings.profileSettings.SetValue(profile, AddressableAssetSettings.kRemoteBuildPath, remote);
        settings.profileSettings.SetValue(profile, AddressableAssetSettings.kRemoteLoadPath, nativeLoadRoot);
        settings.activeProfileId = profile;
        settings.RemoteCatalogBuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        settings.RemoteCatalogLoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        settings.OverridePlayerVersion = "0.1";
        settings.BuildRemoteCatalog = true;
        settings.BundleLocalCatalog = false;
        settings.OptimizeCatalogSize = false;
        settings.DisableCatalogUpdateOnStartup = true;
        settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
        settings.ContentStateBuildPath = Path.Combine(output, "ContentState");
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        if (request.Value<bool>("cleanBuild"))
        {
            // The entry point is restricted to C:/QiDaoBuild validation projects;
            // purge only that project's build cache, never player resource caches.
            AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);
            UnityEditor.Build.Pipeline.Utilities.BuildCache.PurgeCache(false);
        }
        var compilation = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
        {
            target = BuildTarget.StandaloneWindows64, group = BuildTargetGroup.Standalone,
            options = ScriptCompilationOptions.None
        }, Path.Combine(output, "CompiledTypes"));
        if (compilation.assemblies == null || compilation.assemblies.Count == 0) throw new BuildFailedException("Resource script compilation failed.");
        string schema = compilation.typeDB.GetHash128().ToString();
        var contract=HotUpdateResourceWorkflow.Capture(settings,schema);
        if(mode=="update") HotUpdateResourceWorkflow.VerifyUpdate(request,contract);
        string contractPath=Path.Combine(output,"resource-layout-contract.json");
        File.WriteAllText(contractPath,contract.ToString());
        if (!request.Value<bool>("exportOnly"))
        {
            AddressablesPlayerBuildResult result;
            if(mode=="update")
            {
                if(request.Value<bool>("cleanBuild")) throw new BuildFailedException("Content updates must retain their original baseline/cache.");
                var restricted=ContentUpdateScript.GatherModifiedEntries(settings,(string)request["baselineContentState"]);
                if(restricted==null || restricted.Count>0) throw new BuildFailedException("Static content changed; review restrictions and create a fresh baseline.");
                result=ContentUpdateScript.BuildContentUpdate(settings,(string)request["baselineContentState"]);
            }
            else AddressableAssetSettings.BuildPlayerContent(out result);
            if (result == null || !string.IsNullOrEmpty(result.Error)) throw new BuildFailedException("Resource build failed: " + result?.Error);
        }
        string remoteDirectory = Path.Combine(output, "NativeRemote", "StandaloneWindows64");
        string catalog = Directory.GetFiles(remoteDirectory, "catalog_0.1.json", SearchOption.AllDirectories).Single();
        var directories=new List<string>{remoteDirectory,Addressables.BuildPath};
        if(request["fallbackBundleDirectories"] is JArray fallback) directories.AddRange(fallback.Values<string>());
        ExportCatalog(catalog, directories.ToArray(), output, id, root, schema);
        if(request["workflowProtocol"]!=null)
        {
            string reportPath=Path.Combine(output,"resource-build-report.json");var report=JObject.Parse(File.ReadAllText(reportPath));
            report["workflowProtocol"]=1;report["resourceBuildMode"]=mode;report["nativeLoadRoot"]=nativeLoadRoot;
            report["sourceSnapshotPath"]=request["sourceSnapshotPath"];report["sourceSnapshotSha256"]=request["sourceSnapshotSha256"];
            report["layoutContractPath"]=contractPath;report["contentStatePath"]=Path.Combine(output,"ContentState","addressables_content_state.bin");
            report["acceptanceOnly"]=request.Value<bool>("acceptanceOnly");
            if(mode=="update") { report["baselineResourceReleaseId"]=request["baselineResourceReleaseId"];report["baselineResourceReport"]=request["baselineResourceReport"]; }
            File.WriteAllText(reportPath,report.ToString());
        }
    }

    public static void ExportCatalog(string catalogPath, string[] bundleDirectories, string output,
        string id, string root, string scriptTypeHash)
    {
        string destination = Path.Combine(output, "Payload", "resources", id);
        if (Directory.Exists(destination)) throw new BuildFailedException("Resource release output is immutable.");
        Directory.CreateDirectory(Path.Combine(destination, "bundles"));
        var files = bundleDirectories.Where(Directory.Exists).SelectMany(p => Directory.GetFiles(p, "*.bundle", SearchOption.AllDirectories))
            .Select(p => Path.GetFullPath(p).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var data = JsonUtility.FromJson<ContentCatalogData>(File.ReadAllText(catalogPath));
        var originalLocator = data.CreateLocator();
        var generatedDependencyKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (object key in originalLocator.Keys)
            if (originalLocator.Locate(key, null, out var locations))
                foreach (var location in locations)
                    if (location.HasDependencies && location.Dependencies.Count > 1)
                    {
                        var field = location.GetType().GetField("m_Dependency", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (field == null) throw new BuildFailedException("Unsupported Addressables dependency encoding.");
                        generatedDependencyKeys.Add(field.GetValue(location).ToString());
                    }
        var entries = (IList<ContentCatalogDataEntry>)typeof(ContentCatalogData).GetMethod("GetData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(data, null);
        // GetData includes aliases generated for dependency groups. Keeping those
        // aliases during SetData can change the primary bundle order (the asset
        // provider loads from its first bundle). Regenerate only these internal keys.
        foreach (var entry in entries)
        {
            entry.Keys.RemoveAll(key => generatedDependencyKeys.Contains(key.ToString()));
            if (entry.Keys.Count == 0) throw new BuildFailedException("Catalog entry lost its primary key.");
        }
        var remappedIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var records = new Dictionary<string, ResourceBundleRecord>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!(entry.Data is AssetBundleRequestOptions options)) continue;
            string name = Path.GetFileName(entry.InternalId.Replace('\\', '/'));
            var candidates = files.Where(p => Path.GetFileName(p) == name).ToArray();
            if (candidates.Length == 0) throw new BuildFailedException("Built bundle is missing: " + name);
            string file = Path.GetFullPath(candidates[0]).Replace('\\', '/');
            if (candidates.Skip(1).Any(p => StableHash(p) != StableHash(file)))
                throw new BuildFailedException("Conflicting native bundle copies: " + name);
            long size = new FileInfo(file).Length;
            bool crcRead = BuildPipeline.GetCRCForAssetBundle(file, out uint computedCrc);
            uint crc = options.Crc != 0 ? options.Crc : computedCrc;
            if (crc == 0 || (crcRead && options.Crc != 0 && computedCrc != options.Crc)
                || (options.BundleSize > 0 && options.BundleSize != size))
                throw new BuildFailedException("Native bundle metadata differs from the build catalog: " + name
                    + " expectedCrc=" + options.Crc + " actualCrc=" + crc + " expectedSize=" + options.BundleSize
                    + " actualSize=" + size + " file=" + file);
            string relative = "bundles/" + name;
            string copy = Path.Combine(destination, "bundles", name);
            if (!records.ContainsKey(relative))
            {
                // Compare the source before and after a streamed, flushed copy.
                // Never accept a copy merely because the filesystem API returned.
                string sourceHash = StableHash(file);
                bool identical = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var target = new FileStream(copy, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(target, 1024 * 1024);
                        target.Flush(true);
                    }
                    if (StableHash(file) != sourceHash)
                        throw new BuildFailedException("Native bundle changed during export: " + name);
                    identical = StableHash(copy) == sourceHash;
                    if (identical) break;
                    Debug.LogWarning("Bundle copy verification failed; retrying unpublished copy: " + name);
                }
                if (!identical) throw new BuildFailedException("Copied bundle differs after bounded retries: " + name);
                var native = AssetBundle.LoadFromFile(Path.GetFullPath(copy).Replace('\\', '/'), crc);
                if (native == null) throw new BuildFailedException("Native CRC validation failed: " + name);
                native.Unload(true);
            }
            if (string.IsNullOrEmpty(options.Hash)) options.Hash = Hash128.Compute(File.ReadAllBytes(copy)).ToString();
            options.Crc = crc;
            options.BundleSize = size;
            options.UseCrcForCachedBundle = true;
            options.ClearOtherCachedVersionsWhenLoaded = false;
            remappedIds[entry.InternalId] = root + "/" + relative;
            entry.InternalId = root + "/" + relative;
            entry.Data = options;
            records[relative] = new ResourceBundleRecord
            {
                relativePath = relative, bytes = size, sha256 = StableHash(copy),
                crc = crc, cacheHash = options.Hash, bundleName = options.BundleName
            };
        }
        data.SetData(entries);
        var rewrittenLocator = data.CreateLocator();
        Func<string, string> mapped = old => remappedIds.TryGetValue(old, out var replacement) ? replacement : old;
        foreach (object key in originalLocator.Keys.Where(key => !generatedDependencyKeys.Contains(key.ToString())))
        {
            originalLocator.Locate(key, null, out var before);
            if (!rewrittenLocator.Locate(key.ToString(), null, out var after) || before.Count != after.Count)
                throw new BuildFailedException("Catalog rewrite changed an asset key: " + key + " type=" + key.GetType()
                    + " before=" + before.Count + " after=" + (after == null ? -1 : after.Count));
            for (int i = 0; i < before.Count; i++)
            {
                var expected = before[i]; var actual = after[i];
                if (mapped(expected.InternalId) != actual.InternalId || expected.ResourceType != actual.ResourceType
                    || !(expected.Dependencies?.Select(d => mapped(d.InternalId)) ?? Enumerable.Empty<string>())
                        .SequenceEqual(actual.Dependencies?.Select(d => d.InternalId) ?? Enumerable.Empty<string>()))
                    throw new BuildFailedException("Catalog rewrite changed asset dependency order: " + key);
            }
        }
        string json = JsonUtility.ToJson(data);
        string catalogCopy = Path.Combine(destination, "catalog_0.1.json");
        File.WriteAllText(catalogCopy, json, new UTF8Encoding(false));
        string catalogHash = Hash128.Compute(json).ToString();
        File.WriteAllText(Path.Combine(destination, "catalog_0.1.hash"), catalogHash, new UTF8Encoding(false));
        var manifest = new PairedResourceManifest
        {
            schemaVersion = 1, resourceReleaseId = id, target = CodeUpdateProtocol.Target,
            unityVersion = Application.unityVersion, gameAssemblyName = CodeUpdateProtocol.AssemblyName,
            scriptTypeHash = scriptTypeHash, catalogHash = catalogHash, catalogSha256 = ResourceReleaseFiles.Hash(catalogCopy),
            catalogBytes = new FileInfo(catalogCopy).Length, buildRoot = root,
            bundles = records.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToArray()
        };
        string manifestPath = Path.Combine(destination, "resource-manifest.json");
        File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest), new UTF8Encoding(false));
        var report = new JObject
        {
            ["status"] = "succeeded", ["resourceReleaseId"] = id, ["resourceBuildRoot"] = root,
            ["scriptTypeHash"] = scriptTypeHash, ["resourceCatalogHash"] = catalogHash,
            ["resourceCatalogSha256"] = manifest.catalogSha256, ["resourceCatalogBytes"] = manifest.catalogBytes,
            ["resourceManifestSha256"] = ResourceReleaseFiles.Hash(manifestPath),
            ["resourceManifestBytes"] = new FileInfo(manifestPath).Length,
            ["catalogPath"] = catalogCopy, ["resourceManifestPath"] = manifestPath,
            ["allBundlesNativeCrcVerified"] = true, ["assetDependencyOrderVerified"] = true, ["bundleCount"] = records.Count,
            ["totalBundleBytes"] = records.Values.Sum(b => b.bytes), ["legacyResourceChannelChanged"] = false
        };
        File.WriteAllText(Path.Combine(output, "resource-build-report.json"), report.ToString());
    }

    private static string StableHash(string path)
    {
        string previous = ResourceReleaseFiles.Hash(path);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string current = ResourceReleaseFiles.Hash(path);
            if (current == previous) return current;
            Debug.LogWarning("Bundle read verification differed; requiring matching consecutive reads: " + path);
            previous = current;
        }
        throw new BuildFailedException("Bundle bytes could not be read consistently: " + path);
    }
}
