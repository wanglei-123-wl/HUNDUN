Shader "World009/Flow URP" {
 Properties {
  _Control("Shore distance",2D)="black"{}
  _SurfaceTex("Surface color",2D)="white"{}
  _Tint("Surface tint",Color)=(.06,.31,.37,1)
  _ZWrite("Write depth",Float)=1
  _Storm("Cold storm surface",Range(0,1))=0
  _StormSkyTex("Cloud reflection",2D)="black"{}
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
   #include "StormSky.hlsl"
   TEXTURE2D(_World009Reflection);
   SAMPLER(sampler_World009Reflection);
   float _World009ReflectionHeight,_World009ReflectionReady;
   struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;half fog:TEXCOORD2;};
   V Vert(A a){V o;VertexPositionInputs p=GetVertexPositionInputs(a.p.xyz);o.p=p.positionCS;o.world=p.positionWS;o.uv=a.uv;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float Noise(float2 x){float2 p=floor(x),f=frac(x);f=f*f*(3-2*f);
    return lerp(lerp(Hash(p),Hash(p+float2(1,0)),f.x),lerp(Hash(p+float2(0,1)),Hash(p+1),f.x),f.y);}
   half3 ScreenReflection(float3 world,half3 direction,out half validity) {
    validity=0;
    half3 color=0;
    [loop] for(int j=1;j<=11;j++) {
     float d=.63*j*j;
     float3 q=world+direction*d;
     float4 clip=TransformWorldToHClip(q);
     float4 sc=ComputeScreenPos(clip);
     float2 uv=sc.xy/max(sc.w,.001);
     if(clip.w>0&&all(uv>.012)&&all(uv<.988)) {
      float surface=LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams);
      float eye=-TransformWorldToView(q).z;
      float delta=eye-surface;
      if(delta>.06&&delta<1.75) {
       color=SampleSceneColor(uv);
       validity=saturate(1-d/75)*smoothstep(.017,.16,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
       if(validity>.01)break;
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
     float wind=dot(p,float2(.77,.637));
     float gust=Noise(p*.14+float2(.028,-.018)*t);
     float chop=Noise(p*.63+float2(3.16,-.16)*t);
     float phase0=wind*3.61+gust*6.13+chop*1.77+t*1.33;
     float phase1=dot(p,float2(.54,.84))*7.72+gust*2.1+chop*5.3+t*2.4;
     float wave0=sin(phase0)*(1-smoothstep(.34,1.72,fwidth(phase0)));
     float wave1=sin(phase1)*(1-smoothstep(.27,1.54,fwidth(phase1)));
     float wave2=sin(dot(p,float2(.93,-.36))*2.36+gust*5.26-t*.68);
     half3 n=normalize(half3(wave0*.011+wave1*.004+wave2*.006,1,wave0*.011+wave1*.007-wave2*.002));
     half3 view=GetWorldSpaceNormalizeViewDir(i.world);
     half f=pow(1-saturate(dot(n,view)),4);
     float2 suv=GetNormalizedScreenSpaceUV(i.p);
     float bed=LinearEyeDepth(SampleSceneDepth(suv),_ZBufferParams);
     float own=-TransformWorldToView(i.world).z;
     float depth=clamp(bed-own,0,15);
     float2 refraction=clamp(suv+n.xz*.042*saturate(depth*1.9),.001,.999);
     float refractedDepth=LinearEyeDepth(SampleSceneDepth(refraction),_ZBufferParams);
     refraction=lerp(suv,refraction,smoothstep(.09,.49,refractedDepth-own));
     half3 bedColor=SampleSceneColor(refraction);
     half3 deep=_Tint.rgb*(.62+gust*.29);
     half3 rim=lerp(_Tint.rgb,half3(.16,.30,.23),.48);
     half clarity=exp(-depth*(.55+_Storm*.28));
     color=lerp(deep,bedColor*half3(.65,.88,.82)+rim*.27,clarity*.84);
     half validity;
     half3 reflection=ScreenReflection(i.world,reflect(-view,n),validity);
     float3 reflected=reflect(-view,n);
     half3 sky=(StormPanorama(reflected,sin(t*.035)*.008)*.66+
                 StormPanorama(normalize(reflected+float3(.021,.042,-.012)),0)*.19+
                 StormPanorama(normalize(reflected+float3(-.018,-.035,.016)),0)*.15)*.49+half3(.017,.036,.047);
     if(_Storm<.5)sky=half3(.35,.49,.54);
     half3 reflectedScene=lerp(sky,reflection,validity*.35);
     if(_World009ReflectionReady>.5&&abs(i.world.y-_World009ReflectionHeight)<.075) {
      float2 reflectionUV=suv+n.xz*float2(.30,.21)+float2(.001,-.0015)*wave2;
      float edge=smoothstep(.0,.055,min(min(reflectionUV.x,reflectionUV.y),min(1-reflectionUV.x,1-reflectionUV.y)));
      half3 planar=SAMPLE_TEXTURE2D_LOD(_World009Reflection,sampler_World009Reflection,saturate(reflectionUV),1.80+gust*.90).rgb;
      reflectedScene=lerp(reflectedScene,planar*half3(.63,.77,.80)+half3(.009,.020,.024),edge*.86);
     }
     color=lerp(color,reflectedScene,f*.80+.13);
     float heading=dot(reflected.xz/max(length(reflected.xz),.001),float2(.267,.964));
     float reflectedBreak=pow(saturate(heading),56)*exp(-pow((reflected.y-.216)*3.55,2));
     float across=dot(p,float2(-.637,.77));
     float flecks=Noise(float2(wind*5.1+gust*4.1,across*1.36+chop*3.2)+float2(t*.77,-t*.24));
     float silver=reflectedBreak*smoothstep(.49,.91,flecks)*(.20+.67*gust);
     color+=half3(.082,.169,.216)*silver*saturate(.32+f);
     half3 halfv=normalize(view+GetMainLight().direction);
     color+=pow(saturate(dot(n,halfv)),65)*half3(.39,.52,.67)*.14;
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
