using UnityEngine;

namespace ServerMapBaking.Editor.Collectors
{
    internal static class RegionCollector
    {
        public static void Collect(ServerMapRoot root, ServerMapData output)
        {
            ServerRegion[] regions = Object.FindObjectsOfType<ServerRegion>(root.IncludeInactiveObjects);
            for (int regionIndex = 0; regionIndex < regions.Length; regionIndex++)
            {
                ServerRegion region = regions[regionIndex];
                if (region.gameObject.scene != root.gameObject.scene)
                {
                    continue;
                }

                BoxCollider boxCollider = region.GetComponent<BoxCollider>();
                if (boxCollider == null || !root.ContainsWorldPoint(boxCollider.bounds.center))
                {
                    continue;
                }

                Vector3 scale = ServerMapEditorUtility.Abs(region.transform.lossyScale);
                output.Regions.Add(new BoxRegionData
                {
                    RegionId = region.RegionId,
                    RegionType = region.RegionType,
                    Name = ServerMapEditorUtility.GetHierarchyPath(region.transform),
                    Payload = region.Payload ?? string.Empty,
                    Center = ServerMapEditorUtility.ToData(root.transform.InverseTransformPoint(region.transform.TransformPoint(boxCollider.center))),
                    Size = ServerMapEditorUtility.ToData(Vector3.Scale(boxCollider.size, scale)),
                    Rotation = ServerMapEditorUtility.ToData(Quaternion.Inverse(root.transform.rotation) * region.transform.rotation)
                });
            }
        }
    }
}
