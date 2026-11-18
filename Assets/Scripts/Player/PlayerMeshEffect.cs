using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
internal sealed class PlayerMeshEffect : MonoBehaviour
{
    private static readonly int BoundsMinId = Shader.PropertyToID("_LocalBoundsMinimum");
    private static readonly int BoundsMaxId = Shader.PropertyToID("_LocalBoundsMaximum");
    private const float ParticleSizePerMeter = 0.15f;
    private const float DefaultModelHeight = 1.8f;

    private readonly List<Emitter> emitters = new List<Emitter>();
    private readonly List<Surface> surfaces = new List<Surface>();
    private Material template;
    private Material overlayMaterial;

    public void Configure(OverlayFX source)
    {
        template = source.overlayMaterial;
        foreach (ParticleSystem particle in source.particleSystems)
        {
            if (particle != null)
                emitters.Add(new Emitter(particle));
        }

        // Galaxy's root holds the effect settings and has a mesh shape without an emitter.
        if (source.TryGetComponent(out ParticleSystem rootParticle) && !rootParticle.emission.enabled
            && !source.particleSystems.Contains(rootParticle))
        {
            ParticleSystem.ShapeModule shape = rootParticle.shape;
            shape.enabled = false;
        }
    }

    public void BindModelMeshes(IReadOnlyList<ModelMesh> meshes)
    {
        ClearOverlay();
        Bounds bounds = meshes.Count > 0
            ? meshes[0].Renderer.bounds
            : new Bounds(transform.TransformPoint(Vector3.up), Vector3.one * DefaultModelHeight);
        Renderer emissionRenderer = null;
        Renderer largestRenderer = null;
        float emissionSize = -1f;
        float largestSize = -1f;
        foreach (ModelMesh model in meshes)
        {
            Bounds partBounds = model.Renderer.bounds;
            bounds.Encapsulate(partBounds);
            float partSize = partBounds.size.sqrMagnitude;
            if (partSize > largestSize)
            {
                largestSize = partSize;
                largestRenderer = model.Renderer;
            }

            if (model.Mesh.isReadable && partSize > emissionSize)
            {
                emissionSize = partSize;
                emissionRenderer = model.Renderer;
            }
        }

        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        foreach (Emitter emitter in emitters)
            emitter.Bind(emissionRenderer, bounds.center, size);

        if (meshes.Count == 0 || template == null)
            return;

        overlayMaterial = new Material(template) { hideFlags = HideFlags.DontSave };
        if (largestRenderer is SkinnedMeshRenderer skinned && skinned.rootBone != null)
            SetOverlayAxes(skinned.rootBone.rotation.eulerAngles.x);

        var properties = new MaterialPropertyBlock();
        foreach (ModelMesh model in meshes)
            CreateOverlay(model, properties);
    }

    private void CreateOverlay(ModelMesh model, MaterialPropertyBlock properties)
    {
        Renderer source = model.Renderer;
        Mesh mesh = model.Mesh;
        var overlayObject = new GameObject("玩家表面特效");
        overlayObject.SetActive(false);
        overlayObject.layer = source.gameObject.layer;
        overlayObject.transform.SetParent(source.transform, false);

        Renderer overlayRenderer;
        if (source is SkinnedMeshRenderer modelSkin)
        {
            SkinnedMeshRenderer skin = overlayObject.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.rootBone = modelSkin.rootBone;
            skin.bones = modelSkin.bones;
            skin.quality = modelSkin.quality;
            skin.updateWhenOffscreen = modelSkin.updateWhenOffscreen;
            skin.localBounds = modelSkin.localBounds;
            overlayRenderer = skin;
        }
        else
        {
            overlayObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
        }

        // A separate renderer covers every submesh and keeps the model's material slots intact.
        var materials = new Material[mesh.subMeshCount];
        for (int index = 0; index < materials.Length; index++)
            materials[index] = overlayMaterial;
        overlayRenderer.sharedMaterials = materials;
        overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;
        overlayRenderer.sortingLayerID = source.sortingLayerID;
        overlayRenderer.sortingOrder = source.sortingOrder;
        properties.SetVector(BoundsMinId, source.localBounds.min);
        properties.SetVector(BoundsMaxId, source.localBounds.max);
        overlayRenderer.SetPropertyBlock(properties);
        overlayObject.SetActive(true);
        var surface = new Surface(source, overlayRenderer, mesh.blendShapeCount);
        surfaces.Add(surface);
        surface.Sync();
    }

