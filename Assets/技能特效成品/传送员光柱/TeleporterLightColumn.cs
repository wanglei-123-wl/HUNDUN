using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Game/传送员光柱")]
public sealed class TeleporterLightColumn : MonoBehaviour
{
    private const int Segments = 64;
    private static readonly int TintId = Shader.PropertyToID("_Tint");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");

    [SerializeField] private Material effectMaterial;
    [SerializeField, Min(0.1f)] private float radius = 0.85f;
    [SerializeField, Min(0.1f)] private float height = 3.3f;
    [SerializeField, Min(0.01f)] private float ringWidth = 0.20f;
    [SerializeField] private float footOffset = 0.025f;
    [SerializeField] private Color tint = new Color(0.48f, 0.79f, 1f);
    [SerializeField, Range(0f, 2f)] private float intensity = 1f;
    [SerializeField, Min(0f)] private float flowSpeed = 0.75f;

    private GameObject visual;
    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock properties;
    private bool refreshNeeded;

#if UNITY_EDITOR
    private void Reset()
    {
        MonoScript script = MonoScript.FromMonoBehaviour(this);
        string path = AssetDatabase.GetAssetPath(script);
        if (AssetImporter.GetAtPath(path) is MonoImporter importer)
            effectMaterial = importer.GetDefaultReference(nameof(effectMaterial)) as Material;
        refreshNeeded = true;
    }
#endif

    private void OnEnable() => RefreshVisual();

    private void OnValidate() => refreshNeeded = true;

    private void Update()
    {
        if (refreshNeeded)
            RefreshVisual();
    }

    private void OnDisable() => ReleaseVisual();

    private void RefreshVisual()
    {
        refreshNeeded = false;
        if (effectMaterial == null)
        {
            ReleaseVisual();
            return;
        }

        if (visual == null)
        {
            visual = new GameObject("传送员光柱", typeof(MeshFilter), typeof(MeshRenderer))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            visual.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Teleporter light column", hideFlags = HideFlags.HideAndDontSave };
            visual.GetComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = visual.GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        visual.layer = gameObject.layer;
        visual.transform.localPosition = Vector3.up * footOffset;
        BuildMesh();
        meshRenderer.sharedMaterial = effectMaterial;
        properties ??= new MaterialPropertyBlock();
        properties.SetColor(TintId, tint);
        properties.SetFloat(IntensityId, intensity);
        properties.SetFloat(FlowSpeedId, flowSpeed);
        meshRenderer.SetPropertyBlock(properties);
    }

    private void BuildMesh()
    {
        float wallRadius = Mathf.Max(0.1f, radius);
        float halfWidth = Mathf.Clamp(ringWidth, 0.01f, wallRadius) * 0.5f;
        float columnHeight = Mathf.Max(0.1f, height);
        int vertexCount = (Segments + 1) * 4;
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uv = new Vector2[vertexCount];
        var surface = new Vector2[vertexCount];
        var triangles = new int[Segments * 12];

        for (int i = 0; i <= Segments; i++)
        {
            float turn = (float)i / Segments;
            float angle = (i % Segments) * (2f * Mathf.PI / Segments);
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            int vertex = i * 4;

            positions[vertex] = radial * wallRadius;
            positions[vertex + 1] = positions[vertex] + Vector3.up * columnHeight;
            positions[vertex + 2] = radial * (wallRadius - halfWidth);
            positions[vertex + 3] = radial * (wallRadius + halfWidth);
            normals[vertex] = normals[vertex + 1] = radial;
            normals[vertex + 2] = normals[vertex + 3] = Vector3.up;
            uv[vertex] = uv[vertex + 2] = new Vector2(turn, 0f);
            uv[vertex + 1] = uv[vertex + 3] = new Vector2(turn, 1f);
            surface[vertex + 2] = surface[vertex + 3] = Vector2.right;

            if (i < Segments)
            {
                WriteStripQuad(triangles, i * 12, vertex);
                WriteStripQuad(triangles, i * 12 + 6, vertex + 2);
            }
        }

        mesh.Clear();
        mesh.vertices = positions;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.uv2 = surface;
        mesh.triangles = triangles;
    }

    private static void WriteStripQuad(int[] triangles, int index, int first)
    {
        triangles[index] = first;
        triangles[index + 1] = first + 1;
        triangles[index + 2] = first + 4;
        triangles[index + 3] = first + 4;
        triangles[index + 4] = first + 1;
        triangles[index + 5] = first + 5;
    }

    private void ReleaseVisual()
    {
        if (visual != null)
        {
            visual.SetActive(false);
            DestroyTransientObject(visual);
        }
        if (mesh != null)
            DestroyTransientObject(mesh);
        visual = null;
        mesh = null;
        meshRenderer = null;
    }

    private void DestroyTransientObject(Object target)
    {
        if (Application.IsPlaying(gameObject))
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(TeleporterLightColumn))]
[CanEditMultipleObjects]
internal sealed class TeleporterLightColumnInspector : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            DrawField("m_Script", "脚本");
        DrawField("effectMaterial", "特效材质");
        DrawField("radius", "光圈半径");
        DrawField("height", "光柱高度");
        DrawField("ringWidth", "光圈宽度");
        DrawField("footOffset", "脚底高度偏移");
        DrawField("tint", "颜色");
        DrawField("intensity", "亮度");
        DrawField("flowSpeed", "光流速度", "游戏运行时持续循环。设为 0 时保持静止。");

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawField(string propertyName, string label, string tooltip = null)
    {
        EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName),
            new GUIContent(label, tooltip));
    }
}
#endif
