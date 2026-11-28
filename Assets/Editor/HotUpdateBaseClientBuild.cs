using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class HotUpdateBaseClientBuild
{
    public static void BuildFromRequest()
    {
        if (!Path.GetFullPath(Application.dataPath).StartsWith(@"C:\QiDaoBuild\", StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("Use the dedicated isolated build project.");
        var request=JObject.Parse(File.ReadAllText(HotUpdateCodeBuild.Argument("-hotupdate-request")));
        HotUpdateResourceWorkflow.VerifyInputs(request);
        if (PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone).Split(';')
            .Any(x=>x.Trim().StartsWith("QIDAO_HOTUPDATE_PROBE",StringComparison.Ordinal)))
            throw new BuildFailedException("The production client cannot include acceptance defines.");
        string output=Path.GetFullPath((string)request["outputDirectory"]);
        if (Directory.Exists(output) || File.Exists(output)) throw new BuildFailedException("Client output is immutable.");
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        if(settings==null) throw new BuildFailedException("Addressables settings are missing.");
        settings.BuildAddressablesWithPlayerBuild=AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone,ManagedStrippingLevel.Disabled);
        PlayerSettings.stripEngineCode=false;
        string[] scenes=EditorBuildSettings.scenes.Where(x=>x.enabled).Select(x=>x.path).ToArray();
        if(scenes.Length!=1 || scenes[0]!="Assets/Scenes/启动场景.unity") throw new BuildFailedException("Only the fixed startup scene belongs in the base client.");
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(output);
        var native=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=scenes,target=BuildTarget.StandaloneWindows64,
            targetGroup=BuildTargetGroup.Standalone,locationPathName=Path.Combine(output,"齐道.exe"),options=BuildOptions.DetailedBuildReport });
        if(native==null || native.summary.result!=BuildResult.Succeeded) throw new BuildFailedException("Base client build failed.");
        if(File.Exists(Path.Combine(output,"齐道_Data/Managed/QiDao.GameLogic.dll"))) throw new BuildFailedException("Business code was included in the fixed client.");
        if(Directory.GetFiles(output,"*.bundle",SearchOption.AllDirectories).Length!=0) throw new BuildFailedException("Game resource bundles were unexpectedly embedded.");
        var metadata=JObject.Parse(File.ReadAllText(Path.Combine(output,"base-client-build.json")));
        if(metadata.Value<bool>("acceptanceOnly")) throw new BuildFailedException("This is not a production base client.");
        File.WriteAllText((string)request["reportPath"],new JObject { ["status"]="succeeded",["appVersion"]=PlayerSettings.bundleVersion,
            ["outputDirectory"]=output,["executablePath"]=Path.Combine(output,"齐道.exe"),["nativeResult"]=native.summary.result.ToString(),
            ["totalErrors"]=native.summary.totalErrors,["totalWarnings"]=native.summary.totalWarnings,["baseBuildGuid"]=native.summary.guid.ToString(),
            ["businessDllExcluded"]=true,["remoteBundlesExcluded"]=true,["acceptanceOnly"]=false,
            ["gameplayTested"]=false,["resourceReleaseId"]=request["resourceReleaseId"],["codeReleaseId"]=request["codeReleaseId"] }.ToString());
    }
}
