#ifndef WORLD005_BUILTIN_INCLUDED
#define WORLD005_BUILTIN_INCLUDED

#include "UnityCG.cginc"

#define TEXTURE2D(textureName) Texture2D textureName
#define SAMPLER(samplerName) SamplerState samplerName
#define SAMPLE_TEXTURE2D(textureName, samplerName, uv) textureName.Sample(samplerName, uv)
#define SAMPLE_TEXTURE2D_GRAD(textureName, samplerName, uv, dx, dy) textureName.SampleGrad(samplerName, uv, dx, dy)
#define TransformObjectToHClip(position) UnityObjectToClipPos(position)
#define TransformObjectToWorldNormal(normal) UnityObjectToWorldNormal(normal)
#define TransformWorldToHClip(position) mul(UNITY_MATRIX_VP, float4(position, 1))
#define TransformWorldToView(position) mul(UNITY_MATRIX_V, float4(position, 1)).xyz
#define TransformWorldToObject(position) mul(unity_WorldToObject, float4(position, 1)).xyz
#define GetWorldSpaceNormalizeViewDir(position) normalize(_WorldSpaceCameraPos.xyz - position)
#define GetCameraPositionWS() _WorldSpaceCameraPos.xyz

#ifndef UNITY_RAW_FAR_CLIP_VALUE
    #if defined(UNITY_REVERSED_Z)
        #define UNITY_RAW_FAR_CLIP_VALUE 0.0
    #else
        #define UNITY_RAW_FAR_CLIP_VALUE 1.0
    #endif
#endif

struct VertexPositionInputs
{
    float4 positionCS;
    float3 positionWS;
};

VertexPositionInputs GetVertexPositionInputs(float3 position)
{
    VertexPositionInputs output;
    output.positionCS = UnityObjectToClipPos(position);
    output.positionWS = mul(unity_ObjectToWorld, float4(position, 1)).xyz;
    return output;
}

half ComputeFogFactor(float clipDepth)
{
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
    UNITY_CALC_FOG_FACTOR(clipDepth);
    return saturate(unityFogFactor);
#else
    return 1;
#endif
}

half3 MixFog(half3 color, half fog)
{
    return lerp(unity_FogColor.rgb, color, fog);
}

#ifdef WORLD005_SCENE_DEPTH
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

float World005EyeDepth(float2 uv)
{
    float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
    if (unity_OrthoParams.w > 0.5)
    {
    #if defined(UNITY_REVERSED_Z)
        depth = 1 - depth;
    #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, depth);
    }

    return LinearEyeDepth(depth);
}
#endif

#ifdef WORLD005_GRAB_COLOR
sampler2D _World005OpaqueColor;

half3 SampleSceneColor(float2 uv)
{
#if UNITY_UV_STARTS_AT_TOP
    const float grabSign = -1;
#else
    const float grabSign = 1;
#endif
    uv.y = 0.5 + (uv.y - 0.5) * grabSign * _ProjectionParams.x;
    return tex2D(_World005OpaqueColor, uv).rgb;
}
#endif

#endif
