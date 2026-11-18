Shader "Map001/BuiltIn/MeadowGrass"
{
    Properties
    {
        _BaseMap ("Grass Atlas", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.28
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #include "UnityCG.cginc"

        sampler2D _BaseMap;
        float4 _BaseMap_ST;
        fixed4 _BaseColor;
        half _Cutoff;

        struct Input
        {
            float2 atlasUV;
            float2 localUV;
            fixed4 vertexColor;
        };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.atlasUV = (vertex.texcoord.xy * 0.5 + vertex.texcoord1.xy) * _BaseMap_ST.xy + _BaseMap_ST.zw;
            output.localUV = vertex.texcoord.xy;
            output.vertexColor = vertex.color;
            vertex.normal = UnityWorldToObjectDir(float3(0, 1, 0));
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            clip(min(min(input.localUV.x, 1 - input.localUV.x), min(input.localUV.y, 1 - input.localUV.y)));
            fixed4 tuft = tex2D(_BaseMap, input.atlasUV) * input.vertexColor * _BaseColor;
            clip(tuft.a - _Cutoff);
            output.Albedo = tuft.rgb;
            output.Metallic = 0;
            output.Smoothness = 0;
            output.Alpha = 1;
        }
        ENDCG
    }

    Fallback "Transparent/Cutout/VertexLit"
}
