// Used by the Big Walk map preview only: draws the game's rocks with their granite texture, projected from three sides in world space (the
// game's rock shader does the same, so the rocks need no UVs), with simple shading from the scene's main light. Nothing here is exported.
Shader "Hidden/BigPuzzleSDK/MapRock"
{
    Properties
    {
        _MainTex ("Granite", 2D) = "grey" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _TileSize ("Metres per repeat", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _TileSize;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 w = pow(abs(n), 4.0);
                w /= max(w.x + w.y + w.z, 0.0001);
                float3 p = i.positionWS / max(_TileSize, 0.01);
                half3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p.zy).rgb * w.x
                          + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p.xz).rgb * w.y
                          + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p.xy).rgb * w.z;
                Light light = GetMainLight();
                float facing = saturate(dot(n, light.direction));
                half3 color = tex * _BaseColor.rgb * (0.5 + 0.5 * facing);
                return half4(min(color, half3(0.85, 0.85, 0.85)), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
