using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Client.Vfx
{
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlayerGroundRingEffect : MonoBehaviour
    {
        private const int SegmentCount = 64;
        private const int RadialSampleCount = 3;
        private const int VertexCount = (SegmentCount + 1) * RadialSampleCount;
        private const int IndexCount = SegmentCount * (RadialSampleCount - 1) * 6;
        private static readonly int RingPhaseId = Shader.PropertyToID("_RingPhase");

        [Header("Follow")]
        [SerializeField] private Transform followTarget;
        [SerializeField] private Transform ignoredRoot;

        [Header("Ground")]
        [SerializeField] private LayerMask groundLayers = 1;
        [SerializeField, Min(0.1f)] private float radius = 0.67f;
        [SerializeField, Range(0.01f, 0.2f)] private float halfWidth = 0.1f;
        [SerializeField, Min(0.001f)] private float surfaceOffset = 0.018f;
        [SerializeField, Min(0.01f)] private float anchorProbeAbove = 0.2f;
        [SerializeField, Min(0.01f)] private float anchorProbeBelow = 2.5f;
        [SerializeField, Min(0.01f)] private float ringProbeRange = 0.26f;
        [SerializeField, Range(0f, 75f)] private float maxGroundSlope = 55f;
        [SerializeField, Min(0f)] private float surfaceTolerance = 0.015f;

        [Header("Animation")]
        [SerializeField, Min(0.1f)] private float breathingPeriod = 2.8f;
        [SerializeField, Min(0.1f)] private float flowPeriod = 4.2f;

        private Vector2[] directions;
        private Vector3[] surfacePoints;
        private Vector3[] surfaceNormals;
        private Vector3[] vertices;
        private bool[] sampleValid;
        private int[] indices;
        private RaycastHit[] groundHits;

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock properties;
        private Scene? groundScene;
        private System.Predicate<Collider> groundColliderFilter;
        private double animationStartTime;

        public void SetFollowTarget(Transform target, Transform owner,
            System.Predicate<Collider> groundFilter = null)
        {
            followTarget = target;
            ignoredRoot = owner;
            groundColliderFilter = groundFilter;
        }

        public void SetGroundScene(Scene scene)
        {
            groundScene = scene;
        }

        public void EnsureMinimumRadius(float minimumRadius)
        {
            radius = Mathf.Max(radius, minimumRadius);
        }

        private void Awake()
        {
            directions = new Vector2[SegmentCount];
            surfacePoints = new Vector3[VertexCount];
            surfaceNormals = new Vector3[VertexCount];
            vertices = new Vector3[VertexCount];
            sampleValid = new bool[VertexCount];
            indices = new int[IndexCount];
            groundHits = new RaycastHit[16];
            var uvs = new Vector2[VertexCount];
            for (int segment = 0; segment <= SegmentCount; segment++)
            {
                float progress = (float)segment / SegmentCount;
                if (segment < SegmentCount)
                {
                    float angle = progress * Mathf.PI * 2f;
                    directions[segment] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }

                for (int radial = 0; radial < RadialSampleCount; radial++)
                {
                    float across = (float)radial / (RadialSampleCount - 1);
                    uvs[segment * RadialSampleCount + radial] = new Vector2(progress, across);
                }
            }

            mesh = new Mesh { name = "Player Ground Ring (Runtime)" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            GetComponent<MeshFilter>().sharedMesh = mesh;

            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.enabled = false;
            properties = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            animationStartTime = Time.unscaledTimeAsDouble;
        }

        private void OnDisable()
        {
            meshRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            Destroy(mesh);
        }

        private void LateUpdate()
        {
            Vector3 anchor = followTarget == null ? transform.position : followTarget.position;
            Scene probeScene = groundScene ?? gameObject.scene;
            if (!probeScene.IsValid() || !probeScene.isLoaded)
            {
                meshRenderer.enabled = false;
                return;
            }

            PhysicsScene physicsScene = probeScene.GetPhysicsScene();
            float slopeRadians = maxGroundSlope * Mathf.Deg2Rad;
            float minNormalY = Mathf.Cos(slopeRadians);
            if (!TrySampleGround(physicsScene, anchor, anchorProbeAbove, anchorProbeBelow,
                    minNormalY, out RaycastHit centerHit))
            {
                meshRenderer.enabled = false;
                return;
            }

            SampleRing(physicsScene, centerHit, minNormalY);
            int indexCount = BuildRingIndices(physicsScene, minNormalY);
            meshRenderer.enabled = indexCount > 0;
            if (indexCount == 0)
                return;

            mesh.vertices = vertices;
            mesh.SetIndices(indices, 0, indexCount, MeshTopology.Triangles, 0);

            double elapsed = Time.unscaledTimeAsDouble - animationStartTime;
            float breath = 0.5f - 0.5f * Mathf.Cos((float)(elapsed % breathingPeriod)
                / breathingPeriod * Mathf.PI * 2f);
            float flow = (float)(elapsed % flowPeriod) / flowPeriod;
            properties.SetVector(RingPhaseId, new Vector4(breath, flow, 0f, 0f));
            meshRenderer.SetPropertyBlock(properties);
        }

        private void SampleRing(PhysicsScene physicsScene, RaycastHit centerHit, float minNormalY)
        {
            Vector3 center = centerHit.point;
            Vector3 normal = centerHit.normal;
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
            float innerRadius = Mathf.Max(0.01f, radius - halfWidth);
            float outerRadius = radius + halfWidth;

            for (int segment = 0; segment < SegmentCount; segment++)
            {
                Vector2 direction = directions[segment];
                for (int radial = 0; radial < RadialSampleCount; radial++)
                {
                    float distance = Mathf.Lerp(innerRadius, outerRadius,
                        (float)radial / (RadialSampleCount - 1));
                    float dx = direction.x * distance;
                    float dz = direction.y * distance;
                    // The slope under the player predicts where to look for the same surface.
                    float expectedY = center.y - (normal.x * dx + normal.z * dz) / normal.y;
                    Vector3 expected = new Vector3(center.x + dx, expectedY, center.z + dz);
                    int index = segment * RadialSampleCount + radial;
                    sampleValid[index] = TrySampleGround(physicsScene, expected, ringProbeRange,
                        ringProbeRange, minNormalY, out RaycastHit hit);
                    surfacePoints[index] = hit.point;
                    surfaceNormals[index] = hit.normal;
                    vertices[index] = sampleValid[index]
                        ? worldToLocal.MultiplyPoint3x4(hit.point + Vector3.up * surfaceOffset)
                        : Vector3.zero;
                }
            }

            // Duplicate the seam positions so the animated UV can wrap without a gap.
            int seam = SegmentCount * RadialSampleCount;
            for (int radial = 0; radial < RadialSampleCount; radial++)
            {
                sampleValid[seam + radial] = sampleValid[radial];
                surfacePoints[seam + radial] = surfacePoints[radial];
                surfaceNormals[seam + radial] = surfaceNormals[radial];
                vertices[seam + radial] = vertices[radial];
            }
        }

        private bool TrySampleGround(PhysicsScene physicsScene, Vector3 expected,
            float probeAbove, float probeBelow, float minNormalY, out RaycastHit closest)
        {
            int hitCount = physicsScene.Raycast(expected + Vector3.up * probeAbove, Vector3.down,
                groundHits, probeAbove + probeBelow, groundLayers, QueryTriggerInteraction.Ignore);
            closest = default;
            float closestHeightError = float.PositiveInfinity;
            if (hitCount == groundHits.Length)
                return false;

            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = groundHits[index];
                Collider collider = hit.collider;
                if (hit.normal.y < minNormalY || collider is CharacterController
                    || (groundScene.HasValue && collider.gameObject.scene != groundScene.Value)
                    || (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
                    || (ignoredRoot != null && collider.transform.IsChildOf(ignoredRoot)))
                {
                    continue;
                }

                float heightError = Mathf.Abs(hit.point.y - expected.y);
                if (heightError < closestHeightError
                    && (groundColliderFilter == null || groundColliderFilter(collider)))
                {
                    closestHeightError = heightError;
                    closest = hit;
                }
            }

            return closest.collider != null;
        }

        private int BuildRingIndices(PhysicsScene physicsScene, float minNormalY)
        {
            int count = 0;
            for (int segment = 0; segment < SegmentCount; segment++)
            {
                for (int radial = 0; radial < RadialSampleCount - 1; radial++)
                {
                    int inner = segment * RadialSampleCount + radial;
                    int nextInner = inner + RadialSampleCount;
                    AddTriangle(physicsScene, minNormalY, inner, nextInner, inner + 1, ref count);
                    AddTriangle(physicsScene, minNormalY, inner + 1, nextInner, nextInner + 1, ref count);
                }
            }
            return count;
        }

        private void AddTriangle(PhysicsScene physicsScene, float minNormalY,
            int a, int b, int c, ref int count)
        {
            if (!sampleValid[a] || !sampleValid[b] || !sampleValid[c]
                || !IsContinuous(a, b) || !IsContinuous(b, c) || !IsContinuous(c, a))
            {
                return;
            }

            // A triangle can cut through a ridge even when its corners clear the ground.
            Vector3 center = (surfacePoints[a] + surfacePoints[b] + surfacePoints[c]) / 3f;
            if (!TrySampleGround(physicsScene, center, ringProbeRange, ringProbeRange,
                    minNormalY, out RaycastHit ground)
                || Mathf.Abs(ground.point.y - center.y) > Mathf.Min(surfaceOffset, surfaceTolerance))
            {
                return;
            }

            indices[count++] = a;
            indices[count++] = b;
            indices[count++] = c;
        }

        private bool IsContinuous(int from, int to)
        {
            Vector3 delta = surfacePoints[to] - surfacePoints[from];
            float fromRise = GetSurfaceRise(surfaceNormals[from], delta);
            float toRise = GetSurfaceRise(surfaceNormals[to], delta);
            // A step between flat surfaces cannot be joined by an interpolated slope.
            return delta.y >= Mathf.Min(fromRise, toRise) - surfaceTolerance
                && delta.y <= Mathf.Max(fromRise, toRise) + surfaceTolerance;
        }

        private static float GetSurfaceRise(Vector3 normal, Vector3 delta)
        {
            return -(normal.x * delta.x + normal.z * delta.z) / normal.y;
        }
    }
}
