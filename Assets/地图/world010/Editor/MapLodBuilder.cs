using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace World010 {
    public static class MapLodBuilder {
        const string Id = "world010";
        const string Root = "Assets/地图/" + Id;
        const float DemoFov = 49f;

        [Serializable] class Piece {
            public string name, mesh;
            public int triangles;
        }

        [Serializable] class Plan {
            public Piece[] lod1, lod2;
        }

        public static bool IsCeiling(MeshRenderer renderer) {
            string name = IsAlternate(renderer) ? renderer.transform.parent.name : renderer.name;
            return name.StartsWith("Solid_Ceiling") || name.StartsWith("Decor_CeilingStalactite") ||
                   name.StartsWith("Decor_CavernPerimeter");
        }

        public static bool IsAlternate(Renderer renderer) => renderer.name == "LOD1" || renderer.name == "LOD2";

        public static void Apply(GameObject environment) {
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Root + "/Documentation/LODGeometry.json"));
            var medium = plan.lod1.ToDictionary(p => p.name);
            var far = plan.lod2.ToDictionary(p => p.name);
            var originals = environment.GetComponentsInChildren<MeshRenderer>(true).Where(r => !IsAlternate(r))
                .GroupBy(r => Regex.Replace(r.name, @"_MeshPart\d+$", "")).ToArray();
            if (medium.Count != originals.Length || far.Count != originals.Length)
                throw new InvalidDataException("LOD manifest does not match the original renderer set for " + Id);
            var materials = AssetDatabase.FindAssets("t:Material", new[] { Root + "/Materials" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(m => m).ToDictionary(m => m.name);
            var models = new[] { ImportVariants(1, materials), ImportVariants(2, materials) };
            foreach (var part in originals) {
                var original = part.First();
                Renderer[] near = part.Cast<Renderer>().ToArray();
                var mid = Variant(original, medium[part.Key], models[0], materials, "LOD1", environment.transform);
                var distant = Variant(original, far[part.Key], models[1], materials, "LOD2", environment.transform);
                if (!mid && !distant)
                    continue;
                // The original object, renderer, collider and prefab identity remain in place.
                if (!original.TryGetComponent<LODGroup>(out var group))
                    group = original.gameObject.AddComponent<LODGroup>();
                var worldBounds = original.bounds;
                foreach (var renderer in near.Skip(1)) worldBounds.Encapsulate(renderer.bounds);
                float radius = worldBounds.extents.magnitude;
                float worldSize = Mathf.Max(.15f, 2 * Mathf.Max(worldBounds.extents.x, worldBounds.extents.y, worldBounds.extents.z));
                float verticalProjection = 2 * Mathf.Tan(DemoFov * .5f * Mathf.Deg2Rad);
                float nearThreshold = Mathf.Min(.91f, worldSize / ((72f + radius * 1.25f) * verticalProjection));
                float midThreshold = Mathf.Min(nearThreshold * .82f, worldSize / ((187f + radius * 1.25f) * verticalProjection));
                group.SetLODs(new[] { new LOD(nearThreshold, near), new LOD(midThreshold, mid ? new[] { mid } : near),
                                      new LOD(0f, distant ? new[] { distant } : near) });
                group.localReferencePoint = original.transform.InverseTransformPoint(worldBounds.center);
                var scale = original.transform.lossyScale;
                group.size = worldSize / Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                group.fadeMode = LODFadeMode.None;
            }
        }

        static Dictionary<string, MeshRenderer> ImportVariants(int tier, Dictionary<string, Material> materials) {
            string path = $"{Root}/Models/{Id}_LOD{tier}.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer)
                throw new InvalidDataException("Missing LOD FBX " + path);
            var external = importer.GetExternalObjectMap();
            var originalImporter = (ModelImporter)AssetImporter.GetAtPath($"{Root}/Models/{Id}.fbx");
            bool changed = importer.indexFormat != ModelImporterIndexFormat.UInt32 ||
                           importer.bakeAxisConversion != originalImporter.bakeAxisConversion;
            importer.indexFormat = ModelImporterIndexFormat.UInt32;
            importer.bakeAxisConversion = originalImporter.bakeAxisConversion;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var aliases = model.GetComponentsInChildren<MeshRenderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name).Distinct();
            foreach (var alias in aliases) {
                var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), alias);
                var canonical = ResolveMaterial(alias, materials);
                if (!external.TryGetValue(key, out var bound) || bound != canonical) {
                    importer.AddRemap(key, canonical);
                    changed = true;
                }
            }
            if (changed)
                importer.SaveAndReimport();
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return model.GetComponentsInChildren<MeshRenderer>(true).ToDictionary(r => r.name);
        }

        static Material ResolveMaterial(string name, Dictionary<string, Material> materials) {
            if (materials.TryGetValue(name, out var material)) return material;
            return materials[Regex.Replace(name, @"\.\d+$", "")];
        }

        static Renderer Variant(MeshRenderer original, Piece piece, Dictionary<string, MeshRenderer> model,
                                Dictionary<string, Material> materials, string name, Transform environment) {
            if (string.IsNullOrEmpty(piece.mesh))
                return null;
            var variant = model[piece.mesh];
            var mesh = variant.GetComponent<MeshFilter>().sharedMesh;
            if (mesh.GetIndexCount(0) == 0 || mesh.subMeshCount != variant.sharedMaterials.Length)
                throw new InvalidDataException("Empty variant or wrong material slots for " + original.name);
            var child = new GameObject(name);
            child.layer = original.gameObject.layer;
            child.transform.SetParent(original.transform, false);
            Matrix4x4 local = original.transform.worldToLocalMatrix * environment.parent.localToWorldMatrix *
                             variant.transform.localToWorldMatrix;
            child.transform.localPosition = local.GetColumn(3);
            Vector3 right = local.GetColumn(0), up = local.GetColumn(1), forward = local.GetColumn(2);
            float parity = Mathf.Sign(Vector3.Dot(Vector3.Cross(right, up), forward));
            child.transform.localRotation = Quaternion.LookRotation(forward, up);
            child.transform.localScale = new Vector3(right.magnitude * parity, up.magnitude, forward.magnitude);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = variant.sharedMaterials.Select(m => ResolveMaterial(m.name, materials)).ToArray();
            renderer.enabled = original.enabled;
            renderer.shadowCastingMode = original.shadowCastingMode;
            renderer.receiveShadows = original.receiveShadows;
            renderer.lightProbeUsage = original.lightProbeUsage;
            renderer.reflectionProbeUsage = original.reflectionProbeUsage;
            renderer.renderingLayerMask = original.renderingLayerMask;
            return renderer;
        }
    }
}