    private void SetOverlayAxes(float bonePitch)
    {
        if (bonePitch <= 90f)
        {
            OverlayFX.ShaderKeywordController.SetGradientAxis(overlayMaterial, OverlayFX.GradientAxis.Y);
            OverlayFX.ShaderKeywordController.SetUvDirection(overlayMaterial, OverlayFX.UvDirection.X);
        }
        else if (bonePitch >= 270f)
        {
            OverlayFX.ShaderKeywordController.SetGradientAxis(overlayMaterial, OverlayFX.GradientAxis.Z);
            OverlayFX.ShaderKeywordController.SetUvDirection(overlayMaterial, OverlayFX.UvDirection.Z);
        }
    }

    private void LateUpdate()
    {
        foreach (Surface surface in surfaces)
            surface.Sync();
    }

    private void OnDisable() => ClearOverlay();

    private void OnDestroy() => ClearOverlay();

    private void ClearOverlay()
    {
        foreach (Surface surface in surfaces)
        {
            Renderer renderer = surface.Overlay;
            if (renderer == null)
                continue;
            renderer.enabled = false;
            Destroy(renderer.gameObject);
        }

        surfaces.Clear();
        if (overlayMaterial != null)
            Destroy(overlayMaterial);

        overlayMaterial = null;
    }

    public static List<ModelMesh> FindModelMeshes(GameObject visual)
    {
        var meshes = new List<ModelMesh>();
        if (visual == null)
            return meshes;

        bool hasSkinnedMesh = false;
        foreach (Renderer candidate in visual.GetComponentsInChildren<Renderer>())
        {
            if (!candidate.enabled || candidate.forceRenderingOff)
                continue;

            Mesh candidateMesh = null;
            if (candidate is SkinnedMeshRenderer skinned)
                candidateMesh = skinned.sharedMesh;
            else if (candidate is MeshRenderer && candidate.TryGetComponent(out MeshFilter filter))
                candidateMesh = filter.sharedMesh;

            if (candidateMesh == null || candidateMesh.subMeshCount == 0)
                continue;

            // A skinned character can have separate body, clothes and armor renderers.
            bool isSkinned = candidate is SkinnedMeshRenderer;
            if (hasSkinnedMesh && !isSkinned)
                continue;

            if (isSkinned && !hasSkinnedMesh)
            {
                meshes.Clear();
                hasSkinnedMesh = true;
            }

            meshes.Add(new ModelMesh(candidate, candidateMesh));
        }

        return meshes;
    }

    internal readonly struct ModelMesh
    {
        public readonly Renderer Renderer;
        public readonly Mesh Mesh;

        public ModelMesh(Renderer renderer, Mesh mesh)
        {
            Renderer = renderer;
            Mesh = mesh;
        }
    }

    private readonly struct Surface
    {
        private readonly Renderer source;
        private readonly int blendShapeCount;
        public readonly Renderer Overlay;

        public Surface(Renderer source, Renderer overlay, int blendShapeCount)
        {
            this.source = source;
            this.blendShapeCount = blendShapeCount;
            Overlay = overlay;
        }

        public void Sync()
        {
            if (Overlay == null)
                return;

            Overlay.enabled = source != null && source.enabled && !source.forceRenderingOff;
            if (Overlay.enabled && Overlay is SkinnedMeshRenderer overlaySkin && source is SkinnedMeshRenderer modelSkin)
            {
                for (int index = 0; index < blendShapeCount; index++)
                    overlaySkin.SetBlendShapeWeight(index, modelSkin.GetBlendShapeWeight(index));
            }
        }
    }

    private readonly struct Emitter
    {
        private readonly ParticleSystem particle;
        private readonly Vector3 authoredPosition;
        private readonly float authoredRadius;

        public Emitter(ParticleSystem particle)
        {
            this.particle = particle;
            authoredPosition = particle.shape.position;
            authoredRadius = particle.shape.radius;
        }

        public void Bind(Renderer renderer, Vector3 center, float size)
        {
            if (particle == null)
                return;

            ParticleSystem.ShapeModule shape = particle.shape;
            shape.enabled = true;
            shape.meshRenderer = null;
            shape.skinnedMeshRenderer = null;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                shape.skinnedMeshRenderer = skinned;
            }
            else if (renderer is MeshRenderer meshRenderer)
            {
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshRenderer = meshRenderer;
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Sphere;
            }

            shape.position = renderer != null ? authoredPosition : particle.transform.InverseTransformPoint(center);
            shape.radius = renderer != null ? authoredRadius : Mathf.Max(0.1f, size * 0.5f);
            ParticleSystem.MainModule main = particle.main;
            main.startSizeMultiplier = Mathf.Max(0.1f, size) * ParticleSizePerMeter;
        }
    }
}
