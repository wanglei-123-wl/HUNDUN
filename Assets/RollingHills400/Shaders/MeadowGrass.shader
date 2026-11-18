Shader "RollingHills400/MeadowGrass"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        _BaseMap("Grass atlas", 2D) = "white" {}
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.28
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half _Cutoff;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; float2 uv:TEXCOORD0; float2 atlasCell:TEXCOORD1; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half4 color:COLOR; half fog:TEXCOORD2; float2 uv:TEXCOORD3; float2 localUV:TEXCOORD4; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS;
                o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.color=input.color*_BaseColor;
                o.uv=TRANSFORM_TEX(input.uv*.5+input.atlasCell,_BaseMap);
                o.localUV=input.uv;
                o.fog=ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                // Upward grass lighting avoids black reverse sides on thin cards.
                half3 n=half3(0,1,0);
                clip(min(min(i.localUV.x,1-i.localUV.x),min(i.localUV.y,1-i.localUV.y)));
                half4 tuft=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
                clip(tuft.a-_Cutoff);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse=.4h+.6h*abs(dot(n,sun.direction));
                half3 illumination=SampleSH(half3(0,1,0))+sun.color*diffuse*sun.shadowAttenuation;
                return half4(MixFog(tuft.rgb*i.color.rgb*illumination,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half _Cutoff;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float2 atlasCell:TEXCOORD1; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 localUV:TEXCOORD1; };
            Varyings DepthVert(Attributes i)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
                o.uv=TRANSFORM_TEX(i.uv*.5+i.atlasCell,_BaseMap);
                o.localUV=i.uv;
                return o;
            }
            half4 DepthFrag(Varyings i):SV_Target
            {
                clip(min(min(i.localUV.x,1-i.localUV.x),min(i.localUV.y,1-i.localUV.y)));
                clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
