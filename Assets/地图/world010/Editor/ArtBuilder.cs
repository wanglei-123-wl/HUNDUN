using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace World010 {
    public static partial class Builder {
        static void MakeMatte(Material material) {
            material.SetFloat("_SpecularHighlights",0);
            material.SetFloat("_EnvironmentReflections",0);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        static void AddArtMaterials(Dictionary<string,Material> result) {
            if(MapName=="world010") {
                foreach(var key in new[]{"Basalt","BasaltDark","BasaltWarm","BasaltInset","HotVein"}) {
                    var m=Make(key,key=="HotVein"?"World010/Art Effects":"Universal Render Pipeline/Lit");
                    m.SetTexture("_BaseMap",Tex(key=="HotVein"?"Glow":key=="BasaltInset"?"Rock":"Road"));
                    if(key=="HotVein") {
                        m.SetColor("_Tint",Color.white);
                        m.EnableKeyword("ART_FAULT");
                    } else {
                        m.SetColor("_BaseColor",key=="BasaltDark"?new Color(.63f,.66f,.73f):key=="BasaltWarm"?new Color(.79f,.71f,.65f):
                            key=="BasaltInset"?new Color(.33f,.30f,.29f):new Color(.75f,.77f,.82f));
                        MakeMatte(m);
                        if(key!="BasaltInset") {
                            m.SetTexture("_BumpMap",Tex("RoadLitNormal"));
                            m.SetFloat("_BumpScale",.83f);
                            m.EnableKeyword("_NORMALMAP");
                        }
                    }
                    result[key]=m;
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
                var m=Make(key,"World010/Art Effects");
                m.SetTexture("_BaseMap",Tex(key));
                m.SetColor("_Tint",Color.white);
                if(key=="MagmaFall")m.EnableKeyword("ART_MAGMA");
                if(key=="Flame")m.EnableKeyword("ART_FLAME");
                if(key=="Ash"){m.DisableKeyword("ART_FLAME");m.EnableKeyword("ART_EMBER");}
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
            bloom.intensity.Override(MapName=="world010"?.65f:MapName=="world009"?.32f:.16f);
            bloom.threshold.Override(MapName=="world010"?.94f:1.13f);
            bloom.scatter.Override(MapName=="world010"?.73f:.57f);
            var color=ProfileOverride<ColorAdjustments>(profile);
            color.postExposure.Override(MapName=="world010"?.63f:MapName=="world006"?.14f:MapName=="world009"?.26f:.12f);
            color.contrast.Override(MapName=="world008"?8:MapName=="world006"?8:6);
            color.saturation.Override(MapName=="world005"?-12:MapName=="world008"?-10:MapName=="world006"?-12:-4);
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
            var material=Make("Mist_"+name,"World010/Local Atmosphere");
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

        static void Embers(Transform root,Material mat,Vector3 p,float width,bool small=false) {
            var obj=new GameObject("Lava embers");
            obj.transform.SetParent(root,false);
            obj.transform.localPosition=p;
            obj.transform.localRotation=Quaternion.Euler(-90,0,0);
            var system=obj.AddComponent<ParticleSystem>();
            var main=system.main;
            main.loop=true;
            main.playOnAwake=true;
            main.maxParticles=small?36:96;
            main.startLifetime=new ParticleSystem.MinMaxCurve(.72f,small?2.1f:3.7f);
            main.startSpeed=new ParticleSystem.MinMaxCurve(.7f,small?2.6f:2.15f);
            main.startSize=new ParticleSystem.MinMaxCurve(small?.025f:.08f,small?.088f:.26f);
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            var shape=system.shape;
            shape.shapeType=ParticleSystemShapeType.Circle;
            shape.radius=width;
            var emission=system.emission;
            emission.rateOverTime=small?11:16;
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
                    Mist(rig,"LowerFall",new Vector3(-24,11.5f,-137),new Vector3(31,16,27),new Color(.67f,.76f,.73f),2.1f);
                    Mist(rig,"BluffVeils",new Vector3(-11,28,-82),new Vector3(135,32,60),new Color(.65f,.73f,.68f),.84f);
                    Mist(rig,"Creek",new Vector3(-32,17,15),new Vector3(68,18,244),new Color(.62f,.73f,.70f),.59f);
                    Mist(rig,"Spring",new Vector3(-60,46,344),new Vector3(46,15,65),new Color(.68f,.77f,.72f),.86f);
                    break;
                case "world006":
                    Mist(rig,"FarGates",new Vector3(0,13,165),new Vector3(143,24,247),new Color(.60f,.65f,.72f),.53f);
                    Fill(rig,"Stone relief fill",new Vector3(-13,11,-229),new Color(.72f,.80f,.96f),38,65);
                    break;
                case "world007":
                    Mist(rig,"TombDust",new Vector3(19,21,62),new Vector3(220,22,160),new Color(.75f,.58f,.44f),.42f);
                    Mist(rig,"LowDust",new Vector3(-99,11,-126),new Vector3(102,7,97),new Color(.68f,.51f,.33f),.28f);
                    Fill(rig,"Sword edge light",new Vector3(-152,13,-154),new Color(1,.81f,.59f),34,34);
                    break;
                case "world008":
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
                    Mist(rig,"EastFallSteam",new Vector3(102,-9,57),new Vector3(43,39,53),new Color(.84f,.47f,.34f),2.05f);
                    Mist(rig,"GalleryDistance",new Vector3(79,-2,72),new Vector3(81,39,79),new Color(.52f,.32f,.30f),1.25f);
                    Mist(rig,"EastFissure",new Vector3(119,-24,1),new Vector3(36,13,89),new Color(.73f,.23f,.075f),1.05f);
                    Mist(rig,"MoltenBasin",new Vector3(24,-22,-2),new Vector3(91,23,91),new Color(.72f,.32f,.18f),1.17f);
                    Mist(rig,"DeepGrotto",new Vector3(-52,-16,21),new Vector3(91,31,74),new Color(.61f,.38f,.32f),1.03f);
                    Fill(rig,"East lavafall bounce",new Vector3(100,-6,50),new Color(1,.42f,.17f),56,468);
                    Fill(rig,"Lava channel bounce",new Vector3(115,-24,-1),new Color(1,.35f,.11f),49,258);
                    Fill(rig,"Lower lava shelf",new Vector3(119,-23,34),new Color(1,.32f,.08f),52,280);
                    Fill(rig,"Entrance reflected skylight",new Vector3(85,-2,-46),new Color(.70f,.74f,1),46,74);
                    Fill(rig,"Left rim cascade bounce",new Vector3(69,1,-31),new Color(1,.42f,.15f),34,142);
                    Fill(rig,"Gallery diffuse fire fill",new Vector3(96,-12,-22),new Color(1,.46f,.19f),37,30);
                    Fill(rig,"Basin cascade bounce",new Vector3(33,-12,30),new Color(1,.41f,.13f),62,349);
                    Fill(rig,"Core bounce",new Vector3(-67,-17,10),new Color(1,.40f,.16f),64,245);
                    Fill(rig,"Core approach diffuse bounce",new Vector3(-30,-16,48),new Color(1,.62f,.42f),48,92);
                    Fill(rig,"Upper hall reflected glow",new Vector3(55,-4,-68),new Color(1,.58f,.32f),40,98);
                    Fill(rig,"Cooling platform fill",new Vector3(-49,8,-104),new Color(.62f,.68f,1),57,65);
                    for(int i=0;i<(spec.galleryFire?.Length??0);i++) {
                        var p=spec.galleryFire[i];
                        Fill(rig,"Brazier light "+i,p,new Color(1,.51f,.23f),15,10.5f);
                        Embers(rig,mats["Ash"],p-Vector3.up*.45f,.55f,true);
                    }
                    Embers(rig,mats["Ash"],new Vector3(115,-26,42),5);
                    Embers(rig,mats["Ash"],new Vector3(38,-28,28),6);
                    break;
            }
        }
    }
}
