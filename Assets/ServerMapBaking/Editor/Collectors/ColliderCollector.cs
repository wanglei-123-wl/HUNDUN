using UnityEngine;

namespace ServerMapBaking.Editor.Collectors
{
    internal static class ColliderCollector
    {
        public static void Collect(ServerMapRoot root, ServerMapData output)
        {
            uint nextId = 1;
            Collider[] colliders = Object.FindObjectsOfType<Collider>(root.IncludeInactiveObjects);
            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                Collider collider = colliders[colliderIndex];
                if (!ShouldCollect(root, collider))
                {
                    continue;
                }

                BoxCollider boxCollider = collider as BoxCollider;
                if (boxCollider != null)
                {
                    CollectBox(boxCollider, output, nextId++);
                    continue;
                }

                CapsuleCollider capsuleCollider = collider as CapsuleCollider;
                if (capsuleCollider != null && capsuleCollider.direction == 1)
                {
                    CollectCapsule(capsuleCollider, output, nextId++);
                    continue;
                }

                SphereCollider sphereCollider = collider as SphereCollider;
                if (sphereCollider != null)
                {
                    CollectSphere(sphereCollider, output, nextId++);
                }
            }
        }

        private static bool ShouldCollect(ServerMapRoot root, Collider collider)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || collider is TerrainCollider)
            {
                return false;
            }

            if (collider.GetComponent<ServerRegion>() != null ||
                collider.gameObject.scene != root.gameObject.scene ||
                !ServerMapEditorUtility.LayerIncluded(collider.gameObject.layer, root.CollisionLayers))
            {
                return false;
            }

            return root.WorldBounds.Intersects(collider.bounds);
        }

        private static void CollectBox(BoxCollider collider, ServerMapData output, uint colliderId)
        {
            Vector3 scale = ServerMapEditorUtility.Abs(collider.transform.lossyScale);
            output.Colliders.Add(new ColliderRecordData
            {
                ColliderId = colliderId,
                ColliderType = ServerColliderType.Box,
                Flags = 0,
                Name = ServerMapEditorUtility.GetHierarchyPath(collider.transform),
                Center = ServerMapEditorUtility.ToData(collider.transform.TransformPoint(collider.center)),
                Rotation = ServerMapEditorUtility.ToNormalizedEulerData(collider.transform.rotation),
                Size = ServerMapEditorUtility.ToData(Vector3.Scale(collider.size, scale))
            });
        }

        private static void CollectCapsule(CapsuleCollider collider, ServerMapData output, uint colliderId)
        {
            Vector3 scale = ServerMapEditorUtility.Abs(collider.transform.lossyScale);
            float radius = collider.radius * Mathf.Max(scale.x, scale.z);
            float height = Mathf.Max(collider.height * scale.y, radius * 2f);
            output.Colliders.Add(new ColliderRecordData
            {
                ColliderId = colliderId,
                ColliderType = ServerColliderType.Capsule,
                Flags = 0,
                Name = ServerMapEditorUtility.GetHierarchyPath(collider.transform),
                Center = ServerMapEditorUtility.ToData(collider.transform.TransformPoint(collider.center)),
                Rotation = ServerMapEditorUtility.ToNormalizedEulerData(collider.transform.rotation),
                Size = new Float3(radius * 2f, height, radius * 2f)
            });
        }

        private static void CollectSphere(SphereCollider collider, ServerMapData output, uint colliderId)
        {
            Vector3 scale = ServerMapEditorUtility.Abs(collider.transform.lossyScale);
            float diameter = collider.radius * 2f * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            output.Colliders.Add(new ColliderRecordData
            {
                ColliderId = colliderId,
                ColliderType = ServerColliderType.Sphere,
                Flags = 0,
                Name = ServerMapEditorUtility.GetHierarchyPath(collider.transform),
                Center = ServerMapEditorUtility.ToData(collider.transform.TransformPoint(collider.center)),
                Rotation = new Float3(0f, 0f, 0f),
                Size = new Float3(diameter, diameter, diameter)
            });
        }
    }
}
