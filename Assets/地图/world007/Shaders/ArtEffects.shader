Shader "World007/Art Effects" {
 Properties {
  _BaseMap("Effect color",2D)="white"{}
  _Tint("Effect tint",Color)=(1,1,1,1)
  _Flash("Lightning multiplier",Range(0,1))=1
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+4"}
  Cull Off ZWrite Off
  Blend SrcAlpha OneMinusSrcAlpha
  Pass {
   Tags {"LightMode"="UniversalForwardOnly"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #pragma shader_feature_local_fragment _ ART_MAGMA ART_FLAME ART_PUDDLE ART_BOLT ART_STREAM_FOAM
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
    half4 _Tint;
    half _Flash;
   CBUFFER_END
   struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;half fog:TEXCOORD2;};
   V Vert(A a){V o;VertexPositionInputs p=GetVertexPositionInputs(a.p.xyz);o.p=p.positionCS;o.world=p.positionWS;o.uv=a.uv;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(115.7,243.19)))*43758.5453);}
   float n(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   half4 Frag(V i):SV_Target{
    float2 p=i.uv;
    float time=_Time.y;
    half3 col;
    half alpha;
    #if defined(ART_BOLT)
     col=half3(2.1,3.5,6.5)*_Tint.rgb;alpha=_Flash*.92;
    #elif defined(ART_STREAM_FOAM)
     float r=length(p-.5)*2;
     float patch=n(i.world.xz*4.4+float2(time*.73,-time*3.25));
     float filaments=n(float2(p.x*10,p.y*6-time*1.94));
     col=lerp(half3(.26,.55,.57),half3(.83,.96,.97),patch);
     alpha=(1-smoothstep(.17,.99,r))*.69*smoothstep(.27,.88,patch*.60+filaments*.40);
    #elif defined(ART_FLAME)
     float drift=sin(p.y*8+time*8+i.world.x*.13)*.115*p.y;
     float w=abs(p.x-.5-drift);
     float flicker=n(float2(p.x*7,p.y*5-time*4.2)+i.world.z*.069);
     float body=1-smoothstep(.14,.45,w/max(.20,1-p.y*.86));
     float tip=smoothstep(.40,.78,1-p.y+(flicker-.49)*.28);
     alpha=body*tip*smoothstep(0,.15,p.y)*.96;
     col=lerp(half3(3.1,.34,.017),half3(5.6,2.3,.33),pow(body,2)*(1-p.y*.52));
    #elif defined(ART_MAGMA)
     float run=n(float2(p.x*6.3,p.y*6.5-time*.8));
     float strands=n(float2(p.x*22+3.2*run,p.y*9.5-time*1.42));
     float eddy=n(i.world.xz*2.33+float2(i.world.y*.31-time*.55,0));
     float hot=smoothstep(.24,.86,run*.57+strands*.34+eddy*.34);
     col=lerp(half3(.69,.032,.002),half3(4.50,1.02,.074),hot);
     alpha=smoothstep(0,.17,p.x)*smoothstep(1,.83,p.x)*(.70+hot*.26);
    #elif defined(ART_PUDDLE)
     float grain=n(i.world.xz*2.8)*.66+n(i.world.xz*.64)*.34;
     float3 view=GetWorldSpaceNormalizeViewDir(i.world);
     half sheen=pow(1-saturate(view.y),6);
     col=lerp(half3(.051,.062,.068),half3(.19,.24,.27),grain)+sheen*half3(.19,.23,.26);
     float edge=1-length(p-.5)*2;
     alpha=smoothstep(-.08,.28,edge+(n(p*18)-.5)*.27)*lerp(.26,.43,grain);
    #else
     float jitter=n(i.world.xz*1.88+float2(i.world.y*.35-time*.73,0));
     float streak=n(float2(p.x*31+jitter*1.7,p.y*7-time*2.1));
     float turbulence=n(float2(p.x*8+jitter,p.y*24-time*3.5));
     float foam=smoothstep(.38,.88,streak*.52+turbulence*.35+jitter*.35);
     col=lerp(half3(.12,.26,.28),half3(.67,.82,.86),foam);
     alpha=smoothstep(0,.19,p.x)*smoothstep(1,.81,p.x)*(.46+foam*.44);
    #endif
    return half4(MixFog(col,i.fog),alpha);
   }
   ENDHLSL
  }
 }

 SubShader {
  Tags {"RenderType"="Transparent" "Queue"="Transparent+4"}
  Cull Off ZWrite Off
  Blend SrcAlpha OneMinusSrcAlpha
  Pass {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #pragma shader_feature_local_fragment _ ART_MAGMA ART_FLAME ART_PUDDLE ART_BOLT ART_STREAM_FOAM
   #define WORLD005_BUILTIN
   #include "../../SharedRendering/Builtin.hlsl"
   #include "../../SharedRendering/ArtEffectsPass.hlsl"
   ENDCG
  }
 }
}
