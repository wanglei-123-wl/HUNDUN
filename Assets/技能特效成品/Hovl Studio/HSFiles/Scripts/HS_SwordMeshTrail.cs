using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Hovl
{
    /// <summary>
    /// Generates a procedural ribbon mesh between two points on a moving object.
    /// Add this component to a sword or another object that contains only the
    /// geometry that should be used for automatic point placement.
    ///
    /// Animation Events can call StartTrail() and StopTrail().
    /// </summary>
    [DisallowMultipleComponent]
    public class HS_SwordMeshTrail : MonoBehaviour
    {
        public enum AutomaticAxis
        {
            [InspectorName("最长轴")]
            Longest = 0,
            [InspectorName("本地 X 轴")]
            LocalX = 1,
            [InspectorName("本地 Y 轴")]
            LocalY = 2,
            [InspectorName("本地 Z 轴")]
            LocalZ = 3
        }

        [Header("预设")]
        [Tooltip("选择预设后，材质层、持续时间和淡出曲线等取自预设。修改预设后，可在组件菜单中点「应用刀光预设」。")]
        [SerializeField] private HS_SwordTrailPreset preset;

        [Header("拖尾")]
        [Tooltip("未使用预设的材质层时，使用这里的材质。已配置预设时可留空。")]
        [SerializeField] private Material trailMaterial;

        [Tooltip("使用组件材质时，沿用这里填写的着色器数值属性，例如 _Dissolve。停止生成拖尾后，使用该属性做溶解。")]
        [SerializeField] private string dissolvePropertyName = "_Dissolve";

        [Tooltip("停止生成拖尾后，在持续时间内渐变到的溶解值。最小变化范围由材质的初始值决定，填 0 时沿用材质的溶解值。")]
        [SerializeField, Range(0f, 1f)] private float maximumDissolve;

        [Tooltip("每一段拖尾从生成到消失的时间，单位为秒。")]
        [SerializeField, Min(0.01f)] private float trailLifetime = 0.35f;

        [Tooltip("A、B 任意一点移动达到此距离后，才会新增一段拖尾。")]
        [SerializeField, Min(0f)] private float minimumSectionDistance = 0.015f;

        [Tooltip("两次采样的最短时间间隔。填 0 可逐帧采样。")]
        [SerializeField, Min(0f)] private float sampleInterval = 0f;

        [Tooltip("在 A、B 之间增加的网格细分线数。开启自定义中间点后，也决定中间点的数量。")]
        [SerializeField, Range(0, 10)] private int linesAlongTrail = 2;

        [Header("自定义中间点")]
        [Tooltip("开启后，可拖动子物体来调整每个中间点。关闭时，中间点均匀分布在 A、B 之间。")]
        public bool useCustomIntermediatePoints = false;

        [Tooltip("按横向细分线数自动生成。可以单独移动每个中间点。")]
        [SerializeField]
        private List<Transform> customIntermediatePoints =
            new List<Transform>();

        [Tooltip("开启后，各段拖尾会按淡出曲线改变透明度。")]
        [SerializeField] private bool fadeAlphaOverLifetime = true;

        [Tooltip("曲线左侧是刚生成的拖尾，右侧是快消失的拖尾。材质需支持顶点透明度。")]
        [SerializeField]
        private AnimationCurve alphaOverLifetime =
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

        [SerializeField] private bool startActive;
        [SerializeField] private bool clearPreviousTrailOnStart = true;

        [Header("移动端优化")]
        [Tooltip("降低移动端开销。限制采样、网格刷新和溶解更新频率，减少细分和平滑段，关闭阴影。")]
        [SerializeField] private bool optimizeForMobile;

        [Header("低帧率平滑")]
        [Tooltip("帧率较低时，在采样点之间补出曲线，让拖尾过渡更平滑。")]
        [SerializeField] private bool smoothLowFps = true;

        [Tooltip("平滑曲线上相邻网格段的大致最大距离。值越小，补出的轨迹越平滑。")]
        [SerializeField, Min(0.001f)] private float maximumSmoothedSectionDistance = 0.08f;

        [Tooltip("相邻两次采样之间最多补出的网格段数。")]
        [SerializeField, Range(0, 32)] private int maxIntermediateSectionsPerFrame = 8;

        [Header("拖尾端点")]
        [Tooltip("刀尖端的采样位置。未指定时，组件会创建端点物体。")]
        [SerializeField] private Transform pointA;
        [Tooltip("靠近护手的采样位置。A、B 两点之间形成刀光的宽度。")]
        [SerializeField] private Transform pointB;

        [Tooltip("选择「最长轴」时，沿模型包围盒最长的本地轴放置端点。")]
        [SerializeField] private AutomaticAxis automaticAxis = AutomaticAxis.Longest;

        [Tooltip("启动时按模型大小重新定位端点。手动对齐刀尖和刀刃根部后，请关闭此项。")]
        [SerializeField] private bool recalculatePointsOnAwake = true;

        [Tooltip("自动定位时，将 A 点从端点向内移动。")]
        [SerializeField, Min(0f)] private float pointAInset;

        [Tooltip("自动定位时，将 B 点从端点向内移动。")]
        [SerializeField, Min(0f)] private float pointBInset;

        [Tooltip("正值向两端延长，负值向两端缩短。")]
        [SerializeField] private float endpointPadding;

        [Header("渲染")]
        [SerializeField] private int sortingOrder;
        [SerializeField] private bool receiveShadows;
        [SerializeField] private ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;

        [SerializeField, HideInInspector]
        private bool lockAutomaticEditorUpdates = false;

        private const string TrailTopName = "Trail Top";
        private const string TrailBottomName = "Trail Bottom";
        private const string IntermediatePointNamePrefix =
            "Trail Intermediate Point ";
        private const float MobileUpdateInterval = 1f / 30f;
        private const int MobileMaximumLinesAlongTrail = 1;
        private const int MobileMaximumIntermediateSections = 2;
        private const float MobileMinimumSmoothedSectionDistance = 0.12f;

        private sealed class RuntimeTrailLayer
        {
            public GameObject gameObject;
            public MeshFilter meshFilter;
            public MeshRenderer meshRenderer;
            public MaterialPropertyBlock propertyBlock;
            public Material configuredMaterial;
            public string dissolvePropertyName;
            public int dissolvePropertyId;
            public float startingDissolveValue;
            public float maximumDissolve;
            public float lastDissolveValue = float.NaN;
            public bool controlsDissolve;
            public bool missingDissolvePropertyWarningShown;
        }

        private struct TrailSection
        {
            public Vector3 pointA;
            public Vector3 pointB;
            public float spawnTime;
            public float distance;

            public TrailSection(Vector3 pointA, Vector3 pointB, float spawnTime, float distance)
            {
                this.pointA = pointA;
                this.pointB = pointB;
                this.spawnTime = spawnTime;
                this.distance = distance;
            }
        }

        private readonly List<TrailSection> sections = new List<TrailSection>(128);
        private readonly List<TrailSection> smoothedSections = new List<TrailSection>(256);
        // Keep the optional buffers without a backing array until custom points
        // are actually used. Disabled trails therefore do not reserve the extra
        // sample memory.
        private readonly List<Vector3> sectionIntermediatePositions =
            new List<Vector3>();
        private readonly List<Vector3> smoothedIntermediatePositions =
            new List<Vector3>();
        private readonly List<Vector3> vertices = new List<Vector3>(512);
        private readonly List<Vector3> normals = new List<Vector3>(512);
        private readonly List<Vector2> uvs = new List<Vector2>(512);
        private readonly List<Color> colors = new List<Color>(512);
        private readonly List<int> triangles = new List<int>(768);

        private readonly List<RuntimeTrailLayer> runtimeLayers =
            new List<RuntimeTrailLayer>();

        [SerializeField, HideInInspector]
        private List<GameObject> presetPointAEffectInstances = new List<GameObject>();

        [SerializeField, HideInInspector]
        private HS_SwordTrailPreset appliedEffectPreset;

        private Mesh generatedMesh;

        private bool isEmitting;
        private float lastSampleTime = float.NegativeInfinity;
        private float trailStopTime = float.NegativeInfinity;
        private float nextMobileMeshUpdateTime = float.NegativeInfinity;
        private float nextMobileDissolveUpdateTime = float.NegativeInfinity;
        private bool dissolveWasStarted;
        private bool missingMaterialWarningShown;
        private bool meshContainsGeometry;
        private bool rendererSettingsDirty = true;
        private int sampledIntermediatePointCount;
        private List<Vector3> activeRenderIntermediatePositions;
        private bool trailPointValidationQueued;

        // Automatically refreshed from all descendants of Trail Top.
        // No ParticleSystem references need to be assigned in the Inspector.
        private ParticleSystem[] pointAParticleSystems = System.Array.Empty<ParticleSystem>();

        public Transform PointA => pointA;
        public Transform PointB => pointB;
        public bool IsEmitting => isEmitting;
        public HS_SwordTrailPreset Preset => preset;
        public bool OptimizeForMobile => optimizeForMobile;
        public bool UseCustomIntermediatePoints => useCustomIntermediatePoints;
        public bool LockAutomaticEditorUpdates => lockAutomaticEditorUpdates;

        private void Reset()
        {
            EnsureTrailPoints();
            RecalculateTrailPoints();
            EnsureCustomIntermediatePoints();
            EnsureAnimationEventsComponent();
        }

        private void Awake()
        {
            ApplyPresetValues();
            EnsureTrailPoints();
            EnsureAnimationEventsComponent();
            RefreshPresetPointAEffects();
            RefreshPointAParticleSystems();

            if (recalculatePointsOnAwake)
            {
                RecalculateTrailPoints();
            }

            EnsureCustomIntermediatePoints();

            EnsureTrailObjects();
        }

        /// <summary>
        /// Finds the shared animation-event receiver in the parent hierarchy
        /// and registers this trail without replacing trails from other swords.
        /// If there is no Animator, a receiver is created on the hierarchy root
        /// and switched to continuous Work Without Animation mode.
        /// </summary>
        [ContextMenu("创建或连接动画事件组件")]
        public void EnsureAnimationEventsComponent()
        {
            Animator animator = GetComponentInParent<Animator>(true);
            HS_SwordTrailAnimationEvents animationEvents;
            GameObject receiverObject;

            if (animator != null)
            {
                receiverObject = animator.gameObject;
                animationEvents =
                    receiverObject.GetComponent<HS_SwordTrailAnimationEvents>();
            }
            else
            {
                animationEvents =
                    GetComponentInParent<HS_SwordTrailAnimationEvents>(true);
                receiverObject = animationEvents != null
                    ? animationEvents.gameObject
                    : transform.root.gameObject;
            }

            if (animationEvents == null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    animationEvents = Undo.AddComponent<HS_SwordTrailAnimationEvents>(
                        receiverObject);
                }
                else
#endif
                {
                    animationEvents =
                        receiverObject.AddComponent<HS_SwordTrailAnimationEvents>();
                }
            }

            animationEvents.RegisterSwordTrail(this);

            if (animator == null)
            {
                animationEvents.SetWorkWithoutAnimation(true);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(animationEvents);
            }
#endif
        }

        /// <summary>
        /// Copies all reusable settings from the assigned preset, rebuilds its
        /// material layers and refreshes the effect prefabs under Trail Top.
        /// </summary>
        [ContextMenu("应用刀光预设")]
        public void ApplyTrailPreset()
        {
            ApplyPresetValues();
            EnsureTrailPoints();

            if (recalculatePointsOnAwake)
            {
                RecalculateTrailPoints();
            }

            EnsureCustomIntermediatePoints();

            RefreshPresetPointAEffects();
            DestroyRuntimeLayers();

            if (Application.isPlaying)
            {
                EnsureTrailObjects();
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
            }
#endif
        }

        private void ApplyPresetValues()
        {
            if (preset == null)
            {
                return;
            }

            trailLifetime = preset.trailLifetime;
            minimumSectionDistance = preset.minimumSectionDistance;
            sampleInterval = preset.sampleInterval;
            linesAlongTrail = preset.linesAlongTrail;
            fadeAlphaOverLifetime = preset.fadeAlphaOverLifetime;
            alphaOverLifetime = preset.alphaOverLifetime;
            startActive = preset.startActive;
            clearPreviousTrailOnStart = preset.clearPreviousTrailOnStart;
            optimizeForMobile = preset.optimizeForMobile;
            smoothLowFps = preset.smoothLowFps;
            maximumSmoothedSectionDistance = preset.maximumSmoothedSectionDistance;
            maxIntermediateSectionsPerFrame = preset.maxIntermediateSectionsPerFrame;
            automaticAxis = (AutomaticAxis)preset.automaticAxis;
            recalculatePointsOnAwake = preset.recalculatePointsOnAwake;
            pointAInset = preset.pointAInset;
            pointBInset = preset.pointBInset;
            endpointPadding = preset.endpointPadding;
            receiveShadows = preset.receiveShadows;
            shadowCastingMode = preset.shadowCastingMode;
            rendererSettingsDirty = true;
        }

        private void OnEnable()
        {
            if (Application.isPlaying && startActive)
            {
                StartTrail();
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            float currentTime = Time.time;
            bool hasPendingDissolve =
                dissolveWasStarted &&
                !isEmitting &&
                !float.IsNegativeInfinity(trailStopTime);

            // A stopped and completely faded trail sleeps here. Particle
            // Systems continue simulating independently and need no polling.
            if (!isEmitting && sections.Count == 0 && !hasPendingDissolve)
            {
                ClearGeneratedMeshIfNeeded();
                return;
            }

            EnsureTrailObjects();

            if (isEmitting)
            {
                TryAddSection(currentTime, false);
            }

            RemoveExpiredSections(currentTime);

            bool shouldUpdateMesh =
                !optimizeForMobile ||
                currentTime >= nextMobileMeshUpdateTime;

            if (sections.Count >= 2 && shouldUpdateMesh)
            {
                RebuildMesh(currentTime);
                nextMobileMeshUpdateTime =
                    currentTime + MobileUpdateInterval;
            }
            else if (sections.Count < 2)
            {
                ClearGeneratedMeshIfNeeded();
            }

            if (hasPendingDissolve &&
                (!optimizeForMobile ||
                 currentTime >= nextMobileDissolveUpdateTime))
            {
                UpdateDissolveProperties(currentTime);
                nextMobileDissolveUpdateTime =
                    currentTime + MobileUpdateInterval;
            }
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            isEmitting = false;
            StopPointAParticleSystems();
            ClearTrail();
        }

        private void OnDestroy()
        {
            if (generatedMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(generatedMesh);
                }
                else
                {
                    DestroyImmediate(generatedMesh);
                }
            }

            DestroyRuntimeLayers();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // A finalized scene can opt out of every automatic Edit Mode
            // mutation. Runtime initialization in Awake remains unaffected.
            if (!Application.isPlaying && lockAutomaticEditorUpdates)
            {
                rendererSettingsDirty = true;
                return;
            }
#endif

            ApplyPresetValues();
            trailLifetime = Mathf.Max(0.01f, trailLifetime);
            minimumSectionDistance = Mathf.Max(0f, minimumSectionDistance);
            sampleInterval = Mathf.Max(0f, sampleInterval);
            linesAlongTrail = Mathf.Clamp(linesAlongTrail, 0, 10);
            maximumSmoothedSectionDistance =
                Mathf.Max(0.001f, maximumSmoothedSectionDistance);
            maxIntermediateSectionsPerFrame =
                Mathf.Clamp(maxIntermediateSectionsPerFrame, 0, 32);
            maximumDissolve = Mathf.Clamp01(maximumDissolve);
            pointAInset = Mathf.Max(0f, pointAInset);
            pointBInset = Mathf.Max(0f, pointBInset);

            rendererSettingsDirty = true;

            if (runtimeLayers.Count > 0)
            {
                ApplyRendererSettings();
                rendererSettingsDirty = false;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying && appliedEffectPreset != preset)
            {
                EditorApplication.delayCall += ApplyPresetAfterValidation;
            }

            if (!Application.isPlaying &&
                !trailPointValidationQueued)
            {
                trailPointValidationQueued = true;
                EditorApplication.delayCall +=
                    EnsureTrailPointsAfterValidation;
            }
#endif
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (pointA == null || pointB == null)
            {
                return;
            }

            List<Vector3> arrowPoints = new List<Vector3>();
            arrowPoints.Add(pointB.position);

            if (useCustomIntermediatePoints &&
                customIntermediatePoints != null)
            {
                int usedPointCount = Mathf.Min(
                    linesAlongTrail,
                    customIntermediatePoints.Count);

                for (int i = usedPointCount - 1; i >= 0; i--)
                {
                    Transform intermediatePoint = customIntermediatePoints[i];
                    if (intermediatePoint != null)
                    {
                        arrowPoints.Add(intermediatePoint.position);
                    }
                }
            }

            arrowPoints.Add(pointA.position);

            Vector3 arrowDirection = Vector3.zero;

            for (int i = arrowPoints.Count - 2; i >= 0; i--)
            {
                arrowDirection = pointA.position - arrowPoints[i];
                if (arrowDirection.sqrMagnitude > 0.00000001f)
                {
                    break;
                }
            }

            float arrowLength = arrowDirection.magnitude;

            if (arrowLength <= 0.0001f)
            {
                return;
            }

            Handles.color = new Color(0.1f, 0.9f, 1f, 0.95f);
            Handles.DrawAAPolyLine(3f, arrowPoints.ToArray());

            Vector3 direction = arrowDirection / arrowLength;
            Vector3 cameraForward = SceneView.currentDrawingSceneView != null &&
                                    SceneView.currentDrawingSceneView.camera != null
                ? SceneView.currentDrawingSceneView.camera.transform.forward
                : Vector3.forward;

            Vector3 arrowSide = Vector3.Cross(direction, cameraForward);
            if (arrowSide.sqrMagnitude <= 0.0001f)
            {
                arrowSide = Vector3.Cross(direction, Vector3.up);
            }

            if (arrowSide.sqrMagnitude <= 0.0001f)
            {
                arrowSide = Vector3.Cross(direction, Vector3.right);
            }

            arrowSide.Normalize();

            float headLength = Mathf.Min(
                arrowLength * 0.25f,
                HandleUtility.GetHandleSize(pointA.position) * 0.2f);

            Vector3 headBase =
                pointA.position - direction * headLength;
            Vector3 headWidth = arrowSide * headLength * 0.5f;

            // The first vertex is exactly Point A, so the arrow never extends
            // beyond the configured end of the trail.
            Handles.DrawAAConvexPolygon(
                pointA.position,
                headBase + headWidth,
                headBase - headWidth);
        }

        private void ApplyPresetAfterValidation()
        {
            if (this != null &&
                !Application.isPlaying &&
                !lockAutomaticEditorUpdates &&
                appliedEffectPreset != preset)
            {
                ApplyTrailPreset();
            }
        }

        private void EnsureTrailPointsAfterValidation()
        {
            trailPointValidationQueued = false;

            if (this != null &&
                !Application.isPlaying &&
                !lockAutomaticEditorUpdates)
            {
                EnsureTrailPoints();
                EnsureCustomIntermediatePoints();
                EditorUtility.SetDirty(this);
            }
        }
#endif

        /// <summary>
        /// Starts generating trail sections. Suitable for a Unity Animation Event.
        /// </summary>
        public void StartTrail()
        {
            EnsureTrailPoints();
            EnsureCustomIntermediatePoints();
            EnsureTrailObjects();

            if (isEmitting)
            {
                return;
            }

            if (clearPreviousTrailOnStart)
            {
                ClearTrail();
            }

            if (!HasAnyTrailMaterial() && !missingMaterialWarningShown)
            {
                Debug.LogWarning(
                    $"{nameof(HS_SwordMeshTrail)} on '{name}' has no Trail Material assigned.",
                    this);
                missingMaterialWarningShown = true;
            }

            isEmitting = true;
            lastSampleTime = float.NegativeInfinity;
            trailStopTime = float.NegativeInfinity;
            nextMobileMeshUpdateTime = float.NegativeInfinity;
            nextMobileDissolveUpdateTime = float.NegativeInfinity;
            dissolveWasStarted = true;

            // Always restore the dissolve override from the material before
            // the first trail section can be rendered. This prevents a value
            // left in the MaterialPropertyBlock by the previous trail from
            // becoming the next trail's starting value.
            RefreshDissolveStartValues();
            UpdateDissolveProperties(Time.time, true);

            PlayPointAParticleSystems();
            TryAddSection(Time.time, true);
        }

        /// <summary>
        /// Stops generating new sections. Existing sections disappear according to Trail Lifetime.
        /// Suitable for a Unity Animation Event.
        /// </summary>
        public void StopTrail()
        {
            if (isEmitting)
            {
                trailStopTime = Time.time;
                nextMobileDissolveUpdateTime = trailStopTime;
            }

            isEmitting = false;
            StopPointAParticleSystems();
        }

        /// <summary>
        /// Finds every ParticleSystem below Trail Top, including systems on
        /// inactive child objects. This is refreshed automatically whenever the
        /// trail starts or stops, so manually adding effect prefabs requires no
        /// Inspector references.
        /// </summary>
        private void RefreshPointAParticleSystems()
        {
            if (pointA == null)
            {
                pointAParticleSystems = System.Array.Empty<ParticleSystem>();
                return;
            }

            pointAParticleSystems = pointA.GetComponentsInChildren<ParticleSystem>(true);
        }

        private void PlayPointAParticleSystems()
        {
            RefreshPointAParticleSystems();

            // Enable top-level effect objects below Point A first. This also
            // activates complete prefabs whose root object was disabled.
            for (int i = 0; i < pointA.childCount; i++)
            {
                Transform child = pointA.GetChild(i);

                if (child.GetComponentInChildren<ParticleSystem>(true) != null &&
                    !child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(true);
                }
            }

            // Refresh once more because enabling effect roots can initialize
            // additional child ParticleSystems.
            RefreshPointAParticleSystems();

            for (int i = 0; i < pointAParticleSystems.Length; i++)
            {
                ParticleSystem particleSystem = pointAParticleSystems[i];

                if (particleSystem == null)
                {
                    continue;
                }

                // Preserve the configured hierarchy, but ensure the actual
                // ParticleSystem object itself is enabled.
                if (!particleSystem.gameObject.activeSelf)
                {
                    particleSystem.gameObject.SetActive(true);
                }

                particleSystem.Clear(true);
                particleSystem.Play(true);
            }
        }

        private void StopPointAParticleSystems()
        {
            RefreshPointAParticleSystems();

            for (int i = 0; i < pointAParticleSystems.Length; i++)
            {
                ParticleSystem particleSystem = pointAParticleSystems[i];

                if (particleSystem == null)
                {
                    continue;
                }

                // Stop only new emission. Existing particles remain alive and
                // disappear naturally according to their configured lifetime.
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// Immediately removes the complete generated trail.
        /// </summary>
        public void ClearTrail()
        {
            sections.Clear();
            sectionIntermediatePositions.Clear();
            smoothedIntermediatePositions.Clear();
            activeRenderIntermediatePositions = null;
            sampledIntermediatePointCount = 0;
            lastSampleTime = float.NegativeInfinity;
            nextMobileMeshUpdateTime = float.NegativeInfinity;

            if (generatedMesh != null)
            {
                generatedMesh.Clear(false);
            }

            meshContainsGeometry = false;
        }

        private void ClearGeneratedMeshIfNeeded()
        {
            if (!meshContainsGeometry || generatedMesh == null)
            {
                return;
            }

            generatedMesh.Clear(false);
            meshContainsGeometry = false;
        }

        /// <summary>
        /// Creates missing child points and places them at the ends of the selected local bounds axis.
        /// This method is also available from the component context menu.
        /// </summary>
        [ContextMenu("重新计算拖尾端点")]
        public void RecalculateTrailPoints()
        {
            EnsureTrailPoints();

            if (!TryCalculateLocalGeometryBounds(out Bounds localBounds))
            {
                localBounds = new Bounds(Vector3.zero, Vector3.one);
                Debug.LogWarning(
                    $"{nameof(HS_SwordMeshTrail)} could not find a MeshFilter, SkinnedMeshRenderer, " +
                    $"Renderer, or Collider under '{name}'. Default point positions were used.",
                    this);
            }

            int axisIndex = GetAxisIndex(localBounds.size);
            Vector3 axis = AxisVector(axisIndex);
            float extent = GetVectorComponent(localBounds.extents, axisIndex);

            float negativeDistance = Mathf.Max(0f, extent + endpointPadding - pointAInset);
            float positiveDistance = Mathf.Max(0f, extent + endpointPadding - pointBInset);

            pointA.localPosition = localBounds.center - axis * negativeDistance;
            pointB.localPosition = localBounds.center + axis * positiveDistance;

            pointA.localRotation = Quaternion.identity;
            pointB.localRotation = Quaternion.identity;
            pointA.localScale = Vector3.one;
            pointB.localScale = Vector3.one;

            EnsureCustomIntermediatePoints();
        }

        [ContextMenu("补建 A 点和 B 点")]
        public void EnsureTrailPoints()
        {
            if (pointA == null)
            {
                Transform existing = transform.Find(TrailTopName);
                pointA = existing != null
                    ? existing
                    : CreateChildPoint(TrailTopName);
            }

            if (pointB == null)
            {
                Transform existing = transform.Find(TrailBottomName);
                pointB = existing != null
                    ? existing
                    : CreateChildPoint(TrailBottomName);
            }
        }

        private Transform CreateChildPoint(string pointName)
        {
            GameObject pointObject = new GameObject(pointName);
            Transform pointTransform = pointObject.transform;
            pointTransform.SetParent(transform, false);
            pointTransform.localPosition = Vector3.zero;
            pointTransform.localRotation = Quaternion.identity;
            pointTransform.localScale = Vector3.one;
            return pointTransform;
        }

        [ContextMenu("补建自定义中间点")]
        public void EnsureCustomIntermediatePoints()
        {
            if (!useCustomIntermediatePoints || linesAlongTrail <= 0)
            {
                return;
            }

            EnsureTrailPoints();

            if (customIntermediatePoints == null)
            {
                customIntermediatePoints = new List<Transform>();
            }

            while (customIntermediatePoints.Count < linesAlongTrail)
            {
                customIntermediatePoints.Add(null);
            }

            for (int i = 0; i < linesAlongTrail; i++)
            {
                if (customIntermediatePoints[i] != null)
                {
                    continue;
                }

                string pointName = IntermediatePointNamePrefix + (i + 1);
                Transform intermediatePoint = transform.Find(pointName);

                if (intermediatePoint == null)
                {
                    intermediatePoint = CreateChildPoint(pointName);
                    float widthV = (float)(i + 1) / (linesAlongTrail + 1);
                    intermediatePoint.localPosition = Vector3.Lerp(
                        pointA.localPosition,
                        pointB.localPosition,
                        widthV);
                }

                customIntermediatePoints[i] = intermediatePoint;
            }
        }

        private int GetCustomIntermediatePointCountForSampling()
        {
            if (!useCustomIntermediatePoints ||
                linesAlongTrail <= 0 ||
                customIntermediatePoints == null)
            {
                return 0;
            }

            int configuredPointCount = Mathf.Min(
                linesAlongTrail,
                customIntermediatePoints.Count);

            return optimizeForMobile
                ? Mathf.Min(
                    configuredPointCount,
                    MobileMaximumLinesAlongTrail)
                : configuredPointCount;
        }

        private Vector3 GetCurrentIntermediatePointPosition(
            int intermediateIndex,
            int intermediateCount)
        {
            float widthV =
                (float)(intermediateIndex + 1) / (intermediateCount + 1);
            return EvaluateCurrentCustomShape(widthV);
        }

        private Vector3 EvaluateCurrentCustomShape(float widthV)
        {
            int configuredPointCount = Mathf.Min(
                linesAlongTrail,
                customIntermediatePoints.Count);
            int shapeSegmentCount = configuredPointCount + 1;
            float scaledPosition =
                Mathf.Clamp01(widthV) * shapeSegmentCount;
            int leftControlIndex = Mathf.Min(
                Mathf.FloorToInt(scaledPosition),
                shapeSegmentCount);
            int rightControlIndex = Mathf.Min(
                leftControlIndex + 1,
                shapeSegmentCount);
            float interpolation = scaledPosition - leftControlIndex;

            return Vector3.Lerp(
                GetCurrentCustomControlPoint(
                    leftControlIndex,
                    configuredPointCount),
                GetCurrentCustomControlPoint(
                    rightControlIndex,
                    configuredPointCount),
                interpolation);
        }

        private Vector3 GetCurrentCustomControlPoint(
            int controlIndex,
            int configuredPointCount)
        {
            if (controlIndex <= 0)
            {
                return pointA.position;
            }

            if (controlIndex > configuredPointCount)
            {
                return pointB.position;
            }

            Transform intermediatePoint =
                customIntermediatePoints[controlIndex - 1];

            if (intermediatePoint != null)
            {
                return intermediatePoint.position;
            }

            float widthV =
                (float)controlIndex / (configuredPointCount + 1);
            return Vector3.Lerp(pointA.position, pointB.position, widthV);
        }

        private void EnsureTrailObjects()
        {
            if (generatedMesh == null)
            {
                generatedMesh = new Mesh
                {
                    name = $"{name} - Runtime Trail Mesh"
                };
                generatedMesh.MarkDynamic();
            }

            int desiredLayerCount = GetConfiguredLayerCount();
            bool rebuildLayers = runtimeLayers.Count != desiredLayerCount;

            if (!rebuildLayers)
            {
                for (int i = 0; i < runtimeLayers.Count; i++)
                {
                    RuntimeTrailLayer layer = runtimeLayers[i];
                    if (layer == null || layer.gameObject == null ||
                        layer.meshFilter == null || layer.meshRenderer == null)
                    {
                        rebuildLayers = true;
                        break;
                    }
                }
            }

            if (rebuildLayers)
            {
                DestroyRuntimeLayers();

                for (int i = 0; i < desiredLayerCount; i++)
                {
                    GameObject layerObject = new GameObject(
                        $"{name} - Generated Mesh Trail - Layer {i + 1}");

                    layerObject.transform.SetPositionAndRotation(
                        Vector3.zero,
                        Quaternion.identity);
                    layerObject.transform.localScale = Vector3.one;

                    if (gameObject.scene.IsValid())
                    {
                        SceneManager.MoveGameObjectToScene(
                            layerObject,
                            gameObject.scene);
                    }

                    MeshFilter meshFilter = layerObject.AddComponent<MeshFilter>();
                    MeshRenderer meshRenderer = layerObject.AddComponent<MeshRenderer>();
                    meshFilter.sharedMesh = generatedMesh;

                    runtimeLayers.Add(new RuntimeTrailLayer
                    {
                        gameObject = layerObject,
                        meshFilter = meshFilter,
                        meshRenderer = meshRenderer,
                        propertyBlock = new MaterialPropertyBlock()
                    });
                }

                rendererSettingsDirty = true;
            }

            if (rendererSettingsDirty)
            {
                ApplyRendererSettings();
                rendererSettingsDirty = false;
            }
        }

        private void ApplyRendererSettings()
        {
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                RuntimeTrailLayer layer = runtimeLayers[i];
                if (layer == null || layer.meshRenderer == null)
                {
                    continue;
                }

                MeshRenderer renderer = layer.meshRenderer;
                Material material = GetLayerMaterial(i);
                renderer.sharedMaterial = material;
                renderer.sortingOrder = GetLayerSortingOrder(i);
                renderer.receiveShadows =
                    optimizeForMobile ? false : receiveShadows;
                renderer.shadowCastingMode = optimizeForMobile
                    ? ShadowCastingMode.Off
                    : shadowCastingMode;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;

                ConfigureLayerDissolve(layer, i, material);
            }
        }

        private void ConfigureLayerDissolve(
            RuntimeTrailLayer runtimeLayer,
            int layerIndex,
            Material material)
        {
            string propertyName = GetLayerDissolvePropertyName(layerIndex);
            float layerMaximumDissolve =
                GetLayerMaximumDissolve(layerIndex);

            bool configurationChanged =
                runtimeLayer.configuredMaterial != material ||
                runtimeLayer.dissolvePropertyName != propertyName ||
                !Mathf.Approximately(
                    runtimeLayer.maximumDissolve,
                    layerMaximumDissolve);

            if (!configurationChanged)
            {
                return;
            }

            runtimeLayer.configuredMaterial = material;
            runtimeLayer.dissolvePropertyName = propertyName;
            runtimeLayer.maximumDissolve = layerMaximumDissolve;
            runtimeLayer.lastDissolveValue = float.NaN;
            runtimeLayer.missingDissolvePropertyWarningShown = false;

            // A maximum of zero disables the feature completely. In that case
            // the renderer property is never read, reset, or overwritten.
            runtimeLayer.controlsDissolve =
                layerMaximumDissolve > 0f &&
                !string.IsNullOrWhiteSpace(propertyName) &&
                material != null &&
                material.HasProperty(propertyName);

            if (runtimeLayer.controlsDissolve)
            {
                runtimeLayer.dissolvePropertyId =
                    Shader.PropertyToID(propertyName);

                runtimeLayer.startingDissolveValue =
                    ReadDissolveValueFromMaterial(runtimeLayer);
                return;
            }

            if (layerMaximumDissolve > 0f &&
                !string.IsNullOrWhiteSpace(propertyName) &&
                material != null &&
                !runtimeLayer.missingDissolvePropertyWarningShown)
            {
                Debug.LogWarning(
                    $"Material '{material.name}' on trail layer {layerIndex + 1} " +
                    $"does not contain the shader property '{propertyName}'.",
                    this);

                runtimeLayer.missingDissolvePropertyWarningShown = true;
            }
        }

        private void RefreshDissolveStartValues()
        {
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                RuntimeTrailLayer layer = runtimeLayers[i];
                if (layer == null || !layer.controlsDissolve ||
                    layer.configuredMaterial == null)
                {
                    continue;
                }

                // Read from the material currently assigned to the generated
                // renderer, not from the MaterialPropertyBlock and not from a
                // value cached by a previous trail.
                layer.startingDissolveValue =
                    ReadDissolveValueFromMaterial(layer);

                layer.lastDissolveValue = float.NaN;
            }
        }

        private static float ReadDissolveValueFromMaterial(
            RuntimeTrailLayer runtimeLayer)
        {
            Material material = runtimeLayer.meshRenderer != null
                ? runtimeLayer.meshRenderer.sharedMaterial
                : null;

            if (material == null ||
                !material.HasProperty(runtimeLayer.dissolvePropertyId))
            {
                material = runtimeLayer.configuredMaterial;
            }

            return material != null &&
                   material.HasProperty(runtimeLayer.dissolvePropertyId)
                ? material.GetFloat(runtimeLayer.dissolvePropertyId)
                : runtimeLayer.startingDissolveValue;
        }

        private void UpdateDissolveProperties(
            float currentTime,
            bool force = false)
        {
            if (!dissolveWasStarted)
            {
                return;
            }

            float dissolveProgress = 0f;

            if (!isEmitting && !float.IsNegativeInfinity(trailStopTime))
            {
                dissolveProgress = Mathf.Clamp01(
                    (currentTime - trailStopTime) / trailLifetime);
            }

            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                RuntimeTrailLayer layer = runtimeLayers[i];
                if (layer == null || layer.meshRenderer == null ||
                    !layer.controlsDissolve)
                {
                    continue;
                }

                float targetDissolveValue = Mathf.Max(
                    layer.startingDissolveValue,
                    layer.maximumDissolve);

                float dissolveValue = Mathf.Lerp(
                    layer.startingDissolveValue,
                    targetDissolveValue,
                    dissolveProgress);

                if (!force &&
                    Mathf.Approximately(
                        layer.lastDissolveValue,
                        dissolveValue))
                {
                    continue;
                }

                if (layer.propertyBlock == null)
                {
                    layer.propertyBlock = new MaterialPropertyBlock();
                }

                layer.meshRenderer.GetPropertyBlock(layer.propertyBlock);
                layer.propertyBlock.SetFloat(
                    layer.dissolvePropertyId,
                    dissolveValue);
                layer.meshRenderer.SetPropertyBlock(layer.propertyBlock);
                layer.lastDissolveValue = dissolveValue;
            }

            if (!isEmitting && dissolveProgress >= 1f)
            {
                dissolveWasStarted = false;
                trailStopTime = float.NegativeInfinity;
                nextMobileDissolveUpdateTime = float.NegativeInfinity;
            }
        }

        private bool UsesPresetMaterialLayers()
        {
            return preset != null &&
                   preset.materialLayers != null &&
                   preset.materialLayers.Count > 0;
        }

        private int GetConfiguredLayerCount()
        {
            return UsesPresetMaterialLayers()
                ? preset.materialLayers.Count
                : 1;
        }

        private Material GetLayerMaterial(int layerIndex)
        {
            if (!UsesPresetMaterialLayers())
            {
                return trailMaterial;
            }

            HS_SwordTrailPreset.MaterialLayer layer =
                preset.materialLayers[layerIndex];
            return layer != null ? layer.material : null;
        }

        private int GetLayerSortingOrder(int layerIndex)
        {
            if (!UsesPresetMaterialLayers())
            {
                return sortingOrder;
            }

            HS_SwordTrailPreset.MaterialLayer layer =
                preset.materialLayers[layerIndex];
            return layer != null ? layer.sortingOrder : 0;
        }

        private string GetLayerDissolvePropertyName(int layerIndex)
        {
            if (!UsesPresetMaterialLayers())
            {
                return dissolvePropertyName;
            }

            HS_SwordTrailPreset.MaterialLayer layer =
                preset.materialLayers[layerIndex];

            return layer != null ? layer.dissolvePropertyName : null;
        }

        private float GetLayerMaximumDissolve(int layerIndex)
        {
            if (!UsesPresetMaterialLayers())
            {
                return maximumDissolve;
            }

            HS_SwordTrailPreset.MaterialLayer layer =
                preset.materialLayers[layerIndex];

            return layer != null
                ? Mathf.Clamp01(layer.maximumDissolve)
                : 0f;
        }

        private bool HasAnyTrailMaterial()
        {
            int layerCount = GetConfiguredLayerCount();
            for (int i = 0; i < layerCount; i++)
            {
                if (GetLayerMaterial(i) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void DestroyRuntimeLayers()
        {
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                RuntimeTrailLayer layer = runtimeLayers[i];
                if (layer == null || layer.gameObject == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(layer.gameObject);
                }
                else
                {
                    DestroyImmediate(layer.gameObject);
                }
            }

            runtimeLayers.Clear();
            rendererSettingsDirty = true;
        }

        private void RefreshPresetPointAEffects()
        {
            EnsureTrailPoints();

            if (presetPointAEffectInstances == null)
            {
                presetPointAEffectInstances = new List<GameObject>();
            }

            for (int i = presetPointAEffectInstances.Count - 1; i >= 0; i--)
            {
                GameObject instance = presetPointAEffectInstances[i];
                if (instance == null)
                {
                    continue;
                }

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    Undo.DestroyObjectImmediate(instance);
                }
                else
#endif
                {
                    // Destroy is deferred in Play Mode. Disable the previous
                    // instance immediately so a forced runtime refresh cannot
                    // play both the old and new effects for one frame.
                    instance.SetActive(false);
                    Destroy(instance);
                }
            }

            presetPointAEffectInstances.Clear();

            if (preset != null && preset.trailTopEffectPrefabs != null)
            {
                foreach (GameObject effectPrefab in preset.trailTopEffectPrefabs)
                {
                    if (effectPrefab == null)
                    {
                        continue;
                    }

                    GameObject instance;

#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        instance = PrefabUtility.InstantiatePrefab(
                            effectPrefab,
                            pointA) as GameObject;

                        if (instance != null)
                        {
                            Undo.RegisterCreatedObjectUndo(
                                instance,
                                "Create Trail Top Effect");
                        }
                    }
                    else
#endif
                    {
                        instance = Instantiate(effectPrefab, pointA, false);
                    }

                    if (instance != null)
                    {
                        presetPointAEffectInstances.Add(instance);
                    }
                }
            }

            appliedEffectPreset = preset;
        }

        private void EnsurePresetPointAEffects()
        {
            int expectedCount = 0;

            if (preset != null && preset.trailTopEffectPrefabs != null)
            {
                for (int i = 0; i < preset.trailTopEffectPrefabs.Count; i++)
                {
                    if (preset.trailTopEffectPrefabs[i] != null)
                    {
                        expectedCount++;
                    }
                }
            }

            bool needsRefresh = appliedEffectPreset != preset ||
                                presetPointAEffectInstances == null ||
                                presetPointAEffectInstances.Count != expectedCount;

            if (!needsRefresh)
            {
                for (int i = 0; i < presetPointAEffectInstances.Count; i++)
                {
                    if (presetPointAEffectInstances[i] == null)
                    {
                        needsRefresh = true;
                        break;
                    }
                }
            }

            if (needsRefresh)
            {
                RefreshPresetPointAEffects();
            }
        }

        private void TryAddSection(float currentTime, bool force)
        {
            if (pointA == null || pointB == null)
            {
                return;
            }

            Vector3 currentPointA = pointA.position;
            Vector3 currentPointB = pointB.position;
            int currentIntermediatePointCount =
                GetCustomIntermediatePointCountForSampling();

            if (sections.Count > 0 &&
                currentIntermediatePointCount != sampledIntermediatePointCount)
            {
                // A runtime setting change would invalidate the flat sample
                // buffer, so begin a new compatible trail instead.
                ClearTrail();
            }

            if (sections.Count == 0)
            {
                sampledIntermediatePointCount =
                    currentIntermediatePointCount;
            }

            if (sections.Count == 0)
            {
                AddSection(currentPointA, currentPointB, currentTime);
                lastSampleTime = currentTime;
                return;
            }

            if (!force)
            {
                float effectiveSampleInterval = optimizeForMobile
                    ? Mathf.Max(sampleInterval, MobileUpdateInterval)
                    : sampleInterval;

                if (effectiveSampleInterval > 0f &&
                    currentTime - lastSampleTime < effectiveSampleInterval)
                {
                    return;
                }

                TrailSection previousSection = sections[sections.Count - 1];
                float movementA =
                    Vector3.Distance(previousSection.pointA, currentPointA);
                float movementB =
                    Vector3.Distance(previousSection.pointB, currentPointB);

                float maximumMovement = Mathf.Max(movementA, movementB);

                for (int i = 0; i < sampledIntermediatePointCount; i++)
                {
                    int previousFlatIndex =
                        (sections.Count - 1) *
                        sampledIntermediatePointCount + i;

                    if (previousFlatIndex >=
                        sectionIntermediatePositions.Count)
                    {
                        break;
                    }

                    float intermediateMovement = Vector3.Distance(
                        sectionIntermediatePositions[previousFlatIndex],
                        GetCurrentIntermediatePointPosition(
                            i,
                            sampledIntermediatePointCount));

                    maximumMovement = Mathf.Max(
                        maximumMovement,
                        intermediateMovement);
                }

                if (maximumMovement < minimumSectionDistance)
                {
                    return;
                }
            }

            // Store only real sampled poses. Intermediate smoothing sections are
            // generated later while rebuilding the mesh, where neighboring trail
            // samples are available and can define an averaged curve tangent.
            AddSection(currentPointA, currentPointB, currentTime);
            lastSampleTime = currentTime;
        }

        private void AddSection(
            Vector3 sectionPointA,
            Vector3 sectionPointB,
            float spawnTime)
        {
            float accumulatedDistance = 0f;

            if (sections.Count > 0)
            {
                TrailSection previous = sections[sections.Count - 1];

                float movementA =
                    Vector3.Distance(previous.pointA, sectionPointA);
                float movementB =
                    Vector3.Distance(previous.pointB, sectionPointB);

                accumulatedDistance =
                    previous.distance + (movementA + movementB) * 0.5f;
            }

            sections.Add(new TrailSection(
                sectionPointA,
                sectionPointB,
                spawnTime,
                accumulatedDistance));

            for (int i = 0; i < sampledIntermediatePointCount; i++)
            {
                sectionIntermediatePositions.Add(
                    GetCurrentIntermediatePointPosition(
                        i,
                        sampledIntermediatePointCount));
            }
        }

        private void RemoveExpiredSections(float currentTime)
        {
            int expiredCount = 0;

            while (expiredCount < sections.Count &&
                   currentTime - sections[expiredCount].spawnTime >= trailLifetime)
            {
                expiredCount++;
            }

            if (expiredCount > 0)
            {
                sections.RemoveRange(0, expiredCount);

                int expiredIntermediatePositionCount =
                    expiredCount * sampledIntermediatePointCount;

                if (expiredIntermediatePositionCount > 0)
                {
                    sectionIntermediatePositions.RemoveRange(
                        0,
                        Mathf.Min(
                            expiredIntermediatePositionCount,
                            sectionIntermediatePositions.Count));
                }
            }
        }

        private void RebuildMesh(float currentTime)
        {
            if (generatedMesh == null)
            {
                return;
            }

            if (sections.Count < 2)
            {
                ClearGeneratedMeshIfNeeded();
                return;
            }

            List<TrailSection> renderSections = GetRenderSections();

            if (renderSections.Count < 2)
            {
                ClearGeneratedMeshIfNeeded();
                return;
            }

            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            colors.Clear();
            triangles.Clear();

            int effectiveLinesAlongTrail = optimizeForMobile
                ? Mathf.Min(linesAlongTrail, MobileMaximumLinesAlongTrail)
                : linesAlongTrail;

            int widthSegments = effectiveLinesAlongTrail + 1;
            int verticesPerSide = widthSegments + 1;
            int verticesPerSection = verticesPerSide * 2;

            float oldestDistance = renderSections[0].distance;
            float newestDistance = renderSections[renderSections.Count - 1].distance;
            float trailDistance = newestDistance - oldestDistance;
            bool hasDistance = trailDistance > 0.000001f;
            Vector3 previousNormal = Vector3.up;
            Bounds meshBounds = new Bounds(
                renderSections[0].pointA,
                Vector3.zero);

            for (int i = 0; i < renderSections.Count; i++)
            {
                TrailSection section = renderSections[i];
                meshBounds.Encapsulate(section.pointA);
                meshBounds.Encapsulate(section.pointB);
                Vector3 midpoint = (section.pointA + section.pointB) * 0.5f;

                Vector3 previousMidpoint = i > 0
                    ? (renderSections[i - 1].pointA +
                       renderSections[i - 1].pointB) * 0.5f
                    : midpoint;

                Vector3 nextMidpoint = i < renderSections.Count - 1
                    ? (renderSections[i + 1].pointA +
                       renderSections[i + 1].pointB) * 0.5f
                    : midpoint;

                Vector3 alongTrail = nextMidpoint - previousMidpoint;
                Vector3 acrossTrail = section.pointB - section.pointA;

                Vector3 normal = Vector3.Cross(alongTrail, acrossTrail).normalized;
                if (normal.sqrMagnitude < 0.000001f)
                {
                    normal = previousNormal;
                }

                previousNormal = normal;

                float age01 = Mathf.Clamp01(
                    (currentTime - section.spawnTime) / trailLifetime);

                float alpha = 1f;

                if (fadeAlphaOverLifetime)
                {
                    alpha = alphaOverLifetime != null
                        ? Mathf.Clamp01(alphaOverLifetime.Evaluate(age01))
                        : 1f - age01;
                }

                Color vertexColor = new Color(1f, 1f, 1f, alpha);
                float trailU = hasDistance
                    ? (newestDistance - section.distance) / trailDistance
                    : 1f - (float)i / (renderSections.Count - 1);

                // The newest section next to the weapon is always the left side
                // of the complete square UV layout (U = 0).
                trailU = Mathf.Clamp01(trailU);

                // Front side. Additional lines divide only the geometry;
                // all sections still share one continuous 0-1 UV square.
                for (int widthIndex = 0; widthIndex <= widthSegments; widthIndex++)
                {
                    float widthV = (float)widthIndex / widthSegments;
                    Vector3 widthPosition = EvaluateRenderWidthPoint(
                        renderSections,
                        i,
                        widthV);

                    vertices.Add(widthPosition);
                    meshBounds.Encapsulate(widthPosition);

                    normals.Add(normal);
                    uvs.Add(new Vector2(trailU, widthV));
                    colors.Add(vertexColor);
                }

                // Back side. Separate vertices preserve opposite normals for lit materials.
                for (int widthIndex = 0; widthIndex <= widthSegments; widthIndex++)
                {
                    float widthV = (float)widthIndex / widthSegments;
                    vertices.Add(EvaluateRenderWidthPoint(
                        renderSections,
                        i,
                        widthV));

                    normals.Add(-normal);
                    uvs.Add(new Vector2(trailU, widthV));
                    colors.Add(vertexColor);
                }
            }

            for (int i = 0; i < renderSections.Count - 1; i++)
            {
                int current = i * verticesPerSection;
                int next = (i + 1) * verticesPerSection;

                for (int widthIndex = 0; widthIndex < widthSegments; widthIndex++)
                {
                    int frontA0 = current + widthIndex;
                    int frontB0 = frontA0 + 1;
                    int frontA1 = next + widthIndex;
                    int frontB1 = frontA1 + 1;

                    triangles.Add(frontA0);
                    triangles.Add(frontA1);
                    triangles.Add(frontB0);

                    triangles.Add(frontB0);
                    triangles.Add(frontA1);
                    triangles.Add(frontB1);

                    int backA0 = current + verticesPerSide + widthIndex;
                    int backB0 = backA0 + 1;
                    int backA1 = next + verticesPerSide + widthIndex;
                    int backB1 = backA1 + 1;

                    triangles.Add(backA0);
                    triangles.Add(backB0);
                    triangles.Add(backA1);

                    triangles.Add(backB0);
                    triangles.Add(backB1);
                    triangles.Add(backA1);
                }
            }

            generatedMesh.Clear(false);
            generatedMesh.indexFormat = vertices.Count > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

            generatedMesh.SetVertices(vertices);
            generatedMesh.SetNormals(normals);
            generatedMesh.SetColors(colors);
            generatedMesh.SetUVs(0, uvs);
            generatedMesh.SetTriangles(triangles, 0, false);
            generatedMesh.bounds = meshBounds;
            meshContainsGeometry = true;
        }

        private Vector3 EvaluateRenderWidthPoint(
            List<TrailSection> renderSections,
            int sectionIndex,
            float widthV)
        {
            TrailSection section = renderSections[sectionIndex];

            if (sampledIntermediatePointCount <= 0 ||
                activeRenderIntermediatePositions == null)
            {
                return Vector3.Lerp(
                    section.pointA,
                    section.pointB,
                    widthV);
            }

            int requiredPositionCount =
                renderSections.Count * sampledIntermediatePointCount;

            if (activeRenderIntermediatePositions.Count < requiredPositionCount)
            {
                return Vector3.Lerp(
                    section.pointA,
                    section.pointB,
                    widthV);
            }

            int shapeSegmentCount = sampledIntermediatePointCount + 1;
            float scaledPosition =
                Mathf.Clamp01(widthV) * shapeSegmentCount;
            int leftControlIndex = Mathf.Min(
                Mathf.FloorToInt(scaledPosition),
                shapeSegmentCount);
            int rightControlIndex = Mathf.Min(
                leftControlIndex + 1,
                shapeSegmentCount);
            float interpolation = scaledPosition - leftControlIndex;

            Vector3 leftPosition = GetRenderControlPoint(
                renderSections,
                sectionIndex,
                leftControlIndex);
            Vector3 rightPosition = GetRenderControlPoint(
                renderSections,
                sectionIndex,
                rightControlIndex);

            return Vector3.Lerp(
                leftPosition,
                rightPosition,
                interpolation);
        }

        private Vector3 GetRenderControlPoint(
            List<TrailSection> renderSections,
            int sectionIndex,
            int controlIndex)
        {
            TrailSection section = renderSections[sectionIndex];

            if (controlIndex <= 0)
            {
                return section.pointA;
            }

            if (controlIndex > sampledIntermediatePointCount)
            {
                return section.pointB;
            }

            int flatIndex =
                sectionIndex * sampledIntermediatePointCount +
                controlIndex - 1;

            return activeRenderIntermediatePositions[flatIndex];
        }

        /// <summary>
        /// Returns the original sampled sections or a subdivided Catmull-Rom
        /// version. Catmull-Rom calculates every intermediate section from four
        /// neighboring samples, so its tangent is the average direction of the
        /// trail before and after that point instead of a straight midpoint.
        /// </summary>
        private List<TrailSection> GetRenderSections()
        {
            activeRenderIntermediatePositions =
                sampledIntermediatePointCount > 0
                    ? sectionIntermediatePositions
                    : null;

            float effectiveSmoothedSectionDistance = optimizeForMobile
                ? Mathf.Max(
                    maximumSmoothedSectionDistance,
                    MobileMinimumSmoothedSectionDistance)
                : maximumSmoothedSectionDistance;

            int effectiveMaximumIntermediateSections = optimizeForMobile
                ? Mathf.Min(
                    maxIntermediateSectionsPerFrame,
                    MobileMaximumIntermediateSections)
                : maxIntermediateSectionsPerFrame;

            if (!smoothLowFps ||
                effectiveSmoothedSectionDistance <= 0f ||
                effectiveMaximumIntermediateSections <= 0 ||
                sections.Count < 3)
            {
                return sections;
            }

            smoothedSections.Clear();
            smoothedIntermediatePositions.Clear();

            TrailSection first = sections[0];
            smoothedSections.Add(new TrailSection(
                first.pointA,
                first.pointB,
                first.spawnTime,
                0f));
            AppendSourceIntermediatePositions(
                0,
                smoothedIntermediatePositions);

            for (int segmentIndex = 0;
                 segmentIndex < sections.Count - 1;
                 segmentIndex++)
            {
                int section0Index = Mathf.Max(segmentIndex - 1, 0);
                int section1Index = segmentIndex;
                int section2Index = segmentIndex + 1;
                int section3Index = Mathf.Min(
                    segmentIndex + 2,
                    sections.Count - 1);

                TrailSection section0 = sections[section0Index];
                TrailSection section1 = sections[section1Index];
                TrailSection section2 = sections[section2Index];
                TrailSection section3 = sections[section3Index];

                float movementA = Vector3.Distance(
                    section1.pointA,
                    section2.pointA);

                float movementB = Vector3.Distance(
                    section1.pointB,
                    section2.pointB);

                float maximumMovement = Mathf.Max(movementA, movementB);

                // A custom middle point can move farther than either endpoint.
                // Include it when choosing the Catmull-Rom subdivision count so
                // an animated uneven profile remains smooth between samples.
                for (int i = 0; i < sampledIntermediatePointCount; i++)
                {
                    float intermediateMovement = Vector3.Distance(
                        GetSampledIntermediatePosition(section1Index, i),
                        GetSampledIntermediatePosition(section2Index, i));

                    maximumMovement = Mathf.Max(
                        maximumMovement,
                        intermediateMovement);
                }

                int intermediateCount = Mathf.CeilToInt(
                    maximumMovement / effectiveSmoothedSectionDistance) - 1;

                intermediateCount = Mathf.Clamp(
                    intermediateCount,
                    0,
                    effectiveMaximumIntermediateSections);

                for (int i = 1; i <= intermediateCount; i++)
                {
                    float t = (float)i / (intermediateCount + 1);

                    Vector3 curvedPointA = EvaluateCatmullRom(
                        section0.pointA,
                        section1.pointA,
                        section2.pointA,
                        section3.pointA,
                        t);

                    Vector3 curvedPointB = EvaluateCatmullRom(
                        section0.pointB,
                        section1.pointB,
                        section2.pointB,
                        section3.pointB,
                        t);

                    float interpolatedTime = Mathf.Lerp(
                        section1.spawnTime,
                        section2.spawnTime,
                        t);

                    AddSmoothedSection(
                        curvedPointA,
                        curvedPointB,
                        interpolatedTime);
                    AppendCurvedIntermediatePositions(
                        section0Index,
                        section1Index,
                        section2Index,
                        section3Index,
                        t);
                }

                AddSmoothedSection(
                    section2.pointA,
                    section2.pointB,
                    section2.spawnTime);
                AppendSourceIntermediatePositions(
                    section2Index,
                    smoothedIntermediatePositions);
            }

            activeRenderIntermediatePositions =
                sampledIntermediatePointCount > 0
                    ? smoothedIntermediatePositions
                    : null;
            return smoothedSections;
        }

        private void AppendCurvedIntermediatePositions(
            int section0Index,
            int section1Index,
            int section2Index,
            int section3Index,
            float t)
        {
            for (int i = 0; i < sampledIntermediatePointCount; i++)
            {
                smoothedIntermediatePositions.Add(EvaluateCatmullRom(
                    GetSampledIntermediatePosition(section0Index, i),
                    GetSampledIntermediatePosition(section1Index, i),
                    GetSampledIntermediatePosition(section2Index, i),
                    GetSampledIntermediatePosition(section3Index, i),
                    t));
            }
        }

        private void AppendSourceIntermediatePositions(
            int sectionIndex,
            List<Vector3> destination)
        {
            for (int i = 0; i < sampledIntermediatePointCount; i++)
            {
                destination.Add(
                    GetSampledIntermediatePosition(sectionIndex, i));
            }
        }

        private Vector3 GetSampledIntermediatePosition(
            int sectionIndex,
            int intermediateIndex)
        {
            int flatIndex =
                sectionIndex * sampledIntermediatePointCount +
                intermediateIndex;

            if (flatIndex >= 0 &&
                flatIndex < sectionIntermediatePositions.Count)
            {
                return sectionIntermediatePositions[flatIndex];
            }

            TrailSection section = sections[sectionIndex];
            float widthV =
                (float)(intermediateIndex + 1) /
                (sampledIntermediatePointCount + 1);

            return Vector3.Lerp(
                section.pointA,
                section.pointB,
                widthV);
        }

        private void AddSmoothedSection(
            Vector3 sectionPointA,
            Vector3 sectionPointB,
            float spawnTime)
        {
            float accumulatedDistance = 0f;

            if (smoothedSections.Count > 0)
            {
                TrailSection previous =
                    smoothedSections[smoothedSections.Count - 1];

                float movementA = Vector3.Distance(
                    previous.pointA,
                    sectionPointA);

                float movementB = Vector3.Distance(
                    previous.pointB,
                    sectionPointB);

                accumulatedDistance =
                    previous.distance + (movementA + movementB) * 0.5f;
            }

            smoothedSections.Add(new TrailSection(
                sectionPointA,
                sectionPointB,
                spawnTime,
                accumulatedDistance));
        }

        private static Vector3 EvaluateCatmullRom(
            Vector3 point0,
            Vector3 point1,
            Vector3 point2,
            Vector3 point3,
            float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            // The tangent at point1 is based on (point2 - point0), and the
            // tangent at point2 is based on (point3 - point1). This averages
            // the incoming and outgoing trail direction and rounds sharp bends.
            return 0.5f * (
                2f * point1 +
                (-point0 + point2) * t +
                (2f * point0 - 5f * point1 +
                 4f * point2 - point3) * t2 +
                (-point0 + 3f * point1 -
                 3f * point2 + point3) * t3);
        }

        private bool TryCalculateLocalGeometryBounds(out Bounds combinedBounds)
        {
            combinedBounds = default;
            bool hasBounds = false;

            MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                {
                    continue;
                }

                EncapsulateTransformedBounds(
                    meshFilter.sharedMesh.bounds,
                    meshFilter.transform,
                    ref combinedBounds,
                    ref hasBounds);
            }

            SkinnedMeshRenderer[] skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer skinnedRenderer in skinnedRenderers)
            {
                EncapsulateTransformedBounds(
                    skinnedRenderer.localBounds,
                    skinnedRenderer.transform,
                    ref combinedBounds,
                    ref hasBounds);
            }

            if (hasBounds)
            {
                return true;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer childRenderer in renderers)
            {
                EncapsulateWorldBounds(
                    childRenderer.bounds,
                    ref combinedBounds,
                    ref hasBounds);
            }

            if (hasBounds)
            {
                return true;
            }

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider childCollider in colliders)
            {
                EncapsulateWorldBounds(
                    childCollider.bounds,
                    ref combinedBounds,
                    ref hasBounds);
            }

            return hasBounds;
        }

        private void EncapsulateTransformedBounds(
            Bounds sourceBounds,
            Transform sourceTransform,
            ref Bounds combinedBounds,
            ref bool hasBounds)
        {
            Vector3 center = sourceBounds.center;
            Vector3 extents = sourceBounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 sourceLocalCorner = center + Vector3.Scale(
                            extents,
                            new Vector3(x, y, z));

                        Vector3 worldCorner = sourceTransform.TransformPoint(sourceLocalCorner);
                        Vector3 ownerLocalCorner = transform.InverseTransformPoint(worldCorner);
                        EncapsulatePoint(ownerLocalCorner, ref combinedBounds, ref hasBounds);
                    }
                }
            }
        }

        private void EncapsulateWorldBounds(
            Bounds worldBounds,
            ref Bounds combinedBounds,
            ref bool hasBounds)
        {
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 worldCorner = center + Vector3.Scale(
                            extents,
                            new Vector3(x, y, z));

                        Vector3 ownerLocalCorner = transform.InverseTransformPoint(worldCorner);
                        EncapsulatePoint(ownerLocalCorner, ref combinedBounds, ref hasBounds);
                    }
                }
            }
        }

        private static void EncapsulatePoint(
            Vector3 point,
            ref Bounds combinedBounds,
            ref bool hasBounds)
        {
            if (!hasBounds)
            {
                combinedBounds = new Bounds(point, Vector3.zero);
                hasBounds = true;
                return;
            }

            combinedBounds.Encapsulate(point);
        }

        private int GetAxisIndex(Vector3 boundsSize)
        {
            switch (automaticAxis)
            {
                case AutomaticAxis.LocalX:
                    return 0;
                case AutomaticAxis.LocalY:
                    return 1;
                case AutomaticAxis.LocalZ:
                    return 2;
                default:
                    if (boundsSize.x >= boundsSize.y && boundsSize.x >= boundsSize.z)
                    {
                        return 0;
                    }

                    return boundsSize.y >= boundsSize.z ? 1 : 2;
            }
        }

        private static Vector3 AxisVector(int axisIndex)
        {
            switch (axisIndex)
            {
                case 0:
                    return Vector3.right;
                case 1:
                    return Vector3.up;
                default:
                    return Vector3.forward;
            }
        }

        private static float GetVectorComponent(Vector3 vector, int axisIndex)
        {
            switch (axisIndex)
            {
                case 0:
                    return vector.x;
                case 1:
                    return vector.y;
                default:
                    return vector.z;
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(HS_SwordMeshTrail))]
    [CanEditMultipleObjects]
    public class HS_SwordMeshTrailEditor : Editor
    {
        private SerializedProperty lockAutomaticEditorUpdatesProperty;

        private void OnEnable()
        {
            lockAutomaticEditorUpdatesProperty =
                serializedObject.FindProperty("lockAutomaticEditorUpdates");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SwordTrailInspectorFields.Draw(serializedObject, "lockAutomaticEditorUpdates");

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                "场景编辑保护",
                EditorStyles.boldLabel);

            if (lockAutomaticEditorUpdatesProperty == null)
            {
                EditorGUILayout.HelpBox(
                    "找不到自动更新锁定选项。",
                    MessageType.Error);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUI.showMixedValue =
                lockAutomaticEditorUpdatesProperty.hasMultipleDifferentValues;

            EditorGUI.BeginChangeCheck();
            bool requestedLock = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "锁定编辑器自动更新",
                    "请在拖尾和预设调好后再勾选。" +
                    "打开场景时会跳过编辑模式下的自动更新。"),
                lockAutomaticEditorUpdatesProperty.boolValue);

            if (EditorGUI.EndChangeCheck())
            {
                if (requestedLock)
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        "锁定编辑器自动更新？",
                        "确认拖尾设置完成后再锁定。\n\n" +
                        "锁定后，打开场景时不会自动" +
                        "应用预设、补建端点或中间点，" +
                        "也不会刷新刀尖附加特效。\n\n" +
                        "进入播放模式后，仍会应用所选预设并" +
                        "初始化拖尾。",
                        "锁定更新",
                        "取消");

                    if (confirmed)
                    {
                        lockAutomaticEditorUpdatesProperty.boolValue = true;
                    }
                }
                else
                {
                    lockAutomaticEditorUpdatesProperty.boolValue = false;
                }
            }

            EditorGUI.showMixedValue = false;

            if (lockAutomaticEditorUpdatesProperty.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "编辑模式下的自动更新已锁定。播放模式下依然会应用" +
                    "所选预设。需要手动更新时，可点击「应用刀光预设」" +
                    "或使用组件的其他菜单命令。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "请在配置完成后再锁定。锁定后，打开场景时" +
                    "拖尾组件不会自动修改配置并将场景标为已更改。",
                    MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    internal static class SwordTrailInspectorFields
    {
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            ["m_Script"] = "脚本",
            ["preset"] = "刀光预设",
            ["trailMaterial"] = "拖尾材质",
            ["dissolvePropertyName"] = "溶解属性名",
            ["maximumDissolve"] = "最大溶解值",
            ["trailLifetime"] = "拖尾持续时间（秒）",
            ["minimumSectionDistance"] = "最小采样距离",
            ["sampleInterval"] = "采样间隔（秒）",
            ["linesAlongTrail"] = "横向细分线数",
            ["useCustomIntermediatePoints"] = "使用自定义中间点",
            ["customIntermediatePoints"] = "中间点列表",
            ["fadeAlphaOverLifetime"] = "随时间淡出",
            ["alphaOverLifetime"] = "淡出曲线",
            ["startActive"] = "启用时自动播放",
            ["clearPreviousTrailOnStart"] = "播放前清除旧拖尾",
            ["optimizeForMobile"] = "启用移动端优化",
            ["smoothLowFps"] = "平滑低帧率轨迹",
            ["maximumSmoothedSectionDistance"] = "平滑段最大间距",
            ["maxIntermediateSectionsPerFrame"] = "每帧最多补点数",
            ["pointA"] = "A 点（刀尖）",
            ["pointB"] = "B 点（刀刃根部）",
            ["automaticAxis"] = "端点定位轴",
            ["recalculatePointsOnAwake"] = "启动时重新定位端点",
            ["pointAInset"] = "A 点向内偏移",
            ["pointBInset"] = "B 点向内偏移",
            ["endpointPadding"] = "两端延长距离",
            ["sortingOrder"] = "渲染排序",
            ["receiveShadows"] = "接收阴影",
            ["shadowCastingMode"] = "阴影投射模式",
            ["material"] = "材质",
            ["materialLayers"] = "材质层列表",
            ["trailTopEffectPrefabs"] = "刀尖附加特效列表",
            ["swordTrails"] = "绑定的刀光拖尾",
            ["workWithoutAnimation"] = "没有动画时持续播放",
        };

        internal static GUIContent Label(SerializedProperty property)
        {
            string tooltip = property.tooltip;
            return Labels.TryGetValue(property.name, out string label)
                ? new GUIContent(label, tooltip)
                : new GUIContent(property.displayName, tooltip);
        }

        internal static void Draw(SerializedObject serialized, string hiddenProperty = null)
        {
            SerializedProperty property = serialized.GetIterator();
            for (bool hasProperty = property.NextVisible(true); hasProperty; hasProperty = property.NextVisible(false))
            {
                if (property.name == hiddenProperty)
                    continue;

                using (new EditorGUI.DisabledScope(property.name == "m_Script"))
                    EditorGUILayout.PropertyField(property, Label(property), true);
            }
        }
    }

    internal abstract class SwordTrailSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SwordTrailInspectorFields.Draw(serializedObject);
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(HS_SwordTrailPreset))]
    [CanEditMultipleObjects]
    internal sealed class SwordTrailPresetEditor : SwordTrailSettingsEditor { }

    [CustomEditor(typeof(HS_SwordTrailAnimationEvents))]
    [CanEditMultipleObjects]
    internal sealed class SwordTrailAnimationEventsEditor : SwordTrailSettingsEditor { }

    [CustomPropertyDrawer(typeof(HS_SwordTrailPreset.MaterialLayer))]
    internal sealed class SwordTrailMaterialLayerDrawer : PropertyDrawer
    {
        private static readonly string[] Fields =
        {
            nameof(HS_SwordTrailPreset.MaterialLayer.material),
            nameof(HS_SwordTrailPreset.MaterialLayer.sortingOrder),
            nameof(HS_SwordTrailPreset.MaterialLayer.dissolvePropertyName),
            nameof(HS_SwordTrailPreset.MaterialLayer.maximumDissolve)
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;

            foreach (string field in Fields)
            {
                SerializedProperty child = property.FindPropertyRelative(field);
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(child);
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(position, label, property);
            position.height = EditorGUIUtility.singleLineHeight;
            property.isExpanded = EditorGUI.Foldout(position, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (string field in Fields)
                    {
                        SerializedProperty child = property.FindPropertyRelative(field);
                        position.y += position.height + EditorGUIUtility.standardVerticalSpacing;
                        position.height = EditorGUI.GetPropertyHeight(child);
                        EditorGUI.PropertyField(position, child, SwordTrailInspectorFields.Label(child));
                    }
                }
            }

            EditorGUI.EndProperty();
        }
    }
#endif
}