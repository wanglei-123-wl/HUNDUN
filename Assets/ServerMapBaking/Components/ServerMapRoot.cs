using UnityEngine;

namespace ServerMapBaking
{
    [DisallowMultipleComponent]
    public sealed class ServerMapRoot : MonoBehaviour
    {
        [SerializeField] private string sceneId = "world_001";
        [SerializeField] private string mapId = "map001";
        [SerializeField] private Vector3 bakeBoundsCenter;
        [SerializeField] private Vector3 bakeBoundsSize = new Vector3(1000f, 500f, 1000f);
        [SerializeField] private float cellSize = 0.5f;
        [SerializeField] private LayerMask terrainLayers = ~0;
        [SerializeField] private LayerMask collisionLayers = ~0;
        [SerializeField] private bool includeInactiveObjects;
        [SerializeField] private bool includeNavMesh = true;

        public string SceneId => sceneId;
        public string MapId => mapId;
        public Vector3 BakeBoundsCenter => bakeBoundsCenter;
        public Vector3 BakeBoundsSize => bakeBoundsSize;
        public float CellSize => cellSize;
        public LayerMask TerrainLayers => terrainLayers;
        public LayerMask CollisionLayers => collisionLayers;
        public bool IncludeInactiveObjects => includeInactiveObjects;
        public bool IncludeNavMesh => includeNavMesh;
        public Bounds LocalBounds => new Bounds(bakeBoundsCenter, bakeBoundsSize);

        public Bounds WorldBounds
        {
            get { return new Bounds(transform.TransformPoint(bakeBoundsCenter), bakeBoundsSize); }
        }

        public bool ContainsWorldPoint(Vector3 worldPoint)
        {
            return WorldBounds.Contains(worldPoint);
        }

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.85f);
            Gizmos.DrawWireCube(bakeBoundsCenter, bakeBoundsSize);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
