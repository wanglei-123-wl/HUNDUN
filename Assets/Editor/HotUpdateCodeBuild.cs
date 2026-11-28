using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using QiDao.Bootstrap.CodeUpdate;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class HotUpdateCodeBuild : IFilterBuildAssemblies, IPostprocessBuildWithReport, IPreprocessBuildWithReport
{
    public int callbackOrder => 10000;
    public static bool IsIsolatedAcceptanceBuild { get; private set; }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64
            || PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone) != ScriptingImplementation.Mono2x
            || PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup.Standalone) != ManagedStrippingLevel.Disabled
            || PlayerSettings.stripEngineCode)
            throw new BuildFailedException("The code-update base requires Windows Mono with managed and engine stripping disabled.");
    }
    public string[] OnFilterAssemblies(BuildOptions options, string[] assemblies)
    {
        return assemblies.Where(p => Path.GetFileNameWithoutExtension(p) != CodeUpdateProtocol.AssemblyName).ToArray();
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64
            || PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone) != ScriptingImplementation.Mono2x)
            throw new BuildFailedException("The hot-update base currently targets Windows x64 Mono only.");
        string data = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), Path.GetFileNameWithoutExtension(report.summary.outputPath) + "_Data");
        if (File.Exists(Path.Combine(data, "Managed", CodeUpdateProtocol.AssemblyName + ".dll")))
            throw new BuildFailedException("Game code must be downloaded, not shipped as a preloaded managed DLL.");
        HotUpdateBootstrapCatalog.MakeEmpty(Path.Combine(data, "StreamingAssets", "aa"));
        // Native asset deserialization needs the stable game assembly name in the
        // scripting list even though the DLL is loaded later from verified bytes.
        string path = Path.Combine(data, "ScriptingAssemblies.json");
        var scripting = JObject.Parse(File.ReadAllText(path));
        var names = (JArray)scripting["names"];
        var types = (JArray)scripting["types"];
        if (!names.Any(n => (string)n == CodeUpdateProtocol.AssemblyName + ".dll"))
        {
            names.Add(CodeUpdateProtocol.AssemblyName + ".dll");
            types.Add(16);
            File.WriteAllText(path, scripting.ToString(Newtonsoft.Json.Formatting.None));
        }
        var bootstrapConfig = JsonUtility.FromJson<CodeUpdateConfiguration>(File.ReadAllText("Assets/Resources/Startup/HotUpdateBootstrap.json"));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "base-client-build.json"), new JObject
        {
            ["schemaVersion"] = 1, ["kind"] = "QiDaoBaseClientBuild", ["status"] = "built",
            ["baseBuildGuid"] = report.summary.guid.ToString(), ["unityVersion"] = Application.unityVersion,
            ["bootstrapApiVersion"] = bootstrapConfig.bootstrapApiVersion, ["target"] = CodeUpdateProtocol.Target,
            ["businessDllExcluded"] = true, ["acceptanceOnly"] = IsIsolatedAcceptanceBuild,
            ["publisherKeySha256"] = CodeUpdateProtocol.Hash(Encoding.UTF8.GetBytes(bootstrapConfig.publicKeyXml))
        }.ToString());
    }

    public static void BuildCodeRelease()
    {
        var request = JObject.Parse(File.ReadAllText(Argument("-hotupdate-request")));
        string output = Path.GetFullPath((string)request["outputDirectory"]);
        Directory.CreateDirectory(output);
        string variant = (string)request["probeVariant"];
        if (!string.IsNullOrEmpty(variant) && variant != "A" && variant != "B" && variant != "FAIL")
            throw new BuildFailedException("Unknown acceptance code variant.");
        if (string.IsNullOrEmpty(variant) && PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone)
            .Split(';').Any(d => d.Trim().StartsWith("QIDAO_HOTUPDATE_PROBE", StringComparison.Ordinal)))
            throw new BuildFailedException("Production game code must be compiled without acceptance scripting defines.");
        string[] defines = string.IsNullOrEmpty(variant) ? new string[0]
            : new[] { "QIDAO_HOTUPDATE_PROBE" }.Concat(variant == "B" ? new[] { "QIDAO_HOTUPDATE_PROBE_B" }
                : variant == "FAIL" ? new[] { "QIDAO_HOTUPDATE_PROBE_FAIL" } : new string[0]).ToArray();
        string compiled = Path.Combine(output, "Compiled");
        Directory.CreateDirectory(compiled);
        var result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
        {
            target = BuildTarget.StandaloneWindows64, group = BuildTargetGroup.Standalone,
            options = ScriptCompilationOptions.None, extraScriptingDefines = defines
        }, compiled);
        if (result.assemblies == null || result.assemblies.Count == 0) throw new BuildFailedException("Game code compilation failed.");
        string source = Path.Combine(compiled, CodeUpdateProtocol.AssemblyName + ".dll");
        byte[] bytes = File.ReadAllBytes(source);
        var config = JsonUtility.FromJson<CodeUpdateConfiguration>(File.ReadAllText("Assets/Resources/Startup/HotUpdateBootstrap.json"));
        var release = new CodeRelease
        {
            schemaVersion = 1, releaseId = (string)request["releaseId"] ?? Guid.NewGuid().ToString("N"),
            sequence = request.Value<long>("sequence"), target = CodeUpdateProtocol.Target,
            unityVersion = Application.unityVersion, bootstrapApiVersion = config.bootstrapApiVersion,
            assemblyName = CodeUpdateProtocol.AssemblyName, entryType = CodeUpdateProtocol.EntryType,
            codeBytes = bytes.LongLength, codeSha256 = CodeUpdateProtocol.Hash(bytes),
            resourceReleaseId = (string)request["resourceReleaseId"], resourceCatalogHash = (string)request["resourceCatalogHash"]
        };
        release.scriptTypeHash = result.typeDB.GetHash128().ToString();
        bool paired = false;
        if (request["resourceBuildReport"] != null)
        {
            var resource = JObject.Parse(File.ReadAllText((string)request["resourceBuildReport"]));
            if ((string)resource["status"] != "succeeded" || !resource.Value<bool>("allBundlesNativeCrcVerified")
                || !resource.Value<bool>("assetDependencyOrderVerified")
                || (string)resource["scriptTypeHash"] != release.scriptTypeHash
                || ResourceReleaseFiles.Hash((string)resource["catalogPath"]) != (string)resource["resourceCatalogSha256"]
                || ResourceReleaseFiles.Hash((string)resource["resourceManifestPath"]) != (string)resource["resourceManifestSha256"])
                throw new BuildFailedException("Resource schema or verified resource report does not match this game code.");
            release.resourceReleaseId = (string)resource["resourceReleaseId"];
            release.resourceBuildRoot = (string)resource["resourceBuildRoot"];
            release.resourceCatalogHash = (string)resource["resourceCatalogHash"];
            release.resourceCatalogSha256 = (string)resource["resourceCatalogSha256"];
            release.resourceCatalogBytes = resource.Value<long>("resourceCatalogBytes");
            release.resourceManifestSha256 = (string)resource["resourceManifestSha256"];
            release.resourceManifestBytes = resource.Value<long>("resourceManifestBytes");
            paired = true;
        }
        release.codePath = "releases/" + release.releaseId + "/" + CodeUpdateProtocol.AssemblyName + ".dll.bytes";
        CodeUpdateProtocol.Validate(release, config);
        byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(release));
        var envelope = new SignedCodeEnvelope { payload = Convert.ToBase64String(payload) };
        using (var rsa = RSA.Create())
        {
            rsa.FromXmlString(File.ReadAllText((string)request["signingKeyPath"]));
            envelope.signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        CodeUpdateProtocol.VerifyEnvelope(envelope, config.publicKeyXml);
        string destination = Path.Combine(output, release.codePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        if (File.Exists(destination) && CodeUpdateProtocol.Hash(File.ReadAllBytes(destination)) != release.codeSha256)
            throw new BuildFailedException("A code release ID is immutable; choose a new ID for changed code.");
        File.WriteAllBytes(destination, bytes);
        File.WriteAllText(Path.Combine(output, "current.json"), JsonUtility.ToJson(envelope));
        File.WriteAllText(Path.Combine(output, "code-build-report.json"), new JObject
        {
            ["status"] = "succeeded", ["releaseId"] = release.releaseId, ["sequence"] = release.sequence,
            ["codeSha256"] = release.codeSha256, ["codeBytes"] = release.codeBytes, ["probeVariant"] = variant,
            ["unityVersion"] = Application.unityVersion, ["resourceCompatibilityVerified"] = false
        }.ToString());
        var buildReport = JObject.Parse(File.ReadAllText(Path.Combine(output, "code-build-report.json")));
        buildReport["scriptTypeHash"] = release.scriptTypeHash;
        buildReport["resourceCompatibilityVerified"] = paired;
        buildReport["resourceReleaseId"] = release.resourceReleaseId;
        File.WriteAllText(Path.Combine(output, "code-build-report.json"), buildReport.ToString());
    }

    public static void BuildAcceptanceBase()
    {
        string isolatedAssets = Path.GetFullPath(@"C:\QiDaoBuild\HotUpdateMigration-Stage2\Assets");
        if (!string.Equals(Path.GetFullPath(Application.dataPath), isolatedAssets, StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("Acceptance fixtures may only be built inside the isolated code validation project.");
        var request = JObject.Parse(File.ReadAllText(Argument("-hotupdate-request")));
        string output = Path.GetFullPath((string)request["outputDirectory"]);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory("Assets/HotUpdateAcceptance/Editor");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Add the conditional probe component using the explicit compiler define.
        // A second invocation after define import builds this same scene.
        Type runner = typeof(StartupContentUpdate).Assembly.GetType("QiDao.Bootstrap.CodeUpdate.HotUpdateCodeAcceptanceRunner");
        if (runner == null) throw new BuildFailedException("Acceptance base requires the QIDAO_HOTUPDATE_PROBE scripting define.");
        new GameObject("CodeAcceptance").AddComponent(runner);
        string scenePath = "Assets/HotUpdateAcceptance/CodeAcceptance.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        var prefab = new GameObject("HotCodeProbePrefab");
        prefab.AddComponent<QiDao.GameLogic.HotCodeProbe>();
        PrefabUtility.SaveAsPrefabAsset(prefab, "Assets/HotUpdateAcceptance/HotCodeProbePrefab.prefab");
        UnityEngine.Object.DestroyImmediate(prefab);
        var data = ScriptableObject.CreateInstance<QiDao.GameLogic.HotCodeProbeData>();
        if (AssetDatabase.LoadMainAssetAtPath("Assets/HotUpdateAcceptance/HotCodeProbeData.asset") != null)
            AssetDatabase.DeleteAsset("Assets/HotUpdateAcceptance/HotCodeProbeData.asset");
        AssetDatabase.CreateAsset(data, "Assets/HotUpdateAcceptance/HotCodeProbeData.asset");
        AssetDatabase.SaveAssets();
        string bundleOutput = Path.Combine(output, "ProbeBundle");
        Directory.CreateDirectory(bundleOutput);
        var bundle = BuildPipeline.BuildAssetBundles(bundleOutput, new[] { new AssetBundleBuild
        {
            assetBundleName = "hotcodeprobe.bundle", assetNames = new[]
            { "Assets/HotUpdateAcceptance/HotCodeProbePrefab.prefab", "Assets/HotUpdateAcceptance/HotCodeProbeData.asset" }
        } }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        if (bundle == null) throw new BuildFailedException("Acceptance bundle build failed.");
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        PlayerSettings.stripEngineCode = false;
        string executable = Path.Combine(output, "BasePlayer", "HotUpdateAcceptance.exe");
        BuildReport report;
        IsIsolatedAcceptanceBuild = true;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath }, locationPathName = executable,
                target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None, extraScriptingDefines = new[] { "QIDAO_HOTUPDATE_PROBE" }
            });
        }
        finally { IsIsolatedAcceptanceBuild = false; }
        if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Acceptance base build failed.");
        File.WriteAllText(Path.Combine(output, "base-build-report.json"), new JObject
        {
            ["status"] = "succeeded", ["executable"] = executable, ["gameDllPreloaded"] = false,
            ["nativeBuildResult"] = report.summary.result.ToString(), ["unityVersion"] = Application.unityVersion
        }.ToString());
    }

    public static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing " + name);
        return args[index + 1];
    }
}
