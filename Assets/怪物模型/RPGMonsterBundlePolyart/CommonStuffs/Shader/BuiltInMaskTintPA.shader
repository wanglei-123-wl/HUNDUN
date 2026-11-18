Shader "RPGMonsterBundlePolyart/BuiltInMaskTintPA"
{
    Properties
    {
        _Albedo ("Albedo", 2D) = "white" {}
        _Mask01 ("Mask 01", 2D) = "black" {}
        _Mask02 ("Mask 02", 2D) = "black" {}
        _Mask03 ("Mask 03", 2D) = "black" {}
        _Emission ("Emission", 2D) = "black" {}

        _Color01 ("Color 01", Color) = (1, 1, 1, 0)
        _Color02 ("Color 02", Color) = (1, 1, 1, 0)
        _Color03 ("Color 03", Color) = (1, 1, 1, 0)
        _Color04 ("Color 04", Color) = (1, 1, 1, 0)
        _Color05 ("Color 05", Color) = (1, 1, 1, 0)
        _Color06 ("Color 06", Color) = (1, 1, 1, 0)
        _Color07 ("Color 07", Color) = (1, 1, 1, 0)
        _Color08 ("Color 08", Color) = (1, 1, 1, 0)
        _Color09_Skin ("Color 09 Skin", Color) = (1, 1, 1, 0)

        _Color01Power ("Color 01 Power", Float) = 1
        _Color02Power ("Color 02 Power", Float) = 1
        _Color03Power ("Color 03 Power", Float) = 1
        _Color04Power ("Color 04 Power", Float) = 1
        _Color05Power ("Color 05 Power", Float) = 1
        _Color06Power ("Color 06 Power", Float) = 1
        _Color07Power ("Color 07 Power", Float) = 1
        _Color08Power ("Color 08 Power", Float) = 1
        _Color09Power ("Color 09 Power", Float) = 1
        [HDR] Color_df356a6b29d64aee805a61f7bcdc2368 ("Emission Color", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _Albedo;
        sampler2D _Mask01;
        sampler2D _Mask02;
        sampler2D _Mask03;
        sampler2D _Emission;

        float4 _Color01, _Color02, _Color03, _Color04, _Color05;
        float4 _Color06, _Color07, _Color08, _Color09_Skin;
        float _Color01Power, _Color02Power, _Color03Power;
        float _Color04Power, _Color05Power, _Color06Power;
        float _Color07Power, _Color08Power, _Color09Power;
        float4 Color_df356a6b29d64aee805a61f7bcdc2368;

        struct Input
        {
            float2 uv_Albedo;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.uv_Albedo;
            float3 mask01 = tex2D(_Mask01, uv).rgb;
            float3 mask02 = tex2D(_Mask02, uv).rgb;
            float3 mask03 = tex2D(_Mask03, uv).rgb;
            float3 baseColor = tex2D(_Albedo, uv).rgb;

            float3 tint =
                min(mask01.r, _Color01.rgb) * _Color01Power +
                min(mask01.g, _Color02.rgb) * _Color02Power +
                min(mask01.b, _Color03.rgb) * _Color03Power +
                min(mask02.r, _Color04.rgb) * _Color04Power +
                min(mask02.g, _Color05.rgb) * _Color05Power +
                min(mask02.b, _Color06.rgb) * _Color06Power +
                min(mask03.r, _Color07.rgb) * _Color07Power +
                min(mask03.g, _Color08.rgb) * _Color08Power +
                min(mask03.b, _Color09_Skin.rgb) * _Color09Power;

            float coverage =
                mask01.r + mask01.g + mask01.b +
                mask02.r + mask02.g + mask02.b +
                mask03.r + mask03.g + mask03.b;
            o.Albedo = lerp(baseColor, baseColor * tint, coverage);
            o.Metallic = 0;
            o.Smoothness = 0.3;
            o.Occlusion = 1;
            o.Alpha = 1;

            float3 emission = tex2D(_Emission, uv).rgb;
            float3 emissionColor = Color_df356a6b29d64aee805a61f7bcdc2368.rgb;
            float3 below = 2.0 * emission * emissionColor;
            float3 above = 1.0 - 2.0 * (1.0 - emission) * (1.0 - emissionColor);
            o.Emission = lerp(above, below, step(emission, 0.5));
        }
        ENDCG
    }

    FallBack "Standard"
}
