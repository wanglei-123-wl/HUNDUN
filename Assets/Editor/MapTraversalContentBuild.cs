using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

// An explicit, expiring request lets an already-open editor perform the same
// build as the menu/batch entry point. No request means no automatic build.
[InitializeOnLoad]
public static class MapTraversalContentBuild
{
    private const string ImportReportPath = "Docs/MapTraversalStage2Import.json";
    public const string BuildReportPath = "Docs/MapTraversalStage4Build.json";
    private const string RequestPath = "Temp/MapTraversalContentBuild.request.json";
    private const string VerifyRequestPath = "Temp/MapTraversalContentVerify.request.json";
    private static bool building;
    private static double nextPoll;

    static MapTraversalContentBuild()
    {
        if (!Application.isBatchMode) EditorApplication.update += PollRequest;
    }

    [MenuItem("Tools/Resource Update/Build Map Traversal Content")]
    public static void BuildFromMenu()
    {
        if (File.Exists(RequestPath)) TryQueuedBuild();
        else BuildContent(Guid.NewGuid().ToString("N"));
    }

    public static void BuildBatch()
    {
        bool success = BuildContent(Guid.NewGuid().ToString("N"));
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }

    [MenuItem("Tools/Resource Update/Verify Map Traversal Content")]
    public static void VerifyFromMenu()
    {
        if (File.Exists(VerifyRequestPath)) TryQueuedBuild(VerifyRequestPath, true);
        else BuildContent(Guid.NewGuid().ToString("N"), true);
    }

