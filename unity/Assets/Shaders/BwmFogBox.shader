// The freezing fog that hides the Winter Isle from afar, seen from outside a box round the whole island. Made for this
// edition. Drawn on the box's outer faces: the eye's ray is stepped through a round field of fog inside the box up to
// where it meets something (the scene's depth), coloured like the sky near the horizon behind it. It fades out over
// _FadeOutside metres as the camera comes up to the box and is not drawn from inside it (on the island the sky
// system's own fog takes over). At night a faint red and green from the island's lights (_BwmNight, set by Winter Walk).
Shader "BWM/FogBox"
{
    Properties
    {
        _Color ("Tint on the sky colour", Color) = (1, 1, 1, 1)
        _Density ("Density (1 / metres)", Float) = 0.012
        _Ceiling ("Fog height scale (m): it thins by e every this much above 150 m", Float) = 180
        _FadeOutside ("Fades out within this of the box (m)", Float) = 60
        _BoxMin ("Box min (world)", Vector) = (0, 0, 0, 0)
        _BoxMax ("Box max (world)", Vector) = (1, 1, 1, 0)
        _Festive ("Red and green at night", Range(0, 1)) = 0.003
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+2" "IgnoreProjector" = "True" }
        Pass
        {
            Name "FogBox"
            Tags { "LightMode" = "UniversalForwardOnly" }      // the game renders deferred: see BwmSnow.shader
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Density, _Ceiling, _FadeOutside, _Festive;
                float4 _BoxMin, _BoxMax;
            CBUFFER_END
            float _BwmNight;
            float _BwmBlizzard;
            float _BwmSkyCubeOn;                    // Winter Walk hands over the game's live sky cubemap (Plugin.SkyCube)
            TEXTURECUBE(_BwmSkyCube); SAMPLER(sampler_BwmSkyCube);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 screen : TEXCOORD1; float3 light : TEXCOORD2; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screen = ComputeScreenPos(o.positionCS);
                Light sun = GetMainLight();
                o.light = sun.color * 0.5 + SampleSH(float3(0, 1, 0)) * 0.95 + 0.05;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 cam = _WorldSpaceCameraPos;
                // how far the camera is outside the box (0 inside): no fog from inside, a fade as it comes up to it
                float3 outside = max(max(_BoxMin.xyz - cam, cam - _BoxMax.xyz), 0);
                float away = length(outside);
                clip(away - 0.01);
                float weight = smoothstep(0.0, max(_FadeOutside, 1.0), away);

                float3 ray = i.positionWS - cam;
                float toFace = length(ray);
                float3 dir = ray / max(toFace, 1e-4);
                // where the ray enters and leaves the box
                float3 inv = 1.0 / (abs(dir) > 1e-5 ? dir : (dir >= 0 ? 1e-5 : -1e-5));
                float3 t0 = (_BoxMin.xyz - cam) * inv, t1 = (_BoxMax.xyz - cam) * inv;
                float3 tNear = min(t0, t1), tFar = max(t0, t1);
                float enter = max(max(tNear.x, tNear.y), tNear.z);
                float leave = min(min(tFar.x, tFar.y), tFar.z);
                // where it meets the scene: the depth buffer's eye depth, turned into a distance along the ray
                float2 uv = i.screen.xy / i.screen.w;
                float eye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float hit = eye / max(dot(dir, forward), 1e-3);
                // The fog is a smooth round field, stepped through along the ray (16 samples, dithered) and added up: dense
                // up to 150 m, thinning above, fading out over the outer half of an ellipse inscribed in the box, so no side
                // or corner of the box ever shows (2026-11-22: a box-shaped field stood as a hard wall against the sky).
                float tStart = max(enter, 0.0), tEnd = min(leave, hit);
                float inside = max(0.0, tEnd - tStart);
                float2 centre = (_BoxMin.xz + _BoxMax.xz) * 0.5, halfSize = max((_BoxMax.xz - _BoxMin.xz) * 0.5, 1.0);
                float dither = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                float dt = inside / 16.0, optical = 0.0;
                [unroll] for (int k = 0; k < 16; k++)
                {
                    float3 p = cam + dir * (tStart + (k + dither) * dt);
                    float r = length((p.xz - centre) / halfSize);
                    float edge = 1.0 - smoothstep(0.5, 0.95, r);
                    float height = exp(-max(0.0, p.y - 150.0) / max(_Ceiling, 1.0));
                    optical += edge * height * dt;
                }
                float fog = 1.0 - exp(-optical * _Density * (1.0 + _BwmBlizzard));
                // Its colour is the sky's own just above the horizon in that direction (the sky's reflection map, which the
                // sky system keeps up with the hour), as the game's own haze takes the sky's colour: fog of a fixed colour
                // read as an object in front of the sky whatever its shape, and at night as a green slab (2026-11-22).
                float3 horizon = normalize(float3(dir.x, saturate(dir.y) * 0.5 + 0.03, dir.z));
                float3 color = _BwmSkyCubeOn > 0.5
                    // (never darker than the night sky's clouds, which the cubemap leaves out: a black dome at night)
                    ? max(SAMPLE_TEXTURECUBE_LOD(_BwmSkyCube, sampler_BwmSkyCube, horizon, 4).rgb, float3(0.008, 0.008, 0.012)) * _Color.rgb
                    : _Color.rgb * min(i.light, 1.1) * float3(0.88, 0.92, 0.98);
                // at night a faint red and green from the island's lights, in broad patches round the coast
                // (narrow bands, so red and green alternate across the view; the night's exposure makes even a little a lot)
                float wave = sin(i.positionWS.x * 0.011 + i.positionWS.z * 0.008 + i.positionWS.y * 0.006);
                color += (float3(1.0, 0.15, 0.1) * saturate(wave) + float3(0.1, 0.9, 0.3) * saturate(-wave)) * (_Festive * _BwmNight * fog);
                return half4(color, saturate(fog * weight));
            }
            ENDHLSL
        }
    }
}
