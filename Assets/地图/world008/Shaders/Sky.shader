Shader "World008/Cloud Sky" {
 Properties {
  _Zenith("Zenith",Color)=(.25,.34,.42,1)
  _Horizon("Horizon",Color)=(.55,.60,.63,1)
  _CloudTint("Cloud tint",Color)=(.60,.63,.66,1)
  _SunDir("Sun direction",Vector)=(0,1,0,0)
  _Cover("Cloud coverage",Float)=.52
  _Gain("Sky intensity",Float)=1
  _StormFlash("Storm flash",Float)=0
  _StormDir("Storm direction",Vector)=(0,1,0,0)
  _Panorama("Sky panorama",2D)="white"{}
  _UsePanorama("Use panorama",Float)=0
  _PhotoYaw("Panorama yaw",Float)=0
  _PhotoGain("Panorama gain",Float)=1
  _SolarDisk("Solar disk",Float)=0
 }
 SubShader {
  Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off
  Pass {
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_Panorama); SAMPLER(sky_linear_clamp_sampler);
   CBUFFER_START(UnityPerMaterial)
    half4 _Zenith,_Horizon,_CloudTint;
    float4 _SunDir;
    float _Cover,_Gain;
    float _StormFlash;
    float4 _StormDir;
    float _UsePanorama,_PhotoYaw,_PhotoGain,_SolarDisk;
   CBUFFER_END
   struct A{float4 p:POSITION;};
   struct V{float4 p:SV_POSITION;float3 dir:TEXCOORD0;};
   V Vert(A a){
    V o;o.p=TransformObjectToHClip(a.p.xyz);o.p.z=o.p.w*UNITY_RAW_FAR_CLIP_VALUE;o.dir=a.p.xyz;return o;
   }
   float hash(float2 p){return frac(sin(dot(p,float2(91.43,174.36)))*43758.5453);}
   float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   float fbm(float2 p){
    float n=0,a=.54;
    for(int i=0;i<5;i++){n+=a*noise(p);p=mul(float2x2(1.62,-1.18,1.18,1.62),p)+17.21;a*=.50;}
    return n;
   }
   half4 Frag(V i):SV_Target{
    float3 dir=normalize(i.dir);
    if(_UsePanorama>.5) {
     float theta=atan2(dir.x,dir.z)-_PhotoYaw;
     theta=atan2(sin(theta),cos(theta));
     float opposite=atan2(sin(theta+3.1415927),cos(theta+3.1415927));
     float e=atan2(dir.y,length(dir.xz));
     float v=e<=.45?max(.023,.105+e*1.37):.7215+.251*(1-exp(-4.13*(e-.45)));
     float u=saturate(.5+theta*.282),u2=saturate(.52-opposite*.285);
     half3 a=SAMPLE_TEXTURE2D(_Panorama,sky_linear_clamp_sampler,float2(u,v)).rgb;
     half3 b=SAMPLE_TEXTURE2D(_Panorama,sky_linear_clamp_sampler,float2(u2,v*.94+.043)).rgb;
     half3 color=lerp(a,b*.88,smoothstep(1.13,2.02,abs(theta)))*_PhotoGain;
     color=lerp(color,dot(color,half3(.21,.72,.07)).xxx,.22*_SolarDisk);
     float mu=saturate(dot(dir,normalize(_SunDir.xyz)));
     color+=half3(1,.69,.35)*_SolarDisk*(.23*pow(mu,119)+2.2*smoothstep(.99989,.99998,mu));
     color=lerp(color,_Horizon.rgb*.60,.42*(1-smoothstep(-.09,.18,e)));
     return half4(color,1);
    }
    float h=saturate(dir.y);
    half3 col=lerp(_Horizon.rgb,_Zenith.rgb,sqrt(h));
    float2 p=(dir.xz/ max(.16,dir.y+.23))*2.3;
    float g=fbm(p+float2(5.4,18.7));
    float cover=smoothstep(_Cover-.20,_Cover+.16,g);
    float lit=smoothstep(.30,.77,g+noise(p*.64+12.2)*.22);
    half3 cloud=lerp(_CloudTint.rgb*.50,_CloudTint.rgb*1.16,lit);
    col=lerp(col,cloud,cover*smoothstep(.0,.19,h));
    float sun=saturate(dot(dir,normalize(_SunDir.xyz)));
    col+=half3(.76,.59,.39)*pow(sun,17)*.25*(1-cover*.56);
    col+=half3(1.1,.95,.73)*pow(sun,2400)*.64*(1-cover);
    float storm=pow(saturate(dot(dir,normalize(_StormDir.xyz))),58);
    col+=half3(.29,.67,1.45)*storm*_StormFlash*(.40+cover*.72);
    return half4(col*_Gain,1);
   }
   ENDHLSL
  }
 }

 SubShader {
  Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox"}
  Cull Off ZWrite Off
  Pass {
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #define WORLD005_BUILTIN
   #include "../../SharedRendering/Builtin.hlsl"
   #include "../../SharedRendering/SkyPass.hlsl"
   ENDCG
  }
 }
}
