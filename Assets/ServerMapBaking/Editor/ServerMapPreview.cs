using UnityEditor;
using UnityEngine;

namespace ServerMapBaking.Editor
{
    [InitializeOnLoad]
    internal static class ServerMapPreview
    {
        private static ServerMapData data;
        private static ServerMapBakeSettings settings;

        static ServerMapPreview()
        {
            SceneView.duringSceneGui += Draw;
        }

        public static bool IsLoaded => data != null;

        public static void Load(string manifestPath, ServerMapRoot mapRoot, ServerMapBakeSettings bakeSettings)
        {
            data = ServerMapExportReader.Read(manifestPath);
            settings = bakeSettings;
            SceneView.RepaintAll();
        }

        public static void Clear()
        {
            data = null;
            settings = null;
            SceneView.RepaintAll();
        }

        private static void Draw(SceneView sceneView)
        {
            if (data == null || settings == null)
            {
                return;
            }

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            Handles.matrix = Matrix4x4.identity;

            if (settings.DrawTerrain)
            {
                DrawHeightfield();
            }

            if (settings.DrawColliders)
            {
                DrawColliders();
            }

            if (settings.DrawNavMesh)
            {
                DrawNavMesh();
            }

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
        }

        private static void DrawHeightfield()
        {
            ServerMapManifest manifest = data.Manifest;
            if (manifest.width < 2 || manifest.height < 2 || data.Heightfield.Length == 0)
            {
                return;
            }

            Handles.color = new Color(0.1f, 0.95f, 1f, 0.5f);
            int maxSamples = Mathf.Max(2, settings.TerrainPreviewSamples);
            int stepX = Mathf.Max(1, manifest.width / maxSamples);
            int stepZ = Mathf.Max(1, manifest.height / maxSamples);

            for (int z = 0; z < manifest.height; z += stepZ)
            {
                DrawHeightLineX(z, stepX);
            }

            for (int x = 0; x < manifest.width; x += stepX)
            {
                DrawHeightLineZ(x, stepZ);
            }
        }

        private static void DrawHeightLineX(int z, int stepX)
        {
            bool hasPrevious = false;
            Vector3 previous = Vector3.zero;
            for (int x = 0; x < data.Manifest.width; x += stepX)
            {
                if (!TryGetHeightPoint(x, z, out Vector3 current))
                {
                    hasPrevious = false;
                    continue;
                }

                if (hasPrevious)
                {
                    Handles.DrawLine(previous, current);
                }

                previous = current;
                hasPrevious = true;
            }
        }

        private static void DrawHeightLineZ(int x, int stepZ)
        {
            bool hasPrevious = false;
            Vector3 previous = Vector3.zero;
            for (int z = 0; z < data.Manifest.height; z += stepZ)
            {
                if (!TryGetHeightPoint(x, z, out Vector3 current))
                {
                    hasPrevious = false;
                    continue;
                }

                if (hasPrevious)
                {
                    Handles.DrawLine(previous, current);
                }

                previous = current;
                hasPrevious = true;
            }
        }

        private static bool TryGetHeightPoint(int x, int z, out Vector3 point)
        {
            int index = z * data.Manifest.width + x;
            float y = data.Heightfield[index];
            if (float.IsNaN(y))
            {
                point = Vector3.zero;
                return false;
            }

            JsonFloat3 origin = data.Manifest.origin;
            float cellSize = data.Manifest.cellSize;
            point = new Vector3(
                origin.x + (x + 0.5f) * cellSize,
                y,
                origin.z + (z + 0.5f) * cellSize);
            return true;
        }

        private static void DrawColliders()
        {
            for (int index = 0; index < data.Colliders.Count; index++)
            {
                ColliderRecordData collider = data.Colliders[index];
                if (collider.ColliderType == ServerColliderType.Box)
                {
                    DrawBox(collider.Center, collider.Size, collider.Rotation, new Color(1f, 0.2f, 0.15f, 0.9f));
                }
                else if (collider.ColliderType == ServerColliderType.Sphere)
                {
                    DrawSphere(collider);
                }
                else if (collider.ColliderType == ServerColliderType.Capsule)
                {
                    DrawCapsule(collider);
                }
            }
        }

        private static void DrawNavMesh()
        {
            Handles.color = new Color(0.15f, 1f, 0.25f, 0.75f);
            int[] indices = data.NavMesh.Indices;
            Float3[] vertices = data.NavMesh.Vertices;
            for (int index = 0; index + 2 < indices.Length; index += 3)
            {
                Vector3 first = ServerMapEditorUtility.ToVector3(vertices[indices[index]]);
                Vector3 second = ServerMapEditorUtility.ToVector3(vertices[indices[index + 1]]);
                Vector3 third = ServerMapEditorUtility.ToVector3(vertices[indices[index + 2]]);
                Handles.DrawLine(first, second);
                Handles.DrawLine(second, third);
                Handles.DrawLine(third, first);
            }
        }

        private static void DrawBox(Float3 centerData, Float3 sizeData, Float3 rotationData, Color color)
        {
            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.color = color;
            Matrix4x4 localMatrix = Matrix4x4.TRS(
                ServerMapEditorUtility.ToVector3(centerData),
                Quaternion.Euler(ServerMapEditorUtility.ToVector3(rotationData)),
                ServerMapEditorUtility.ToVector3(sizeData));
            Handles.matrix = localMatrix;
            Handles.DrawWireCube(Vector3.zero, Vector3.one);
            Handles.matrix = previousMatrix;
        }

        private static void DrawSphere(ColliderRecordData collider)
        {
            Handles.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Vector3 center = ServerMapEditorUtility.ToVector3(collider.Center);
            float radius = Mathf.Max(collider.Size.X, Mathf.Max(collider.Size.Y, collider.Size.Z)) * 0.5f;
            Handles.DrawWireDisc(center, Vector3.right, radius);
            Handles.DrawWireDisc(center, Vector3.up, radius);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }

        private static void DrawCapsule(ColliderRecordData collider)
        {
            Handles.color = new Color(0.8f, 0.25f, 1f, 0.9f);
            Vector3 center = ServerMapEditorUtility.ToVector3(collider.Center);
            Quaternion rotation = Quaternion.Euler(ServerMapEditorUtility.ToVector3(collider.Rotation));
            Vector3 axis = rotation * Vector3.up;
            float radius = Mathf.Max(collider.Size.X, collider.Size.Z) * 0.5f;
            float height = Mathf.Max(collider.Size.Y, radius * 2f);
            float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 firstCenter = center + axis * halfSegment;
            Vector3 secondCenter = center - axis * halfSegment;
            Vector3 tangent = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(axis, tangent).normalized;

            Handles.DrawWireDisc(firstCenter, axis, radius);
            Handles.DrawWireDisc(secondCenter, axis, radius);
            Handles.DrawLine(firstCenter + tangent * radius, secondCenter + tangent * radius);
            Handles.DrawLine(firstCenter - tangent * radius, secondCenter - tangent * radius);
            Handles.DrawLine(firstCenter + bitangent * radius, secondCenter + bitangent * radius);
            Handles.DrawLine(firstCenter - bitangent * radius, secondCenter - bitangent * radius);
        }
    }
}
