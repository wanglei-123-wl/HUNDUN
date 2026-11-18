Shader "World005/Local Atmosphere" {
 Properties {
  _Tint("Mist light color",Color)=(.62,.73,.73,1)
  _Density("Density",Range(0,4))=1
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+34"}
  Cull Front ZWrite Off ZTest Always
  Blend SrcAlpha OneMinusSrcAlpha
  Pass {
   Tags {"LightMode"="UniversalForwardOnly"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   CBUFFER_START(UnityPerMaterial)
    half4 _Tint;
    half _Density;
   CBUFFER_END
   struct A{float4 p:POSITION;};
   struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;};
   V Vert(A a){V o;VertexPositionInputs p=GetVertexPositionInputs(a.p.xyz);o.p=p.positionCS;o.world=p.positionWS;return o;}
   half4 Frag(V i):SV_Target{
    float3 cam=GetCameraPositionWS();
    float3 ray=normalize(i.world-cam);
    float3 start=TransformWorldToObject(cam);
    float3 dir=mul((float3x3)unity_WorldToObject,ray);
    float3 inv=rcp(dir+1e-7);
    float3 a=(-.5-start)*inv,b=(.5-start)*inv;
    float3 near=min(a,b),far=max(a,b);
    float t0=max(0,max(near.x,max(near.y,near.z)));
    float t1=min(far.x,min(far.y,far.z));
    float eye=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.p)),_ZBufferParams);
    t1=min(t1,eye/max(.10,dot(ray,-UNITY_MATRIX_V[2].xyz))-.06);
    float ds=max(0,t1-t0)/10;
    if(ds<.002)return half4(0,0,0,0);
    half sum=0;
    float jitter=frac(dot(i.p.xy,float2(.75487,.56984)))*.68+.16;
    [unroll] for(int j=0;j<10;j++){
     float t=t0+(j+jitter)*ds;
     float3 q=start+dir*t;
     half shape=saturate(1-dot(q*2,q*2));
     float3 w=cam+ray*t;
     half swirl=sin(w.x*.114+w.z*.089+w.y*.23+_Time.y*.32+sin(w.z*.118)*1.48)*.34+
                sin(w.x*.263-w.z*.143+w.y*.27-_Time.y*.16)*.18+.53;
     sum+=shape*shape*smoothstep(.16,.78,swirl);
    }
    half opacity=1-exp(-sum*ds*_Density*.032);
    return half4(_Tint.rgb,saturate(opacity));
   }
   ENDHLSL
  }
 }

 SubShader {
  Tags {"RenderType"="Transparent" "Queue"="Transparent+34"}
  Cull Front ZWrite Off ZTest Always
  Blend SrcAlpha OneMinusSrcAlpha
  Pass {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #define WORLD005_BUILTIN
   #define WORLD005_SCENE_DEPTH
   #include "../../SharedRendering/Builtin.hlsl"
   #include "../../SharedRendering/MistPass.hlsl"
   ENDCG
  }
 }
}
