using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ResourceReleaseBuildStage4
{
    private const string RequestPath = "Temp/ResourceReleaseBuildStage4.v3.request.json";
    private const string SettingsPath = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";
    private const string DefaultRemote = "http://47.93.78.106:18761/[BuildTarget]";
    private static bool running;
    private static double nextPoll;

    static ResourceReleaseBuildStage4()
    {
        if (!Application.isBatchMode) EditorApplication.update += Poll;
    }

    [MenuItem("Tools/Resource Update/Build Release Resources")]
    public static void BuildFromMenu()
    {
        var request = new JObject { ["requestId"] = Guid.NewGuid().ToString("N"), ["action"] = "build",
            ["projectRoot"] = Directory.GetCurrentDirectory(), ["expiresAtUtc"] = DateTime.UtcNow.AddMinutes(30).ToString("o") };
        if (File.Exists(RequestPath)) throw new IOException("A queued release request already exists.");
        File.WriteAllText(RequestPath, request.ToString(Formatting.Indented));
    }

    public static void BuildQueuedRequest()
    {
        Require(Application.isBatchMode, "This entry is for batch builds only.");
        const string batchRequest = "ResourceReleaseBuildStage4.batch-request.json";
        if (File.Exists(batchRequest))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RequestPath));
            Require(!File.Exists(RequestPath), "Another release request is pending.");
            File.Copy(batchRequest, RequestPath, false);
        }
        var request = JObject.Parse(File.ReadAllText(RequestPath));
        string id = (string)request["requestId"];
        Require(Guid.TryParseExact(id, "N", out _), "Invalid release request ID.");
        Poll();
        string reportPath = Output(id) + "/report.json";
        Require(File.Exists(reportPath), "The release request was not consumed; inspect its status file.");
        var report = JObject.Parse(File.ReadAllText(reportPath));
        Require((string)report["status"] == "succeeded", "Release validation failed: " + report);
    }

    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string claimed = null, id = null;
        try
        {
            JObject request = JObject.Parse(File.ReadAllText(RequestPath));
            id = (string)request["requestId"];
            Require(Guid.TryParseExact(id, "N", out _), "Invalid release request ID.");
            double remaining = (request.Value<DateTime>("expiresAtUtc").ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
            Require(remaining > 0 && remaining <= 30, "Release request expired.");
            Require(string.Equals(Path.GetFullPath((string)request["projectRoot"]).TrimEnd('\\', '/'),
                Directory.GetCurrentDirectory().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase), "Wrong project.");
            if (DirtyScenes().Length > 0)
            {
                Status(id, "waiting_for_saved_scenes", new JObject { ["scenes"] = JArray.FromObject(DirtyScenes()) });
                return;
            }
            claimed = RequestPath + "." + id + ".running";
            File.Move(RequestPath, claimed);
            running = true;
            Run(id, request);
        }
        catch (Exception error)
        {
            if (Guid.TryParseExact(id, "N", out _)) Status(id, "failed", new JObject { ["error"] = error.ToString() });
            Debug.LogError("[ResourceReleaseStage4] " + error);
        }
        finally
        {
            if (claimed != null && File.Exists(claimed)) File.Delete(claimed);
            running = false;
        }
    }

    private static void Run(string id, JObject request)
    {
        string action = (string)request["action"];
        string buildId = action == "verify" ? (string)request["buildId"] : id;
        Require(Guid.TryParseExact(buildId, "N", out _), "Invalid target build ID.");
        string release = ProjectPath("Build/ResourceReleases/" + buildId);
        var report = new JObject { ["requestId"] = id, ["buildId"] = buildId, ["action"] = action,
            ["startedAtUtc"] = DateTime.UtcNow.ToString("o"), ["status"] = "preparing",
            ["releaseDirectory"] = release, ["published"] = false, ["playerBuilt"] = false };
        string cache = ProjectPath(Addressables.BuildPath);
        string previousCache = ResourceReleaseFiles.Inside(release, Path.Combine(release, "PreviousPlayerContent"));
        byte[] originalSettings = File.ReadAllBytes(SettingsPath);
        bool built = false, movedCache = false;
        try
        {
            VerifyProtected((JObject)request["expectedHashes"]);
            Require(EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64
                && BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64), "Windows 64-bit build support must be active.");
            if (action == "build")
            {
                Require(!Directory.Exists(release), "Refusing to overwrite a release directory.");
                Directory.CreateDirectory(release);
                File.WriteAllBytes(Path.Combine(release, "settings-before.asset"), originalSettings);
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                JObject inputs = CaptureInputs(settings);
                inputs["requiredStartupAssets"] = request["requiredStartupAssets"]?.DeepClone() ?? new JArray();
                PrepareRelease(settings, buildId, release);
                inputs["settingsAfterPreparation"] = Record(ProjectPath(SettingsPath));
                inputs["remoteDirectory"] = ProjectPath(settings.RemoteCatalogBuildPath.GetValue(settings));
                inputs["catalogFileName"] = settings.profileSettings.EvaluateString(settings.activeProfileId, "catalog_" + settings.PlayerBuildVersion + ".json");
                inputs["remoteLoadPath"] = settings.RemoteCatalogLoadPath.GetValue(settings);
                WriteFile(Path.Combine(release, "inputs.json"), inputs);
                report["inputs"] = inputs;
                // Only the generated current-platform AA cache is moved. Both
                // resolved targets are checked before this reversible operation.
                ResourceReleaseFiles.Inside(ProjectPath("Library/com.unity.addressables/aa"), cache);
                ResourceReleaseFiles.Inside(release, previousCache);
                if (Directory.Exists(cache))
                {
                    Require(!Directory.Exists(previousCache), "Previous cache archive already exists.");
                    Directory.Move(cache, previousCache);
                    movedCache = true;
                }
                var oldReports = new HashSet<string>(ProjectConfigData.BuildReportFilePaths, StringComparer.OrdinalIgnoreCase);
                bool previousLayout = ProjectConfigData.GenerateBuildLayout;
                var previousFormat = ProjectConfigData.BuildLayoutReportFileFormat;
                Status(id, "building");
                report["status"] = "building";
                SaveReport(id, report);
                try
                {
                    ProjectConfigData.GenerateBuildLayout = true;
                    ProjectConfigData.BuildLayoutReportFileFormat = ProjectConfigData.ReportFileFormat.JSON;
                    AddressableAssetSettings.BuildPlayerContent(out var result);
                    Require(result != null && string.IsNullOrEmpty(result.Error), "Addressables build failed: " + result?.Error);
                    built = true;
                    report["nativeBuildSucceeded"] = true;
                    report["nativeBuildDurationSeconds"] = result.Duration;
                }
                finally
                {
                    ProjectConfigData.GenerateBuildLayout = previousLayout;
                    ProjectConfigData.BuildLayoutReportFileFormat = previousFormat;
                }
                string layout = ProjectConfigData.BuildReportFilePaths.Where(path => File.Exists(path) && !oldReports.Contains(path))
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                Require(layout != null, "This build did not generate a new Build Layout.");
                File.Copy(layout, Path.Combine(release, "buildlayout.json"), false);
                ResourceReleaseFiles.CopyTree(cache, Path.Combine(release, "PlayerContent"));
            }
            else Require(action == "verify" && Directory.Exists(release), "Unknown action or release missing.");

            Status(id, "verifying");
            JObject input = JObject.Parse(File.ReadAllText(Path.Combine(release, "inputs.json")));
            foreach (string key in new[] { "verificationCopyDirectory", "payloadDirectory" })
            {
                if (request[key] == null) continue;
                input[key] = Path.GetFullPath((string)request[key]);
            }
            WriteFile(Path.Combine(release, "inputs.json"), input);
            JObject audit = ResourceReleaseVerifier.Verify(release, input);
            report["verification"] = audit;
            VerifyInputHashes(input);
            VerifyProtected((JObject)request["expectedHashes"]);
            Require(DirtyScenes().Length == 0, "An open scene changed during the build.");
            report["protectedSourcesUnchanged"] = true;
            report["status"] = ((JArray)audit["errors"]).Count == 0 ? "succeeded" : "requires_review";
            report["finishedAtUtc"] = DateTime.UtcNow.ToString("o");
            SaveReport(id, report);
            Status(id, (string)report["status"]);
            if ((string)report["status"] == "succeeded") WriteFile("Build/ResourceReleases/latest-verified.json", new JObject {
                ["buildId"] = buildId, ["releaseDirectory"] = release, ["report"] = ProjectPath(Output(id) + "/report.json"),
                ["manifest"] = Path.Combine(release, "release-manifest.json") });
            Debug.Log("[ResourceReleaseStage4] " + report["status"] + ": " + Output(id) + "/report.json");
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            report["finishedAtUtc"] = DateTime.UtcNow.ToString("o");
            if (action == "build" && !built)
            {
                if (movedCache)
                {
                    if (Directory.Exists(cache)) Directory.Move(cache, ResourceReleaseFiles.Inside(release, Path.Combine(release, "FailedPlayerContent")));
                    ResourceReleaseFiles.Inside(ProjectPath("Library/com.unity.addressables/aa"), cache);
                    Directory.Move(previousCache, cache);
                }
                File.WriteAllBytes(SettingsPath, originalSettings);
                AssetDatabase.ImportAsset(SettingsPath, ImportAssetOptions.ForceUpdate);
                report["previousCacheAndSettingsRestored"] = true;
            }
            SaveReport(id, report);
            Status(id, "failed", new JObject { ["error"] = error.ToString() });
            Debug.LogError("[ResourceReleaseStage4] " + error);
        }
    }

    private static JObject CaptureInputs(AddressableAssetSettings settings)
    {
        Require(settings != null && settings.BuildRemoteCatalog && !EditorUtility.IsDirty(settings), "Save intended Addressables settings first.");
        Require(EditorBuildSettings.scenes.Count(scene => scene.enabled) == 1
            && EditorBuildSettings.scenes.Single(scene => scene.enabled).path == "Assets/Scenes/启动场景.unity", "Only the startup scene should be built into the player.");
        var entries = new JArray();
        var hashes = new JObject();
        foreach (var group in settings.groups.Where(group => group != null))
        {
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null || !schema.IncludeInBuild) continue;
            Require(!EditorUtility.IsDirty(group) && !EditorUtility.IsDirty(schema), "Unsaved group changes: " + group.Name);
            bool remote = schema.LoadPath.Id == settings.RemoteCatalogLoadPath.Id;
            Require(remote || group.entries.Count == 0, "Review unexpected local addressable content: " + group.Name);
            foreach (var entry in group.entries)
            {
                Require(!AssetDatabase.IsValidFolder(entry.AssetPath), "Folder roots need explicit review.");
                entries.Add(new JObject { ["guid"] = entry.guid, ["path"] = entry.AssetPath, ["address"] = entry.address,
                    ["group"] = group.Name, ["labels"] = JArray.FromObject(entry.labels.OrderBy(label => label, StringComparer.Ordinal)),
                    ["sourceSha256"] = ResourceReleaseFiles.Hash(entry.AssetPath) });
                hashes[entry.AssetPath] = AssetDatabase.GetAssetDependencyHash(entry.AssetPath).ToString();
            }
        }
        Require(entries.Count > 0 && entries.Count(item => ((JArray)item["labels"]).Values<string>().Contains(RemoteGameContent.RequiredContentLabel)) == 5,
            "Required startup roots changed.");
        return new JObject { ["entries"] = entries, ["dependencyHashes"] = hashes,
            ["originalProfile"] = settings.profileSettings.GetProfileName(settings.activeProfileId),
            ["unityVersion"] = Application.unityVersion, ["buildTarget"] = "StandaloneWindows64", ["playerVersion"] = PlayerSettings.bundleVersion };
    }

    private static void PrepareRelease(AddressableAssetSettings settings, string id, string release)
    {
        string profile = settings.profileSettings.GetProfileId("Release");
        if (string.IsNullOrEmpty(profile)) profile = settings.profileSettings.AddProfile("Release", settings.activeProfileId);
        string load = settings.profileSettings.GetValueByName(profile, AddressableAssetSettings.kRemoteLoadPath);
        if (settings.profileSettings.GetProfileName(settings.activeProfileId) != "Release"
            && load == settings.profileSettings.GetValueByName(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath)) load = DefaultRemote;
        Require(Uri.TryCreate(load.Replace("[BuildTarget]", "StandaloneWindows64"), UriKind.Absolute, out var uri)
            && (uri.Scheme == "http" || uri.Scheme == "https") && !uri.IsLoopback, "Configure a public Release Remote.LoadPath first.");
        settings.profileSettings.SetValue(profile, AddressableAssetSettings.kRemoteBuildPath, "Build/ResourceReleases/" + id + "/Remote/[BuildTarget]");
        settings.profileSettings.SetValue(profile, AddressableAssetSettings.kRemoteLoadPath, load);
        settings.activeProfileId = profile;
        settings.ContentStateBuildPath = Path.Combine(release, "ContentState");
        settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    private static void VerifyInputHashes(JObject inputs)
    {
        foreach (JObject entry in (JArray)inputs["entries"])
            Require(ResourceReleaseFiles.Hash((string)entry["path"]) == (string)entry["sourceSha256"], "Root source changed: " + entry["path"]);
        foreach (JProperty dependency in ((JObject)inputs["dependencyHashes"]).Properties())
            Require(AssetDatabase.GetAssetDependencyHash(dependency.Name).ToString() == (string)dependency.Value, "Imported dependencies changed: " + dependency.Name);
    }
    private static void VerifyProtected(JObject hashes)
    {
        if (hashes == null) return; // Menu builds still capture all explicit-root hashes.
        foreach (JProperty file in hashes.Properties())
            Require(ResourceReleaseFiles.Hash(ProjectPath(file.Name)) == (string)file.Value, "Protected file changed: " + file.Name);
    }
    private static string[] DirtyScenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .Where(scene => scene.isDirty).Select(scene => scene.path).ToArray();
    public static string ProjectPath(string path) => ResourceReleaseFiles.Inside(Directory.GetCurrentDirectory(), path);
    public static JObject Record(string path) => new JObject { ["path"] = Path.GetFullPath(path), ["bytes"] = new FileInfo(path).Length, ["sha256"] = ResourceReleaseFiles.Hash(path) };
    private static string Output(string id) => "Build/ResourceMigrationStage4/" + id;
    private static void Status(string id, string state, JObject details = null)
    {
        var data = details ?? new JObject(); data["state"] = state; data["utc"] = DateTime.UtcNow.ToString("o");
        WriteFile(Output(id) + "/status.json", data);
    }
    private static void SaveReport(string id, JObject report) => WriteFile(Output(id) + "/report.json", report);
    public static void WriteFile(string path, JObject data)
    {
        path = ProjectPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, data.ToString(Formatting.Indented));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
