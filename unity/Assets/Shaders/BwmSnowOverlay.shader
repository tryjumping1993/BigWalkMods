// Snow laid over one of the game's own objects (its granite rocks, logs, dead trees). Made for this edition.
// Winter Walk adds this as an extra material on the copy's renderers, so Unity draws the mesh a second time
// with it, over the game's own lit surface: the rock keeps its own texture and lighting, and snow settles on
// whatever faces up, with a soft ragged edge and a thicker cap on the flattest tops. Where there is no snow
// the pass draws nothing. For foliage, _AlphaTex and _Cutoff take the leaf texture's cut-out, so snow only
// lands on leaves, not on the empty parts of their cards.
Shader "BWM/SnowOverlay"
{
    Properties
    {
        _SnowColor ("Snow", Color) = (0.94, 0.96, 1, 1)
        _ShadeColor ("Shade", Color) = (0.58, 0.70, 0.93, 1)
        _SnowLine ("Snow line (0 = everything, 1 = only flat tops)", Range(-0.5, 1)) = 0.45
        _SnowEdge ("Snow edge softness", Range(0.01, 0.5)) = 0.16
        _SnowNoise ("Snow edge raggedness", Range(0, 1.5)) = 0.55
        _WindNormal ("Wind-carved snow: normals", 2D) = "bump" {}
        _WindHeight ("Wind-carved snow: height (R), cavity (G), scoured flats (B)", 2D) = "gray" {}
        _WindTile ("Wind-carved snow: tile (metres)", Float) = 5
        _GrainNormal ("Crust: normals", 2D) = "bump" {}
        _GrainHeight ("Crust: height (R), cavity (G)", 2D) = "gray" {}
        _GrainTile ("Crust: tile (metres)", Float) = 1.6
        _AlphaTex ("Cut-out source (foliage)", 2D) = "white" {}
        _Cutoff ("Cut-out threshold", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (0 off for leaves, 2 back for solids)", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+40" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _SnowColor;
            half4 _ShadeColor;
            float _SnowLine;
            float _SnowEdge;
            float _SnowNoise;
            float4 _AlphaTex_ST;
            float _Cutoff;
            float _WindTile;
            float _GrainTile;
        CBUFFER_END
        TEXTURE2D(_WindNormal); SAMPLER(sampler_WindNormal);
        TEXTURE2D(_WindHeight); SAMPLER(sampler_WindHeight);
        TEXTURE2D(_GrainNormal); SAMPLER(sampler_GrainNormal);
        TEXTURE2D(_AlphaTex); SAMPLER(sampler_AlphaTex);

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
            Name "SnowOver"
            Tags { "LightMode" = "UniversalForwardOnly" }      // the game renders deferred: see BwmSnow.shader
            ZWrite Off
            ZTest LEqual
            Offset -2, -2
            Blend SrcAlpha OneMinusSrcAlpha
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "BwmSnowCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv * _AlphaTex_ST.xy + _AlphaTex_ST.zw;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                if (_Cutoff > 0)
                    clip(SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, i.uv).a - _Cutoff);
                float3 positionWS = i.positionWS;
                float3 n = normalize(i.normalWS) * (frontFace ? 1.0 : -1.0);

                // Snow on what faces up: ragged at three scales, so the line wanders like drifted snow
                // (and by the generated snow maps, laid from above at a smaller size than on the ground: a rock's cap
                // is a few metres across)
                float3 viewVector = GetWorldSpaceViewDir(positionWS);
                float viewDistance = length(viewVector);
                float3 v = viewVector / max(viewDistance, 1e-4);
                float2 windUV = positionWS.xz / _WindTile;
                float3 windN = UnpackNormal(SAMPLE_TEXTURE2D(_WindNormal, sampler_WindNormal, windUV));
                float3 windH = SAMPLE_TEXTURE2D(_WindHeight, sampler_WindHeight, windUV).rgb;
                float3 grainN = UnpackNormal(SAMPLE_TEXTURE2D(_GrainNormal, sampler_GrainNormal, positionWS.xz / _GrainTile));
                BwmSnowDetail detail;
                detail.tilt = windN.xy + grainN.xy * saturate(1.0 - viewDistance / 10.0);
                detail.cavity = windH.g;
                detail.flats = windH.b;
                float ragged = (Noise3(positionWS * 0.45) - 0.5) * 0.8 + (Noise3(positionWS * 2.3) - 0.5) * 0.45 + (Noise3(positionWS * 9.0) - 0.5) * 0.2;
                float cover = smoothstep(_SnowLine - _SnowEdge, _SnowLine + _SnowEdge, n.y + ragged * _SnowNoise);
                // a crisp edge: snow lies or it does not (a soft one reads as a white haze over the rock); the edge
                // follows the snow's own relief, so it is scalloped, not a smooth curve
                cover = smoothstep(0.38, 0.62, cover + (Noise3(positionWS * 14.0) - 0.5) * 0.2 + (detail.cavity - 0.5) * 0.3 + grainN.x * 0.15);
                if (cover < 0.01) discard;

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord, positionWS, half4(1, 1, 1, 1));
                // lit as all the island's snow is (BwmSnowCommon); the cap is thinner, so a little darker, at its edge
                float3 color = BwmSnowOn(_SnowColor.rgb, _ShadeColor.rgb, positionWS, n, v, viewDistance, detail, sun.direction, sun.color, sun.shadowAttenuation, 1.0)
                    * lerp(0.86, 1.0, cover);
                color = MixFog(color, i.fogFactor);
                return half4(color, cover);
            }
            ENDHLSL
        }
    }
}
