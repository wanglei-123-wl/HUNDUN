using System.Collections.Generic;
using ServerMapBaking.Editor.Collectors;

namespace ServerMapBaking.Editor
{
    internal sealed class ServerMapBakeResult
    {
        public ServerMapData Data;
        public ServerMapExportPaths Paths;
        public string ReportPath = string.Empty;
        public readonly List<string> Warnings = new List<string>();
    }

    internal static class ServerMapBaker
    {
        public static ServerMapBakeResult Bake(ServerMapRoot root, string outputFolder)
        {
            ServerMapBakeResult result = new ServerMapBakeResult();
            ServerMapData data = new ServerMapData();
            data.Manifest.sceneId = root.SceneId ?? string.Empty;
            data.Manifest.mapId = root.MapId ?? string.Empty;
            data.Manifest.coordinateSystem = ServerMapFormat.CoordinateSystem;
            data.Manifest.heightFile = ServerMapFormat.HeightFileName;
            data.Manifest.walkableFile = ServerMapFormat.WalkableFileName;
            data.Manifest.colliderFile = ServerMapFormat.ColliderFileName;
            data.Manifest.navMeshFile = ServerMapFormat.NavMeshFileName;
            data.Manifest.regionsFile = null;

            TerrainDataCollector.Collect(root, data, result.Warnings);
            ColliderCollector.Collect(root, data);
            NavMeshCollector.Collect(root, data);

            result.Data = data;
            result.Paths = ServerMapBinaryWriter.Write(data, outputFolder);
            result.ReportPath = ServerMapReportWriter.Write(result);
            return result;
        }
    }
}
