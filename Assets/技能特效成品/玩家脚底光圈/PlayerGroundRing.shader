Shader "Game/VFX/Player Ground Ring"
{
    Properties
    {
        _Tint ("Ring Color", Color) = (0.16, 0.96, 0.44, 1)
        _HighlightTint ("Highlight Color (Linear RGBA)", Vector) = (0.68, 1.0, 0.80, 1)
        [HideInInspector] _RingPhase ("Breathing and Flow", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Tint;
            fixed4 _HighlightTint;
            float4 _RingPhase;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float radial = abs(input.uv.y * 2.0 - 1.0);
                float core = 1.0 - smoothstep(0.19, 0.42, radial);
                float halo = 1.0 - smoothstep(0.30, 1.0, radial);

                float travel = frac(input.uv.x - _RingPhase.y);
                float angleDistance = min(travel, 1.0 - travel);
                float highlight = 1.0 - smoothstep(0.012, 0.12, angleDistance);
                float breath = lerp(0.78, 1.0, _RingPhase.x);

                fixed3 color = lerp(_Tint.rgb, _HighlightTint.rgb, highlight * 0.42);
                float alpha = (core * 0.65 + halo * 0.23) * breath + highlight * core * 0.12;
                return fixed4(color, saturate(alpha * _Tint.a));
            }
            ENDCG
        }
    }
    Fallback Off
}
