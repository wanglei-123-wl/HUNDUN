using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceProviders;

// Read-only catalog verification, gated by an explicit, expiring request.
[InitializeOnLoad]
public static class StartupFullResourceDownloadVerify
{
    private const string RequestPath = "Temp/StartupFullResourceDownloadVerify.v2.request.json";
    private static double nextPoll;

    static StartupFullResourceDownloadVerify()
    {
        if (!Application.isBatchMode) EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string claimed = null;
        JObject request = null;
        var report = new JObject { ["status"] = "verifying", ["startedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["assetsModified"] = false, ["playerBuilt"] = false, ["resourceFilesDownloaded"] = false };
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
            string release = (string)request["releaseDirectory"];
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(release, "release-manifest.json")));
            string catalogPath = Path.Combine(release, "PlayerContent/catalog.json");
            var catalogFile = ((JArray)manifest["files"]).OfType<JObject>()
                .Single(file => (string)file["relativePath"] == "catalog_0.1.json");
            Require(ResourceReleaseFiles.Hash(catalogPath) == (string)catalogFile["sha256"], "Release catalog changed.");
            var locator = JsonUtility.FromJson<ContentCatalogData>(File.ReadAllText(catalogPath)).CreateLocator();
            var plan = StartupResourceDownloadPlan.Create(new[] { locator });
            string remoteBase = ((string)manifest["remoteLoadPath"]).TrimEnd('/') + "/";
            var expected = ((JArray)manifest["files"]).OfType<JObject>()
                .Where(file => ((string)file["relativePath"]).EndsWith(".bundle", StringComparison.Ordinal))
                .ToDictionary(file => (string)file["relativePath"], StringComparer.Ordinal);
            var actual = new HashSet<string>(StringComparer.Ordinal);
            var records = new JArray();
            foreach (var bundle in plan.RemoteBundles)
            {
                Require(bundle.InternalId.StartsWith(remoteBase, StringComparison.Ordinal), "Wrong resource endpoint: " + bundle.InternalId);
                string relative = bundle.InternalId.Substring(remoteBase.Length);
                Require(expected.TryGetValue(relative, out var file) && actual.Add(relative), "Unexpected or duplicate bundle: " + relative);
                var options = (AssetBundleRequestOptions)bundle.Data;
                Require(options.BundleSize == (long)file["bytes"], "Catalog bundle size changed: " + relative);
                records.Add(new JObject { ["relativePath"] = relative, ["hash"] = options.Hash,
                    ["bytes"] = options.BundleSize, ["crc"] = options.Crc });
            }
            Require(actual.SetEquals(expected.Keys), "Full startup plan omits published bundles.");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Startup/Fonts/StartupChineseSDF.asset");
            Require(font != null && font.HasCharacters(StartupUiText.FontCharacters), "The local startup font misses updated UI characters.");
            foreach (JObject file in (JArray)request["protectedFiles"])
                Require(ResourceReleaseFiles.Hash((string)file["path"]) == (string)file["sha256"], "Protected file changed: " + file["path"]);
            report["catalog"] = catalogPath;
            report["publishedBundleCount"] = expected.Count;
            report["plannedRemoteBundleCount"] = actual.Count;
            report["plannedRemoteBytes"] = records.OfType<JObject>().Sum(record => (long)record["bytes"]);
            report["requiredAddresses"] = JArray.FromObject(WorldMapSceneCatalog.GetAllResourceAddresses());
            report["allPublishedBundlesIncluded"] = true;
            report["startupFontCovered"] = true;
            report["protectedFilesUnchanged"] = true;
            report["bundles"] = records;
            report["status"] = "succeeded";
            Debug.Log("[StartupFullResourceDownloadVerify] Full startup plan matches every published remote bundle.");
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            Debug.LogError("[StartupFullResourceDownloadVerify] " + error);
        }
        finally
        {
            report["finishedAtUtc"] = DateTime.UtcNow.ToString("O");
            if (request != null) File.WriteAllText((string)request["reportPath"], report.ToString(Formatting.Indented));
            if (claimed != null && File.Exists(claimed)) File.Delete(claimed);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
