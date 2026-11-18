using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ServerMapBaking.Editor
{
    internal static class ServerMapReportWriter
    {
        public static string Write(ServerMapBakeResult result)
        {
            string reportPath = Path.Combine(result.Paths.OutputDirectory, "bake.report.txt");
            ServerMapData data = result.Data;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Server Map Bake Report V1");
            builder.AppendLine("Scene ID: " + data.Manifest.sceneId);
            builder.AppendLine("Map ID: " + data.Manifest.mapId);
            builder.AppendLine("Coordinate System: " + data.Manifest.coordinateSystem);
            builder.AppendLine("Cell Size: " + data.Manifest.cellSize);
            builder.AppendLine("Grid Width: " + data.Manifest.width);
            builder.AppendLine("Grid Height: " + data.Manifest.height);
            builder.AppendLine("Height Samples: " + data.Heightfield.Length);
            builder.AppendLine("Walkable Samples: " + data.Walkable.Length);
            builder.AppendLine("Colliders: " + data.Colliders.Count);
            builder.AppendLine("NavMesh Vertices: " + data.NavMesh.Vertices.Length);
            builder.AppendLine("NavMesh Indices: " + data.NavMesh.Indices.Length);
            builder.AppendLine("NavMesh Triangles: " + data.NavMesh.Indices.Length / 3);
            builder.AppendLine("NavMesh Areas: " + data.NavMesh.Areas.Length);
            builder.AppendLine("Manifest: " + result.Paths.ManifestPath);
            builder.AppendLine("Height File Bytes: " + GetLength(result.Paths.HeightPath));
            builder.AppendLine("Walkable File Bytes: " + GetLength(result.Paths.WalkablePath));
            builder.AppendLine("Collider File Bytes: " + GetLength(result.Paths.ColliderPath));
            builder.AppendLine("NavMesh File Bytes: " + GetLength(result.Paths.NavMeshPath));
            builder.AppendLine("Warnings: " + result.Warnings.Count);

            for (int warningIndex = 0; warningIndex < result.Warnings.Count; warningIndex++)
            {
                builder.AppendLine("- " + result.Warnings[warningIndex]);
            }

            File.WriteAllText(reportPath, builder.ToString(), new UTF8Encoding(false));
            if (reportPath.StartsWith(Path.GetFullPath(Application.dataPath)))
            {
                AssetDatabase.Refresh();
            }

            return reportPath;
        }

        private static long GetLength(string path)
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0L;
        }
    }
}
