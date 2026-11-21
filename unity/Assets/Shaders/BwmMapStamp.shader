// Stamps a picture onto part of another (the Winter Isle's close-up onto the game's folding map): drawn over the
// whole target, it shows its texture only inside _Rect (x, y, width, height of the target, 0..1). Made for this edition.
Shader "BWM/MapStamp"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "white" {}
        _Rect ("Where (x, y, width, height)", Vector) = (0.7, 0.3, 0.28, 0.22)
        _Flip ("Flip (x, y)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Rect;
                float4 _Flip;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - _Rect.xy) / _Rect.zw;
                if (p.x < 0.0 || p.y < 0.0 || p.x > 1.0 || p.y > 1.0) return half4(0, 0, 0, 0);
                p = lerp(p, 1.0 - p, _Flip.xy);
                return half4(pow(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p).rgb, 0.72), 1);      // (lifted: it drew darker on the map than the map itself)
            }
            ENDHLSL
        }
    }
}
