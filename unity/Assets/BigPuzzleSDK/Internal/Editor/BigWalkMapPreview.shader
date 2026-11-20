// Used by the Big Walk map preview only: draws a model in its vertex colours (that is where the game keeps the colours of its
// buildings) with simple shading from the scene's main light. Nothing here is exported with puzzles.
Shader "Hidden/BigPuzzleSDK/MapPreview"
{
    Properties { _BaseColor ("Color", Color) = (1, 1, 1, 1) }
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

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                Light light = GetMainLight();
                float facing = saturate(dot(normalize(i.normalWS), light.direction));
                // Vertex colours that carry a real hue (a red tower, a green door) are used as they are; grey or white ones take the material's colour
                half saturation = max(i.color.r, max(i.color.g, i.color.b)) - min(i.color.r, min(i.color.g, i.color.b));
                half3 tinted = i.color.rgb * _BaseColor.rgb;
                half3 albedo = lerp(tinted, i.color.rgb, saturate(saturation * 4.0));
                half3 color = albedo * (0.5 + 0.5 * facing);
                return half4(min(color, half3(0.85, 0.85, 0.85)), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
