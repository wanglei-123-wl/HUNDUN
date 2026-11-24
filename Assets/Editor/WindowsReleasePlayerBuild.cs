using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

// A request is consumed only after explicit authorization, while the editor is idle.
[InitializeOnLoad]
public static class WindowsReleasePlayerBuild
{
    private const string RequestPath = "Temp/WindowsReleasePlayerBuild.request.json";
    private const string StartupScene = "Assets/Scenes/启动场景.unity";
    private const string RemoteBase = "http://47.93.78.106:18761/StandaloneWindows64";
    private static bool running;
    private static double nextPoll;

    static WindowsReleasePlayerBuild()
    {
        if (!Application.isBatchMode) EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        JObject request = null;
        string claimed = null;
        try
        {
            request = JObject.Parse(File.ReadAllText(RequestPath));
            string id = (string)request["requestId"];
            Require(Guid.TryParseExact(id, "N", out _), "Invalid build request ID.");
            Require(SamePath((string)request["projectRoot"], Directory.GetCurrentDirectory()), "Wrong project.");
            double remaining = (request.Value<DateTime>("expiresAtUtc").ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
            Require(remaining > 0 && remaining <= 30, "Build request expired.");
            if (Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(scene => scene.isDirty))
            {
                WriteStatus(request, "waiting_for_saved_scenes");
                return;
            }
            claimed = RequestPath + "." + id + ".running";
            File.Move(RequestPath, claimed);
            running = true;
            Build(request);
        }
        catch (Exception error)
        {
            Debug.LogError("[WindowsReleasePlayerBuild] " + error);
        }
        finally
        {
            if (claimed != null && File.Exists(claimed)) File.Delete(claimed);
            running = false;
        }
    }

    private static void Build(JObject request)
    {
        var report = new JObject
        {
            ["requestId"] = (string)request["requestId"], ["status"] = "preparing",
            ["startedAtUtc"] = DateTime.UtcNow.ToString("O"), ["productName"] = "齐道",
            ["releaseId"] = (string)request["releaseId"], ["buildTarget"] = "StandaloneWindows64",
            ["playerLaunched"] = false, ["installerBuilt"] = false
        };
        try
        {
            SaveReport(request, report);
            Require(EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64
                && BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64),
                "Windows 64-bit build support must be installed and active.");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            Require(settings != null && settings.profileSettings.GetProfileName(settings.activeProfileId) == "Release"
                && settings.BuildAddressablesWithPlayerBuild == AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer,
                "The verified Release profile and existing Addressables data are required.");
            Require(settings.RemoteCatalogLoadPath.GetValue(settings).TrimEnd('/') == RemoteBase,
                "Release resource address changed.");
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            Require(scenes.Length == 1 && scenes[0] == StartupScene, "Only the startup scene may be built into this client.");
            foreach (JObject source in (JArray)request["protectedFiles"]) VerifyRecord(source);
            string release = Path.GetFullPath((string)request["releaseDirectory"]);
            var inputs = JObject.Parse(File.ReadAllText(Path.Combine(release, "inputs.json")));
            foreach (JObject entry in (JArray)inputs["entries"])
                Require(Hash((string)entry["path"]) == (string)entry["sourceSha256"], "Remote root changed: " + entry["path"]);
            foreach (JProperty dependency in ((JObject)inputs["dependencyHashes"]).Properties())
                Require(AssetDatabase.GetAssetDependencyHash(dependency.Name).ToString() == (string)dependency.Value,
                    "Imported remote dependency changed: " + dependency.Name);
            bool stagedResources = request.Value<bool>("allowStagedResources");
            string receiptName = stagedResources ? "staged-upload-success.json" : "upload-success.json";
            var receipt = JObject.Parse(File.ReadAllText(Path.Combine(release, receiptName)));
            Require((string)receipt["releaseId"] == (string)request["releaseId"]
                && (stagedResources
                    ? (string)receipt["status"] == "staged" && receipt.Value<bool>("allRemoteFileHashesVerified")
                    : receipt.Value<bool>("publicHeadersAndCatalogHashesVerified")
                        && receipt.Value<bool>("allRemoteFileHashesVerifiedByServer")), "Successful resource upload receipt missing.");
            if (stagedResources)
            {
                var startup = JObject.Parse(File.ReadAllText(Path.Combine(release, "startup-download-verification.json")));
                Require((string)startup["status"] == "succeeded" && startup.Value<bool>("allPublishedBundlesIncluded")
                    && startup.Value<bool>("allNewBackgroundsIncluded")
                    && Hash(Path.Combine(release, "release-manifest.json")) == (string)startup["manifestSha256"]
                    && Hash((string)startup["nativeCrcVerification"]) == (string)startup["nativeCrcVerificationSha256"],
                    "Staged resources require matching manifest, startup download and native CRC proofs.");
                var nativeCheck = JObject.Parse(File.ReadAllText((string)startup["nativeCrcVerification"]));
                Require((string)nativeCheck["status"] == "succeeded"
                    && ((JArray)nativeCheck["bundles"]).All(item => item.Value<bool>("accepted")), "Native resource CRC validation failed.");
            }
            report["resourcePublicationState"] = stagedResources ? "staged" : "published";
            report["publicResourcesChanged"] = false;

            // Reuse the saved open scene, or inspect it additively and close only our copy.
            Scene preview = SceneManager.GetSceneByPath(StartupScene);
            bool openedScene = !preview.IsValid() || !preview.isLoaded;
            if (openedScene) preview = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                StartupScene, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var endpoints = preview.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<StartupResourceServerSettings>(true)).ToArray();
                Require(endpoints.Length == 1 && endpoints[0].TryGetSelectedAddress(out string address, out _)
                    && address?.TrimEnd('/') == RemoteBase, "Startup must select the published cloud resource server.");
            }
            finally { if (openedScene) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(preview, true); }

