using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

// Explicit metadata-only native verification. Never sends a gameplay message,
// downloads a bundle, saves a scene, or changes the persistent cache.
[InitializeOnLoad]
public static class MapTransferPreflightVerify
{
    private const string RequestPath = "Temp/MapTransferPreflightVerify.v2.request.json";
    private static double nextPoll;

    static MapTransferPreflightVerify()
    {
        if (!Application.isBatchMode) EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling
            || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
            || BuildPipeline.isBuildingPlayer) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(RequestPath)) return;
        JObject request = null;
        string claimed = null;
        var report = new JObject { ["status"] = "verifying", ["bundleDownloads"] = false,
            ["cacheModified"] = false, ["gameplayRequestsSent"] = false, ["playerBuilt"] = false };
        try
        {
            request = JObject.Parse(File.ReadAllText(RequestPath));
            string id = (string)request["requestId"];
            Require(Guid.TryParseExact(id, "N", out _), "Invalid request ID.");
            Require(string.Equals(Path.GetFullPath((string)request["projectRoot"]).TrimEnd('\\', '/'),
                Directory.GetCurrentDirectory().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase), "Wrong project.");
            double remaining = (request.Value<DateTime>("expiresAtUtc").ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
            Require(remaining > 0 && remaining <= 30, "Request expired.");
            claimed = RequestPath + "." + id + ".running";
            File.Move(RequestPath, claimed);
            report["requestId"] = id;
            Verify(request, report);
            report["status"] = "succeeded";
            Debug.Log("[MapTransferPreflightVerify] All map scene, traversal and nested cache dependencies verified.");
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            Debug.LogError("[MapTransferPreflightVerify] " + error);
        }
        finally
        {
            report["finishedAtUtc"] = DateTime.UtcNow.ToString("O");
            if (request != null) File.WriteAllText((string)request["reportPath"], report.ToString());
            if (claimed != null && File.Exists(claimed)) File.Delete(claimed);
        }
    }

    private static void Verify(JObject request, JObject report)
    {
        string release = (string)request["releaseDirectory"];
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(release, "release-manifest.json")));
        var file = ((JArray)manifest["files"]).OfType<JObject>().Single(f => (string)f["relativePath"] == "catalog_0.1.json");
        string catalogPath = Path.Combine(release, "PlayerContent/catalog.json");
        Require(ResourceReleaseFiles.Hash(catalogPath) == (string)file["sha256"], "Release catalog changed.");
        var locator = JsonUtility.FromJson<ContentCatalogData>(File.ReadAllText(catalogPath)).CreateLocator();
        var maps = new JArray();
        foreach (string address in WorldMapSceneCatalog.GetAllResourceAddresses().Where(a => a.StartsWith("game/scene/", StringComparison.Ordinal)))
        {
            Require(WorldMapSceneCatalog.TryGetMapByAddress(address, out string sceneId, out string mapId), "Map lookup failed.");
            var locations = MapTransferResourcePreflight.Resolve(sceneId, mapId, new[] { locator });
            Require(locations.Length >= 2 && locations[0].ResourceType == typeof(SceneInstance)
                && locations[1].ResourceType == typeof(TextAsset), "Incorrect native resource types.");
            var counted = locations.SelectMany(l => l.Dependencies ?? Array.Empty<IResourceLocation>()).Distinct().ToArray();
            var bundles = locations.Where(l => l.Data is AssetBundleRequestOptions).ToArray();
            Require(bundles.Length > 0 && bundles.All(b => counted.Contains(b)), "A nested map bundle would be skipped by cache size checks.");
            maps.Add(new JObject { ["sceneId"] = sceneId, ["mapId"] = mapId,
                ["sceneAddress"] = address, ["traversalAddress"] = "game/map-traversal/" + mapId,
                ["bundleDependencies"] = new JArray(bundles.Select(b => b.InternalId)) });
        }
        report["maps"] = maps;
        // Check all transfer messages without adding glyphs or changing assets.
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath("e73af7cefd86226449d7c1ddcb0273e6"));
        Require(font != null, "Game font is missing.");
        string coordinator = File.ReadAllText("Assets/Scripts/MapTransferCoordinator.cs");
        string pattern = "\"[^\"\\r\\n]*[\u4e00-\u9fff][^\"\\r\\n]*\"";
        string messages = string.Concat(Regex.Matches(coordinator, pattern)
            .Cast<Match>().Select(m => m.Value.Trim('"')));
        Require(font.HasCharacters(messages, out uint[] missing, true, false),
            "Game font misses transfer message glyphs: " + string.Join(",", missing ?? Array.Empty<uint>()));
        foreach (JObject protectedFile in (JArray)request["protectedFiles"])
            Require(ResourceReleaseFiles.Hash((string)protectedFile["path"]) == (string)protectedFile["sha256"],
                "Protected file changed: " + protectedFile["path"]);
        report["allTransferMessagesFontCovered"] = true;
        report["protectedFilesUnchanged"] = true;
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
