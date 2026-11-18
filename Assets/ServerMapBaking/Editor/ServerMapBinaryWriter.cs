using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ServerMapBaking.Editor
{
    internal sealed class ServerMapExportPaths
    {
        public string OutputDirectory = string.Empty;
        public string ManifestPath = string.Empty;
        public string HeightPath = string.Empty;
        public string WalkablePath = string.Empty;
        public string ColliderPath = string.Empty;
        public string NavMeshPath = string.Empty;
    }

    internal static class ServerMapBinaryWriter
    {
        public static ServerMapExportPaths Write(ServerMapData data, string outputFolder)
        {
            ServerMapExportPaths paths = BuildPaths(data.Manifest.sceneId, data.Manifest.mapId, outputFolder);
            Directory.CreateDirectory(paths.OutputDirectory);
            WriteManifest(data.Manifest, paths.ManifestPath);
            WriteHeightfield(data, paths.HeightPath);
            WriteWalkable(data, paths.WalkablePath);
            WriteColliders(data, paths.ColliderPath);
            WriteNavMesh(data, paths.NavMeshPath);

            if (IsInsideAssets(paths.OutputDirectory))
            {
                AssetDatabase.Refresh();
            }

            return paths;
        }

        public static string ResolveManifestFile(ServerMapRoot root, string outputFolder)
        {
            return BuildPaths(root.SceneId, root.MapId, outputFolder).ManifestPath;
        }

        public static string ResolveOutputDirectory(ServerMapRoot root, string outputFolder)
        {
            return BuildPaths(root.SceneId, root.MapId, outputFolder).OutputDirectory;
        }

        private static ServerMapExportPaths BuildPaths(string sceneId, string mapId, string outputFolder)
        {
            string rootFolder = ResolveOutputRoot(outputFolder);
            string directoryName = ServerMapEditorUtility.SanitizePathSegment(sceneId) + "_" +
                                   ServerMapEditorUtility.SanitizePathSegment(mapId);
            string outputDirectory = Path.Combine(rootFolder, directoryName);
            return new ServerMapExportPaths
            {
                OutputDirectory = outputDirectory,
                ManifestPath = Path.Combine(outputDirectory, ServerMapFormat.ManifestFileName),
                HeightPath = Path.Combine(outputDirectory, ServerMapFormat.HeightFileName),
                WalkablePath = Path.Combine(outputDirectory, ServerMapFormat.WalkableFileName),
                ColliderPath = Path.Combine(outputDirectory, ServerMapFormat.ColliderFileName),
                NavMeshPath = Path.Combine(outputDirectory, ServerMapFormat.NavMeshFileName)
            };
        }

        private static void WriteManifest(ServerMapManifest manifest, string path)
        {
            string json = BuildManifestJson(manifest);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static string BuildManifestJson(ServerMapManifest manifest)
        {
            StringBuilder builder = new StringBuilder(768);
            builder.AppendLine("{");
            AppendNumber(builder, "formatVersion", manifest.formatVersion, true);
            AppendString(builder, "sceneId", manifest.sceneId, true);
            AppendString(builder, "mapId", manifest.mapId, true);
            AppendString(builder, "coordinateSystem", manifest.coordinateSystem, true);
            AppendVector(builder, "origin", manifest.origin, true);
            AppendVector(builder, "boundsMin", manifest.boundsMin, true);
            AppendVector(builder, "boundsMax", manifest.boundsMax, true);
            AppendFloat(builder, "cellSize", manifest.cellSize, true);
            AppendNumber(builder, "width", manifest.width, true);
            AppendNumber(builder, "height", manifest.height, true);
            AppendString(builder, "heightFile", manifest.heightFile, true);
            AppendString(builder, "walkableFile", manifest.walkableFile, true);
            AppendString(builder, "colliderFile", manifest.colliderFile, true);
            AppendString(builder, "navMeshFile", manifest.navMeshFile, true);
            AppendNullableString(builder, "regionsFile", manifest.regionsFile, false);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void WriteHeightfield(ServerMapData data, string path)
        {
            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, false))
            {
                for (int index = 0; index < data.Heightfield.Length; index++)
                {
                    writer.Write(data.Heightfield[index]);
                }
            }
        }

        private static void WriteWalkable(ServerMapData data, string path)
        {
            File.WriteAllBytes(path, data.Walkable);
        }

        private static void WriteColliders(ServerMapData data, string path)
        {
            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, false))
            {
                writer.Write(new[] { (byte)'G', (byte)'C', (byte)'O', (byte)'L' });
                writer.Write(ServerMapFormat.ColliderFileVersion);
                writer.Write(ServerMapFormat.ColliderHeaderSize);
                writer.Write((uint)data.Colliders.Count);
                writer.Write(ServerMapFormat.ColliderRecordBytes);
                writer.Write((uint)0);

                for (int index = 0; index < data.Colliders.Count; index++)
                {
                    ColliderRecordData collider = data.Colliders[index];
                    writer.Write(collider.ColliderId);
                    writer.Write((uint)collider.ColliderType);
                    writer.Write(collider.Flags);
                    WriteFloat3(writer, collider.Center);
                    WriteFloat3(writer, collider.Rotation);
                    WriteFloat3(writer, collider.Size);
                }
            }
        }

        private static void WriteNavMesh(ServerMapData data, string path)
        {
            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, false))
            {
                writer.Write(data.NavMesh.Vertices.Length);
                for (int index = 0; index < data.NavMesh.Vertices.Length; index++)
                {
                    WriteFloat3(writer, data.NavMesh.Vertices[index]);
                }

                writer.Write(data.NavMesh.Indices.Length);
                for (int index = 0; index < data.NavMesh.Indices.Length; index++)
                {
                    writer.Write(data.NavMesh.Indices[index]);
                }

                writer.Write(data.NavMesh.Areas.Length);
                for (int index = 0; index < data.NavMesh.Areas.Length; index++)
                {
                    writer.Write(data.NavMesh.Areas[index]);
                }
            }
        }

        private static void WriteFloat3(BinaryWriter writer, Float3 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        private static void AppendString(StringBuilder builder, string name, string value, bool trailingComma)
        {
            builder.Append("  \"").Append(name).Append("\": \"").Append(EscapeJson(value ?? string.Empty)).Append('"');
            AppendCommaAndNewLine(builder, trailingComma);
        }

        private static void AppendNullableString(StringBuilder builder, string name, string value, bool trailingComma)
        {
            builder.Append("  \"").Append(name).Append("\": ");
            if (value == null)
            {
                builder.Append("null");
            }
            else
            {
                builder.Append('"').Append(EscapeJson(value)).Append('"');
            }

            AppendCommaAndNewLine(builder, trailingComma);
        }

        private static void AppendNumber(StringBuilder builder, string name, int value, bool trailingComma)
        {
            builder.Append("  \"").Append(name).Append("\": ").Append(value);
            AppendCommaAndNewLine(builder, trailingComma);
        }

        private static void AppendFloat(StringBuilder builder, string name, float value, bool trailingComma)
        {
            builder.Append("  \"").Append(name).Append("\": ").Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            AppendCommaAndNewLine(builder, trailingComma);
        }

        private static void AppendVector(StringBuilder builder, string name, JsonFloat3 value, bool trailingComma)
        {
            builder.Append("  \"").Append(name).Append("\": { ");
            builder.Append("\"x\": ").Append(value.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(", ");
            builder.Append("\"y\": ").Append(value.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(", ");
            builder.Append("\"z\": ").Append(value.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(" }");
            AppendCommaAndNewLine(builder, trailingComma);
        }

        private static void AppendCommaAndNewLine(StringBuilder builder, bool trailingComma)
        {
            if (trailingComma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ResolveOutputRoot(string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                outputFolder = ServerMapFormat.DefaultOutputFolder;
            }

            if (Path.IsPathRooted(outputFolder))
            {
                return Path.GetFullPath(outputFolder);
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, outputFolder));
        }

        private static bool IsInsideAssets(string path)
        {
            string assetsPath = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
