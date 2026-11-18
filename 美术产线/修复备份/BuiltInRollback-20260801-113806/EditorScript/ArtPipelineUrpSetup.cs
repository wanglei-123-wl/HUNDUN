using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ArtPipelineUrpSetup
{
    private const string SettingsRoot = "Assets/美术产线/已验收资产/_PipelineSettings";
    private const string RendererPath = SettingsRoot + "/ArtPipelineRenderer.asset";
    private const string PipelinePath = SettingsRoot + "/ArtPipelineURP.asset";
    private const string GeneratedGlobalSettingsPath = "Assets/UniversalRenderPipelineGlobalSettings.asset";
    private const string ManagedGlobalSettingsPath = SettingsRoot + "/UniversalRenderPipelineGlobalSettings.asset";

    [MenuItem("美术产线/配置 URP 第一版")]
    public static void ConfigureFromMenu()
    {
        Configure(false);
    }

    public static void ConfigureFromCommandLine()
    {
        Configure(true);
    }

    private static void Configure(bool commandLine)
    {
        EnsureFolder(SettingsRoot);

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, RendererPath);
        }

        var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipelineAsset == null)
        {
            pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipelineAsset, PipelinePath);
        }

        GraphicsSettings.defaultRenderPipeline = pipelineAsset;
        for (var index = 0; index < QualitySettings.names.Length; index++)
        {
            QualitySettings.SetQualityLevel(index, false);
            QualitySettings.renderPipeline = pipelineAsset;
        }

        MoveGeneratedGlobalSettings();

        EditorUtility.SetDirty(pipelineAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"ART_PIPELINE_URP_READY={PipelinePath}");

        if (!commandLine)
        {
            EditorUtility.DisplayDialog("美术产线", "URP 第一版配置完成。现有场景和材质未自动转换。", "确定");
        }
    }

    private static void MoveGeneratedGlobalSettings()
    {
        if (AssetDatabase.LoadMainAssetAtPath(GeneratedGlobalSettingsPath) == null ||
            AssetDatabase.LoadMainAssetAtPath(ManagedGlobalSettingsPath) != null)
        {
            return;
        }
        var error = AssetDatabase.MoveAsset(GeneratedGlobalSettingsPath, ManagedGlobalSettingsPath);
        if (!string.IsNullOrEmpty(error))
        {
            throw new InvalidOperationException($"Failed to move URP global settings: {error}");
        }
    }

    private static void EnsureFolder(string assetPath)
    {
        var segments = assetPath.Split('/');
        var current = segments[0];
        for (var index = 1; index < segments.Length; index++)
        {
            var next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }
            current = next;
        }
    }
}
