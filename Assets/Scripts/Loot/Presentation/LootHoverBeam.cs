using Game.Items;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class LootHoverBeam : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private ItemDefinitionTable itemDefinitions;

    [Header("悬浮与旋转")]
    [SerializeField, Range(0f, 2f)]
    private float hoverHeight = 0.45f;
    [SerializeField, Range(0f, 0.5f)]
    private float hoverAmplitude = 0.08f;
    [SerializeField, Range(0f, 5f)]
    private float hoverSpeed = 1.4f;
    [SerializeField, Range(-180f, 180f)]
    private float rotationSpeed = 45f;

    [Header("小光柱")]
    [SerializeField] private bool showBeam = true;
    [SerializeField, Range(0.1f, 4f)]
    private float beamHeight = 1.2f;
    [SerializeField, Range(0.05f, 2f)]
    private float beamWidth = 0.45f;
    [SerializeField, Range(0f, 1f)]
    private float beamOpacity = 0.55f;
    [SerializeField]
    private Color fallbackColor = new Color(0.5f, 0.8f, 1f, 1f);

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private WorldLootView lootView;
    private Transform beamRoot;
    private Transform haloRoot;
    private MeshRenderer[] effectRenderers;
    private Mesh quadMesh;
    private Material beamMaterial;
    private Material haloMaterial;
    private Texture2D beamTexture;
    private Texture2D haloTexture;
    private MaterialPropertyBlock colorBlock;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private string currentLootId;
    private string currentItemId;
    private Color currentColor;
    private float phase;

    private void Awake()
    {
        if (visualRoot == null)
        {
            enabled = false;
            Debug.LogWarning("掉落物缺少悬浮模型节点。", this);
            return;
        }

        initialPosition = visualRoot.localPosition;
        initialRotation = visualRoot.localRotation;
        currentColor = fallbackColor;
        CreateEffects();
    }

    private void OnEnable()
    {
        currentLootId = null;
        currentItemId = null;
        currentColor = fallbackColor;
        phase = 0f;
    }

    private void Update()
    {
        if (visualRoot == null) return;

        if (lootView == null) lootView = GetComponent<WorldLootView>();
        if (lootView != null)
        {
            if (currentLootId != lootView.LootId)
            {
                currentLootId = lootView.LootId;
                phase = PhaseFor(currentLootId);
            }
            if (currentItemId != lootView.ItemId)
            {
                currentItemId = lootView.ItemId;
                currentColor = fallbackColor;
                if (itemDefinitions != null
                    && itemDefinitions.TryGet(currentItemId, out ItemDefinition definition))
                    currentColor = definition.QualityColor;
            }
        }

        float verticalScale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        float offset = hoverHeight + hoverAmplitude
            * Mathf.Sin(Time.time * hoverSpeed * Mathf.PI * 2f + phase);
        visualRoot.localPosition = initialPosition + Vector3.up * (offset / verticalScale);
        visualRoot.localRotation = initialRotation
            * Quaternion.Euler(0f, rotationSpeed * Time.time + phase * Mathf.Rad2Deg, 0f);

        if (beamRoot == null) return;
        beamRoot.gameObject.SetActive(showBeam && beamOpacity > 0f);
        haloRoot.gameObject.SetActive(showBeam && beamOpacity > 0f);
        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
        float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
        beamRoot.localScale = new Vector3(
            beamWidth / scaleX, beamHeight / verticalScale, beamWidth / scaleZ);
        haloRoot.localPosition = Vector3.up * (0.02f / verticalScale);
        haloRoot.localScale = new Vector3(
            beamWidth * 1.8f / scaleX, beamWidth * 1.8f / scaleZ, 1f);

        if (colorBlock == null) colorBlock = new MaterialPropertyBlock();
        Color color = new Color(currentColor.r, currentColor.g, currentColor.b, beamOpacity);
        for (int index = 0; index < effectRenderers.Length; index++)
        {
            MeshRenderer renderer = effectRenderers[index];
            renderer.GetPropertyBlock(colorBlock);
            colorBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(colorBlock);
        }
    }

    private void OnDisable()
    {
        if (visualRoot == null) return;
        visualRoot.localPosition = initialPosition;
        visualRoot.localRotation = initialRotation;
    }

    private void OnDestroy()
    {
        if (quadMesh != null) Destroy(quadMesh);
        if (beamMaterial != null) Destroy(beamMaterial);
        if (haloMaterial != null) Destroy(haloMaterial);
        if (beamTexture != null) Destroy(beamTexture);
        if (haloTexture != null) Destroy(haloTexture);
    }

    private void CreateEffects()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("找不到光柱所需的 Sprites/Default 着色器。", this);
            return;
        }

        quadMesh = new Mesh { name = "掉落光柱双面片" };
        quadMesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f)
        };
        quadMesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 1f), new Vector2(1f, 1f)
        };
        quadMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        quadMesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 1, 2, 0, 3, 2, 1 };
        quadMesh.RecalculateBounds();

        beamTexture = CreateTexture(false);
        haloTexture = CreateTexture(true);
        beamMaterial = new Material(shader) { mainTexture = beamTexture };
        haloMaterial = new Material(shader) { mainTexture = haloTexture };

        beamRoot = new GameObject("光柱").transform;
        beamRoot.SetParent(transform, false);
        MeshRenderer front = CreateQuad("光束正面", beamRoot, beamMaterial);
        MeshRenderer side = CreateQuad("光束侧面", beamRoot, beamMaterial);
        side.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        haloRoot = new GameObject("地面光圈").transform;
        haloRoot.SetParent(transform, false);
        MeshRenderer halo = CreateQuad("光圈", haloRoot, haloMaterial);
        halo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        halo.transform.localPosition = new Vector3(0f, 0f, -0.5f);
        effectRenderers = new[] { front, side, halo };
    }

    private MeshRenderer CreateQuad(string name, Transform parent, Material material)
    {
        var quad = new GameObject(name);
        quad.transform.SetParent(parent, false);
        quad.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private static Texture2D CreateTexture(bool halo)
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = halo ? "掉落光圈渐变" : "掉落光柱渐变",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;
                float alpha;
                if (halo)
                {
                    float radius = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
                    alpha = Mathf.Clamp01((0.5f - radius) * 4f) * 0.5f;
                }
                else
                {
                    alpha = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2f), 2f)
                        * Mathf.Pow(1f - v, 1.5f);
                }
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static float PhaseFor(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0f;
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in id)
                hash = (hash ^ character) * 16777619;
            return (hash % 1000) / 1000f * Mathf.PI * 2f;
        }
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(LootHoverBeam))]
internal sealed class LootHoverBeamInspector : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        Draw("visualRoot", "悬浮模型节点");
        Draw("itemDefinitions", "物品总表");
        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("悬浮与旋转", UnityEditor.EditorStyles.boldLabel);
        Draw("hoverHeight", "悬浮高度");
        Draw("hoverAmplitude", "浮动幅度");
        Draw("hoverSpeed", "浮动速度");
        Draw("rotationSpeed", "旋转速度（度/秒）");
        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("小光柱", UnityEditor.EditorStyles.boldLabel);
        Draw("showBeam", "显示光柱");
        Draw("beamHeight", "光柱高度");
        Draw("beamWidth", "光柱宽度");
        Draw("beamOpacity", "光柱透明度");
        Draw("fallbackColor", "默认光柱颜色");
        serializedObject.ApplyModifiedProperties();
    }

    private void Draw(string field, string label)
    {
        UnityEditor.SerializedProperty property = serializedObject.FindProperty(field);
        if (property != null)
            UnityEditor.EditorGUILayout.PropertyField(property, new GUIContent(label));
    }
}
#endif
