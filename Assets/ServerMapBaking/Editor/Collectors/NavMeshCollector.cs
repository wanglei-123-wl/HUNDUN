using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ServerMapBaking.Editor.Collectors
{
    internal static class NavMeshCollector
    {
        public static void Collect(ServerMapRoot root, ServerMapData output)
        {
            if (!root.IncludeNavMesh)
            {
                return;
            }

            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            Dictionary<int, int> vertexRemap = new Dictionary<int, int>();
            List<Float3> vertices = new List<Float3>();
            List<int> indices = new List<int>();
            List<int> areas = new List<int>();
            int triangleCount = triangulation.indices.Length / 3;

            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                int sourceIndex = triangleIndex * 3;
                int first = triangulation.indices[sourceIndex];
                int second = triangulation.indices[sourceIndex + 1];
                int third = triangulation.indices[sourceIndex + 2];
                Vector3 centroid = (triangulation.vertices[first] + triangulation.vertices[second] + triangulation.vertices[third]) / 3f;
                if (!root.ContainsWorldPoint(centroid))
                {
                    continue;
                }

                indices.Add(RemapVertex(triangulation.vertices, first, vertexRemap, vertices));
                indices.Add(RemapVertex(triangulation.vertices, second, vertexRemap, vertices));
                indices.Add(RemapVertex(triangulation.vertices, third, vertexRemap, vertices));
                areas.Add(triangleIndex < triangulation.areas.Length ? triangulation.areas[triangleIndex] : 0);
            }

            output.NavMesh.Vertices = vertices.ToArray();
            output.NavMesh.Indices = indices.ToArray();
            output.NavMesh.Areas = areas.ToArray();
        }

        private static int RemapVertex(
            Vector3[] sourceVertices,
            int sourceIndex,
            Dictionary<int, int> vertexRemap,
            List<Float3> outputVertices)
        {
            int outputIndex;
            if (vertexRemap.TryGetValue(sourceIndex, out outputIndex))
            {
                return outputIndex;
            }

            outputIndex = outputVertices.Count;
            vertexRemap.Add(sourceIndex, outputIndex);
            outputVertices.Add(ServerMapEditorUtility.ToData(sourceVertices[sourceIndex]));
            return outputIndex;
        }
    }
}
