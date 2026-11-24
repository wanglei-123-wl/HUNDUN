using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.ResourceProviders;

// Explicit native metadata and live hash verification. No bundle downloads or cache changes.
[InitializeOnLoad]
public static class StartupResourceConsistencyVerify
{
    private const string RequestPath = "Temp/StartupResourceConsistencyVerify.request.json";
    private static IEnumerator verification;
    private static object waiting;
    private static JObject request, report;
    private static string claimed;
    private static double nextPoll;

    static StartupResourceConsistencyVerify()
    {
        if (!Application.isBatchMode) EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        try
        {
            if (verification == null)
            {
                if (EditorApplication.timeSinceStartup < nextPoll) return;
                nextPoll = EditorApplication.timeSinceStartup + 2;
                if (!File.Exists(RequestPath)) return;
                request = JObject.Parse(File.ReadAllText(RequestPath));
                string id = (string)request["requestId"];
                Require(Guid.TryParseExact(id, "N", out _), "Invalid request ID.");
                Require(string.Equals(Path.GetFullPath((string)request["projectRoot"]).TrimEnd('\\', '/'),
                    Directory.GetCurrentDirectory().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase), "Wrong project.");
                double remaining = (request.Value<DateTime>("expiresAtUtc").ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
                Require(remaining > 0 && remaining <= 30, "Request expired.");
                claimed = RequestPath + "." + id + ".running";
                File.Move(RequestPath, claimed);
                report = new JObject { ["requestId"] = id, ["status"] = "verifying",
                    ["startedAtUtc"] = DateTime.UtcNow.ToString("O"), ["bundleDownloads"] = false,
                    ["cacheModified"] = false, ["playerBuilt"] = false };
                verification = Verify();
            }
            if (waiting is AsyncOperation operation && !operation.isDone) return;
            if (verification.MoveNext()) waiting = verification.Current;
            else Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }

    private static IEnumerator Verify()
    {
        string release = (string)request["releaseDirectory"];
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(release, "release-manifest.json")));
        string catalogPath = Path.Combine(release, "PlayerContent/catalog.json");
        var catalogFile = ((JArray)manifest["files"]).OfType<JObject>().Single(file => (string)file["relativePath"] == "catalog_0.1.json");
        Require(ResourceReleaseFiles.Hash(catalogPath) == (string)catalogFile["sha256"], "Release catalog changed.");
        var locator = JsonUtility.FromJson<ContentCatalogData>(File.ReadAllText(catalogPath)).CreateLocator();
        var plan = StartupResourceDownloadPlan.Create(new[] { locator });
        int published = ((JArray)manifest["files"]).OfType<JObject>().Count(file => ((string)file["relativePath"]).EndsWith(".bundle", StringComparison.Ordinal));
        Require(plan.RemoteBundles.Count == published, "Full-resource plan no longer covers published bundles.");
        var bundleChecks = new JArray();
        foreach (var bundle in plan.RemoteBundles)
        {
            var original = (AssetBundleRequestOptions)bundle.Data;
            var verificationLocation = StartupResourceCacheVerification.CreateVerificationLocation(bundle);
            var options = (AssetBundleRequestOptions)verificationLocation.Data;
            Require(!ReferenceEquals(options, original) && options.Crc == original.Crc
                && options.BundleName == original.BundleName && options.Hash == original.Hash
                && options.UseCrcForCachedBundle && !options.ClearOtherCachedVersionsWhenLoaded
                && !verificationLocation.HasDependencies, "Native cache validation options are incorrect.");
            bundleChecks.Add(new JObject { ["bundle"] = bundle.InternalId, ["crc"] = options.Crc,
                ["cachedCrcEnabled"] = true, ["otherVersionsPreserved"] = true });
        }
        string hashPath = Path.Combine(release, "RemotePayload/catalog_0.1.hash");
        Require(StartupCatalogSynchronization.TryNormalizeHash(File.ReadAllText(hashPath), out string expectedHash), "Release catalog hash is invalid.");
        string url = ((string)manifest["remoteLoadPath"]).TrimEnd('/') + "/catalog_0.1.hash";
        using (var probe = StartupCatalogSynchronization.CreateHashRequest(url))
        {
            yield return probe.SendWebRequest();
            Require(probe.result == UnityWebRequest.Result.Success && probe.responseCode == 200
                && StartupCatalogSynchronization.TryNormalizeHash(probe.downloadHandler.text, out string actualHash)
                && actualHash == expectedHash, "Fresh remote catalog hash did not match the published release: " + probe.error);
            report["freshRemoteHashMatches"] = true;
            report["resourceHashUrl"] = url;
            report["catalogHash"] = expectedHash;
        }
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Startup/Fonts/StartupChineseSDF.asset");
        Require(font != null && font.HasCharacters(StartupUiText.FontCharacters), "Local startup font misses update messages.");
        foreach (JObject file in (JArray)request["protectedFiles"])
            Require(ResourceReleaseFiles.Hash((string)file["path"]) == (string)file["sha256"], "Protected file changed: " + file["path"]);
        report["remoteBundleCount"] = plan.RemoteBundles.Count;
        report["nativeCacheContracts"] = bundleChecks;
        report["localFontCovered"] = true;
        report["protectedFilesUnchanged"] = true;
        report["status"] = "succeeded";
    }

    private static void Finish(Exception error)
    {
        try
        {
            (verification as IDisposable)?.Dispose();
            if (error != null)
            {
                if (report == null) report = new JObject();
                report["status"] = "failed";
                report["error"] = error.ToString();
                Debug.LogError("[StartupResourceConsistencyVerify] " + error);
            }
            else Debug.Log("[StartupResourceConsistencyVerify] Catalog hash, cache CRC contracts and font coverage verified.");
            if (report != null && request != null)
            {
                report["finishedAtUtc"] = DateTime.UtcNow.ToString("O");
                File.WriteAllText((string)request["reportPath"], report.ToString(Formatting.Indented));
            }
        }
        finally
        {
            if (claimed != null && File.Exists(claimed)) File.Delete(claimed);
            verification = null; waiting = null; request = null; report = null; claimed = null;
        }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
