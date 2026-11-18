using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class MapTeleporterPointsExporter
{
    [Serializable]
    private sealed class PointManifest
    {
        public int version = 1;
        public string sceneId;
        public string mapId;
        public Point[] points;
    }

    [Serializable]
    private sealed class Point
    {
        public string pointId;
        public float x;
        public float z;
    }

    [MenuItem("Tools/World/导出当前地图传送点")]
    private static void ExportActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var entry = settings?.FindAssetEntry(AssetDatabase.AssetPathToGUID(scene.path));
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty
            || !WorldMapSceneCatalog.TryGetMapByAddress(entry?.address, out string sceneId, out string mapId))
        {
            EditorUtility.DisplayDialog("导出传送点", "请退出播放模式，打开并保存已注册的游戏地图场景。", "确定");
            return;
        }

        string path = EditorUtility.SaveFilePanel("导出传送点清单", "", "teleporters", "json");
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            int count = Export(scene, sceneId, mapId, path);
            Debug.Log($"已导出 {mapId} 的 {count} 个传送点。请随该地图的服务端 MapData 发布 teleporters.json。");
        }
        catch (Exception error)
        {
            Debug.LogError($"传送点导出失败。{error.Message}");
        }
    }

    internal static int Export(Scene scene, string sceneId, string mapId, string path)
    {
        if (!scene.isLoaded || scene.isDirty || string.IsNullOrEmpty(scene.path))
            throw new InvalidOperationException("请先保存地图场景。");
        if (!string.Equals(Path.GetFileName(path), "teleporters.json", StringComparison.Ordinal))
            throw new InvalidOperationException("请使用文件名 teleporters.json。");

        string sceneGuid = AssetDatabase.AssetPathToGUID(scene.path);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var placements = new List<(VillageTeleporter Teleporter, Point Point)>();
        using (var hash = SHA256.Create())
        {
            foreach (var teleporter in scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<VillageTeleporter>())
                .Where(component => component.isActiveAndEnabled))
            {
                var globalId = GlobalObjectId.GetGlobalObjectIdSlow(teleporter);
                if (globalId.targetObjectId == 0 || globalId.assetGUID.ToString() != sceneGuid)
                    throw new InvalidOperationException("请保存场景中的传送员实例后重试。");
                byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(globalId.ToString()));
                string pointId = new Guid(digest.Take(16).ToArray()).ToString("N");
                if (!ids.Add(pointId))
                    throw new InvalidOperationException("存在重复的传送点 ID。");

                Vector3 position = teleporter.transform.position;
                if (float.IsNaN(position.x) || float.IsInfinity(position.x)
                    || float.IsNaN(position.z) || float.IsInfinity(position.z))
                    throw new InvalidOperationException("传送点坐标无效。");
                placements.Add((teleporter, new Point { pointId = pointId, x = position.x, z = position.z }));
            }
        }

        var manifest = new PointManifest
        {
            sceneId = sceneId,
            mapId = mapId,
            points = placements.Select(item => item.Point).OrderBy(point => point.pointId, StringComparer.Ordinal).ToArray()
        };
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(manifest, true) + Environment.NewLine, new UTF8Encoding(false));
            foreach (var item in placements.Where(item => item.Teleporter.SourcePointId != item.Point.pointId))
            {
                var serialized = new SerializedObject(item.Teleporter);
                serialized.FindProperty("sourcePointId").stringValue = item.Point.pointId;
                serialized.ApplyModifiedProperties();
                if (PrefabUtility.IsPartOfPrefabInstance(item.Teleporter))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.Teleporter);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene))
                throw new IOException("传送点 ID 未能保存到地图场景。");
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        return manifest.points.Length;
    }
}
