using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ResourceReleaseRepairs
{
    private const string Request = "Temp/ResourceReleaseRepairs.request.json";
    private static double nextPoll;
    static ResourceReleaseRepairs() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string claimed = null, id = null;
        try
        {
            var request = JObject.Parse(File.ReadAllText(Request)); id = (string)request["requestId"];
            Require(Guid.TryParseExact(id, "N", out _), "Invalid repair ID.");
            double minutes = (request.Value<DateTime>("expiresAtUtc").ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
            Require(minutes > 0 && minutes <= 30 && Path.GetFullPath((string)request["projectRoot"]) == Directory.GetCurrentDirectory(), "Expired or wrong-project request.");
            Require(Enumerable.Range(0, SceneManager.sceneCount).All(index => !SceneManager.GetSceneAt(index).isDirty), "Save intended scene changes first.");
            claimed = Request + "." + id + ".running"; File.Move(Request, claimed);
            string original = ResourceReleaseBuildStage4.ProjectPath((string)request["reportPath"]);
            Require(ResourceReleaseFiles.Hash(original) == (string)request["reportSha256"], "Original build report changed.");
            var audit = (JObject)JObject.Parse(File.ReadAllText(original))["verification"];
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings.FindGroup("RemoteSharedGame");
            Require(group != null && !EditorUtility.IsDirty(group) && !EditorUtility.IsDirty(settings), "Review unsaved Addressables changes first.");
            var additions = new JArray();
            foreach (JObject duplicate in (JArray)audit["duplicatedAssets"])
            {
                string guid = (string)duplicate["guid"];
                long[] ids = ((JArray)duplicate["objects"]).Select(item => (long)item["localId"]).ToArray();
                if (ReleaseDuplicatePolicy.IsSmallMetadata(guid, ids, (ulong)duplicate["extraUncompressedObjectBytes"])) continue;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Require(!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal)
                    && path == (string)duplicate["path"] && File.Exists(path), "Invalid business duplicate: " + guid);
                var existing = settings.FindAssetEntry(guid);
                Require(existing == null || existing.parentGroup == group, "Duplicate already has a different explicit owner: " + path);
                var entry = existing ?? settings.CreateOrMoveEntry(guid, group);
                entry.address = "game/shared/" + guid;
                additions.Add(new JObject { ["guid"] = guid, ["path"] = path, ["address"] = entry.address,
                    ["sourceSha256"] = ResourceReleaseFiles.Hash(path) });
            }
            string materialPath = "Assets/Resources/Startup/Shaders/DynamicEffectUnlit.mat";
            if (!File.Exists(materialPath))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources/Startup/Shaders"))
                    AssetDatabase.CreateFolder("Assets/Resources/Startup", "Shaders");
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                Require(shader != null, "The dynamic effect's source shader is missing.");
                var material = new Material(shader) { name = "DynamicEffectUnlit" };
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            var keep = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Require(keep != null && keep.shader.name == "Universal Render Pipeline/Unlit", "Unexpected shader keep material.");
            EditorUtility.SetDirty(group); AssetDatabase.SaveAssetIfDirty(group);
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
            ResourceReleaseBuildStage4.WriteFile("Build/ResourceMigrationStage4/" + id + "/report.json", new JObject {
                ["status"] = "succeeded", ["action"] = "repair_actual_build_findings", ["addedSharedEntries"] = additions,
                ["keptLocalShaderMaterial"] = ResourceReleaseBuildStage4.Record(materialPath),
                ["shaderInclusionReference"] = "https://docs.unity3d.com/cn/2022.2/ScriptReference/Shader.Find.html",
                ["sourceArtMovedOrChanged"] = false, ["published"] = false });
            Debug.Log("[ResourceReleaseRepairs] Succeeded: " + id);
        }
        catch (Exception error)
        {
            Debug.LogError("[ResourceReleaseRepairs] " + error);
            if (Guid.TryParseExact(id, "N", out _)) ResourceReleaseBuildStage4.WriteFile("Build/ResourceMigrationStage4/" + id + "/report.json",
                new JObject { ["status"] = "failed", ["error"] = error.ToString() });
        }
        finally { if (claimed != null && File.Exists(claimed)) File.Delete(claimed); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
