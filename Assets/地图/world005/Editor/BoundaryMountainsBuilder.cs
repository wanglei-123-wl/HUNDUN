using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace World005 {
    // Upgrade the environment in place so scene/prefab object identities survive.
    public static class BoundaryMountainsBuilder {
        public const string Prefix="Solid_BoundaryMountain";
        public static Material EnsureMaterial(Material ground) {
            const string path="Assets/地图/world005/Materials/world005_BoundarySlope.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(ground);AssetDatabase.CreateAsset(material,path);}
            material.CopyPropertiesFromMaterial(ground);
            material.name="world005_BoundarySlope";
            const string controlPath="Assets/地图/world005/Textures/BoundarySlopeControl.png";
            var textureImporter=(TextureImporter)AssetImporter.GetAtPath(controlPath);
            if(textureImporter.sRGBTexture||textureImporter.textureShape!=TextureImporterShape.Texture2D){
                textureImporter.sRGBTexture=false;textureImporter.textureShape=TextureImporterShape.Texture2D;textureImporter.SaveAndReimport();
            }
            var control=AssetDatabase.LoadAssetAtPath<Texture2D>(controlPath);
            if(!control)throw new InvalidOperationException("Missing boundary slope control Texture2D");
            material.SetTexture("_Control",control);
            material.SetFloat("_WetScale",0);
            material.SetColor("_GroundTint",new Color(.72f,.77f,.62f));
            material.SetTexture("_RockTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/地图/world005/Textures/SlateV05.png"));
            material.SetTexture("_RockNormal",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/地图/world005/Textures/SlateV05Normal.png"));
            EditorUtility.SetDirty(material);return material;
        }
        public static void Apply(GameObject root,bool rebuildLods=true) {
            var environment=root.transform.Find("Environment");
            if(!environment)throw new InvalidOperationException("Missing world005 Environment");
            var slope=EnsureMaterial(AssetDatabase.LoadAssetAtPath<Material>(Builder.Root+"/Materials/world005_Ground.mat"));
            var importer=(ModelImporter)AssetImporter.GetAtPath(Builder.Root+"/Models/world005.fbx");
            var key=new AssetImporter.SourceAssetIdentifier(typeof(Material),"world005_BoundarySlope");
            if(!importer.GetExternalObjectMap().TryGetValue(key,out var bound)||bound!=slope){
                importer.AddRemap(key,slope);importer.SaveAndReimport();
            }
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Builder.Root+"/Models/world005.fbx");
            var temporary=(GameObject)PrefabUtility.InstantiatePrefab(model,root.scene);
            temporary.transform.SetParent(root.transform,false);
            PrefabUtility.UnpackPrefabInstance(temporary,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            try {
                var originals=temporary.GetComponentsInChildren<MeshRenderer>()
                    .Where(r=>r.name.StartsWith(Prefix)).ToArray();
                if(originals.Length!=2)throw new InvalidOperationException("Expected north and south boundary mountains");
                foreach(var source in originals) {
                    if(environment.Find(source.name))continue;
                    source.transform.SetParent(environment,true);
                    source.gameObject.isStatic=true;
                    var collider=source.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh=source.GetComponent<MeshFilter>().sharedMesh;
                    collider.convex=false;collider.isTrigger=false;
                }
            } finally { UnityEngine.Object.DestroyImmediate(temporary); }
            if(!rebuildLods)return;
            // The new FBX LOD manifest also changes variant indices for old pieces.
            foreach(var renderer in environment.GetComponentsInChildren<MeshRenderer>(true)
                .Where(MapLodBuilder.IsAlternate).ToArray())
                UnityEngine.Object.DestroyImmediate(renderer.gameObject);
            MapLodBuilder.Apply(environment.gameObject);
        }
    }
}
