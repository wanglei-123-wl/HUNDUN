using System.IO;
using UnityEngine;

namespace ServerMapBaking.Editor
{
    internal static class ServerMapExportReader
    {
        public static ServerMapData Read(string manifestPath)
        {
            string directory = Path.GetDirectoryName(manifestPath);
            string json = File.ReadAllText(manifestPath);
            ServerMapManifest manifest = JsonUtility.FromJson<ServerMapManifest>(json);
            ServerMapData data = new ServerMapData { Manifest = manifest };
            data.Heightfield = ServerMapBinaryReader.ReadHeightfield(
                Path.Combine(directory, manifest.heightFile), manifest.width, manifest.height);
            data.Walkable = ServerMapBinaryReader.ReadWalkable(
                Path.Combine(directory, manifest.walkableFile), manifest.width, manifest.height);
            data.Colliders.AddRange(ServerMapBinaryReader.ReadColliders(
                Path.Combine(directory, manifest.colliderFile)));

            NavMeshTriangleData navMesh = ServerMapBinaryReader.ReadNavMesh(
                Path.Combine(directory, manifest.navMeshFile));
            data.NavMesh.Vertices = navMesh.Vertices;
            data.NavMesh.Indices = navMesh.Indices;
            data.NavMesh.Areas = navMesh.Areas;
            return data;
        }
    }
}
