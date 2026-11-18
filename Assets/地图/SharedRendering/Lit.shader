Shader "World Maps/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clipping", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        _Metallic("Metallic", Range(0,1)) = 0
        _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        _EmissionMap("Emission", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0,0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1

        [HideInInspector] _WorkflowMode("_WorkflowMode", Float) = 1
        [HideInInspector] _Surface("_Surface", Float) = 0
        [HideInInspector] _Blend("_Blend", Float) = 0
        [HideInInspector] _SrcBlend("_SrcBlend", Float) = 1
        [HideInInspector] _DstBlend("_DstBlend", Float) = 0
        [HideInInspector] _SrcBlendAlpha("_SrcBlendAlpha", Float) = 1
        [HideInInspector] _DstBlendAlpha("_DstBlendAlpha", Float) = 0
        [HideInInspector] _ZWrite("_ZWrite", Float) = 1
        [HideInInspector] _AlphaToMask("_AlphaToMask", Float) = 0
        [HideInInspector] _ReceiveShadows("_ReceiveShadows", Float) = 1
        [HideInInspector] _QueueOffset("_QueueOffset", Float) = 0
        [HideInInspector] _SmoothnessTextureChannel("_SmoothnessTextureChannel", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="Lit" }
        UsePass "Universal Render Pipeline/Lit/ForwardLit"
        UsePass "Universal Render Pipeline/Lit/GBuffer"
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]

        CGPROGRAM
        #pragma target 3.5
        #pragma surface Surf Standard fullforwardshadows addshadow
        #pragma shader_feature_local _ALPHATEST_ON
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _EMISSION
        #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
        #pragma shader_feature_local _ENVIRONMENTREFLECTIONS_OFF

        sampler2D _BaseMap;
        sampler2D _BumpMap;
        sampler2D _EmissionMap;
        fixed4 _BaseColor;
        half4 _EmissionColor;
        half _Cutoff;
        half _BumpScale;
        half _Smoothness;
        half _Metallic;

        struct Input
        {
            float2 uv_BaseMap;
            float2 uv_BumpMap;
            float2 uv_EmissionMap;
        };

        void Surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 baseColor = tex2D(_BaseMap, input.uv_BaseMap) * _BaseColor;

            #if defined(_ALPHATEST_ON)
                clip(baseColor.a - _Cutoff);
            #endif
            #if defined(_NORMALMAP)
                output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_BumpMap), _BumpScale);
            #endif
            #if defined(_EMISSION)
                output.Emission = tex2D(_EmissionMap, input.uv_EmissionMap).rgb * _EmissionColor.rgb;
            #endif

            output.Albedo = baseColor.rgb;
            output.Alpha = baseColor.a;
            output.Metallic = _Metallic;
            output.Smoothness = _Smoothness;
        }
        ENDCG
    }
}
