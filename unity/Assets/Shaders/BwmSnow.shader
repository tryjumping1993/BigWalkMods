// Snow ground for the Winter Isle. Made for this edition; nothing from the game.
//  - soft wind-drift relief from layered noise, so flat ground is not flat-looking
//  - shadows and unlit sides go blue instead of black, as snow does under an open sky
//  - glints that switch on and off with the view angle, fading with distance so they never shimmer far away
//  - a faint bright rim at grazing angles
//  - bare rock where the ground is too steep to hold snow, in rough horizontal bands, with a ragged snow line
//  - trodden paths (the mesh's vertex colour marks them): packed, bluer, no glints
//  - bare ground where the snow is thin (vertex green): heath and grass showing through, dusted with snow
//  - beach sand (vertex blue): pale dry sand, darker and wet at the waterline, with a dusting of snow
// The rock, heath and sand textures are the game's own, lent at runtime by the Winter Walk plugin; without it
// (or in the editor) the same places show in flat colours.
// Lit by the sun (with its shadows) and by lamps. Written against URP 17 (Unity 6000.3), the game's version.
Shader "BWM/Snow"
{
    Properties
    {
        _Color ("Snow", Color) = (0.94, 0.96, 1, 1)
        _ShadeColor ("Shade", Color) = (0.58, 0.70, 0.93, 1)
        _DriftScale ("Drift scale", Float) = 0.11
        _DriftStrength ("Drift strength", Range(0, 2)) = 0.85
        _SparkleScale ("Sparkle scale", Float) = 70
        _SparkleAmount ("Sparkle amount", Range(0, 4)) = 2.4
        _SparkleDistance ("Sparkle distance", Float) = 22
        _Rim ("Rim", Range(0, 1)) = 0.18
        _PathColor ("Trodden path", Color) = (0.74, 0.80, 0.90, 1)
        _RockColor ("Rock", Color) = (0.36, 0.36, 0.40, 1)
        _RockLine ("Rock line (how steep before rock shows)", Range(0.3, 0.95)) = 0.72
        _RockTex ("Rock texture", 2D) = "white" {}
        _RockTiling ("Rock tiling (per metre)", Float) = 0.12
        _BareColor ("Bare ground", Color) = (0.42, 0.40, 0.30, 1)
        _BareTex ("Bare ground texture", 2D) = "white" {}
        _SandColor ("Sand", Color) = (0.70, 0.67, 0.60, 1)
        _SandTex ("Sand texture", 2D) = "white" {}
        _GroundTiling ("Ground tiling (per metre)", Float) = 0.18
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _ShadeColor;
            float _DriftScale;
            float _DriftStrength;
            float _SparkleScale;
            float _SparkleAmount;
            float _SparkleDistance;
            float _Rim;
            half4 _PathColor;
            half4 _RockColor;
            float _RockLine;
            float _RockTiling;
            half4 _BareColor;
            half4 _SandColor;
            float _GroundTiling;
        CBUFFER_END
        TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);
        TEXTURE2D(_BareTex); SAMPLER(sampler_BareTex);
        TEXTURE2D(_SandTex); SAMPLER(sampler_SandTex);

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float3 Hash33(float3 p)
        {
            p = frac(p * float3(0.1031, 0.1030, 0.0973));
            p += dot(p, p.yxz + 33.33);
            return frac((p.xxy + p.yxx) * p.zyx);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        // Wind-drift height: long soft swells with a finer ripple on top
        float Drift(float2 p)
        {
            return ValueNoise(p) * 0.65 + ValueNoise(p * 3.1 + 17.0) * 0.25 + ValueNoise(p * 9.7 + 5.0) * 0.10;
        }

        // Wind-carved surface, in metres of height, everything lying with the wind (from the north-west):
        // sastrugi (sharp-crested ridges a metre or so long, in patches) and fine ripples across the wind
        float WindDetail(float2 xz, float rippleAmount)
        {
            float2 q = float2(dot(xz, float2(0.8, -0.6)), dot(xz, float2(0.6, 0.8)));      // along the wind, across it
            float s = ValueNoise(float2(q.x * 0.33, q.y * 1.35) + 31.0);
            s = 1.0 - abs(s * 2.0 - 1.0);
            s *= s * smoothstep(0.3, 0.7, ValueNoise(xz * 0.11 + 7.0));
            float ripple = sin(q.x * 10.0 + ValueNoise(q * 0.7) * 5.0) * 0.5 + 0.5;
            return s * 0.2 + ripple * 0.025 * rippleAmount;
        }
        ENDHLSL

        Pass
        {
            // "ForwardOnly" is drawn by both of URP's rendering paths. With plain "UniversalForward" the game drew
            // nothing at all, which is what a deferred renderer does with a shader that has no G-buffer pass.
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

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
                half4 color : COLOR;      // red = 1 fresh snow ... 0 a trodden path (a mesh with no colours is all fresh)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float path : TEXCOORD3;
                float2 ground : TEXCOORD4;      // x bare ground, y sand
                float form : TEXCOORD5;         // vertex alpha: 0.5 level, less in a hollow, more on a crest
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.path = 1.0 - v.color.r;
                o.ground = v.color.gb;
                o.form = v.color.a;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 positionWS = i.positionWS;
                // Paths: a ragged-edged band of packed snow, flatter, bluer and without glints
                float path = smoothstep(0.35, 0.65, i.path + (ValueNoise(positionWS.xz * 0.9) - 0.5) * 0.35);
                float3 viewVector = GetWorldSpaceViewDir(positionWS);
                float viewDistance = length(viewVector);
                float3 v = viewVector / max(viewDistance, 1e-4);

                // Drift relief: tilt the normal by the slope of the drift height (finite differences)
                float2 dp = positionWS.xz * _DriftScale;
                float h0 = Drift(dp), hx = Drift(dp + float2(0.35, 0)), hz = Drift(dp + float2(0, 0.35));
                // Rock where the ground itself (not the drift relief) is steep
                float3 ground = normalize(i.normalWS);
                float ragged = (ValueNoise(positionWS.xz * 0.45 + positionWS.y * 0.3) - 0.5) * 0.16;
                float rock = 1.0 - smoothstep(_RockLine - 0.07, _RockLine + 0.07, ground.y + ragged);
                float strata = ValueNoise(positionWS.xz * 0.11 + positionWS.y * 0.17) * 0.7 + ValueNoise(positionWS.xz * 0.5 - positionWS.y * 0.4) * 0.3;
                // granite: the texture projected from all three sides, blended by how much the face looks each way
                // (one projection stretches on faces between them)
                float3 tw = pow(abs(ground), 4.0);
                tw /= tw.x + tw.y + tw.z;
                float3 rockTex = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.zy * _RockTiling).rgb * tw.x
                    + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xz * _RockTiling).rgb * tw.y
                    + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xy * _RockTiling).rgb * tw.z;
                float3 rockColor = _RockColor.rgb * lerp(0.82, 1.1, strata) * rockTex * 1.6;
                // a dusting of snow on the rock's flatter parts
                rockColor = lerp(rockColor, _Color.rgb * 0.92, saturate((ground.y - 0.3) * 2.5) * smoothstep(0.4, 0.7, strata) * 0.8);

                // bare ground and sand where the snow is thin
                float2 groundUV = positionWS.xz * _GroundTiling;
                float patchy = ValueNoise(positionWS.xz * 0.21) * 0.6 + ValueNoise(positionWS.xz * 0.9) * 0.4;
                float bare = smoothstep(0.45, 0.85, i.ground.x * 0.85 + (patchy - 0.5) * 0.6) * (1.0 - path);
                float sand = smoothstep(0.3, 0.6, i.ground.y + (patchy - 0.5) * 0.25);
                float3 bareColor = _BareColor.rgb * SAMPLE_TEXTURE2D(_BareTex, sampler_BareTex, groundUV).rgb * 1.7;
                bareColor = lerp(bareColor, _Color.rgb * 0.9, 0.25 + smoothstep(0.45, 0.85, patchy) * 0.5);      // snow lying in the hollows of the heath
                float wet = 1.0 - smoothstep(0.15, 0.9, positionWS.y);
                float3 sandColor = _SandColor.rgb * SAMPLE_TEXTURE2D(_SandTex, sampler_SandTex, groundUV * 0.7).rgb * 1.5 * lerp(1.0, 0.62, wet);
                sandColor = lerp(sandColor, _Color.rgb * 0.95, (0.2 + smoothstep(0.4, 0.8, patchy) * 0.6) * (1.0 - wet));      // drifted snow on the upper beach
                float3 earth = lerp(bareColor, sandColor, sand);
                float earthy = saturate(max(bare, sand));
                float snowy = (1.0 - rock) * (1.0 - earthy * 0.8);
                float3 n = normalize(ground + _DriftStrength * (1.0 - path * 0.85) * snowy * float3(h0 - hx, 0, h0 - hz));
                // the wind-carved detail, near the eye only (it would shimmer further off)
                float detailFade = saturate(1.0 - viewDistance / 40.0);
                if (detailFade > 0.0)
                {
                    float rf = saturate(1.0 - viewDistance / 16.0), e = 0.08;
                    float d0 = WindDetail(positionWS.xz, rf);
                    float dx = WindDetail(positionWS.xz + float2(e, 0), rf), dz = WindDetail(positionWS.xz + float2(0, e), rf);
                    n = normalize(n + float3(d0 - dx, 0, d0 - dz) / e * detailFade * (1.0 - path * 0.7) * snowy);
                }

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord, positionWS, half4(1, 1, 1, 1));

                // Wrapped diffuse keeps slopes facing away from the sun soft; shadow pulls towards the shade colour
                float wrap = saturate(dot(n, sun.direction) * 0.82 + 0.18);
                float lit = wrap * sun.shadowAttenuation;
                float3 ambient = SampleSH(n);
                float3 shade = _ShadeColor.rgb * (ambient * 1.1 + 0.18);
                // snow is not one white: faintly bluer and greyer crusts, warmer fresh patches
                float crust = ValueNoise(positionWS.xz * 0.07 + 3.0) * 0.6 + ValueNoise(positionWS.xz * 0.35 + 9.0) * 0.4;
                float3 snowAlbedo = _Color.rgb * lerp(float3(0.9, 0.93, 0.98), float3(1.0, 1.0, 1.0), smoothstep(0.25, 0.75, crust));
                float3 pathAlbedo = _PathColor.rgb * (0.9 + 0.1 * ValueNoise(positionWS.xz * 1.3));
                float3 snowColor = lerp(lerp(lerp(snowAlbedo, pathAlbedo, path), earth, earthy), rockColor, rock);
                float3 earthShade = earth * _ShadeColor.rgb * (ambient * 1.1 + 0.16);
                shade = lerp(lerp(shade * lerp(1.0, 0.9, path), earthShade, earthy), rockColor * _ShadeColor.rgb * (ambient * 1.1 + 0.16), rock);
                float3 color = lerp(shade, snowColor * sun.color, lit);

                // Hollows between drifts are a touch bluer; so is every dip in the land, and crests catch more light
                color = lerp(color, color * float3(0.90, 0.95, 1.0), saturate(0.55 - h0));
                float hollow = saturate((0.5 - i.form) * 2.0), crest = saturate((i.form - 0.5) * 2.0);
                color *= lerp(float3(1, 1, 1), float3(0.80, 0.87, 0.98), hollow * (1.0 - rock)) * (1.0 + crest * 0.07);

                // Lamps
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light lamp = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
                        color += snowColor * lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation * saturate(dot(n, lamp.direction) * 0.7 + 0.3));
                    LIGHT_LOOP_END
                }
                #endif

                // Glints: each small cell of the surface has a random facet; it flashes when it mirrors the sun
                float sparkleFade = saturate(1.0 - viewDistance / _SparkleDistance) * (1.0 - path) * (1.0 - rock) * (1.0 - earthy);
                if (sparkleFade > 0.0)
                {
                    float3 scaled = positionWS * _SparkleScale;
                    float3 cell = floor(scaled);
                    float3 random = Hash33(cell);
                    float3 facet = normalize(n + (random * 2.0 - 1.0) * 0.75);
                    float glint = pow(saturate(dot(reflect(-sun.direction, facet), v)), 90.0);
                    // a round grain in the middle of the cell, and only some cells hold one
                    float grain = smoothstep(0.42, 0.12, length(frac(scaled) - 0.5)) * step(0.55, random.x);
                    color += glint * grain * _SparkleAmount * sparkleFade * sparkleFade * sun.color * sun.shadowAttenuation;
                }

                color += pow(1.0 - saturate(dot(n, v)), 4.0) * _Rim * (ambient + 0.1) * (1.0 - earthy);
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

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 Vert(Attributes v) : SV_POSITION
            {
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

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
