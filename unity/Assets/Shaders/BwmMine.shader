// The coal mine's rock, timber and track. Made for this edition.
// Nothing from the sky reaches underground, so this takes no sun and no sky light: the lamps' light is worked out
// when the mine is built and stored in the vertex colour (rock colour times light). uv.x is 1 on lamp glass,
// which glows; uv.y is each lamp's own phase, for a slow flicker. A little grain from the world position keeps
// the rock from looking like flat paint.
Shader "BWM/Mine"
{
    Properties
    {
        _Glow ("Lamp glow", Range(0, 8)) = 2.4
        _Grain ("Rock grain", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Glow;
            float _Grain;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Mine"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

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
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float grain = Noise(i.positionWS * 3.1) * 0.6 + Noise(i.positionWS * 11.0) * 0.4;
                float3 rock = i.color.rgb * (1.0 - _Grain + _Grain * 2.0 * grain);
                float flicker = 0.9 + 0.1 * sin(_Time.y * 2.3 + i.uv.y * 6.2831853) * sin(_Time.y * 7.1 + i.uv.y * 40.0);
                float3 lamp = i.color.rgb * _Glow * flicker;
                return half4(lerp(rock, lamp, step(0.5, i.uv.x)), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }
    }
}
