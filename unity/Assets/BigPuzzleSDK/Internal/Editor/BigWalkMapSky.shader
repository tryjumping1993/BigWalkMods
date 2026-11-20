// Used by the Big Walk map preview only: a clear daytime sky (blue overhead, hazy at the horizon, soft white clouds, a sun) in the look of the
// game's. The game's own sky is made by a time-of-day add-on while it runs, so it can't be copied; this just imitates it. Nothing here is exported.
Shader "Hidden/BigPuzzleSDK/MapSky"
{
    Properties
    {
        _Zenith ("Overhead", Color) = (0.40, 0.61, 0.88, 1)
        _Horizon ("Horizon", Color) = (0.65, 0.76, 0.91, 1)
        _Ground ("Below the horizon", Color) = (0.50, 0.60, 0.75, 1)
        _CloudLight ("Cloud top", Color) = (0.86, 0.89, 0.96, 1)
        _CloudShade ("Cloud shadow", Color) = (0.66, 0.74, 0.90, 1)
        _CloudAmount ("Cloud amount", Range(0, 1)) = 0.52
        _SunDir ("Sun direction", Vector) = (0.4, 0.75, 0.5, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Zenith, _Horizon, _Ground, _CloudLight, _CloudShade;
            float _CloudAmount;
            float4 _SunDir;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * noise(p); p = p * 2.07 + 17.3; a *= 0.5; }
                return v;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 sky = lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(h), 0.45));
                if (h < 0.0) return fixed4(lerp(_Horizon.rgb, _Ground.rgb, saturate(-h * 4.0)), 1);

                // the sun: a small bright disc and a wide soft glow
                float3 sun = normalize(_SunDir.xyz);
                float sd = saturate(dot(d, sun));
                sky += float3(1.0, 0.94, 0.82) * (pow(sd, 1800.0) * 4.0 + pow(sd, 10.0) * 0.12);

                // clouds on a flat layer overhead: they bunch up and flatten towards the horizon, like the game's
                float2 uv = d.xz / (h * 0.9 + 0.1) * 0.5;
                float n = fbm(uv);
                float c = smoothstep(_CloudAmount, _CloudAmount + 0.16, n);
                c *= smoothstep(0.0, 0.1, h);
                float lit = saturate((n - fbm(uv + sun.xz * 0.05)) * 5.0 + 0.55);      // brighter on the side facing the sun
                float3 cloud = lerp(_CloudShade.rgb, _CloudLight.rgb, lit);
                float3 col = lerp(sky, cloud, c * 0.96);
                col = lerp(col, _Horizon.rgb, (1.0 - smoothstep(0.0, 0.07, h)) * 0.6);      // haze at the very edge
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
