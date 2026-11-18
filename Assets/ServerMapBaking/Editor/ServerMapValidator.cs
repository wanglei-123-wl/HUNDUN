using System.Collections.Generic;
using UnityEngine;

namespace ServerMapBaking.Editor
{
    internal enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    internal sealed class ValidationIssue
    {
        public ValidationSeverity Severity;
        public string Message = string.Empty;
    }

    internal static class ServerMapValidator
    {
        public static List<ValidationIssue> Validate(ServerMapRoot root)
        {
            List<ValidationIssue> issues = new List<ValidationIssue>();
            if (root == null)
            {
                Add(issues, ValidationSeverity.Error, "ServerMapRoot is not selected.");
                return issues;
            }

            ValidateId(issues, "sceneId", root.SceneId);
            ValidateId(issues, "mapId", root.MapId);

            if (root.CellSize <= 0f)
            {
                Add(issues, ValidationSeverity.Error, "Cell size must be greater than zero.");
            }

            if (root.BakeBoundsSize.x <= 0f || root.BakeBoundsSize.y <= 0f || root.BakeBoundsSize.z <= 0f)
            {
                Add(issues, ValidationSeverity.Error, "Bake bounds size must be positive on every axis.");
            }

            if (root.CellSize > 0f)
            {
                Bounds bounds = root.WorldBounds;
                int width = Mathf.CeilToInt((bounds.max.x - bounds.min.x) / root.CellSize);
                int height = Mathf.CeilToInt((bounds.max.z - bounds.min.z) / root.CellSize);
                if (width <= 0 || height <= 0)
                {
                    Add(issues, ValidationSeverity.Error, "Grid width and height must be greater than zero.");
                }
                else if ((long)width * height > 50000000L)
                {
                    Add(issues, ValidationSeverity.Warning, "Grid contains more than 50 million cells. Bake time and file size may be large.");
                }
            }

            if (!Approximately(root.transform.lossyScale, Vector3.one))
            {
                Add(issues, ValidationSeverity.Error, "ServerMapRoot scale must be (1, 1, 1).");
            }

            if (Quaternion.Angle(root.transform.rotation, Quaternion.identity) > 0.01f)
            {
                Add(issues, ValidationSeverity.Error, "ServerMapRoot rotation must be identity. Position may be changed safely.");
            }

            if (!root.IncludeNavMesh)
            {
                Add(issues, ValidationSeverity.Warning, "Include NavMesh is disabled. walkable.bin will contain only 0 values.");
            }

            ValidateTerrains(root, issues);
            ValidateColliders(root, issues);
            return issues;
        }

        public static bool HasErrors(List<ValidationIssue> issues)
        {
            for (int index = 0; index < issues.Count; index++)
            {
                if (issues[index].Severity == ValidationSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateId(List<ValidationIssue> issues, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Add(issues, ValidationSeverity.Error, name + " must not be empty.");
                return;
            }

            if (value.Length > ServerMapFormat.MaxIdLength)
            {
                Add(issues, ValidationSeverity.Error, name + " must be at most " + ServerMapFormat.MaxIdLength + " characters.");
            }
        }

        private static void ValidateTerrains(ServerMapRoot root, List<ValidationIssue> issues)
        {
            Terrain[] terrains = Object.FindObjectsOfType<Terrain>(root.IncludeInactiveObjects);
            int matchedCount = 0;
            for (int index = 0; index < terrains.Length; index++)
            {
                Terrain terrain = terrains[index];
                if (terrain.gameObject.scene != root.gameObject.scene ||
                    !ServerMapEditorUtility.LayerIncluded(terrain.gameObject.layer, root.TerrainLayers))
                {
                    continue;
                }

                matchedCount++;
                string path = ServerMapEditorUtility.GetHierarchyPath(terrain.transform);
                if (terrain.terrainData == null)
                {
                    Add(issues, ValidationSeverity.Error, "Terrain has no TerrainData: " + path);
                }

                if (!Approximately(terrain.transform.lossyScale, Vector3.one) ||
                    Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > 0.01f)
                {
                    Add(issues, ValidationSeverity.Error, "Terrain rotation and scale must be identity: " + path);
                }
            }

            if (matchedCount == 0)
            {
                Add(issues, ValidationSeverity.Warning, "No Terrain was found in the selected terrain layers.");
            }
        }

        private static void ValidateColliders(ServerMapRoot root, List<ValidationIssue> issues)
        {
            Collider[] colliders = Object.FindObjectsOfType<Collider>(root.IncludeInactiveObjects);
            int supportedCount = 0;
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider.gameObject.scene != root.gameObject.scene ||
                    !ServerMapEditorUtility.LayerIncluded(collider.gameObject.layer, root.CollisionLayers) ||
                    collider is TerrainCollider || collider.GetComponent<ServerRegion>() != null)
                {
                    continue;
                }

                string path = ServerMapEditorUtility.GetHierarchyPath(collider.transform);
                Vector3 scale = collider.transform.lossyScale;
                if (scale.x < 0f || scale.y < 0f || scale.z < 0f)
                {
                    Add(issues, ValidationSeverity.Error, "Negative scale is not supported: " + path);
                }

                CapsuleCollider capsule = collider as CapsuleCollider;
                if (capsule != null && capsule.direction != 1)
                {
                    Add(issues, ValidationSeverity.Error, "CapsuleCollider must use Y direction for V1 export: " + path);
                }

                if (collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider)
                {
                    supportedCount++;
                }
                else if (!(collider is TerrainCollider))
                {
                    Add(issues, ValidationSeverity.Warning, "Collider type is skipped in V1: " + path + " (" + collider.GetType().Name + ")");
                }
            }

            if (supportedCount == 0)
            {
                Add(issues, ValidationSeverity.Warning, "No supported primitive colliders were found in the selected layers.");
            }
        }

        private static bool Approximately(Vector3 first, Vector3 second)
        {
            return (first - second).sqrMagnitude < 0.000001f;
        }

        private static void Add(List<ValidationIssue> issues, ValidationSeverity severity, string message)
        {
            issues.Add(new ValidationIssue { Severity = severity, Message = message });
        }
    }
}
