using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace World009 {
    public sealed class MarshReflection : MonoBehaviour {
        [SerializeField] Renderer[] water;
        [SerializeField] Renderer[] fog;
        Camera mirror;
        RenderTexture reflection;
        bool rendering;
        bool[] visibility;
        bool[] fogVisibility;

        static readonly int TextureId=Shader.PropertyToID("_World009Reflection");
        static readonly int HeightId=Shader.PropertyToID("_World009ReflectionHeight");
        static readonly int ReadyId=Shader.PropertyToID("_World009ReflectionReady");

        public void Configure(Renderer[] surfaces,Renderer[] atmosphere) {
            water=surfaces;
            fog=atmosphere;
        }

        void OnEnable() {
            if(Application.isPlaying)RenderPipelineManager.beginCameraRendering+=BeforeCamera;
        }

        void OnDisable() {
            RenderPipelineManager.beginCameraRendering-=BeforeCamera;
            if(mirror)ReleaseObject(mirror.gameObject);
            if(reflection) {
                reflection.Release();
                ReleaseObject(reflection);
            }
            mirror=null;
            reflection=null;
            Shader.SetGlobalFloat(ReadyId,0);
        }

        static void ReleaseObject(Object resource) {
            if(Application.isPlaying)Destroy(resource); else DestroyImmediate(resource);
        }

        void BeforeCamera(ScriptableRenderContext context,Camera source) {
            if(rendering||source==mirror)return;
            if(source.cameraType!=CameraType.Game||source!=Camera.main)return;
            // URP 14 renders safely inside the active camera context with this entry point.
            #pragma warning disable CS0618
            Render(source,()=>UniversalRenderPipeline.RenderSingleCamera(context,mirror));
            #pragma warning restore CS0618
        }

        // The editor test uses the same camera and clip setup through the render request API.
        public void PrepareCapture(Camera source) {
            Render(source,()=>RenderPipeline.SubmitRenderRequest(mirror,
                new UniversalRenderPipeline.SingleCameraRequest { destination=reflection }));
        }

        void Render(Camera source,System.Action renderCamera) {
            Shader.SetGlobalFloat(ReadyId,0);
            if(rendering||!source||source.orthographic||water==null)return;
            var surface=NearestVisibleWater(source);
            if(!surface)return;
            float height=surface.bounds.center.y;
            if(source.transform.position.y<height+.09f)return;
            EnsureResources();
            mirror.CopyFrom(source);
            mirror.enabled=false;
            mirror.cameraType=CameraType.Reflection;
            mirror.targetTexture=reflection;
            mirror.aspect=source.aspect;
            mirror.projectionMatrix=source.projectionMatrix;
            mirror.rect=new Rect(0,0,1,1);
            mirror.allowMSAA=false;
            mirror.useOcclusionCulling=false;
            Vector3 position=source.transform.position;
            position.y=2*height-position.y;
            Vector3 forward=source.transform.forward;
            forward.y=-forward.y;
            mirror.transform.SetPositionAndRotation(position,Quaternion.LookRotation(forward,Vector3.up));
            var reflectionMatrix=Matrix4x4.identity;
            reflectionMatrix.m11=-1;
            reflectionMatrix.m13=2*height;
            mirror.worldToCameraMatrix=source.worldToCameraMatrix*reflectionMatrix;
            var view=mirror.worldToCameraMatrix;
            var clipPosition=view.MultiplyPoint(new Vector3(0,height+.025f,0));
            var clipNormal=view.MultiplyVector(Vector3.up).normalized;
            var clipPlane=new Vector4(clipNormal.x,clipNormal.y,clipNormal.z,-Vector3.Dot(clipPosition,clipNormal));
            mirror.projectionMatrix=mirror.CalculateObliqueMatrix(clipPlane);
            rendering=true;
            bool inverted=GL.invertCulling;
            try {
                SetVisible(water,visibility,false);
                SetVisible(fog,fogVisibility,false);
                GL.invertCulling=!inverted;
                renderCamera();
            } finally {
                GL.invertCulling=inverted;
                SetVisible(water,visibility,true);
                SetVisible(fog,fogVisibility,true);
                rendering=false;
            }
            Shader.SetGlobalTexture(TextureId,reflection);
            Shader.SetGlobalFloat(HeightId,height);
            Shader.SetGlobalFloat(ReadyId,1);
        }

        Renderer NearestVisibleWater(Camera source) {
            Renderer best=null;
            float distance=float.PositiveInfinity;
            Plane[] planes=GeometryUtility.CalculateFrustumPlanes(source);
            foreach(var surface in water) {
                if(!surface||!surface.enabled)continue;
                Bounds bounds=surface.bounds;
                if(!GeometryUtility.TestPlanesAABB(planes,bounds))continue;
                float d=bounds.SqrDistance(source.transform.position);
                if(d<distance) { best=surface; distance=d; }
            }
            return best;
        }

        static void SetVisible(Renderer[] renderers,bool[] prior,bool restore) {
            if(renderers==null)return;
            for(int i=0;i<renderers.Length;i++) {
                var renderer=renderers[i];
                if(!renderer)continue;
                if(restore) renderer.enabled=prior[i];
                else {
                    prior[i]=renderer.enabled;
                    renderer.enabled=false;
                }
            }
        }

        void EnsureResources() {
            if(!mirror) {
                var go=new GameObject("world009 lake reflection camera") { hideFlags=HideFlags.HideAndDontSave };
                mirror=go.AddComponent<Camera>();
                mirror.enabled=false;
                var additional=mirror.GetUniversalAdditionalCameraData();
                additional.renderShadows=false;
                additional.renderPostProcessing=false;
                additional.requiresDepthOption=CameraOverrideOption.Off;
                additional.requiresColorOption=CameraOverrideOption.Off;
                visibility=new bool[water.Length];
                fogVisibility=new bool[fog?.Length??0];
            }
            if(!reflection) {
                reflection=new RenderTexture(768,768,24,RenderTextureFormat.DefaultHDR) {
                    name="world009 planar lake reflection",
                    hideFlags=HideFlags.HideAndDontSave,
                    wrapMode=TextureWrapMode.Clamp,
                    filterMode=FilterMode.Bilinear,
                    useMipMap=true,
                    autoGenerateMips=true,
                };
                reflection.Create();
            }
        }
    }
}
