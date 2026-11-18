Shader "World005/Terrain URP" {
 Properties {
  _GroundTex("Ground albedo",2D)="white"{}
  _RoadTex("Road albedo",2D)="white"{}
  _RockTex("Rock albedo",2D)="white"{}
  _RockNormal("Rock relief",2D)="bump"{}
  _GroundNormal("Ground relief",2D)="bump"{}
  _RoadNormal("Road relief",2D)="bump"{}
  _Control("Map control",2D)="black"{}
  _GroundTint("Ground tint",Color)=(1,1,1,1)
  _RoadTint("Road tint",Color)=(1,1,1,1)
  _RockTint("Rock tint",Color)=(1,1,1,1)
  _WetScale("Damp color variation",Range(0,1))=0
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Cull Off
  Pass {
   Name "ForwardLit" Tags {"LightMode"="UniversalForwardOnly"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma shader_feature_local_fragment _ MAP_ROCK_PROP
   // Exposed ground, masonry and bridge stone use diffuse lighting at grazing angles.
   #define _SPECULARHIGHLIGHTS_OFF
   #define _ENVIRONMENTREFLECTIONS_OFF
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_GroundTex); SAMPLER(sampler_GroundTex);
   TEXTURE2D(_RoadTex); SAMPLER(sampler_RoadTex);
   TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);
   TEXTURE2D(_RockNormal); SAMPLER(sampler_RockNormal);
   TEXTURE2D(_GroundNormal); SAMPLER(sampler_GroundNormal);
   TEXTURE2D(_RoadNormal); SAMPLER(sampler_RoadNormal);
   TEXTURE2D(_Control); SAMPLER(sampler_Control);
   CBUFFER_START(UnityPerMaterial)
    half4 _GroundTint,_RoadTint,_RockTint;
    half _WetScale;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
   struct Varyings {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;half3 normal:TEXCOORD2;half fog:TEXCOORD3;};
   Varyings Vert(Attributes v) {
    Varyings o;VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
    o.positionCS=p.positionCS;o.world=p.positionWS;o.normal=TransformObjectToWorldNormal(v.normalOS);
    o.uv=v.uv;o.fog=ComputeFogFactor(p.positionCS.z);return o;
   }
   float2 Hash(float2 p){
    p=frac(p*float2(.1031,.11369));p+=dot(p,p.yx+33.33);
    return frac(float2((p.x+p.y)*p.x,(p.x+p.y)*p.y));
   }
   float Noise(float2 p){
    float2 b=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(Hash(b).x,Hash(b+float2(1,0)).x,f.x),
                lerp(Hash(b+float2(0,1)).x,Hash(b+1).x,f.x),f.y);
   }
   float2 Rot(float2 p,float2 a){return float2(p.x*a.x-p.y*a.y,p.x*a.y+p.y*a.x);}
   struct Patch {float2 q0,q1,q2,r0,r1,r2,dx,dy;float3 w;};
   void Placement(float2 id,float2 uv,out float2 q,out float2 r){
    float2 h=Hash(id+9.42);
    sincos(h.x*6.2831853,r.y,r.x);
    q=Rot(uv,r)+h*17.53;
   }
   Patch SamplePatch(float2 uv){
    Patch p;p.dx=ddx(uv);p.dy=ddy(uv);
    float2 g=float2(uv.x-uv.y*.577350269,uv.y*1.154700538);
    float2 b=floor(g),f=frac(g),v0,v1,v2;
    if(f.x+f.y<1){v0=b;v1=b+float2(1,0);v2=b+float2(0,1);p.w=float3(1-f.x-f.y,f.x,f.y);}
    else{v0=b+1;v1=b+float2(0,1);v2=b+float2(1,0);p.w=float3(f.x+f.y-1,1-f.x,1-f.y);}
    p.w*=p.w;p.w/=dot(p.w,float3(1,1,1));
    Placement(v0,uv,p.q0,p.r0);Placement(v1,uv,p.q1,p.r1);Placement(v2,uv,p.q2,p.r2);
    return p;
   }
   #define SAMPLE_LAYER(T,P) (SAMPLE_TEXTURE2D_GRAD(T,sampler##T,P.q0,Rot(P.dx,P.r0),Rot(P.dy,P.r0)).rgb*P.w.x+SAMPLE_TEXTURE2D_GRAD(T,sampler##T,P.q1,Rot(P.dx,P.r1),Rot(P.dy,P.r1)).rgb*P.w.y+SAMPLE_TEXTURE2D_GRAD(T,sampler##T,P.q2,Rot(P.dx,P.r2),Rot(P.dy,P.r2)).rgb*P.w.z)
   half3 Stone(float3 world,half3 n,out half3 relief){
    half3 weights=pow(abs(n),4);
    weights/=max(dot(weights,half3(1,1,1)),.001);
    Patch x=SamplePatch(float2(-world.z,world.y)*float2(.109,.16));
    Patch y=SamplePatch((world.xz+23.6)*.169);
    Patch z=SamplePatch((world.xy-41.7)*float2(.109,.16));
    half3 c=SAMPLE_LAYER(_RockTex,x)*weights.x+SAMPLE_LAYER(_RockTex,y)*weights.y+SAMPLE_LAYER(_RockTex,z)*weights.z;
    half2 xn=SAMPLE_LAYER(_RockNormal,x).rg*2-1,yn=SAMPLE_LAYER(_RockNormal,y).rg*2-1,zn=SAMPLE_LAYER(_RockNormal,z).rg*2-1;
    relief=normalize(n+half3(0,xn.y,-xn.x)*weights.x*.52+half3(yn.x,0,yn.y)*weights.y*.52+half3(zn.x,zn.y,0)*weights.z*.52);
    return c*_RockTint.rgb;
   }
   half4 Frag(Varyings i):SV_Target{
    half3 n=normalize(i.normal), relief;
    half3 stone=Stone(i.world,n,relief);
    float2 xz=i.world.xz;
    half macro=Noise(xz*.019+6.2)*.62+Noise(xz*.067-8.3)*.38;
    half3 color;
    half rocky;
    #if defined(MAP_ROCK_PROP)
     rocky=1;color=stone;
    #else
     half4 c=SAMPLE_TEXTURE2D(_Control,sampler_Control,i.uv);
     Patch g=SamplePatch(xz*.306),p=SamplePatch((xz+22.5)*.245);
     half3 soil=SAMPLE_LAYER(_GroundTex,g)*_GroundTint.rgb;
     half3 road=SAMPLE_LAYER(_RoadTex,p)*_RoadTint.rgb;
     soil*=lerp(half3(.82,.80,.74),half3(1.13,1.17,.94),c.g);
     rocky=max(saturate((.77-n.y)*2.5),c.b*.78)*(1-c.r*.95);
     color=lerp(lerp(soil,road,c.r),stone,rocky);
     half2 micro=lerp(SAMPLE_LAYER(_GroundNormal,g).rg,SAMPLE_LAYER(_RoadNormal,p).rg,c.r)*2-1;
     n=normalize(n+half3(micro.x,0,micro.y)*.46*(1-rocky));
     half damp=_WetScale*smoothstep(.37,.79,Noise(xz*.109+1.3)*.72+Noise(xz*.43-4.9)*.28);
     color*=lerp(1,.64,damp);
    #endif
    color*=lerp(.76,1.23,macro);
    InputData input=(InputData)0;
    input.positionWS=i.world;input.normalWS=normalize(lerp(n,relief,rocky*.72));
    input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.world);
    input.shadowCoord=TransformWorldToShadowCoord(i.world);input.bakedGI=SampleSH(input.normalWS);
    input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);input.shadowMask=half4(1,1,1,1);
    SurfaceData s=(SurfaceData)0;
    s.albedo=color;s.alpha=1;s.occlusion=1;s.normalTS=half3(0,0,1);
    half4 outColor=UniversalFragmentBlinnPhong(input,s);
    outColor.rgb=MixFog(outColor.rgb,i.fog);
    return outColor;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
  UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }

 SubShader {
  Tags {"RenderType"="Opaque" "Queue"="Geometry"}
  Cull Off
  Pass {
   Name "ForwardBuiltin" Tags {"LightMode"="ForwardBase"}
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fwdbase
   #pragma multi_compile_fog
   #pragma shader_feature_local_fragment _ MAP_ROCK_PROP
   #include "Lighting.cginc"
   #include "AutoLight.cginc"
   #define WORLD005_BUILTIN
   #include "../../SharedRendering/Builtin.hlsl"
   #include "../../SharedRendering/TerrainPass.hlsl"
   ENDCG
  }
  Pass {
   Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
   ZWrite On ZTest LEqual
   CGPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_shadowcaster
   #include "UnityCG.cginc"
   struct ShadowOutput { V2F_SHADOW_CASTER; };
   ShadowOutput Vert(appdata_base v)
   {
    ShadowOutput o;
    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
    return o;
   }
   float4 Frag(ShadowOutput i) : SV_Target
   {
    SHADOW_CASTER_FRAGMENT(i)
   }
   ENDCG
  }
 }
}
