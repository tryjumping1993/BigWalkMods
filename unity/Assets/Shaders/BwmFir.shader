// Snow-laden firs for the Winter Isle. Made for this edition.
// A fir is whorls of drooping branch fronds round a trunk (Kit.AddFir), each frond a shallow roof of two cards
// textured with one of four snow-laden sprays from an atlas (tools/treetex.py: colour with the needles' cut-out,
// a normal map, and a mask of where snow lies on the spray). The snow shows where the mask has it and the card
// faces the sky, so from below the boughs show their dark green undersides. The trunk wears a tiling bark
// texture mapped in metres (uv.x below -9). The vertex colour tints each tree and darkens the inside of the
// crown (alpha = how deep in the crown). Lit by the sun with wrap and a little light through the needles.
// Birches use this shader too, with birch bark; their twigs are plain vertex colour (uv.x -1).
Shader "BWM/Fir"
{
    Properties
    {
        _MainTex ("Needles", 2D) = "white" {}
        _Cutoff ("Cut-out", Range(0, 1)) = 0.42
        _SnowColor ("Snow", Color) = (0.94, 0.96, 1, 1)
        _ShadeColor ("Shade", Color) = (0.50, 0.60, 0.85, 1)
        _SnowShade ("Snow in shade", Color) = (0.58, 0.70, 0.93, 1)
        _SnowLine ("Snow line", Range(-0.5, 1)) = 0.35
        _SnowEdge ("Snow edge softness", Range(0.01, 0.5)) = 0.18
        _Translucency ("Light through the needles", Range(0, 1)) = 0.25
        _BarkColor ("Bark", Color) = (0.46, 0.33, 0.24, 1)
        _FrondNormal ("Spray normals", 2D) = "bump" {}
        _FrondMask ("Spray snow (R), occlusion (G)", 2D) = "black" {}
        _BarkTex ("Bark", 2D) = "white" {}
        [Normal] _BarkNormal ("Bark normals", 2D) = "bump" {}
        _NormalStrength ("Normal strength", Range(0, 2)) = 1
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
            half4 _SnowShade;
            float _SnowLine;
            float _SnowEdge;
            float _Translucency;
            half4 _BarkColor;
            float _NormalStrength;
        CBUFFER_END
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_FrondNormal); SAMPLER(sampler_FrondNormal);
        TEXTURE2D(_FrondMask); SAMPLER(sampler_FrondMask);
        TEXTURE2D(_BarkTex); SAMPLER(sampler_BarkTex);
        TEXTURE2D(_BarkNormal); SAMPLER(sampler_BarkNormal);

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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "BwmSunShadowKeywords.hlsl"      // the sun's shadow, the same at every quality setting (BwmSunShadow.hlsl)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "BwmSunShadow.hlsl"
            #include "BwmSnowCommon.hlsl"
            #include "BwmLamps.hlsl"
            #include "BwmGlow.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
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
                float4 tangentWS : TEXCOORD4;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                bool bark = i.uv.x < -9.0;                          // textured bark, mapped in metres
                bool wood = i.uv.x < -0.5 && i.uv.x > -1.5;         // a plain limb in its vertex colour
                bool core = i.uv.x < -1.5 && i.uv.x > -9.0;         // the far version's dark crown core
                bool spray = i.uv.x >= 0.0;
                float2 barkUV = float2(-10.0 - i.uv.x, i.uv.y);
                half4 tex = spray ? SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) : half4(1, 1, 1, 1);
                clip(tex.a - _Cutoff);
                float3 positionWS = i.positionWS;
                float faceSign = frontFace ? 1.0 : -1.0;
                float3 geometric = normalize(i.normalWS) * faceSign;
                float depth = i.color.a;      // 1 at the crown's surface, 0 deep inside by the trunk

                // the surface's own relief, from the spray's or the bark's normal map
                float3 n = geometric;
                if (spray || bark)
                {
                    float3 t = i.tangentWS.xyz;
                    t = normalize(t - geometric * dot(geometric, t) + 1e-5);
                    float3 b = cross(geometric, t) * (i.tangentWS.w * faceSign >= 0.0 ? 1.0 : -1.0);
                    float3 nt = spray ? UnpackNormal(SAMPLE_TEXTURE2D(_FrondNormal, sampler_FrondNormal, i.uv))
                                      : UnpackNormal(SAMPLE_TEXTURE2D(_BarkNormal, sampler_BarkNormal, barkUV));
                    nt.xy *= _NormalStrength;
                    n = normalize(t * nt.x + b * nt.y + geometric * max(nt.z, 0.05));
                }
                half4 mask = spray ? SAMPLE_TEXTURE2D(_FrondMask, sampler_FrondMask, i.uv) : half4(0, 1, 1, 0);

                float3 needles = tex.rgb * i.color.rgb * lerp(0.55, 1.0, mask.g);
                float3 albedo = needles * lerp(0.5, 1.0, depth);
                if (bark) albedo = SAMPLE_TEXTURE2D(_BarkTex, sampler_BarkTex, barkUV).rgb * i.color.rgb;
                if (wood) albedo = i.color.rgb;
                if (core) albedo = i.color.rgb * float3(0.05, 0.13, 0.07) * (0.8 + 0.4 * Noise3(positionWS * 2.0));

                // Snow: on a spray, where its mask puts it and the card faces the sky (the boughs' undersides stay
                // green), less deep in the crown where little falls; on bark and limbs, on what faces up, and snow the
                // wind has plastered up one side of a trunk in broken patches
                float3 viewVector = GetWorldSpaceViewDir(positionWS);
                float viewDistance = length(viewVector);
                float3 v = viewVector / max(viewDistance, 1e-4);
                BwmSnowDetail detail = BwmSnowDetailOn(positionWS, viewDistance, 4.0);
                float clumps = Noise3(positionWS * 1.7) * 0.6 + Noise3(positionWS * 6.0) * 0.4;
                float snow = mask.r * smoothstep(-0.2, 0.3, geometric.y) * smoothstep(0.1, 0.5, depth);
                if (bark || wood)
                    snow = max(smoothstep(0.62, 0.9, n.y + (clumps - 0.5) * 0.2), 0.55 * smoothstep(0.85, 0.95, dot(geometric, normalize(float3(0.72, 0.2, 0.66))) + (clumps - 0.5) * 0.4)
                        * smoothstep(0.6, 0.72, Noise3(positionWS * float3(5, 2.2, 5) + 3.1)) * (bark ? 1.0 : 0.0));      // (in small patches: a full strip read as a white stripe down the trunk)
                if (core) snow = 0.75 * smoothstep(0.2, 0.6, geometric.y + (clumps - 0.5) * 0.4);      // (the distant version's crown takes snow too, or it reads dark green beside the snowy near trees)

                Light sun = BwmSun(positionWS);
                float ndl = dot(n, sun.direction);
                float lit = saturate(ndl * 0.6 + 0.4) * sun.shadowAttenuation;
                float through = spray ? saturate(-ndl) * _Translucency * (1.0 - snow) * sun.shadowAttenuation : 0.0;
                float3 ambient = SampleSH(n);
                float3 shade = albedo * _ShadeColor.rgb * BwmShadeLight(ambient, 0.14) * lerp(0.6, 1.0, depth);
                float3 color = shade + albedo * sun.color * lit + needles * sun.color * through;      // sunlight adds to the sky's light
                // the snow itself, lit as all the island's snow is (BwmSnowCommon); deep in the crown it is in shade
                color = lerp(color, BwmSnowOn(_SnowColor.rgb, _SnowShade.rgb, positionWS, n, v, viewDistance, detail, sun.direction, sun.color, sun.shadowAttenuation, lerp(0.6, 1.0, depth)), snow);
                albedo = lerp(albedo, _SnowColor.rgb, snow);      // for the lamps

                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        if (!BwmIsLampLight(lightIndex) && !BwmIsGlowLight(lightIndex))      // (the island's lamps and Christmas lights come from globals, below: no four to an object)
                        {
                            Light lamp = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
                            color += albedo * lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation * saturate(dot(n, lamp.direction) * 0.6 + 0.4));
                        }
                    LIGHT_LOOP_END
                }
                #endif
                color += albedo * (BwmLampLight(positionWS, n, 0.4) + BwmGlowLight(positionWS, n, 0.4));

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
        // The game renders deferred and URP copies depth before our forward passes draw: this pass puts the
        // surface into the depth buffer during the GBuffer pass, so the fog and other depth effects do not see through it

        Pass
        {
            Name "GBufferDepth"
            Tags { "LightMode" = "UniversalGBuffer" "UniversalMaterialType" = "Unlit" }      // depth only, so the depth texture holds this surface (see the note above)
            ZWrite On
            ColorMask 0 0
            ColorMask 0 1
            ColorMask 0 2
            ColorMask 0 3
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
