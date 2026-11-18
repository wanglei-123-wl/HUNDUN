Shader "World009/Art Effects" {
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
   #pragma shader_feature_local_fragment _ ART_MAGMA ART_FLAME ART_PUDDLE ART_BOLT ART_CASCADE ART_FOAM
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
     col=half3(2.15,3.42,6.0)*_Tint.rgb;alpha=pow(_Flash,.86)*.96;
    #elif defined(ART_CASCADE)
     float drift=n(float2(p.x*13+1.12,p.y*2+time*1.4));
     float longFlow=n(float2(p.x*46+drift*.82,p.y*6+time*3.31));
     float falling=n(float2(p.x*12.5+drift*.6,p.y*16+time*4.7));
     float breakup=n(float2(p.x*5+i.world.x*.94,p.y*12-time*2.8));
     float filament=pow(saturate(longFlow*.54+drift*.29+falling*.29),1.8);
     float contact=(1-smoothstep(.02,.24,p.y))*.16;
     col=lerp(half3(.075,.143,.175),half3(.68,.86,1.04),saturate(filament+contact));
     float skirt=smoothstep(.04,.33,p.y)+(1-smoothstep(.04,.33,p.y))*smoothstep(.16,.64,breakup);
     alpha=smoothstep(0,.13+(breakup-.5)*.14,p.x)*(1-smoothstep(.86+(breakup-.5)*.14,1,p.x))*smoothstep(.0,.050,p.y)*(.40+filament*.49)*skirt;
    #elif defined(ART_FOAM)
     float radius=length((p-.5)*2);
     float detail=n(i.world.xz*6.3+time*.49);
     float cells=n(i.world.xz*2.4-time*.21);
     float arcs=.5+.5*sin(radius*16-time*3+cells*6);
     col=lerp(half3(.24,.41,.49),half3(.74,.91,.98),smoothstep(.35,.80,detail));
     alpha=(1-smoothstep(.12,1,radius+.22*(cells-.5)))*(.22+.50*arcs*detail);
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
