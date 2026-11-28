// A falling snowflake: a soft round dot drawn on a particle's quad, no texture. Made for this edition.
// It takes the sun's and the sky's colour, so flakes are white by day and dim blue at night instead of glowing,
// and it fades out right in front of the camera so flakes never smear across the view.
Shader "BWM/Snowflake"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 0.9)
        _Softness ("Softness", Range(0.05, 1)) = 0.55
        _NearFade ("Near fade distance", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Flake"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Softness;
                float _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 light : TEXCOORD1;
                float viewDepth : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.color = v.color;
                o.uv = v.uv;
                Light sun = GetMainLight();
                float3 sky = max(SampleSH(float3(0, 1, 0)), 0.0);
                o.light = sun.color * 0.55 + sky * 0.9 + 0.04 * saturate(dot(sky, float3(0.3, 0.5, 0.2)) / 0.3);      // no light of their own at night
                o.viewDepth = -p.positionVS.z;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 2.0;
                float alpha = smoothstep(1.0, 1.0 - _Softness, r);
                alpha *= saturate((i.viewDepth - 0.15) / _NearFade);
                half4 c = _Color * i.color;
                return half4(c.rgb * min(i.light, 1.15), c.a * alpha);
            }
            ENDHLSL
        }
    }
}
