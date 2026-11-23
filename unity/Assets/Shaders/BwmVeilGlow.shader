// The soft red and green glow of the Winter Isle's lights, seen through its fog from afar: big round blooms of
// light that face the camera, added over whatever lies behind. Made for this edition. They fade away as the camera
// comes near (the lights themselves take over), and breathe slowly. Each vertex holds its bloom's centre; the
// corner is in uv (-1..1) and the radius in uv2.x.
Shader "BWM/VeilGlow"
{
    Properties
    {
        _Strength ("Strength", Range(0, 2)) = 0.55
        _FarFrom ("Begins to show from (m)", Float) = 160
        _FarFull ("Full strength from (m)", Float) = 520
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+1" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength, _FarFrom, _FarFull;
            CBUFFER_END
            float _BwmNight;      // 0 day .. 1 night (Winter Walk): the blooms are the island's lights, so only after dusk

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float fade : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 centreWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 centreVS = TransformWorldToView(centreWS);
                centreVS.xy += v.uv * v.uv2.x;      // the corner, facing the camera
                o.positionCS = TransformWViewToHClip(centreVS);
                o.color = v.color;
                o.uv = v.uv;
                float dist = distance(centreWS, _WorldSpaceCameraPos);
                float breathe = 0.82 + 0.18 * sin(_Time.y * 0.5 + v.uv2.y);
                o.fade = smoothstep(_FarFrom, _FarFull, dist) * breathe;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float r = saturate(length(i.uv));
                float bloom = pow(1.0 - r, 2.2);
                // (drawn before the fog box, which covers them from far off; by day they are not lit at all: they hung in
                // the daytime fog as coloured blobs, 2026-11-22)
                return half4(i.color.rgb * (bloom * i.fade * _Strength * _BwmNight), 1);
            }
            ENDHLSL
        }
    }
}
