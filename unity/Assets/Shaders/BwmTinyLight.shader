// The tiny lights strung round the Winter Isle's mountain and along its railway: thousands of small coloured bulbs
// in one mesh. Made for this edition. Each bulb is a square that faces the camera: every vertex holds the bulb's
// centre, the corner is in uv (-1..1), uv2.x is the bulb's own phase (they twinkle out of step) and the vertex
// colour is its colour. By day a bulb is a small coloured bead. As night comes (_BwmNight, set by Winter Walk from
// the hour) it is lit and grows a soft halo. A bulb never draws smaller than about a pixel and a half, so from
// across the island the strings still read as lines of lights; far ones are dimmed, not dropped.
Shader "BWM/TinyLight"
{
    Properties
    {
        _Size ("Bulb radius (m)", Float) = 0.05
        _Halo ("Halo radius at night (m)", Float) = 0.42
        _MinSize ("Smallest size on screen (radians)", Float) = 0.0016
        _Glow ("Brightness at night", Range(0, 8)) = 2.4
        _HaloGlow ("Halo brightness", Range(0, 2)) = 0.3
        _Twinkle ("Twinkle depth", Range(0, 1)) = 0.35
        _Reach ("Dims over (m)", Float) = 2600
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Lights"
            Tags { "LightMode" = "UniversalForwardOnly" }      // the game renders deferred: see BwmSnow.shader
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Size, _Halo, _MinSize, _Glow, _HaloGlow, _Twinkle, _Reach;
            CBUFFER_END
            float _BwmNight;      // 0 day .. 1 night, set by Winter Walk

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float4 shape : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 centreWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 centreVS = TransformWorldToView(centreWS);
                float dist = length(centreVS);
                float want = lerp(_Size, _Halo, _BwmNight);      // the bead by day, bead and halo at night
                float least = dist * _MinSize;
                float radius = max(want, least);
                centreVS.xy += v.uv * radius;
                o.positionCS = TransformWViewToHClip(centreVS);
                o.color = v.color;
                o.uv = v.uv;
                // shape.x: the bead's share of the square; y: the halo's brightness (a far bulb drawn larger than it is
                // gathers its light into that pixel or two, up to the bead's own brightness); z: twinkle; w: dimming with distance
                float spread = want / radius;
                float twinkle = 1.0 - _Twinkle * (0.5 + 0.35 * sin(_Time.y * 1.7 + v.uv2.x * 6.2831853) + 0.15 * sin(_Time.y * 6.3 + v.uv2.x * 31.4));
                o.shape = float4(saturate(_Size / radius), min(_Glow, _HaloGlow / max(spread * spread, 1e-3)), twinkle, exp(-dist / max(_Reach, 1.0)));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float d = length(i.uv);
                float bead = 1.0 - smoothstep(i.shape.x * 0.8, i.shape.x + 0.02, d);
                float halo = pow(saturate(1.0 - d), 2.6);
                float3 day = i.color.rgb * 0.85 * bead;
                float3 night = i.color.rgb * (bead * _Glow + halo * i.shape.y) * i.shape.z * i.shape.w;
                float3 rgb = lerp(day, night, _BwmNight);
                // far by day a bead is under a pixel: let it thin out rather than draw a coloured square
                float alpha = bead * lerp(saturate(i.shape.x * 4.0), 1.0, _BwmNight);
                return half4(rgb * lerp(alpha, 1.0, _BwmNight), alpha);
            }
            ENDHLSL
        }
    }
}
