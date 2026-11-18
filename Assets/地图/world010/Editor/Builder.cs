using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace World010 {
    [Serializable] public class Route {
        public string name;
        public float width;
        public Vector3[] points;
    }

    [Serializable] public class View {
        public Vector3 position,target;
    }

    [Serializable] public class SceneSpec {
        public float width,length,power,density;
        public int waterCount,lavaCount;
        public Color sun,ambient,fog,tint,road,rock,water;
        public Vector3 euler,spawn,overview,focus;
        public Vector3[] galleryFire;
        public View[] shots;
        public Route[] routes,crossings;
    }

    [Serializable] class Hazard {
        public float[] a,b;
    }

    [Serializable] class HazardSet {
        public Hazard[] edges;
    }

    public static partial class Builder {
        public static readonly string MapName="world010";
        public static readonly string Root="Assets/地图/"+MapName;
        public static SceneSpec ReadSpec() =>
            JsonUtility.FromJson<SceneSpec>(File.ReadAllText(Root+"/Documentation/SceneData.json"));

        [MenuItem("Tools/World010/Build or Refresh Map")]
        public static void Build() {
            if(!GraphicsSettings.currentRenderPipeline||
               !GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal"))
                throw new InvalidOperationException("This map requires Unity URP.");
            Scene prior=SceneManager.GetActiveScene();
            if(prior.IsValid()&&string.IsNullOrEmpty(prior.path))
                throw new InvalidOperationException("Save the current untitled scene before rebuilding the map.");
            Directory.CreateDirectory(Root+"/Materials");
            Directory.CreateDirectory(Root+"/Generated");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var spec=ReadSpec();
            var mats=MakeMaterials(spec);
            string fbx=Root+"/Models/"+MapName+".fbx";
            var importer=AssetImporter.GetAtPath(fbx) as ModelImporter;
            if(!importer)throw new InvalidDataException("Missing map FBX");
            foreach(var entry in mats) {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),MapName+"_"+entry.Key),entry.Value);
            }
            importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if(!model)throw new InvalidDataException("Missing map FBX GameObject");
            Scene staging=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try {
                SceneManager.SetActiveScene(staging);
                var root=new GameObject(MapName);
                var source=(GameObject)PrefabUtility.InstantiatePrefab(model,staging);
                PrefabUtility.UnpackPrefabInstance(source,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                source.name="Environment";
                source.transform.SetParent(root.transform,false);
                foreach(var renderer in source.GetComponentsInChildren<MeshRenderer>()) {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>Resolve(m,mats,renderer.name)).ToArray();
                    if(renderer.name.StartsWith("Surface_")||renderer.name.StartsWith("Effect_"))
                        renderer.shadowCastingMode=ShadowCastingMode.Off;
                    else if(renderer.name.StartsWith("Plant_"))
                        renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
                    if(renderer.name.StartsWith("Terrain_")||renderer.name.StartsWith("Solid_")) {
                        renderer.gameObject.isStatic=true;
                        renderer.gameObject.AddComponent<MeshCollider>().sharedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    } else if(renderer.name.StartsWith("Art_"))
                        GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,StaticEditorFlags.BatchingStatic);
                }
                MapLodBuilder.Apply(source);
                AddCollision(root.transform,spec);
                var spawn=new GameObject("PlayerSpawn");
                spawn.transform.SetParent(root.transform,false);
                spawn.transform.position=spec.spawn+Vector3.up*.10f;
                AddArtAtmosphere(root.transform,spec,mats);
                MapNavBuilder.Bake(root);
                PrefabUtility.SaveAsPrefabAsset(root,Root+"/Generated/"+MapName+".prefab");
                AddLighting(spec,mats);
                var cam=new GameObject("WorldCamera");
                var camera=cam.AddComponent<Camera>();
                cam.tag="MainCamera";
                camera.fieldOfView=49;
                camera.nearClipPlane=.12f;
                camera.farClipPlane=2500;
                camera.clearFlags=spec.lavaCount>0?CameraClearFlags.SolidColor:CameraClearFlags.Skybox;
                camera.backgroundColor=spec.fog;
                ConfigureArtCamera(camera);
                cam.AddComponent<AudioListener>();
                cam.transform.position=spec.shots[0].position;
                cam.transform.LookAt(spec.shots[0].target);
                var orbit=cam.AddComponent<OrbitCamera>();
                orbit.target=spec.shots[0].target;
                orbit.home=spec.focus;
                orbit.extents=new Vector2(spec.width*.5f,spec.length*.5f);
                orbit.ceiling=source.GetComponentsInChildren<MeshRenderer>()
                    .Where(MapLodBuilder.IsCeiling).ToArray();
                if(!EditorSceneManager.SaveScene(staging,Root+"/Generated/"+MapName+".unity"))
                    throw new IOException("Unity scene save failed.");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            } finally {
                if(prior.IsValid()&&prior.isLoaded)SceneManager.SetActiveScene(prior);
                if(staging.IsValid()&&staging.isLoaded)EditorSceneManager.CloseScene(staging,true);
            }
        }

        static Material Resolve(Material original,Dictionary<string,Material> mats,string renderer) {
            if(renderer.StartsWith("Effect_Thunder"))return mats["Lightning"];
            if(!original)throw new InvalidDataException("Missing FBX material: "+renderer);
            string name=original.name;
            if(name.StartsWith(MapName+"_"))name=name.Substring(MapName.Length+1);
            if(!mats.TryGetValue(name,out var material))
                throw new InvalidDataException("Unexpected FBX material: "+original.name+" on "+renderer);
            return material;
        }

        static Texture2D Tex(string name) {
            foreach(string extension in new[]{".png",".jpg"}) {
                string path=Root+"/Textures/"+name+extension;
                if(AssetImporter.GetAtPath(path) is TextureImporter importer&&importer.textureShape!=TextureImporterShape.Texture2D) {
                    importer.textureShape=TextureImporterShape.Texture2D;
                    importer.SaveAndReimport();
                }
                if(name.EndsWith("Normal")&&AssetImporter.GetAtPath(path) is TextureImporter relief) {
                    bool raw=name=="GroundNormal"||name=="RoadNormal"||name=="RockNormal";
                    var type=raw?TextureImporterType.Default:TextureImporterType.NormalMap;
                    if(relief.textureType!=type||relief.sRGBTexture) {
                        relief.textureType=type;
                        relief.sRGBTexture=false;
                        if(raw)relief.textureCompression=TextureImporterCompression.Uncompressed;
                        relief.SaveAndReimport();
                    }
                }
                var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(tex)return tex;
            }
            throw new InvalidDataException("Missing texture "+name);
        }

        static Material Make(string key,string shader) {
            var sh=Shader.Find(shader);
            if(!sh)throw new InvalidDataException("Missing shader "+shader);
            var path=Root+"/Materials/"+MapName+"_"+key+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m) {
                m=new Material(sh);
                AssetDatabase.CreateAsset(m,path);
            }
            m.shader=sh;
            m.enableInstancing=true;
            m.name=MapName+"_"+key;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Dictionary<string,Material> MakeMaterials(SceneSpec spec) {
            var result=new Dictionary<string,Material>();
            foreach(string key in new[]{"Ground","Rock"}) {
                var m=Make(key,"World010/Terrain URP");
                m.SetTexture("_GroundTex",Tex("Ground"));
                m.SetTexture("_RoadTex",Tex("Road"));
                m.SetTexture("_RockTex",Tex("Rock"));
                m.SetTexture("_RockNormal",Tex("RockNormal"));
                m.SetTexture("_GroundNormal",Tex("GroundNormal"));
                m.SetTexture("_RoadNormal",Tex("RoadNormal"));
                m.SetTexture("_Control",Tex("TerrainControl"));
                m.SetColor("_GroundTint",spec.tint);
                m.SetColor("_RoadTint",spec.road);
                m.SetColor("_RockTint",spec.rock);
                m.SetFloat("_WetScale",key=="Rock"?0:MapName=="world006"?.59f:MapName=="world009"?.41f:
                    MapName=="world005"?.29f:MapName=="world007"?.07f:.21f);
                if(key=="Rock")m.EnableKeyword("MAP_ROCK_PROP");else m.DisableKeyword("MAP_ROCK_PROP");
                result[key]=m;
            }
            foreach(string key in new[]{"Water","Lava"}) {
                if(key=="Water"&&spec.waterCount==0||key=="Lava"&&spec.lavaCount==0)continue;
                var m=Make(key,"World010/Flow URP");
                m.SetTexture("_Control",Tex("TerrainControl"));
                m.SetTexture("_SurfaceTex",Tex(key));
                m.SetColor("_Tint",spec.water);
                if(key=="Lava") {
                    m.EnableKeyword("MAP_LAVA");
                    m.SetFloat("_ZWrite",1);
                    m.renderQueue=(int)RenderQueue.Geometry+5;
                } else {
                    m.DisableKeyword("MAP_LAVA");
                    m.SetFloat("_ZWrite",0);
                    m.SetFloat("_Storm",MapName=="world009"?1:0);
                    m.renderQueue=(int)RenderQueue.Transparent+1;
                }
                result[key]=m;
            }
            foreach(string key in new[]{"Timber","Bark","Metal","Bone","Cloth","Glow","Road"}) {
                var m=Make(key,"Universal Render Pipeline/Lit");
                m.SetTexture("_BaseMap",Tex(key));
                m.SetColor("_BaseColor",key=="Cloth"?new Color(.72f,.42f,.36f):
                    key=="Metal"?new Color(.77f,.79f,.81f):key=="Bone"?new Color(.87f,.94f,1):Color.white);
                m.SetFloat("_Smoothness",key=="Metal"?.29f:.14f);
                m.SetFloat("_Metallic",key=="Metal"?.53f:0);
                if(key=="Road"||key=="Timber"||key=="Bark")MakeMatte(m);
                string normalName=key=="Road"?"RoadLitNormal":key+"Normal";
                if(File.Exists(Root+"/Textures/"+normalName+".png")||File.Exists(Root+"/Textures/"+normalName+".jpg")) {
                    m.SetTexture("_BumpMap",Tex(normalName));
                    m.SetFloat("_BumpScale",.69f);
                    m.EnableKeyword("_NORMALMAP");
                }
                if(key=="Glow") {
                    m.SetTexture("_EmissionMap",Tex(key));
                    m.SetColor("_EmissionColor",new Color(3.8f,1.33f,.39f));
                    m.EnableKeyword("_EMISSION");
                }
                if(key=="Cloth")m.SetInt("_Cull",0);
                result[key]=m;
            }
            foreach(string filename in Directory.GetFiles(Root+"/Textures","Plant*.png")) {
                string key=Path.GetFileNameWithoutExtension(filename);
                var m=Make(key,"Universal Render Pipeline/Lit");
                m.SetTexture("_BaseMap",Tex(key));
                m.SetColor("_BaseColor",key=="Plant6"?new Color(.83f,.89f,.81f):
                    key=="Plant7"?new Color(.76f,.83f,.77f):key=="Plant4"||key=="Plant5"?
                    new Color(.70f,.78f,.64f):new Color(.58f,.66f,.56f));
                m.SetFloat("_AlphaClip",1);
                m.SetFloat("_Cutoff",.54f);
                m.SetFloat("_SpecularHighlights",0);
                m.SetFloat("_Smoothness",.06f);
                m.SetInt("_Cull",0);
                m.SetOverrideTag("RenderType","TransparentCutout");
                m.EnableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                m.renderQueue=(int)RenderQueue.AlphaTest;
                result[key]=m;
            }
            if(MapName=="world009") {
                var m=Make("Lightning","World010/Art Effects");
                m.SetTexture("_BaseMap",Tex("Lightning"));
                m.SetColor("_Tint",new Color(.80f,.91f,1.05f));
                m.SetFloat("_Flash",1);
                m.EnableKeyword("ART_BOLT");
                result["Lightning"]=m;
            }
            AddArtMaterials(result);
            AssetDatabase.SaveAssets();
            return result;
        }

        static void AddCollision(Transform root,SceneSpec spec) {
            var collision=new GameObject("WorldCollision").transform;
            collision.SetParent(root,false);
            float mx=spec.width*.5f,mz=spec.length*.5f;
            MakeBox("AirWall_West",collision,new Vector3(-mx-1,85,0),new Vector3(2,450,spec.length+4),Quaternion.identity);
            MakeBox("AirWall_East",collision,new Vector3(mx+1,85,0),new Vector3(2,450,spec.length+4),Quaternion.identity);
            MakeBox("AirWall_South",collision,new Vector3(0,85,-mz-1),new Vector3(spec.width+4,450,2),Quaternion.identity);
            MakeBox("AirWall_North",collision,new Vector3(0,85,mz+1),new Vector3(spec.width+4,450,2),Quaternion.identity);
            var data=JsonUtility.FromJson<HazardSet>(File.ReadAllText(Root+"/Collision/Hazards.json"));
            var hazardParent=new GameObject("NonWalkableEdges").transform;
            hazardParent.SetParent(collision,false);
            int i=0;
            foreach(var edge in data.edges) {
                var a=new Vector3(edge.a[0],edge.a[1],edge.a[2]);
                var b=new Vector3(edge.b[0],edge.b[1],edge.b[2]);
                var v=b-a;
                v.y=0;
                float floor=Mathf.Min(a.y,b.y), ceiling=Mathf.Max(a.y,b.y)+9;
                MakeBox("Boundary_"+i++,hazardParent,new Vector3((a.x+b.x)*.5f,(floor+ceiling)*.5f,(a.z+b.z)*.5f),
                        new Vector3(.42f,ceiling-floor,v.magnitude+.24f),Quaternion.LookRotation(v));
            }
        }

        static void MakeBox(string name,Transform parent,Vector3 center,Vector3 size,Quaternion direction) {
            var wall=new GameObject(name);
            wall.transform.SetParent(parent,false);
            wall.transform.SetPositionAndRotation(center,direction);
            wall.AddComponent<BoxCollider>().size=size;
            wall.isStatic=true;
        }

        static void AddLighting(SceneSpec spec,Dictionary<string,Material> mats) {
            var sun=new GameObject("Sun").AddComponent<Light>();
            sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(spec.euler);
            sun.intensity=spec.power;
            sun.color=spec.sun;
            sun.shadows=LightShadows.Soft;
            sun.shadowStrength=.82f;
            sun.shadowBias=.038f;
            sun.shadowNormalBias=.22f;
            RenderSettings.sun=sun;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=spec.ambient;
            RenderSettings.ambientEquatorColor=spec.ambient*.76f;
            RenderSettings.ambientGroundColor=spec.ambient*.48f;
            RenderSettings.fog=true;
            RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogColor=spec.fog;
            RenderSettings.fogDensity=spec.density;
            if(MapName!="world010") {
                var sky=Make("Sky","World010/Cloud Sky");
                var zenith=MapName=="world009"?new Color(.13f,.22f,.36f):
                    MapName=="world006"?new Color(.33f,.36f,.42f):
                    MapName=="world007"?new Color(.51f,.51f,.58f):new Color(.35f,.52f,.64f);
                sky.SetColor("_Zenith",zenith);
                sky.SetColor("_Horizon",spec.fog);
                sky.SetColor("_CloudTint",MapName=="world009"?new Color(.29f,.38f,.52f):spec.fog*1.08f);
                sky.SetFloat("_Cover",MapName=="world006"||MapName=="world009"?.34f:.52f);
                sky.SetVector("_SunDir",-sun.transform.forward);
                sky.SetFloat("_StormFlash",MapName=="world009"?.84f:0f);
                sky.SetVector("_StormDir",new Vector4(.21f,.45f,.86f,0));
                RenderSettings.skybox=sky;
            }
            if(MapName=="world010") {
                RenderSettings.skybox=null;
            }
        }

        [MenuItem("Tools/World010/Open Demo Scene")]
        public static void Open() {
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(Root+"/Generated/"+MapName+".unity");
        }
    }

    public class ImportSettings:AssetPostprocessor {
        void OnPreprocessModel() {
            if(assetPath!=Builder.Root+"/Models/"+Builder.MapName+".fbx")return;
            var importer=(ModelImporter)assetImporter;
            importer.globalScale=1;
            importer.useFileScale=true;
            importer.bakeAxisConversion=true;
            importer.importAnimation=false;
            importer.importCameras=false;
            importer.importLights=false;
            importer.importNormals=ModelImporterNormals.Import;
            importer.importTangents=ModelImporterTangents.CalculateMikk;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.materialName=ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialLocation=ModelImporterMaterialLocation.InPrefab;
        }

        void OnPreprocessTexture() {
            if(!assetPath.StartsWith(Builder.Root+"/Textures/"))return;
            var importer=(TextureImporter)assetImporter;
            string name=Path.GetFileNameWithoutExtension(assetPath);
            importer.textureShape=TextureImporterShape.Texture2D;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=2048;
            importer.anisoLevel=8;
            importer.mipmapEnabled=true;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;
            if(name=="TerrainControl") {
                importer.sRGBTexture=false;
                importer.wrapMode=TextureWrapMode.Clamp;
                importer.alphaSource=TextureImporterAlphaSource.FromInput;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
            } else if(name.StartsWith("Plant")) {
                importer.alphaIsTransparency=true;
                importer.alphaSource=TextureImporterAlphaSource.FromInput;
                importer.wrapMode=TextureWrapMode.Clamp;
                importer.mipMapsPreserveCoverage=true;
                importer.alphaTestReferenceValue=.54f;
            } else if(name=="RockNormal"||name=="RoadNormal"||name=="GroundNormal") {
                importer.textureType=TextureImporterType.Default;
                importer.sRGBTexture=false;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
            } else if(name.EndsWith("Normal")) {
                importer.textureType=TextureImporterType.NormalMap;
            }
        }
    }
}