            string iconPath = (string)request["iconPath"];
            Require(Hash(iconPath) == (string)request["iconSha256"], "The selected icon changed.");
            var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            Require(importer != null, "Icon was not imported as a texture.");
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var desktopImport = importer.GetPlatformTextureSettings("Standalone");
            desktopImport.overridden = false;
            importer.SetPlatformTextureSettings(desktopImport);
            importer.SaveAndReimport();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            Require(icon != null, "Cannot load the game icon.");
            PlayerSettings.productName = "齐道";
            SetIcons(BuildTargetGroup.Unknown, icon);
            SetIcons(BuildTargetGroup.Standalone, icon);
            AssetDatabase.SaveAssets();
            Require(PlayerSettings.productName == "齐道", "Product name was not saved.");
            report["icon"] = Record(iconPath);
            report["iconDimensions"] = new JArray(icon.width, icon.height);
            report["scriptingBackend"] = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone).ToString();
            report["resourceServer"] = RemoteBase;
            report["scenes"] = new JArray(scenes);

            string staging = Path.GetFullPath((string)request["stagingDirectory"]);
            string destination = Path.GetFullPath((string)request["outputDirectory"]);
            Require(SamePath(Path.GetDirectoryName(destination), @"F:\游戏项目")
                && Path.GetFileName(destination).StartsWith("齐道客户端_", StringComparison.Ordinal), "Unexpected final output path.");
            Require(!Directory.Exists(staging) && !File.Exists(staging)
                && !Directory.Exists(destination) && !File.Exists(destination), "Refusing to overwrite an existing client.");
            foreach (JObject file in (JArray)request["playerContentFiles"])
            {
                VerifyRecord(file);
                string cached = Inside(Path.GetFullPath("Library/com.unity.addressables/aa/Windows"), (string)file["relativePath"]);
                Require(Hash(cached) == (string)file["sha256"], "Current Addressables cache changed: " + cached);
            }
            Directory.CreateDirectory(staging);
            string executable = Path.Combine(staging, "齐道.exe");
            report["stagingDirectory"] = staging;
            report["outputDirectory"] = destination;
            report["status"] = "building";
            SaveReport(request, report);
            WriteStatus(request, "building");
            BuildReport native = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = executable, target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.DetailedBuildReport
            });
            Require(native != null, "Unity returned no build report.");
            report["nativeResult"] = native.summary.result.ToString();
            report["totalErrors"] = native.summary.totalErrors;
            report["totalWarnings"] = native.summary.totalWarnings;
            report["nativeTotalBytes"] = native.summary.totalSize;
            report["durationSeconds"] = native.summary.totalTime.TotalSeconds;
            report["nativeWarningsAndErrors"] = new JArray(native.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Warning || message.type == LogType.Error || message.type == LogType.Exception)
                .Select(message => new JObject { ["type"] = message.type.ToString(), ["message"] = message.content }));
            Require(native.summary.result == BuildResult.Succeeded, "Windows client build failed: " + native.summary.result);
            Require(File.Exists(executable) && File.Exists(Path.Combine(staging, "UnityPlayer.dll")), "Built client is incomplete.");
            string runtime = Path.Combine(staging, "齐道_Data/StreamingAssets/aa");
            var packaged = new JArray();
            foreach (JObject file in (JArray)request["playerContentFiles"])
            {
                string path = Inside(runtime, (string)file["relativePath"]);
                // AddressablesLink is consumed by the linker rather than shipped.
                if (((string)file["relativePath"]).StartsWith("AddressablesLink/", StringComparison.Ordinal)) continue;
                Require(Hash(path) == (string)file["sha256"], "Packaged Addressables file differs from the release: " + path);
                packaged.Add(Record(path));
            }
            Require(!Directory.GetFiles(staging, "*.bundle", SearchOption.AllDirectories)
                .Any(path => Path.GetFileName(path).StartsWith("remote", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(path).StartsWith("bootstrapshared", StringComparison.OrdinalIgnoreCase)),
                "Remote art or the old bootstrap font was unexpectedly bundled into the client.");
            foreach (JObject source in (JArray)request["protectedFiles"]) VerifyRecord(source);
            report["packagedAddressables"] = packaged;
            WriteStatus(request, "copying_and_verifying");
            var delivered = new JArray();
            var excludedDebugFiles = new JArray();
            Directory.CreateDirectory(destination);
            foreach (string source in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                string relative = source.Substring(staging.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
                string topLevel = relative.Split('/')[0];
                if (topLevel.Contains("_BackUpThisFolder_ButDontShipItWithYourGame")
                    || topLevel.Contains("_BurstDebugInformation_DoNotShip"))
                {
                    excludedDebugFiles.Add(relative);
                    continue;
                }
                string target = Inside(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string expected = Hash(source);
                File.Copy(source, target, false);
                Require(Hash(target) == expected && Hash(target) == expected, "Copied client hash mismatch: " + relative);
                var file = Record(target);
                file["relativePath"] = relative;
                delivered.Add(file);
            }
            report["files"] = delivered;
            report["excludedDebugFiles"] = excludedDebugFiles;
            report["executablePath"] = Path.Combine(destination, "齐道.exe");
            report["status"] = "succeeded";
            Debug.Log("[WindowsReleasePlayerBuild] Verified client: " + report["executablePath"]);
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            Debug.LogError("[WindowsReleasePlayerBuild] " + error);
        }
        finally
        {
            report["finishedAtUtc"] = DateTime.UtcNow.ToString("O");
            SaveReport(request, report);
            WriteStatus(request, (string)report["status"]);
        }
    }

    private static void SetIcons(BuildTargetGroup target, Texture2D icon)
    {
        int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(target, IconKind.Any);
        Require(sizes.Length > 0, "Unity returned no icon slots: " + target);
        PlayerSettings.SetIconsForTargetGroup(target, sizes.Select(_ => icon).ToArray(), IconKind.Any);
        Require(PlayerSettings.GetIconsForTargetGroup(target, IconKind.Any).All(texture => texture == icon), "Icon assignment failed.");
    }

    private static void VerifyRecord(JObject record) => Require(record != null && File.Exists((string)record["path"])
        && Hash((string)record["path"]) == (string)record["sha256"], "Protected file changed: " + record?["path"]);
    private static JObject Record(string path) => new JObject
        { ["path"] = Path.GetFullPath(path), ["bytes"] = new FileInfo(path).Length, ["sha256"] = Hash(path) };
    private static string Hash(string path)
    {
        using var algorithm = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private static string Inside(string root, string relative)
    {
        root = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        Require(path.StartsWith(root, StringComparison.OrdinalIgnoreCase), "Path escapes its output directory.");
        return path;
    }
    private static bool SamePath(string left, string right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    private static void SaveReport(JObject request, JObject report)
    {
        string path = (string)request["reportPath"];
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, report.ToString(Formatting.Indented));
    }
    private static void WriteStatus(JObject request, string status) => SaveStatus((string)request["reportPath"] + ".status.json", status);
    private static void SaveStatus(string path, string status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, new JObject { ["status"] = status, ["utc"] = DateTime.UtcNow.ToString("O") }.ToString());
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
