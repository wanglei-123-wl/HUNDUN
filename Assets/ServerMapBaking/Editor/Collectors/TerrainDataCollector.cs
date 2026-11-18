using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ServerMapBaking.Editor.Collectors
{
    internal static class TerrainDataCollector
    {
        public static void Collect(ServerMapRoot root, ServerMapData output, List<string> warnings)
        {
            Bounds bounds = root.WorldBounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float cellSize = root.CellSize;
            int width = Mathf.CeilToInt((max.x - min.x) / cellSize);
            int height = Mathf.CeilToInt((max.z - min.z) / cellSize);

            output.Manifest.origin = new JsonFloat3(min.x, 0f, min.z);
            output.Manifest.boundsMin = ServerMapEditorUtility.ToJsonData(min);
            output.Manifest.boundsMax = ServerMapEditorUtility.ToJsonData(max);
            output.Manifest.cellSize = cellSize;
            output.Manifest.width = width;
            output.Manifest.height = height;
            output.Heightfield = new float[width * height];
            output.Walkable = new byte[width * height];

            Terrain[] terrains = FindTerrains(root, warnings);
            for (int gridZ = 0; gridZ < height; gridZ++)
            {
                for (int gridX = 0; gridX < width; gridX++)
                {
                    int index = gridZ * width + gridX;
                    float sampleX = min.x + (gridX + 0.5f) * cellSize;
                    float sampleZ = min.z + (gridZ + 0.5f) * cellSize;
                    if (sampleX > max.x || sampleZ > max.z ||
                        !TrySampleTerrainHeight(terrains, sampleX, sampleZ, out float worldY) ||
                        worldY < min.y || worldY > max.y)
                    {
                        output.Heightfield[index] = float.NaN;
                        output.Walkable[index] = 0;
                        continue;
                    }

                    output.Heightfield[index] = worldY;
                    output.Walkable[index] = IsWalkable(root, sampleX, worldY, sampleZ) ? (byte)1 : (byte)0;
                }
            }

            if (terrains.Length == 0)
            {
                warnings.Add("No Terrain matched the selected terrain layers and bake bounds. Heightfield will contain NaN values.");
            }
        }

        private static Terrain[] FindTerrains(ServerMapRoot root, List<string> warnings)
        {
            List<Terrain> result = new List<Terrain>();
            Terrain[] terrains = Object.FindObjectsOfType<Terrain>(root.IncludeInactiveObjects);
            for (int terrainIndex = 0; terrainIndex < terrains.Length; terrainIndex++)
            {
                Terrain terrain = terrains[terrainIndex];
                if (terrain.gameObject.scene != root.gameObject.scene ||
                    !ServerMapEditorUtility.LayerIncluded(terrain.gameObject.layer, root.TerrainLayers) ||
                    !IntersectsBakeBounds(root, terrain))
                {
                    continue;
                }

                if (terrain.terrainData == null)
                {
                    warnings.Add("Terrain has no TerrainData: " + ServerMapEditorUtility.GetHierarchyPath(terrain.transform));
                    continue;
                }

                result.Add(terrain);
            }

            return result.ToArray();
        }

        private static bool TrySampleTerrainHeight(Terrain[] terrains, float worldX, float worldZ, out float worldY)
        {
            for (int index = 0; index < terrains.Length; index++)
            {
                Terrain terrain = terrains[index];
                TerrainData terrainData = terrain.terrainData;
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrainData.size;
                if (worldX < origin.x || worldX > origin.x + size.x ||
                    worldZ < origin.z || worldZ > origin.z + size.z)
                {
                    continue;
                }

                float normalizedX = Mathf.Clamp01((worldX - origin.x) / size.x);
                float normalizedZ = Mathf.Clamp01((worldZ - origin.z) / size.z);
                worldY = terrainData.GetInterpolatedHeight(normalizedX, normalizedZ) + origin.y;
                return true;
            }

            worldY = float.NaN;
            return false;
        }

        private static bool IsWalkable(ServerMapRoot root, float worldX, float worldY, float worldZ)
        {
            if (!root.IncludeNavMesh)
            {
                return false;
            }

            float maxDistance = Mathf.Max(0.05f, root.CellSize * 0.75f);
            Vector3 query = new Vector3(worldX, worldY, worldZ);
            if (!NavMesh.SamplePosition(query, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
            {
                return false;
            }

            float horizontalLimit = root.CellSize * 0.5f + 0.001f;
            return Mathf.Abs(hit.position.x - worldX) <= horizontalLimit &&
                   Mathf.Abs(hit.position.z - worldZ) <= horizontalLimit &&
                   Mathf.Abs(hit.position.y - worldY) <= maxDistance;
        }

        private static bool IntersectsBakeBounds(ServerMapRoot root, Terrain terrain)
        {
            TerrainData terrainData = terrain.terrainData;
            if (terrainData == null)
            {
                return true;
            }

            Bounds terrainBounds = new Bounds(terrain.transform.position + terrainData.size * 0.5f, terrainData.size);
            return root.WorldBounds.Intersects(terrainBounds);
        }
    }
}
