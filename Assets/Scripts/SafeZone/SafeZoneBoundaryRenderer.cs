using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Client.SafeZone
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class SafeZoneBoundaryRenderer : MonoBehaviour
    {
        private static readonly Vector3 InitialZoneCenter =
            new Vector3(194.9581f, 29.67076f, 104f);
        private const float InitialZoneRadius = 10f;

        [Header("Circle")]
        [SerializeField, Min(16)] private int segmentCount = 96;
        [SerializeField, Min(0.001f)] private float lineWidth = 0.05f;
        [SerializeField, Min(0f)] private float heightOffset = 0.1f;
        [SerializeField, Min(0.01f)] private float curtainHeight = 0.3f;

        [Header("Terrain Following")]
        [SerializeField] private bool followTerrain = true;
        [SerializeField, Min(1f)] private float raycastStartHeight = 100f;
        [SerializeField, Min(1f)] private float raycastDistance = 300f;
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;

        [Header("Appearance")]
        [SerializeField, ColorUsage(true, true)]
        private Color boundaryColor = new Color(0.1f, 1.6f, 1.25f, 1f);
        [SerializeField, Range(0f, 1f)] private float minimumAlpha = 0.55f;
        [SerializeField, Min(0.01f)] private float breathingPeriod = 2f;

        private LineRenderer lineRenderer;
        private MeshFilter curtainMeshFilter;
        private MeshRenderer curtainRenderer;
        private Mesh curtainMesh;
        private Material runtimeMaterial;
        private Vector3 center;
        private float radius;
        private bool zoneVisible;

        public bool IsVisible => zoneVisible;
        public Vector3 Center => center;
        public float Radius => radius;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoadedHandler()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CreateInitialBoundary();
        }

        private static void CreateInitialBoundary()
        {
            GameSession gameSession = FindObjectOfType<GameSession>(true);
            if (gameSession == null)
            {
                return;
            }

            SafeZoneBoundaryRenderer boundary =
                FindObjectOfType<SafeZoneBoundaryRenderer>(true);
            if (boundary == null)
            {
                GameObject boundaryObject = new GameObject("SafeZoneBoundary");
                boundaryObject.transform.SetParent(gameSession.transform, false);
                boundary = boundaryObject.AddComponent<SafeZoneBoundaryRenderer>();
            }

            boundary.SetZone(InitialZoneCenter, InitialZoneRadius);
            Debug.Log("[SAFE_ZONE] Boundary visible. center=" + InitialZoneCenter
                + " radius=" + InitialZoneRadius, boundary);
        }

        private void Awake()
        {
            EnsureRenderer();
            SetVisible(false);
        }

        private void Update()
        {
            if (!zoneVisible || lineRenderer == null)
            {
                return;
            }

            float phase = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / breathingPeriod);
            float alpha = Mathf.Lerp(minimumAlpha, 1f, phase * 0.5f + 0.5f);
            Color animatedColor = boundaryColor;
            animatedColor.a *= alpha;
            lineRenderer.startColor = animatedColor;
            lineRenderer.endColor = animatedColor;
            SetMaterialColor(animatedColor);
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
            if (curtainMesh != null)
            {
                Destroy(curtainMesh);
            }
        }

        public void SetZone(Vector3 worldCenter, float worldRadius)
        {
            if (worldRadius <= 0f)
            {
                Hide();
                return;
            }

            EnsureRenderer();
            center = worldCenter;
            radius = worldRadius;
            RebuildCircle();
            SetVisible(true);
        }

        public void SetZone(float centerX, float centerY, float centerZ, float worldRadius)
        {
            SetZone(new Vector3(centerX, centerY, centerZ), worldRadius);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        private void EnsureRenderer()
        {
            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }

            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = true;
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.numCapVertices = 0;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            if (curtainMeshFilter == null)
            {
                curtainMeshFilter = GetComponent<MeshFilter>();
                if (curtainMeshFilter == null)
                {
                    curtainMeshFilter = gameObject.AddComponent<MeshFilter>();
                }
            }
            if (curtainRenderer == null)
            {
                curtainRenderer = GetComponent<MeshRenderer>();
                if (curtainRenderer == null)
                {
                    curtainRenderer = gameObject.AddComponent<MeshRenderer>();
                }
                curtainRenderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                curtainRenderer.receiveShadows = false;
            }

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Legacy Shaders/Particles/Additive")
                    ?? Shader.Find("Particles/Standard Unlit")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Safe Zone Boundary (Runtime)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    lineRenderer.sharedMaterial = runtimeMaterial;
                    curtainRenderer.sharedMaterial = runtimeMaterial;
                }
            }

            if (curtainMesh == null)
            {
                curtainMesh = new Mesh
                {
                    name = "Safe Zone Curtain (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                curtainMeshFilter.sharedMesh = curtainMesh;
            }
        }

        private void RebuildCircle()
        {
            int count = Mathf.Max(16, segmentCount);
            lineRenderer.positionCount = count;
            lineRenderer.widthMultiplier = Mathf.Max(0.001f, lineWidth);

            Vector3[] vertices = new Vector3[(count + 1) * 2];
            Color32[] colors = new Color32[vertices.Length];
            int[] triangles = new int[count * 12];

            float step = Mathf.PI * 2f / count;
            for (int index = 0; index <= count; index++)
            {
                float angle = index * step;
                Vector3 point = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y,
                    center.z + Mathf.Sin(angle) * radius);
                point.y = ResolveBoundaryHeight(point) + heightOffset;
                if (index < count)
                {
                    lineRenderer.SetPosition(index, point);
                }

                int vertexIndex = index * 2;
                vertices[vertexIndex] = transform.InverseTransformPoint(point);
                vertices[vertexIndex + 1] = transform.InverseTransformPoint(
                    point + Vector3.up * curtainHeight);
                colors[vertexIndex] = new Color32(255, 255, 255, 190);
                colors[vertexIndex + 1] = new Color32(255, 255, 255, 70);

                if (index == count)
                {
                    continue;
                }

                int triangleIndex = index * 12;
                int nextVertex = vertexIndex + 2;
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = vertexIndex + 1;
                triangles[triangleIndex + 2] = nextVertex + 1;
                triangles[triangleIndex + 3] = vertexIndex;
                triangles[triangleIndex + 4] = nextVertex + 1;
                triangles[triangleIndex + 5] = nextVertex;
                triangles[triangleIndex + 6] = vertexIndex;
                triangles[triangleIndex + 7] = nextVertex + 1;
                triangles[triangleIndex + 8] = vertexIndex + 1;
                triangles[triangleIndex + 9] = vertexIndex;
                triangles[triangleIndex + 10] = nextVertex;
                triangles[triangleIndex + 11] = nextVertex + 1;
            }

            curtainMesh.Clear();
            curtainMesh.vertices = vertices;
            curtainMesh.colors32 = colors;
            curtainMesh.triangles = triangles;
            curtainMesh.RecalculateBounds();
        }

        private void SetMaterialColor(Color color)
        {
            if (runtimeMaterial == null)
            {
                return;
            }

            if (runtimeMaterial.HasProperty("_TintColor"))
            {
                runtimeMaterial.SetColor("_TintColor", color);
            }
            else if (runtimeMaterial.HasProperty("_Color"))
            {
                runtimeMaterial.SetColor("_Color", color);
            }
        }

        private float ResolveBoundaryHeight(Vector3 worldPoint)
        {
            if (!followTerrain)
            {
                return center.y;
            }

            Terrain[] terrains = Terrain.activeTerrains;
            for (int index = 0; index < terrains.Length; index++)
            {
                Terrain terrain = terrains[index];
                TerrainData terrainData = terrain == null ? null : terrain.terrainData;
                if (terrainData == null)
                {
                    continue;
                }

                Vector3 terrainPosition = terrain.transform.position;
                Vector3 terrainSize = terrainData.size;
                bool containsPoint = worldPoint.x >= terrainPosition.x
                    && worldPoint.x <= terrainPosition.x + terrainSize.x
                    && worldPoint.z >= terrainPosition.z
                    && worldPoint.z <= terrainPosition.z + terrainSize.z;
                if (containsPoint)
                {
                    return terrain.SampleHeight(worldPoint) + terrainPosition.y;
                }
            }

            Vector3 rayOrigin = new Vector3(
                worldPoint.x,
                center.y + raycastStartHeight,
                worldPoint.z);
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit,
                    raycastDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }

            return center.y;
        }

        private void SetVisible(bool visible)
        {
            zoneVisible = visible;
            if (lineRenderer != null)
            {
                lineRenderer.enabled = visible;
            }
            if (curtainRenderer != null)
            {
                curtainRenderer.enabled = visible;
            }
        }
    }
}
