// The freezing fog that stands round the Winter Isle: tall curtains of drifting mist, drawn on ribbons of mesh that
// ring the island. Made for this edition. Thick from afar, it thins to nothing within a stone's throw of the camera,
// so a player walks into it and through it without ever meeting a wall. It takes the sun's and the sky's colour.
Shader "BWM/Veil"
{
    Properties
    {
        _Color ("Fog colour", Color) = (0.9, 0.94, 1, 1)
        _Density ("Density", Range(0, 1)) = 0.75
        _NearClear ("Clear this near the camera (m)", Float) = 25
        _NearFull ("Full strength from here (m)", Float) = 190
        _Drift ("Drift speed", Float) = 0.012
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+5" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Veil"
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
                float _Density, _NearClear, _NearFull, _Drift;
            CBUFFER_END
            float _BwmBlizzard;      // 0..1, set by Winter Walk: the veil thickens in a blizzard

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 light : TEXCOORD2; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                Light sun = GetMainLight();
                o.light = sun.color * 0.5 + SampleSH(float3(0, 1, 0)) * 0.95 + 0.05;
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // uv.x: metres along the ribbon; uv.y: 0 at its foot, 1 at its top
                float2 p = float2(i.uv.x * 0.006 + _Time.y * _Drift, i.positionWS.y * 0.011 - _Time.y * _Drift * 0.3);
                float billow = Noise(p) * 0.55 + Noise(p * 2.7 + 5.0) * 0.3 + Noise(p * 6.1 + 9.0) * 0.15;
                float top = 1.0 - smoothstep(0.45 + billow * 0.35, 1.0, i.uv.y);      // a ragged, soft top
                float body = lerp(0.6, 1.0, billow);
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                float near = smoothstep(_NearClear, _NearFull, dist);
                float alpha = saturate(_Density + _BwmBlizzard * 0.25) * top * body * near;
                return half4(_Color.rgb * min(i.light, 1.1), alpha);
            }
            ENDHLSL
        }
    }
}
