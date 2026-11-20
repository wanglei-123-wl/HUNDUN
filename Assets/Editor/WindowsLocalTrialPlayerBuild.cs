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

// Builds only after an explicit menu, batch or expiring queued request.
[InitializeOnLoad]
public static class WindowsLocalTrialPlayerBuild
{
    public const string ReportPath = "Docs/WindowsLocalTrialPlayerBuild.json";
    private const string RequestPath = "Temp/WindowsLocalTrialPlayerBuild.request.json";
    private const string ContentReportPath = "Docs/PlayerMeditationContentBuild.json";
    private static bool building;
    private static double nextPoll;

    static WindowsLocalTrialPlayerBuild()
    {
        if (!Application.isBatchMode) EditorApplication.update += PollRequest;
    }

    [MenuItem("Tools/Resource Update/Build Windows Local Trial Player")]
    public static void BuildFromMenu() => Build(Guid.NewGuid().ToString("N"));

    public static void BuildBatch()
    {
        bool success = Build(Guid.NewGuid().ToString("N"));
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }

    private static void PollRequest()
    {
        if (building || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode) return;

        string claimed = null;
        string id = null;
        bool ownsRequest = false;
        try
        {
            JObject request = JObject.Parse(File.ReadAllText(RequestPath));
            id = (string)request["requestId"];
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid player build request id.");
            claimed = RequestPath + "." + id + ".running";
            File.Move(RequestPath, claimed);
            ownsRequest = true;
            string expectedRoot = Path.GetFullPath((string)request["projectRoot"] ?? "");
            double remaining = (request.Value<DateTime>("expiresAtUtc").ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
            if (!string.Equals(expectedRoot.TrimEnd('\\', '/'), Directory.GetCurrentDirectory().TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase) || remaining <= 0 || remaining > 30)
                throw new InvalidDataException("Player build request expired or belongs to another project.");
            Build(id);
        }
        catch (Exception error)
        {
            Debug.LogError("[WindowsPlayerBuild] Request was not executed: " + error);
            if (ownsRequest)
                WriteReport(new JObject { ["requestId"] = id, ["status"] = "request_rejected",
                    ["finishedAtUtc"] = DateTime.UtcNow.ToString("O"), ["error"] = error.ToString(),
                    ["testsRun"] = false, ["playerLaunched"] = false, ["published"] = false });
        }
        finally
        {
            if (ownsRequest && File.Exists(claimed)) File.Delete(claimed);
        }
    }

    private static bool Build(string requestId)
    {
        if (building) return false;
        building = true;
        var report = new JObject
        {
            ["scope"] = "windows-local-trial-player",
            ["requestId"] = requestId,
            ["status"] = "building",
            ["startedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["testsRun"] = false,
            ["playerLaunched"] = false,
            ["published"] = false
        };
        try
        {
            WriteReport(report);
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Stop Play mode and wait for the current build before building the Player.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64
                || !BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("The active target must be the installed Windows 64-bit platform.");

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || settings.profileSettings.GetProfileName(settings.activeProfileId) != "LocalTrial"
                || settings.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer)
                throw new InvalidOperationException("Use the existing LocalTrial profile and previously built Addressables content.");

            JObject content = JObject.Parse(File.ReadAllText(ContentReportPath));
            if ((string)content["status"] != "succeeded" || (string)content["profile"] != "LocalTrial"
                || (string)content["buildTarget"] != BuildTarget.StandaloneWindows64.ToString())
                throw new InvalidDataException("A successful matching meditation content build is required.");
            VerifyRecord((JObject)content["settingsAsset"]);
            VerifyRecord((JObject)content["catalog"]);
            VerifyRecord((JObject)content["catalogHashFile"]);
            VerifyRecord((JObject)content["runtimeSettings"]);
            foreach (JObject source in (JArray)content["sourceFiles"]) VerifyRecord(source);
            JObject world = (JObject)content["worldScene"];
            if (Hash(RequireProjectPath((string)world["source"])) != (string)world["sourceSha256"])
                throw new InvalidDataException("The world scene changed after the content build.");
            foreach (JObject dependency in (JArray)world["dependencies"]) VerifyRecord(dependency);

            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length != 1 || scenes[0] != "Assets/Scenes/启动场景.unity")
                throw new InvalidOperationException("The existing startup scene must be the only Player scene.");

            string outputDirectory = RequireProjectPath("Build/WindowsLocalTrial/MeditationEffect-" + requestId);
            if (Directory.Exists(outputDirectory)) throw new IOException("Refusing to overwrite an existing Player output.");
            Directory.CreateDirectory(outputDirectory);
            string executable = Path.Combine(outputDirectory, "HUNDUN.exe");
            report["outputDirectory"] = outputDirectory;
            report["executablePath"] = executable;
            report["profile"] = "LocalTrial";
            report["buildTarget"] = BuildTarget.StandaloneWindows64.ToString();
            report["scriptingBackend"] = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone).ToString();
            report["scenes"] = new JArray(scenes);
            report["contentReport"] = FileRecord(RequireProjectPath(ContentReportPath));
            report["remoteContentDirectory"] = Path.GetDirectoryName((string)content["catalog"]["path"]);
            report["remoteCatalogLoadPath"] = settings.RemoteCatalogLoadPath.GetValue(settings);
            WriteReport(report);

            BuildReport result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = executable,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.DetailedBuildReport
            });
            if (result == null) throw new InvalidOperationException("The Windows build returned no report.");
            report["nativeResult"] = result.summary.result.ToString();
            report["totalErrors"] = result.summary.totalErrors;
            report["totalWarnings"] = result.summary.totalWarnings;
            report["totalSize"] = result.summary.totalSize;
            report["durationSeconds"] = result.summary.totalTime.TotalSeconds;
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows Player build failed: " + result.summary.result);

            string dataDirectory = Path.Combine(outputDirectory, "HUNDUN_Data");
            string playerRuntimeDirectory = Path.Combine(dataDirectory, "StreamingAssets/aa");
            string playerRuntimeSettings = Path.Combine(playerRuntimeDirectory, "settings.json");
            if (Hash(playerRuntimeSettings) != (string)content["runtimeSettings"]["sha256"])
                throw new InvalidDataException("The Player did not include the verified Addressables settings.");
            string sourceCatalog = Path.Combine(Path.GetDirectoryName((string)content["runtimeSettings"]["path"]), "catalog.json");
            string playerCatalog = Path.Combine(playerRuntimeDirectory, "catalog.json");
            if (Hash(playerCatalog) != Hash(sourceCatalog))
                throw new InvalidDataException("The Player did not include the latest local content catalog.");
            const string runtimePrefix = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}";
            var localDependencies = new JArray();
            foreach (JObject dependency in (JArray)world["dependencies"])
            {
                string internalId = (string)dependency["internalId"];
                if (!internalId.StartsWith(runtimePrefix, StringComparison.Ordinal)) continue;
                string path = Path.Combine(playerRuntimeDirectory, internalId.Substring(runtimePrefix.Length).TrimStart('\\', '/'));
                if (Hash(path) != (string)dependency["sha256"])
                    throw new InvalidDataException("The Player is missing a verified local world dependency: " + internalId);
                localDependencies.Add(FileRecord(path));
            }
            report["executable"] = FileRecord(executable);
            report["playerRuntimeSettings"] = FileRecord(playerRuntimeSettings);
            report["playerCatalog"] = FileRecord(playerCatalog);
            report["localWorldDependencies"] = localDependencies;
            string assembly = Path.Combine(dataDirectory, "Managed/Assembly-CSharp.dll");
            if (File.Exists(assembly)) report["gameAssembly"] = FileRecord(assembly);
            report["status"] = "succeeded";
            Debug.Log("[WindowsPlayerBuild] Player build and packaged content checks succeeded: " + executable);
            return true;
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            Debug.LogError("[WindowsPlayerBuild] " + error);
            return false;
        }
        finally
        {
            report["finishedAtUtc"] = DateTime.UtcNow.ToString("O");
            try { WriteReport(report); }
            finally { building = false; }
        }
    }

    private static void VerifyRecord(JObject record)
    {
        if (record == null || Hash(RequireProjectPath((string)record["path"])) != (string)record["sha256"])
            throw new InvalidDataException("Built content or source changed: " + record?["path"]);
    }

    private static string RequireProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("A build path is empty.");
        string full = Path.GetFullPath(path);
        string root = Directory.GetCurrentDirectory().TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Player build paths must stay inside the project: " + full);
        return full;
    }

    private static void WriteReport(JObject report) => File.WriteAllText(ReportPath, report.ToString(Formatting.Indented));
    private static JObject FileRecord(string path) => new JObject
        { ["path"] = path, ["bytes"] = new FileInfo(path).Length, ["sha256"] = Hash(path) };
    private static string Hash(string path)
    {
        using var algorithm = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
