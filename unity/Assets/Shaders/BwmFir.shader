// Snow-laden firs for the Winter Isle. Made for this edition.
// A fir is whorls of drooping branch fronds round a trunk (Kit.AddFir), each frond a shallow roof of two cards
// textured with a needle spray (Kit.FirBranchTexture: its alpha is the needles' cut-out). Snow settles on the
// fronds' upper faces, thick and clumpy where they face the sky, so from below and from the side the boughs
// show dark green undersides under white tops. The vertex colour tints each tree and darkens the inside of
// the crown (alpha = how deep in the crown). Lit by the sun with wrap and a little light through the needles.
Shader "BWM/Fir"
{
    Properties
    {
        _MainTex ("Needles", 2D) = "white" {}
        _Cutoff ("Cut-out", Range(0, 1)) = 0.42
        _SnowColor ("Snow", Color) = (0.94, 0.96, 1, 1)
        _ShadeColor ("Shade", Color) = (0.50, 0.60, 0.85, 1)
        _SnowLine ("Snow line", Range(-0.5, 1)) = 0.35
        _SnowEdge ("Snow edge softness", Range(0.01, 0.5)) = 0.18
        _Translucency ("Light through the needles", Range(0, 1)) = 0.25
        _BarkColor ("Bark", Color) = (0.26, 0.18, 0.12, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _Cutoff;
            half4 _SnowColor;
            half4 _ShadeColor;
            float _SnowLine;
            float _SnowEdge;
            float _Translucency;
            half4 _BarkColor;
        CBUFFER_END
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

        float Hash31(float3 p)
        {
            p = frac(p * float3(0.1031, 0.1030, 0.0973));
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        float Noise3(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(
                lerp(lerp(Hash31(i), Hash31(i + float3(1, 0, 0)), f.x), lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x), lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x), f.y),
                f.z);
        }

        // uv.x < 0 marks bark (the trunk is not cut out)
        float Alpha(float2 uv)
        {
            if (uv.x < 0) return 1;
            return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }      // the game renders deferred: see BwmSnow.shader
            Cull Off
            AlphaToMask Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                bool bark = i.uv.x < 0 && i.uv.x > -1.5;
                bool core = i.uv.x < -1.5;
                half4 tex = i.uv.x < 0 ? half4(1, 1, 1, 1) : SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                float3 positionWS = i.positionWS;
                float3 n = normalize(i.normalWS) * (frontFace ? 1.0 : -1.0);
                float depth = i.color.a;      // 1 at the crown's surface, 0 deep inside by the trunk

                float3 needles = tex.rgb * i.color.rgb;
                float3 albedo = bark ? _BarkColor.rgb * (0.8 + 0.4 * Noise3(positionWS * float3(8, 1.5, 8))) : needles * lerp(0.45, 1.0, depth);
                if (core) albedo = i.color.rgb * float3(0.05, 0.13, 0.07) * (0.8 + 0.4 * Noise3(positionWS * 2.0));

                // Snow: on what faces up, clumped by noise, thickest on the outer parts of the boughs
                float clumps = Noise3(positionWS * 1.7) * 0.6 + Noise3(positionWS * 6.0) * 0.4;
                float snow = smoothstep(_SnowLine - _SnowEdge, _SnowLine + _SnowEdge, n.y + (clumps - 0.5) * 0.9) * smoothstep(0.15, 0.6, depth);
                if (bark) snow *= smoothstep(0.6, 0.9, n.y);
                albedo = lerp(albedo, _SnowColor.rgb * (0.92 + 0.08 * clumps), snow);

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord, positionWS, half4(1, 1, 1, 1));
                float ndl = dot(n, sun.direction);
                float lit = saturate(ndl * 0.6 + 0.4) * sun.shadowAttenuation;
                float through = saturate(-ndl) * _Translucency * (1.0 - snow) * sun.shadowAttenuation;
                float3 ambient = SampleSH(n);
                float3 shade = albedo * _ShadeColor.rgb * (ambient * 1.1 + 0.14) * lerp(0.6, 1.0, depth);
                float3 color = lerp(shade, albedo * sun.color, lit) + needles * sun.color * through;

                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light lamp = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
                        color += albedo * lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation * saturate(dot(n, lamp.direction) * 0.6 + 0.4));
                    LIGHT_LOOP_END
                }
                #endif

                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { clip(Alpha(i.uv) - _Cutoff); return 0; }
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }
            half4 Frag(Varyings i) : SV_Target { clip(Alpha(i.uv) - _Cutoff); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target { clip(Alpha(i.uv) - _Cutoff); return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
