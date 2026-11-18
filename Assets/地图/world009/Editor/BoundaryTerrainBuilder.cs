using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace World009 {
    public static class BoundaryTerrainBuilder {
        const string Prefix = "Solid_BoundaryTerrain";

        public static Material EnsureMaterial(Material ground) {
            string controlPath = Builder.Root + "/Textures/BoundarySlopeControl.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(controlPath);
            if (!importer) throw new InvalidOperationException("Missing boundary slope control texture");
            if (importer.sRGBTexture || importer.textureShape != TextureImporterShape.Texture2D) {
                importer.sRGBTexture = false;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.SaveAndReimport();
            }
            string path = Builder.Root + "/Materials/" + Builder.MapName + "_BoundarySlope.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) {
                material = new Material(ground);
                AssetDatabase.CreateAsset(material, path);
            }
            material.CopyPropertiesFromMaterial(ground);
            material.name = Builder.MapName + "_BoundarySlope";
            material.SetTexture("_Control", AssetDatabase.LoadAssetAtPath<Texture2D>(controlPath));
            material.SetFloat("_WetScale", Builder.MapName == "world009" ? .14f : 0f);
            var slate = AssetDatabase.LoadAssetAtPath<Texture2D>(Builder.Root + "/Textures/SlateV05.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Builder.Root + "/Textures/SlateV05Normal.png");
            if (slate && normal) {
                material.SetTexture("_RockTex", slate);
                material.SetTexture("_RockNormal", normal);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void UpdateEnvironment(GameObject root, bool updateLod) {
            var environment = root.transform.Find("Environment");
            if (!environment) throw new InvalidOperationException("Missing Environment");
            var ground = AssetDatabase.LoadAssetAtPath<Material>(Builder.Root + "/Materials/" + Builder.MapName + "_Ground.mat");
            var boundary = EnsureMaterial(ground);
            var path = Builder.Root + "/Models/" + Builder.MapName + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var slot = new AssetImporter.SourceAssetIdentifier(typeof(Material), boundary.name);
            if (!importer.GetExternalObjectMap().TryGetValue(slot, out var bound) || bound != boundary) {
                importer.AddRemap(slot, boundary);
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var temporary = (GameObject)PrefabUtility.InstantiatePrefab(model, root.scene);
            temporary.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(temporary, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            try {
                var parts = temporary.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith(Prefix)).ToArray();
                if (parts.Length != 4) throw new InvalidOperationException("Expected four boundary side meshes");
                foreach (var part in parts) {
                    var existing = environment.Find(part.name);
                    if (existing) continue;
                    part.transform.SetParent(environment, true);
                    part.sharedMaterial = boundary;
                    part.gameObject.isStatic = true;
                    part.gameObject.AddComponent<MeshCollider>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                }
            } finally { UnityEngine.Object.DestroyImmediate(temporary); }
            if (!updateLod) return;
            foreach (var renderer in environment.GetComponentsInChildren<MeshRenderer>(true).Where(MapLodBuilder.IsAlternate).ToArray())
                UnityEngine.Object.DestroyImmediate(renderer.gameObject);
            MapLodBuilder.Apply(environment.gameObject);
        }
    }
}
