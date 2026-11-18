Shader "World010/Art Effects" {
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
   #pragma shader_feature_local_fragment _ ART_MAGMA ART_FLAME ART_PUDDLE ART_BOLT ART_EMBER ART_FAULT
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
    #elif defined(ART_FAULT)
     float heat=n(i.world.xz*2.28+i.world.y*.91+time*.052)*.66+n(i.world.xy*4.62)*.34;
     col=lerp(half3(.88,.034,.002),half3(3.18,.67,.045),smoothstep(.20,.80,heat));
     alpha=.77+.20*heat;
    #elif defined(ART_EMBER)
     float radius=length(p-.5)*2;
     col=lerp(half3(3.7,1.58,.34),half3(1.1,.13,.012),saturate(radius*1.6));
     alpha=1-smoothstep(.08,.89,radius);
    #elif defined(ART_FLAME)
     float phase=i.world.z*.056+i.world.x*.071;
     float a=n(float2(p.x*4.2+phase,p.y*3.9-time*3.7));
     float b=n(float2(p.x*13+phase+.8*a,p.y*10-time*6.4));
     float sway=sin(p.y*8+time*7.1+phase)*.12*p.y;
     float w=abs(p.x-.5-sway)+(b-.46)*.18;
     float body=1-smoothstep(.08,.43,w/max(.17,1-p.y*.8));
     float tip=smoothstep(.25,.64,1-p.y+(a-.5)*.5+(b-.5)*.23);
     float tongue=smoothstep(.16,.66,a*.52+b*.48+body*.29);
     alpha=body*tip*tongue*smoothstep(0,.065,p.y)*.88;
     float hot=saturate(body*.56+a*.24+b*.20-p.y*.18);
     col=lerp(half3(3.0,.22,.014),half3(5.5,2.19,.44),hot*hot);
    #elif defined(ART_MAGMA)
     float run=n(float2(p.x*6.3,p.y*6.5-time*.8));
     float strands=n(float2(p.x*22+3.2*run,p.y*9.5-time*1.42));
     float eddy=n(i.world.xz*2.33+float2(i.world.y*.31-time*.55,0));
     float streak=n(float2(p.x*43.1+run*.9,p.y*3.4-time*.82));
     float hot=smoothstep(.30,.95,run*.46+strands*.26+eddy*.26+streak*.35);
     col=lerp(half3(.66,.027,.002),half3(5.3,1.37,.16),hot);
     alpha=smoothstep(0,.19,p.x)*smoothstep(1,.81,p.x)*(.62+hot*.35);
    #elif defined(ART_PUDDLE)
     float grain=n(i.world.xz*2.8)*.66+n(i.world.xz*.64)*.34;
     col=lerp(half3(.047,.051,.052),half3(.12,.135,.138),grain);
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