    private static void PollRequest()
    {
        if (building || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (File.Exists(VerifyRequestPath)) TryQueuedBuild(VerifyRequestPath, true);
        else if (File.Exists(RequestPath)) TryQueuedBuild();
    }

    private static void TryQueuedBuild(string requestPath = RequestPath, bool verificationOnly = false)
    {
        if (building || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string claimed = null;
        bool claimedByThisCall = false;
        try
        {
            JObject request = JObject.Parse(File.ReadAllText(requestPath));
            string id = (string)request["requestId"];
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid build request id.");
            claimed = requestPath + "." + id + ".running";
            File.Move(requestPath, claimed); // Claim once before doing any build work.
            claimedByThisCall = true;
            string expectedRoot = Path.GetFullPath((string)request["projectRoot"] ?? "");
            // JObject may already hold a DateTime. Casting that token to a
            // string loses its UTC kind before reparsing in a local timezone.
            DateTime expiry = request.Value<DateTime>("expiresAtUtc").ToUniversalTime();
            double remaining = (expiry - DateTime.UtcNow).TotalMinutes;
            if (!string.Equals(expectedRoot.TrimEnd('\\', '/'), Directory.GetCurrentDirectory().TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase) || remaining <= 0 || remaining > 30)
                throw new InvalidDataException("Build request expired or belongs to another project.");
            BuildContent(id, verificationOnly);
        }
        catch (Exception error)
        {
            Debug.LogError("[MapTraversalBuild] Request was not executed: " + error);
            if (claimedByThisCall)
                WriteReport(new JObject { ["stage"] = "4-addressables-build", ["status"] = "request_rejected",
                    ["finishedAtUtc"] = DateTime.UtcNow.ToString("O"), ["error"] = error.ToString(),
                    ["addressablesContentBuilt"] = false, ["testsRun"] = false, ["published"] = false,
                    ["runningServerVersionVerified"] = false });
        }
        finally { if (claimedByThisCall && File.Exists(claimed)) File.Delete(claimed); }
    }

    private static bool BuildContent(string requestId, bool verificationOnly = false)
    {
        if (building) return false;
        building = true;
        JObject previous = null;
        var report = new JObject
        {
            ["stage"] = "4-addressables-build",
            ["requestId"] = requestId,
            ["startedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["status"] = "building",
            ["verificationOnly"] = verificationOnly,
            ["addressablesContentBuilt"] = false,
            ["testsRun"] = false,
            ["published"] = false,
            ["runningServerVersionVerified"] = false
        };
        try
        {
            if (verificationOnly && File.Exists(BuildReportPath)) previous = JObject.Parse(File.ReadAllText(BuildReportPath));
            WriteReport(report);
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Exit Play mode and wait for any player build before building content.");
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || !settings.BuildRemoteCatalog)
                throw new InvalidOperationException("Addressables settings or remote catalog configuration are missing.");
            RequireProjectPath(settings.RemoteCatalogBuildPath.GetValue(settings));
            foreach (var group in settings.groups.Where(group => group != null))
            {
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema != null && schema.IncludeInBuild) RequireProjectPath(schema.BuildPath.GetValue(settings));
            }
            JObject import = JObject.Parse(File.ReadAllText(ImportReportPath));
            JArray maps = (JArray)import["maps"] ?? throw new InvalidDataException("Map import report has no map table.");
            if (maps.Count == 0) throw new InvalidDataException("Map import table is empty.");
            foreach (JObject map in maps)
            {
                string sceneId = (string)map["sceneId"], mapId = (string)map["mapId"], asset = (string)map["asset"];
                if (!WorldMapSceneCatalog.TryGetTraversalAddress(sceneId, mapId, out string address)
                    || address != (string)map["address"] || Hash(RequireProjectPath(asset)) != (string)map["archiveSha256"])
                    throw new InvalidDataException("Map catalog or archive does not match the import report: " + mapId);
                var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(asset));
                if (entry == null || entry.address != address
                    || entry.parentGroup.GetSchema<BundledAssetGroupSchema>()?.IncludeInBuild != true)
                    throw new InvalidDataException("Map package is not registered for this build: " + address);
            }
            report["profile"] = settings.profileSettings.GetProfileName(settings.activeProfileId);
            report["buildTarget"] = EditorUserBuildSettings.activeBuildTarget.ToString();
            report["querySourceFingerprint"] = import["querySourceFingerprint"]?.DeepClone();
            WriteReport(report);

            string catalogPath = RequireProjectPath(Path.Combine(settings.RemoteCatalogBuildPath.GetValue(settings),
                settings.profileSettings.EvaluateString(settings.activeProfileId, "catalog_" + settings.PlayerBuildVersion + ".json")));
            string catalogHashPath = Path.ChangeExtension(catalogPath, ".hash");
            string runtimeSettings = Path.Combine(UnityEngine.AddressableAssets.Addressables.BuildPath, "settings.json");
            if (verificationOnly)
            {
                if (previous?.Value<bool>("addressablesContentBuilt") != true
                    || (string)previous["profile"] != (string)report["profile"]
                    || (string)previous["buildTarget"] != (string)report["buildTarget"]
                    || (string)previous["querySourceFingerprint"] != (string)report["querySourceFingerprint"])
                    throw new InvalidDataException("No successful native content build matches this configuration.");
                DateTime contentStarted = (previous["contentBuildStartedAtUtc"] ?? previous["startedAtUtc"])
                    .Value<DateTime>().ToUniversalTime();
                if (!File.Exists(catalogPath) || File.GetLastWriteTimeUtc(catalogPath) < contentStarted)
                    throw new InvalidDataException("The catalog predates the recorded content build.");
                foreach (JObject map in maps)
                    if (File.GetLastWriteTimeUtc((string)map["asset"]) > contentStarted)
                        throw new InvalidDataException("Map package changed after the recorded build: " + map["asset"]);
                report["contentBuildStartedAtUtc"] = contentStarted.ToString("O");
            }
            else
            {
                AddressableAssetSettings.BuildPlayerContent(out var result);
                if (result == null || !string.IsNullOrEmpty(result.Error))
                    throw new InvalidOperationException("Addressables build failed: " + (result?.Error ?? "no result"));
                // Addressables 1.22.3's JSON-catalog path does not fill the
                // RemoteCatalog* result properties. Use its configured paths.
                if (!string.IsNullOrEmpty(result.RemoteCatalogJsonFilePath)) catalogPath = RequireProjectPath(result.RemoteCatalogJsonFilePath);
                catalogHashPath = !string.IsNullOrEmpty(result.RemoteCatalogHashFilePath)
                    ? RequireProjectPath(result.RemoteCatalogHashFilePath) : Path.ChangeExtension(catalogPath, ".hash");
                if (!string.IsNullOrEmpty(result.OutputPath)) runtimeSettings = result.OutputPath;
            }
            report["addressablesContentBuilt"] = true;
            var catalog = JsonUtility.FromJson<ContentCatalogData>(File.ReadAllText(catalogPath)).CreateLocator();
            var bundlePaths = settings.groups.Where(group => group != null)
                .Select(group => group.GetSchema<BundledAssetGroupSchema>())
                .Where(schema => schema != null && schema.IncludeInBuild)
                .Select(schema => RequireProjectPath(schema.BuildPath.GetValue(settings))).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(Directory.Exists).SelectMany(directory => Directory.GetFiles(directory, "*.bundle", SearchOption.AllDirectories))
                .ToDictionary(Path.GetFileName, path => RequireProjectPath(path), StringComparer.Ordinal);
            var bundles = new JObject();
            var addresses = new JArray();
            foreach (JObject map in maps)
            {
                string address = (string)map["address"];
                if (!catalog.Locate(address, typeof(TextAsset), out IList<IResourceLocation> locations) || locations.Count != 1)
                    throw new InvalidDataException("Generated catalog does not resolve the map package: " + address);
                var dependencies = new Stack<IResourceLocation>(locations);
                var seen = new HashSet<IResourceLocation>();
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (dependencies.Count > 0)
                {
                    IResourceLocation location = dependencies.Pop();
                    if (!seen.Add(location)) continue;
                    if (bundlePaths.TryGetValue(Path.GetFileName(location.InternalId), out string bundlePath)) paths.Add(bundlePath);
                    if (location.HasDependencies)
                        foreach (var dependency in location.Dependencies) dependencies.Push(dependency);
                }
                if (paths.Count == 0) throw new InvalidDataException("Catalog has no built bundle for: " + address);
                foreach (string path in paths)
                    if (bundles[path] == null) bundles[path] = FileRecord(path);
                addresses.Add(new JObject { ["address"] = address, ["bundles"] = new JArray(paths.OrderBy(path => path)) });
            }
            report["catalog"] = FileRecord(catalogPath);
            report["catalogHashFile"] = FileRecord(RequireProjectPath(catalogHashPath));
            report["runtimeSettings"] = FileRecord(RequireProjectPath(runtimeSettings));
            report["addresses"] = addresses;
            report["bundles"] = bundles;
            report["status"] = "succeeded";
            Debug.Log("[MapTraversalBuild] Content build and catalog packaging checks succeeded. Report=" + BuildReportPath);
            return true;
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            Debug.LogError("[MapTraversalBuild] " + error);
            return false;
        }
        finally
        {
            report["finishedAtUtc"] = DateTime.UtcNow.ToString("O");
            try { WriteReport(report); }
            finally { building = false; }
        }
    }

    private static void WriteReport(JObject report) => File.WriteAllText(BuildReportPath, report.ToString(Formatting.Indented));
    private static JObject FileRecord(string path) => new JObject
        { ["path"] = path, ["bytes"] = new FileInfo(path).Length, ["sha256"] = Hash(path) };

    private static string RequireProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Build output path is empty.");
        string full = Path.GetFullPath(path);
        string root = Directory.GetCurrentDirectory().TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Build path must remain inside this project: " + full);
        return full;
    }

    private static string Hash(string path)
    {
        using var algorithm = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
