using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace World005 {
    public static partial class Builder {
        static void MakeMatte(Material material) {
            material.SetFloat("_SpecularHighlights",0);
            material.SetFloat("_EnvironmentReflections",0);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        static void AddArtMaterials(Dictionary<string,Material> result) {
            if(File.Exists(Root+"/Textures/BoundarySlopeControl.png"))
                result["BoundarySlope"]=BoundaryMountainsBuilder.EnsureMaterial(result["Ground"]);
            if(File.Exists(Root+"/Textures/SlateV05.png")) {
                var rock=Make("Strata","World005/Terrain URP");
                rock.SetTexture("_GroundTex",Tex("Ground"));
                rock.SetTexture("_RockTex",Tex("SlateV05"));
                rock.SetTexture("_RockNormal",Tex("SlateV05Normal"));
                rock.SetColor("_RockTint",MapName=="world007"?new Color(1.15f,1.02f,.88f):
                    MapName=="world006"?new Color(.91f,1.01f,1.09f):new Color(.95f,1.05f,.95f));
                rock.EnableKeyword("MAP_ROCK_PROP");
                result["Strata"]=rock;
                var rush=Make("Rush","Universal Render Pipeline/Lit");
                rush.SetTexture("_BaseMap",Tex("Ground"));
                rush.SetColor("_BaseColor",MapName=="world007"?new Color(1.30f,1.14f,.73f):
                    MapName=="world006"?new Color(1.16f,1.02f,.71f):new Color(.85f,1.05f,.61f));
                rush.SetFloat("_Cull",0);
                MakeMatte(rush);
                result["Rush"]=rush;
                if(MapName=="world005") {
                    var foam=Make("CreekFoam","World005/Art Effects");
                    foam.SetTexture("_BaseMap",Tex("Fall"));
                    foam.EnableKeyword("ART_STREAM_FOAM");
                    foam.renderQueue=3022;
                    result["CreekFoam"]=foam;
                    var ceramic=Make("Ceramic","Universal Render Pipeline/Lit");
                    ceramic.SetTexture("_BaseMap",Tex("Road"));
                    ceramic.SetColor("_BaseColor",new Color(.55f,.36f,.23f));
                    MakeMatte(ceramic);
                    result["Ceramic"]=ceramic;
                }
            }
            foreach(string key in new[]{"BridgeStone","Carved","Inlay"}) {
                if(!File.Exists(Root+"/Textures/"+key+".jpg"))continue;
                var m=Make(key,"Universal Render Pipeline/Lit");
                m.SetTexture("_BaseMap",Tex(key));
                m.SetTexture("_BumpMap",Tex(key+"Normal"));
                m.SetFloat("_BumpScale",key=="Carved"?.95f:.72f);
                m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Metallic",key=="Inlay"?.52f:0);
                m.SetFloat("_Smoothness",key=="Inlay"?.26f:.14f);
                if(key!="Inlay")MakeMatte(m);
                m.SetColor("_BaseColor",key=="Carved"?new Color(.83f,.84f,.84f):Color.white);
                m.SetFloat("_Cull",0);
                result[key]=m;
            }
            if(MapName=="world007") {
                foreach(string key in new[]{"SwordSteel","SwordBronze","SwordJade","SwordEdge"}) {
                    var m=Make(key,"Universal Render Pipeline/Lit");
                    m.SetTexture("_BaseMap",Tex(key=="SwordJade"?"Inlay":"Metal"));
                    m.SetColor("_BaseColor",key=="SwordBronze"?new Color(.76f,.63f,.43f):
                        key=="SwordJade"?new Color(.43f,.70f,.57f):
                        key=="SwordEdge"?new Color(1.07f,1.05f,1.02f):new Color(.85f,.90f,.95f));
                    m.SetFloat("_Metallic",key=="SwordJade"?.13f:.31f);
                    m.SetFloat("_Smoothness",key=="SwordEdge"?.32f:.22f);
                    m.SetFloat("_EnvironmentReflections",0);
                    m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    result[key]=m;
                }
            }
            foreach(string key in new[]{"Fall","MagmaFall","Flame","Puddle","Ash"}) {
                if(!File.Exists(Root+"/Textures/"+key+".png"))continue;
                var m=Make(key,"World005/Art Effects");
                m.SetTexture("_BaseMap",Tex(key));
                m.SetColor("_Tint",Color.white);
                if(key=="MagmaFall")m.EnableKeyword("ART_MAGMA");
                if(key=="Flame"||key=="Ash")m.EnableKeyword("ART_FLAME");
                if(key=="Puddle")m.EnableKeyword("ART_PUDDLE");
                result[key]=m;
            }
        }

        static void ConfigureArtCamera(Camera camera) {
            camera.allowHDR=true;
            var data=camera.GetUniversalAdditionalCameraData();
            data.requiresDepthOption=CameraOverrideOption.On;
            data.requiresColorOption=CameraOverrideOption.On;
            data.renderPostProcessing=true;
        }

        static T ProfileOverride<T>(VolumeProfile profile) where T:VolumeComponent {
            if(profile.TryGet<T>(out var value))return value;
            value=profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(value,profile);
            return value;
        }

        static void AddPost(Transform root) {
            string path=Root+"/Generated/"+MapName+"_Look.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(!profile) {
                profile=ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile,path);
            }
            var tone=ProfileOverride<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom=ProfileOverride<Bloom>(profile);
            bloom.intensity.Override(MapName=="world010"?.48f:MapName=="world009"?.32f:.16f);
            bloom.threshold.Override(MapName=="world010"?.94f:1.13f);
            bloom.scatter.Override(.57f);
            var color=ProfileOverride<ColorAdjustments>(profile);
            color.postExposure.Override(MapName=="world010"?.66f:MapName=="world006"?.18f:MapName=="world009"?.26f:MapName=="world005"?.24f:.19f);
            color.contrast.Override(MapName=="world008"?8:MapName=="world006"?8:6);
            color.saturation.Override(MapName=="world005"?-12:MapName=="world008"?-17:MapName=="world006"?-12:MapName=="world007"?-17:-4);
            var vignette=ProfileOverride<Vignette>(profile);
            vignette.intensity.Override(.19f);
            vignette.smoothness.Override(.58f);
            foreach(var component in profile.components)EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
            var obj=new GameObject("Art grading and glow");
            obj.transform.SetParent(root,false);
            var volume=obj.AddComponent<Volume>();
            volume.isGlobal=true;
            volume.priority=3;
            volume.sharedProfile=profile;
        }

        static void Mist(Transform root,string name,Vector3 p,Vector3 size,Color color,float density) {
            var material=Make("Mist_"+name,"World005/Local Atmosphere");
            material.SetColor("_Tint",color);
            material.SetFloat("_Density",density);
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name="AtmosphereVolume_"+name;
            Object.DestroyImmediate(obj.GetComponent<Collider>());
            obj.transform.SetParent(root,false);
            obj.transform.localPosition=p;
            obj.transform.localScale=size;
            var r=obj.GetComponent<MeshRenderer>();
            r.sharedMaterial=material;
            r.shadowCastingMode=ShadowCastingMode.Off;
            r.receiveShadows=false;
            r.lightProbeUsage=LightProbeUsage.Off;
            r.reflectionProbeUsage=ReflectionProbeUsage.Off;
        }

        static Light Fill(Transform root,string name,Vector3 p,Color c,float range,float power) {
            var obj=new GameObject(name);
            obj.transform.SetParent(root,false);
            obj.transform.localPosition=p;
            var light=obj.AddComponent<Light>();
            light.type=LightType.Point;
            light.color=c;
            light.range=range;
            light.intensity=power;
            return light;
        }

        static void Embers(Transform root,Material mat,Vector3 p,float width) {
            var obj=new GameObject("Lava embers");
            obj.transform.SetParent(root,false);
            obj.transform.localPosition=p;
            obj.transform.localRotation=Quaternion.Euler(-90,0,0);
            var system=obj.AddComponent<ParticleSystem>();
            var main=system.main;
            main.loop=true;
            main.playOnAwake=true;
            main.maxParticles=96;
            main.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,3.7f);
            main.startSpeed=new ParticleSystem.MinMaxCurve(.7f,2.15f);
            main.startSize=new ParticleSystem.MinMaxCurve(.08f,.26f);
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            var shape=system.shape;
            shape.shapeType=ParticleSystemShapeType.Circle;
            shape.radius=width;
            var emission=system.emission;
            emission.rateOverTime=16;
            var renderer=obj.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial=mat;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
        }

        static void AddArtAtmosphere(Transform root,SceneSpec spec,Dictionary<string,Material> mats) {
            var rig=new GameObject("ArtAtmosphere").transform;
            rig.SetParent(root,false);
            AddPost(rig);
            switch(MapName) {
                case "world005":
                    Mist(rig,"TerraceSunHaze",new Vector3(9,32,-102),new Vector3(108,42,78),new Color(.73f,.79f,.72f),1.22f);
                    Fill(rig,"Creekbank Warm Fill",new Vector3(-51,15,-174),new Color(.98f,.90f,.69f),25,13);
                    Mist(rig,"LowerFall",new Vector3(-24,11.5f,-137),new Vector3(31,16,27),new Color(.67f,.76f,.73f),2.1f);
                    Mist(rig,"BluffVeils",new Vector3(-11,28,-82),new Vector3(135,32,60),new Color(.65f,.73f,.68f),.84f);
                    Mist(rig,"Creek",new Vector3(-32,17,15),new Vector3(68,18,244),new Color(.62f,.73f,.70f),.59f);
                    Mist(rig,"Spring",new Vector3(-60,46,344),new Vector3(46,15,65),new Color(.68f,.77f,.72f),.86f);
                    break;
                case "world006":
                    Mist(rig,"North Stone Tiers",new Vector3(0,21,241),new Vector3(119,49,105),new Color(.65f,.69f,.73f),1.13f);
                    Mist(rig,"Long Road Perspective",new Vector3(2,11,-18),new Vector3(118,18,138),new Color(.58f,.64f,.68f),.62f);
                    Mist(rig,"FarGates",new Vector3(0,13,165),new Vector3(143,24,247),new Color(.60f,.65f,.72f),.53f);
                    Fill(rig,"Stone relief fill",new Vector3(-13,11,-229),new Color(.72f,.80f,.96f),38,65);
                    break;
                case "world007":
                    Mist(rig,"Grave Horizon Bands",new Vector3(14,30,115),new Vector3(300,26,135),new Color(.79f,.65f,.50f),.82f);
                    Mist(rig,"TombDust",new Vector3(19,21,62),new Vector3(220,22,160),new Color(.75f,.58f,.44f),.42f);
                    Mist(rig,"LowDust",new Vector3(-99,11,-126),new Vector3(102,7,97),new Color(.68f,.51f,.33f),.28f);
                    Fill(rig,"Sword edge light",new Vector3(-152,13,-154),new Color(1,.87f,.71f),34,18);
                    break;
                case "world008":
                    Mist(rig,"Ravine Lower Basin",new Vector3(-54,36,-128),new Vector3(134,27,98),new Color(.68f,.69f,.57f),1.36f);
                    Fill(rig,"Fossil Soft Bounce",new Vector3(-60,35,-215),new Color(1,.86f,.61f),29,45);
                    Mist(rig,"FossilRavine",new Vector3(-43,28,-162),new Vector3(76,26,75),new Color(.65f,.66f,.60f),1.35f);
                    Mist(rig,"ForestRavine",new Vector3(-26,29,-117),new Vector3(179,25,178),new Color(.66f,.65f,.53f),.80f);
                    Mist(rig,"LowerRidge",new Vector3(-86,55,61),new Vector3(256,35,198),new Color(.64f,.65f,.56f),.66f);
                    Mist(rig,"HighRidge",new Vector3(109,82,197),new Vector3(175,25,178),new Color(.69f,.70f,.61f),.67f);
                    Fill(rig,"Pine warm rim",new Vector3(-53,39,-198),new Color(1,.82f,.61f),29,105);
                    break;
                case "world009":
                    Mist(rig,"WestBank",new Vector3(-54,4.6f,-71),new Vector3(123,8,77),new Color(.27f,.46f,.58f),.82f);
                    Mist(rig,"EastLake",new Vector3(115,7.5f,65),new Vector3(104,13,68),new Color(.29f,.46f,.60f),1.0f);
                    Mist(rig,"DistantStorm",new Vector3(82,15,115),new Vector3(170,16,113),new Color(.30f,.48f,.63f),.78f);
                    var weather=rig.gameObject.AddComponent<StormPulse>();
                    weather.bolts=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Effect_Thunder")).ToArray();
                    weather.flashLight=Fill(rig,"Lightning bounce",new Vector3(-48,20,-60),new Color(.43f,.69f,1),105,184);
                    Fill(rig,"Cold shore light",new Vector3(118,17,66),new Color(.57f,.72f,.96f),61,134);
                    break;
                case "world010":
                    Mist(rig,"LavafallSteam",new Vector3(34,-12,43),new Vector3(61,34,43),new Color(.79f,.37f,.19f),1.62f);
                    Mist(rig,"MoltenBasin",new Vector3(15,-22,-5),new Vector3(90,15,74),new Color(.59f,.19f,.07f),.80f);
                    Mist(rig,"DeepGrotto",new Vector3(-71,-16,18),new Vector3(43,19,62),new Color(.44f,.17f,.10f),.48f);
                    Fill(rig,"Lavafall bounce",new Vector3(38,-11,33),new Color(1,.34f,.11f),74,655);
                    Fill(rig,"Pool bounce",new Vector3(36,-20,-18),new Color(1,.35f,.078f),85,198);
                    Fill(rig,"Guard fire",new Vector3(83,-11,-24),new Color(1,.49f,.16f),37,54);
                    Fill(rig,"Cave wall rim",new Vector3(78,-16,19),new Color(1,.51f,.29f),56,178);
                    Fill(rig,"Core bounce",new Vector3(-86,-18,6),new Color(1,.39f,.12f),45,78);
                    Embers(rig,mats["Ash"],new Vector3(26,-27,36),8);
                    Embers(rig,mats["Ash"],new Vector3(8,-27,-21),11);
                    break;
            }
        }
    }
}
