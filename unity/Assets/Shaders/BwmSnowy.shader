// Anything on the Winter Isle that is not snow itself: trees, rocks, posts, walls, props. Made for this edition.
// The mesh's vertex colour is the surface colour. Snow settles on whatever faces up: the more a surface faces
// the sky, the whiter it is, with a ragged edge so the snow line is not a ruler line. Shadows go blue, as on
// the snow ground, so the whole island shares one light. Lit by the sun (with shadows) and by lamps; casts shadows.
Shader "BWM/Snowy"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _SnowColor ("Snow", Color) = (0.94, 0.96, 1, 1)
        _ShadeColor ("Shade", Color) = (0.50, 0.60, 0.85, 1)
        _SnowShade ("Snow in shade", Color) = (0.58, 0.70, 0.93, 1)
        _SnowLine ("Snow line (0 = everything, 1 = only flat tops)", Range(0, 1)) = 0.55
        _SnowEdge ("Snow edge softness", Range(0.01, 0.5)) = 0.12
        _SnowNoise ("Snow edge raggedness", Range(0, 1)) = 0.35
        _Through ("Light through thin things (dry grass)", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (2 back for solids, 0 off for blades)", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half4 _SnowColor;
            half4 _ShadeColor;
            half4 _SnowShade;
            float _SnowLine;
            float _SnowEdge;
            float _SnowNoise;
            float _Through;
        CBUFFER_END

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
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }      // the game renders deferred: see BwmSnow.shader
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing      // the island's grass, sticks and stones are drawn instanced (winterwalk/Scatter.cs)
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
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 positionWS = i.positionWS;
                float3 n = normalize(i.normalWS) * (frontFace ? 1.0 : -1.0);

                // Snow on what faces up, with a ragged edge that follows the snow's own relief (the same maps as the
                // ground's, at the size of a roof or a post's cap)
                float3 viewVector = GetWorldSpaceViewDir(positionWS);
                float viewDistance = length(viewVector);
                float3 v = viewVector / max(viewDistance, 1e-4);
                BwmSnowDetail detail = BwmSnowDetailOn(positionWS, viewDistance, 5.0);
                float ragged = (Noise3(positionWS * 3.1) - 0.5) * _SnowNoise + (detail.cavity - 0.5) * 0.2 * _SnowNoise;
                // (a vertex colour with no alpha is a face under cover, which takes no snow: a coach's floor and benches
                // under its roof, a cabin's. Kit.Dry makes one. Snow lay on them as on any face that looks up.)
                float snow = smoothstep(_SnowLine - _SnowEdge, _SnowLine + _SnowEdge, n.y + ragged) * step(0.02, i.color.a);
                float3 albedo = i.color.rgb * _Tint.rgb;

                Light sun = BwmSun(positionWS);
                float towards = dot(n, sun.direction);
                float wrap = saturate(max(towards, -towards * _Through) * 0.65 + 0.35);      // a blade of grass is lit from behind too
                float lit = wrap * sun.shadowAttenuation;
                float3 ambient = SampleSH(n);
                float3 shade = albedo * _ShadeColor.rgb * BwmShadeLight(ambient, 0.16);
                float3 color = shade + albedo * sun.color * lit;      // sunlight adds to the sky's light
                // the snow itself, lit as all the island's snow is (BwmSnowCommon)
                color = lerp(color, BwmSnowOn(_SnowColor.rgb, _SnowShade.rgb, positionWS, n, v, viewDistance, detail, sun.direction, sun.color, sun.shadowAttenuation, 1.0), snow);
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
                            color += albedo * lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation * saturate(dot(n, lamp.direction) * 0.65 + 0.35));
                        }
                    LIGHT_LOOP_END
                }
                #endif
                color += albedo * (BwmLampLight(positionWS, n, 0.35) + BwmGlowLight(positionWS, n, 0.35));

                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 Vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
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
                return positionCS;
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
        // The game renders deferred and URP copies depth before our forward passes draw: this pass puts the
        // surface into the depth buffer during the GBuffer pass, so the fog and other depth effects do not see through it

        Pass
        {
            Name "GBufferDepth"
            Cull [_Cull]
            Tags { "LightMode" = "UniversalGBuffer" "UniversalMaterialType" = "Unlit" }      // depth, normal and a plain albedo: BwmGBuffer.hlsl
            ZWrite On
            ColorMask 0 3

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
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

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 Vert(Attributes v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
            half4 Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
