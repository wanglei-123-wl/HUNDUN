Shader "World008/Flow URP" {
 Properties {
  _Control("Shore distance",2D)="black"{}
  _SurfaceTex("Surface color",2D)="white"{}
  _Tint("Surface tint",Color)=(.06,.31,.37,1)
  _ZWrite("Write depth",Float)=1
  _Storm("Cold storm surface",Range(0,1))=0
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+5"}
  Cull Off ZWrite [_ZWrite]
  Pass {
   Name "Flow" Tags {"LightMode"="UniversalForwardOnly"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #pragma shader_feature_local_fragment _ MAP_LAVA
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
   TEXTURE2D(_Control);SAMPLER(sampler_Control);
   TEXTURE2D(_SurfaceTex);SAMPLER(sampler_SurfaceTex);
   CBUFFER_START(UnityPerMaterial)
    half4 _Tint;
    half _Storm;
   CBUFFER_END
   struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;half fog:TEXCOORD2;};
   V Vert(A a){V o;VertexPositionInputs p=GetVertexPositionInputs(a.p.xyz);o.p=p.positionCS;o.world=p.positionWS;o.uv=a.uv;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float Noise(float2 x){float2 p=floor(x),f=frac(x);f=f*f*(3-2*f);
    return lerp(lerp(Hash(p),Hash(p+float2(1,0)),f.x),lerp(Hash(p+float2(0,1)),Hash(p+1),f.x),f.y);}
   half3 ScreenReflection(float3 world,half3 direction,out half validity) {
    validity=0;
    half3 color=0;
    [unroll] for(int j=1;j<=8;j++) {
     float d=1.3*j*j*.48;
     float3 q=world+direction*d;
     float4 clip=TransformWorldToHClip(q);
     float4 sc=ComputeScreenPos(clip);
     float2 uv=sc.xy/max(sc.w,.001);
     if(clip.w>0&&all(uv>.012)&&all(uv<.988)) {
      float surface=LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams);
      float eye=-TransformWorldToView(q).z;
      float delta=eye-surface;
      if(delta>.04&&delta<2.3&&validity<.01) {
       color=SampleSceneColor(uv);
       validity=saturate(1-d/48)*smoothstep(.02,.18,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
      }
     }
    }
    return color;
   }
   half4 Frag(V i):SV_Target{
    float2 p=i.world.xz;
    float t=_Time.y;
    half shore=SAMPLE_TEXTURE2D(_Control,sampler_Control,i.uv).a;
    half3 color;
    #if defined(MAP_LAVA)
     float2 q=p*.17+float2(t*.035,-t*.021);
     float broad=Noise(q*.55+4.4);
     float flow=Noise(q*2.1+2*broad)+Noise(q*4.4-broad)*.30;
     float vein=pow(saturate(1-abs(flow-.56)*4.6),5);
     half heat=smoothstep(.20,.82,broad+.26*vein);
     half3 crust=half3(.095,.022,.008);
     color=lerp(crust,half3(.55,.07,.012),heat)+vein*half3(1.21,.36,.027);
     color+=half3(.47,.10,.01)*pow(heat,3);
    #else
     float phase=Noise(p*.23+float2(t*.12,-t*.15));
     float n1=Noise(p*1.31+float2(t*.37,-t*.66));
     float n2=Noise(p*.95+float2(-t*.58,t*.34));
     float w1=sin(dot(p,float2(1.76,1.24))+1.7*t+8.1*phase)*.35+n1-.5;
     float w2=cos(dot(p,float2(-2.2,1.05))-1.1*t+7.1*phase)*.30+n2-.5;
     half3 n=normalize(half3(w1*.13,1,w2*.12));
     half3 view=GetWorldSpaceNormalizeViewDir(i.world);
     half f=pow(1-saturate(dot(n,view)),4);
     float2 suv=GetNormalizedScreenSpaceUV(i.p);
     float bed=LinearEyeDepth(SampleSceneDepth(suv),_ZBufferParams);
     float own=-TransformWorldToView(i.world).z;
     float depth=clamp(bed-own,0,15);
     float2 refraction=clamp(suv+n.xz*.018*saturate(depth*1.9),.001,.999);
     float refractedDepth=LinearEyeDepth(SampleSceneDepth(refraction),_ZBufferParams);
     refraction=lerp(suv,refraction,smoothstep(.09,.49,refractedDepth-own));
     half3 bedColor=SampleSceneColor(refraction);
     half3 deep=_Tint.rgb*(.56+phase*.44);
     half3 rim=lerp(_Tint.rgb,half3(.16,.30,.23),.48);
     half clarity=exp(-depth*(.37+_Storm*.46));
     color=lerp(deep,bedColor*half3(.65,.88,.82)+rim*.27,clarity*.84);
     half validity;
     half3 reflection=ScreenReflection(i.world,reflect(-view,n),validity);
     half3 sky=lerp(half3(.35,.49,.54),half3(.21,.35,.49),_Storm);
     color=lerp(color,lerp(sky,reflection,validity*.84),f*.74+.09);
     half3 halfv=normalize(view+GetMainLight().direction);
     color+=pow(saturate(dot(n,halfv)),46)*half3(.54,.60,.66)*.46;
     half foam=smoothstep(.028,.15,depth)*(1-smoothstep(.24,.92,depth))*smoothstep(.48,.85,phase+.23*w2);
     foam+=.48*(1-smoothstep(.02,.115,shore))*smoothstep(.51,.85,phase+.22*n2);
     color+=foam*lerp(half3(.34,.46,.46),half3(.15,.24,.29),_Storm);
    #endif
    return half4(MixFog(color,i.fog),1);
   }
   ENDHLSL
  }
 }

 SubShader {
  Tags {"RenderType"="Opaque" "Queue"="Geometry+5"}
  Cull Off ZWrite [_ZWrite]
  GrabPass {"_World005OpaqueColor"}
  Pass {
   Name "FlowBuiltin"
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #pragma shader_feature_local_fragment _ MAP_LAVA
   #define WORLD005_BUILTIN
   #define WORLD005_SCENE_DEPTH
   #define WORLD005_GRAB_COLOR
   #include "../../SharedRendering/Builtin.hlsl"
   #include "../../SharedRendering/SurfacePass.hlsl"
   ENDCG
  }
 }
}
