// Painted and printed things (the pizzeria: its signs, logo, paintings, chalkboards, brick, planks, steel): the mesh's
// vertex colour times a picture (_MainTex, by the mesh's texture coordinates; faces with none sample its white
// corner), its relief from a normal map in the same layout (_NormalMap; the mesh needs tangents) and its shine from
// the picture's alpha (0 matte .. 1 glossy). Outdoors (_Indoor 0) it is lit by the sun as BWM/Snowy is, without the
// snow; indoors (_Indoor 1) by a warm room light from above and in front (_RoomDir, in the object's space) that does
// not depend on the sun, so a room under a roof is not left in the sky's blue shade. Lamps add to both. With
// _AlphaMode 1 the picture's alpha (times _Tint's and the vertex colour's) is its opacity instead, its shine _Gloss:
// glass and the logo's window vinyl, drawn in the transparent queue with _SrcBlend 5, _DstBlend 10, _ZWrite 0. Made for
// this edition.
Shader "BWM/Picture"
{
    Properties
    {
        _MainTex ("Picture (alpha: gloss)", 2D) = "white" {}
        [Normal] _NormalMap ("Relief (normal map)", 2D) = "bump" {}
        _NormalStrength ("Relief strength", Range(0, 2)) = 1
        _RoomDir ("Room light from (object space)", Vector) = (0.25, 1, 0.45, 0)
        _Shine ("Highlights", Range(0, 2)) = 0.8
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade", Color) = (0.50, 0.60, 0.85, 1)
        _Indoor ("Indoors (lit by the room, not the sun)", Range(0, 1)) = 0
        _RoomColor ("Room light", Color) = (1.0, 0.86, 0.68, 1)
        _RoomLevel ("Room light level", Range(0, 2)) = 0.95
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _AlphaMode ("Alpha is opacity (glass)", Range(0, 1)) = 0
        _Gloss ("Gloss when alpha is opacity", Range(0, 1)) = 0.9
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Blend from", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Blend to", Float) = 0
        _ZWrite ("Write depth", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _NormalStrength;
            float4 _RoomDir;
            float _Shine;
            half4 _Tint;
            half4 _ShadeColor;
            float _Indoor;
            half4 _RoomColor;
            float _RoomLevel;
            float _AlphaMode;
            float _Gloss;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }      // the game renders deferred: see BwmSnow.shader
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "BwmSunShadowKeywords.hlsl"      // the sun's shadow, the same at every quality setting (BwmSunShadow.hlsl)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "BwmSunShadow.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; half4 color : COLOR; };
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
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w * GetOddNegativeScale());
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS) * (frontFace ? 1.0 : -1.0);
                // the relief: the normal map turned into world space along the face's tangent frame
                float3 tp = i.tangentWS.xyz - n * dot(i.tangentWS.xyz, n);
                if (dot(tp, tp) > 1e-8)      // no tangent, or one along the normal: leave the face flat
                {
                    float3 t = normalize(tp);
                    float3 bt = cross(n, t) * (i.tangentWS.w >= 0.0 ? 1.0 : -1.0);
                    float3 rel = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv), _NormalStrength);
                    n = normalize(t * rel.x + bt * rel.y + n * rel.z);
                }
                half4 picture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 albedo = i.color.rgb * _Tint.rgb * picture.rgb;
                float gloss = lerp(picture.a, _Gloss, _AlphaMode);
                float alpha = lerp(1.0, picture.a * _Tint.a * i.color.a, _AlphaMode);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float power = exp2(2.0 + gloss * 9.0);      // tighter highlights on glossier things
                float shine = _Shine * gloss * gloss;
                Light sun = BwmSun(i.positionWS);
                float lit = saturate(dot(n, sun.direction) * 0.65 + 0.35) * sun.shadowAttenuation;
                float3 ambient = SampleSH(n);
                // (the little light that is always there fades with the sky's own: the game's nights are black, see BwmDaylight in BwmSnowCommon.hlsl)
                float daylight = saturate(dot(max(SampleSH(float3(0.0, 1.0, 0.0)), 0.0), float3(0.3, 0.5, 0.2)) / 0.3);
                float3 outdoors = albedo * _ShadeColor.rgb * (ambient * 1.1 + 0.16 * daylight) + albedo * sun.color * lit;      // sunlight adds to the sky's light
                outdoors += sun.color * shine * pow(saturate(dot(n, normalize(sun.direction + v))), power) * sun.shadowAttenuation;
                // the room: a warm light from above and in front, and a little from everywhere
                float3 room = normalize(TransformObjectToWorldDir(_RoomDir.xyz));
                float3 indoors = albedo * _RoomColor.rgb * _RoomLevel * (0.5 + 0.38 * saturate(dot(n, room)) + 0.12 * saturate(n.y * 0.5 + 0.5));
                indoors += _RoomColor.rgb * _RoomLevel * shine * 0.7 * pow(saturate(dot(n, normalize(room + v))), power);
                float3 color = lerp(outdoors, indoors, _Indoor);
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light lamp = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                        float reach = lamp.distanceAttenuation * lamp.shadowAttenuation;
                        color += albedo * lamp.color * (reach * saturate(dot(n, lamp.direction) * 0.65 + 0.35));
                        color += lamp.color * reach * shine * pow(saturate(dot(n, normalize(lamp.direction + v))), power);
                    LIGHT_LOOP_END
                }
                #endif
                color = MixFog(color, i.fogFactor);
                return half4(color, alpha);
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
        // The game renders deferred and URP copies depth before our forward passes draw: this pass puts the
        // surface into the depth buffer during the GBuffer pass, so the fog and other depth effects do not see through it

        Pass
        {
            Name "GBufferDepth"
            Cull [_Cull]
            Tags { "LightMode" = "UniversalGBuffer" "UniversalMaterialType" = "Unlit" }      // depth only, so the depth texture holds this surface (see the note above)
            ZWrite On
            ColorMask 0 0
            ColorMask 0 1
            ColorMask 0 2
            ColorMask 0 3
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
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
