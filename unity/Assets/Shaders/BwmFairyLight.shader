// Christmas light strings: wire and bulbs in one mesh. Made for this edition.
// The vertex colour is the bulb's colour; uv.x is 1 on bulbs and 0 on the wire; uv.y is each bulb's own phase,
// so the bulbs twinkle out of step. Bulbs are unlit and brighter than white, so they read as lit by day and
// glow at night; the wire takes a little sky light so it is not a black line.
Shader "BWM/FairyLight"
{
    Properties
    {
        _Glow ("Glow", Range(0, 8)) = 2.6
        _Twinkle ("Twinkle depth", Range(0, 1)) = 0.45
        _Speed ("Twinkle speed", Float) = 1.7
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Glow;
            float _Twinkle;
            float _Speed;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Lights"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 sky : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                o.sky = SampleSH(float3(0, 1, 0)) + GetMainLight().color * 0.25;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // slow breathing plus a quick shimmer, each bulb on its own phase
                float phase = i.uv.y * 6.2831853;
                float twinkle = 1.0 - _Twinkle * (0.5 + 0.35 * sin(_Time.y * _Speed + phase) + 0.15 * sin(_Time.y * _Speed * 3.7 + phase * 5.0));
                float3 bulb = i.color.rgb * _Glow * twinkle;
                float3 wire = i.color.rgb * (i.sky * 0.6 + 0.03);
                return half4(lerp(wire, bulb, step(0.5, i.uv.x)), 1);
            }
            ENDHLSL
        }
        // The game renders deferred and URP copies depth before our forward passes draw: this pass puts the
        // surface into the depth buffer during the GBuffer pass, so the fog and other depth effects do not see through it

        Pass
        {
            Name "GBufferDepth"
            Tags { "LightMode" = "UniversalGBuffer" "UniversalMaterialType" = "Unlit" }      // depth, normal and a plain albedo: BwmGBuffer.hlsl
            ZWrite On
            ColorMask 0 3
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "BwmGBuffer.hlsl"
            BwmGBufferVaryings Vert(BwmGBufferAttributes v) { return BwmGBufferVert(v); }
            BwmGBufferOut Frag(BwmGBufferVaryings i, bool front : SV_IsFrontFace) { return BwmGBuffer(i.normalWS, front); }
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
