Shader "Game/Effects/Teleporter Light Column"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.48, 0.79, 1, 1)
        _Intensity ("Intensity", Range(0, 2)) = 1
        _FlowSpeed ("Flow Speed", Float) = 0.75
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            half4 _Tint;
            half _Intensity;
            float _FlowSpeed;

            struct Attributes
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 surface : TEXCOORD1;
            };

            struct Interpolators
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 effectUV : TEXCOORD2;
            };

            Interpolators Vert(Attributes input)
            {
                Interpolators output;
                output.position = UnityObjectToClipPos(input.position);
                output.worldPosition = mul(unity_ObjectToWorld, input.position).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.effectUV = float3(input.uv, input.surface.x);
                return output;
            }

            float Band(float value, float center, float width)
            {
                return 1.0 - smoothstep(width * 0.25, width, abs(value - center));
            }

            float Pulse(float value, float width)
            {
                return Band(frac(value + 0.5), 0.5, width);
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                const float tau = 6.2831853;
                float turn = input.effectUV.x;
                float vertical = input.effectUV.y;
                float time = _Time.y * _FlowSpeed;
                float coverage;
                float core;

                if (input.effectUV.z > 0.5)
                {
                    float bandFade = smoothstep(0.0, 0.12, vertical)
                        * (1.0 - smoothstep(0.88, 1.0, vertical));
                    float outerLine = Band(vertical, 0.77, 0.065);
                    float innerLine = Band(vertical, 0.22, 0.055);
                    float ticks = Pulse(turn * 24.0, 0.15) * Band(vertical, 0.49, 0.22);
                    float orbit = Pulse(turn * 4.0 - time * 0.32, 0.17)
                        * Band(vertical, 0.53, 0.17);
                    core = saturate(outerLine * 0.65 + innerLine * 0.4 + orbit * 0.72 + ticks * 0.3);
                    coverage = bandFade * (0.12 + outerLine * 0.51 + innerLine * 0.34
                        + ticks * 0.31 + orbit * 0.38);
                }
                else
                {
                    float3 viewDirection = normalize(UnityWorldSpaceViewDir(input.worldPosition));
                    float rim = 1.0 - abs(dot(normalize(input.worldNormal), viewDirection));
                    rim *= rim;
                    float heightFade = smoothstep(0.0, 0.035, vertical)
                        * (1.0 - smoothstep(0.20, 1.0, vertical));
                    float wisp = 0.5 + 0.5 * sin(tau * (turn * 22.0
                        + 0.15 * sin(vertical * 9.0 - time)));
                    float strands = smoothstep(0.72, 0.99, wisp);
                    float spiral = Pulse(turn * 3.0 - vertical * 1.55 + time * 0.30, 0.04);
                    float sparks = Pulse(turn * 19.0 + 0.18 * sin(vertical * 9.0), 0.12)
                        * Pulse(vertical * 7.0 - time * 0.8 + 0.35 * sin(turn * 8.0 * tau), 0.10);
                    core = saturate(spiral * 0.36 + sparks * 0.60);
                    coverage = heightFade * (0.060 + rim * 0.14 + strands * 0.12
                        + spiral * 0.14 + sparks * 0.17);
                }

                float breathing = 0.94 + 0.06 * sin(time * 2.1);
                half alpha = saturate(coverage * _Tint.a * _Intensity * breathing);
                half3 color = lerp(_Tint.rgb, half3(0.94, 0.99, 1.0), core);
                return half4(color * alpha * (1.0 + core * 0.45), alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
