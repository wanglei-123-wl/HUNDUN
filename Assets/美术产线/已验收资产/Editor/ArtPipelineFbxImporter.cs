using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ArtPipelineFbxImporter
{
    private const string ApprovedRoot = "Assets/美术产线/已验收资产";

    [MenuItem("美术产线/处理已验收 FBX")]
    public static void ProcessApprovedFbxFromMenu()
    {
        ProcessApprovedFbx();
    }

    public static void ProcessApprovedFbxFromCommandLine()
    {
        ProcessApprovedFbx();
    }

    private static void ProcessApprovedFbx()
    {
        var guids = AssetDatabase.FindAssets("t:Model", new[] { ApprovedRoot });
        var processed = 0;
        foreach (var guid in guids)
        {
            var fbxPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!fbxPath.EndsWith("_LODs.fbx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            ConfigureModelImporter(fbxPath);
            CreatePrefab(fbxPath);
            processed++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"ART_PIPELINE_FBX_PROCESSED={processed}");
    }

    private static void ConfigureModelImporter(string fbxPath)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"ModelImporter unavailable: {fbxPath}");
        }
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.isReadable = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.SaveAndReimport();
    }

    private static void CreatePrefab(string fbxPath)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (model == null)
        {
            throw new InvalidOperationException($"FBX load failed: {fbxPath}");
        }

        var directory = Path.GetDirectoryName(fbxPath)?.Replace('\\', '/');
        var assetName = Path.GetFileNameWithoutExtension(fbxPath).Replace("_LODs", string.Empty);
        var material = CreateMaterial(directory, assetName);
        var instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
        if (instance == null)
        {
            throw new InvalidOperationException($"FBX instantiate failed: {fbxPath}");
        }

        instance.name = assetName;
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
        }
        ConfigureLodGroup(instance);

        var prefabPath = $"{directory}/{assetName}.prefab";
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        UnityEngine.Object.DestroyImmediate(instance);
    }

    private static Material CreateMaterial(string directory, string assetName)
    {
        var materialPath = $"{directory}/{assetName}_URP.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP Lit shader is unavailable. URP setup must run first.");
            }
            material = new Material(shader) { name = assetName + "_URP" };
            AssetDatabase.CreateAsset(material, materialPath);
        }

        AssignTexture(material, "_BumpMap", $"{directory}/{assetName}_Normal.png", true);
        AssignTexture(material, "_OcclusionMap", $"{directory}/{assetName}_AO.png", false);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AssignTexture(Material material, string property, string texturePath, bool normalMap)
    {
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            return;
        }
        if (normalMap && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        else if (!normalMap && importer.sRGBTexture)
        {
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        material.SetTexture(property, AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        if (normalMap)
        {
            material.EnableKeyword("_NORMALMAP");
        }
    }

    private static void ConfigureLodGroup(GameObject root)
    {
        var levels = new[] { "LOD0", "LOD1", "LOD2" };
        var transitions = new[] { 0.6f, 0.3f, 0.1f };
        var lods = new List<LOD>();
        for (var index = 0; index < levels.Length; index++)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.gameObject.name.IndexOf(levels[index], StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            if (renderers.Length > 0)
            {
                lods.Add(new LOD(transitions[index], renderers));
            }
        }
        if (lods.Count == 0)
        {
            return;
        }
        var group = root.GetComponent<LODGroup>() ?? root.AddComponent<LODGroup>();
        group.SetLODs(lods.ToArray());
        group.RecalculateBounds();
    }
}
