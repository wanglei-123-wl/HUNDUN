using System;
using System.IO;
using Newtonsoft.Json.Linq;
using QiDao.Bootstrap.CodeUpdate;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HotUpdateResourceAcceptanceBuild
{
    public static void BuildFixtures()
    {
        RequireIsolated();
        var request = JObject.Parse(File.ReadAllText(HotUpdateCodeBuild.Argument("-hotupdate-request")));
        string output = (string)request["outputDirectory"];
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            settings = AddressableAssetSettings.Create("Assets/AddressableAssetsData", "AddressableAssetSettings", true, true);
            AddressableAssetSettingsDefaultObject.Settings = settings;
        }
        var group = settings.FindGroup("Stage3Fixture") ?? settings.CreateGroup("Stage3Fixture", false, false, false, null,
            typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        foreach (string name in new[] { "HotCodeProbePrefab.prefab", "HotCodeProbeData.asset" })
        {
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID("Assets/HotUpdateAcceptance/" + name), group);
            entry.address = name.EndsWith(".prefab") ? "fixture/prefab" : "fixture/data";
        }
        int marker = request.Value<int>("marker");
        var asset = AssetDatabase.LoadAssetAtPath<QiDao.GameLogic.HotCodeProbeData>("Assets/HotUpdateAcceptance/HotCodeProbeData.asset");
        asset.marker = marker;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        HotUpdateResourceBuild.BuildFromRequest();
    }

    public static void BuildBase()
    {
        RequireIsolated();
        var request = JObject.Parse(File.ReadAllText(HotUpdateCodeBuild.Argument("-hotupdate-request")));
        Type runner = typeof(StartupContentUpdate).Assembly.GetType("QiDao.Bootstrap.CodeUpdate.ResourceBindingAcceptanceRunner");
        if (runner == null) throw new BuildFailedException("Resource acceptance scripting define missing.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("PairedResourcesAcceptance").AddComponent(runner);
        const string path = "Assets/HotUpdateAcceptance/ResourceAcceptance.unity";
        EditorSceneManager.SaveScene(scene, path);
        PlayerSettings.productName = "QiDaoResourceAcceptance";
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        PlayerSettings.stripEngineCode = false;
        // Existing isolated-build scope prevents the production network exporter
        // from requiring a world scene in this deliberately empty fixture.
        typeof(HotUpdateCodeBuild).GetProperty("IsIsolatedAcceptanceBuild").GetSetMethod(true).Invoke(null, new object[] { true });
        BuildReport result;
        try
        {
            result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { path }, target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine((string)request["outputDirectory"], "PairedResourceAcceptance.exe"),
                options = BuildOptions.None, extraScriptingDefines = new[] { "QIDAO_HOTUPDATE_PROBE" }
            });
        }
        finally { typeof(HotUpdateCodeBuild).GetProperty("IsIsolatedAcceptanceBuild").GetSetMethod(true).Invoke(null, new object[] { false }); }
        if (result.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Resource acceptance base failed.");
    }

    private static void RequireIsolated()
    {
        if (!string.Equals(Path.GetFullPath(Application.dataPath), @"C:\QiDaoBuild\HotUpdateMigration-Stage2\Assets", StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("Resource fixture operation is restricted to the isolated acceptance project.");
    }
}
