// Snow ground for the Winter Isle. Made for this edition; nothing from the game.
//  - a wind-carved surface from generated maps (tools/snowtex.py): sastrugi, ripples, pillows and crust, over
//    slow swells of noise, so flat ground is not flat-looking and nothing repeats
//  - shadows and unlit sides go blue instead of black, as snow does under an open sky
//  - glints that switch on and off with the view angle, fading with distance so they never shimmer far away
//  - a faint bright rim at grazing angles
//  - bare rock where the ground is too steep to hold snow, in rough horizontal bands, with a ragged snow line
//  - trodden paths (the mesh's vertex colour marks them): packed snow covered in boot prints, no glints
//  - bare ground where the snow is thin (vertex green): heath and grass showing through, dusted with snow
//  - beach sand (vertex blue): pale dry sand, darker and wet at the waterline, with a dusting of snow
// The rock, heath and sand textures are the game's own, lent at runtime by the Winter Walk plugin; without it
// (or in the editor) the same places show in flat colours.
// Lit by the sun (with its shadows) and by lamps. Written against URP 17 (Unity 6000.3), the game's version.
Shader "BWM/Snow"
{
    Properties
    {
        _Color ("Snow", Color) = (0.94, 0.96, 1, 1)
        _ShadeColor ("Shade", Color) = (0.58, 0.70, 0.93, 1)
        _DriftScale ("Drift scale", Float) = 0.11
        _DriftStrength ("Drift strength", Range(0, 2)) = 0.3
        _WindNormal ("Wind-carved snow: normals", 2D) = "bump" {}
        _WindHeight ("Wind-carved snow: height (R), cavity (G), scoured flats (B)", 2D) = "gray" {}
        _WindTile ("Wind-carved snow: tile (metres)", Float) = 12
        _WindStrength ("Wind-carved snow: relief", Range(0, 3)) = 1
        _GrainNormal ("Crust: normals", 2D) = "bump" {}
        _GrainHeight ("Crust: height (R), cavity (G)", 2D) = "gray" {}
        _GrainTile ("Crust: tile (metres)", Float) = 1.6
        _GrainStrength ("Crust: relief", Range(0, 3)) = 1
        _TrackNormal ("Trodden snow: normals", 2D) = "bump" {}
        _TrackHeight ("Trodden snow: height (R), cavity (G)", 2D) = "gray" {}
        _TrackTile ("Trodden snow: tile (metres)", Float) = 3
        _SparkleScale ("Sparkle scale", Float) = 70
        _SparkleAmount ("Sparkle amount", Range(0, 4)) = 2.4
        _SparkleDistance ("Sparkle distance", Float) = 22
        _Rim ("Rim", Range(0, 1)) = 0.18
        _PathColor ("Trodden path", Color) = (0.80, 0.84, 0.91, 1)
        _RockColor ("Rock", Color) = (0.47, 0.43, 0.39, 1)
        _RockLine ("Rock line (how steep before rock shows)", Range(0.3, 0.95)) = 0.72
        _RockTex ("Rock texture", 2D) = "white" {}
        _RockTiling ("Rock tiling (per metre)", Float) = 0.12
        _BareColor ("Bare ground", Color) = (0.42, 0.40, 0.30, 1)
        _BareTex ("Bare ground texture", 2D) = "white" {}
        _SandColor ("Sand", Color) = (0.70, 0.67, 0.60, 1)
        _SandTex ("Sand texture", 2D) = "white" {}
        _GroundTiling ("Ground tiling (per metre)", Float) = 0.18
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _ShadeColor;
            float _DriftScale;
            float _DriftStrength;
            float _SparkleScale;
            float _SparkleAmount;
            float _SparkleDistance;
            float _Rim;
            half4 _PathColor;
            half4 _RockColor;
            float _RockLine;
            float _RockTiling;
            half4 _BareColor;
            half4 _SandColor;
            float _GroundTiling;
            float _WindTile;
            float _WindStrength;
            float _GrainTile;
            float _GrainStrength;
            float _TrackTile;
        CBUFFER_END
        // Tracks pressed into the snow by players: Winter Walk keeps the snow's height round the camera in this
        // texture (millimetres, 128 = untouched) and sets these globals; without it _BwmTrail is 0
        TEXTURE2D(_BwmTrailTex); SAMPLER(sampler_BwmTrailTex);
        float4 _BwmTrailParams;      // x: texture coordinates per metre; y, z: full strength within y metres of the eye, none beyond z; w: metres per texel
        float _BwmTrail;
        float TrailHeight(float2 xz)      // metres: below 0 pressed down, above 0 heaped up
        {
            return (SAMPLE_TEXTURE2D_LOD(_BwmTrailTex, sampler_BwmTrailTex, xz * _BwmTrailParams.x, 0).r * 255.0 - 128.0) * 0.001;
        }
        // An edge between two surfaces that stays about a pixel wide however near or far it is seen (a fixed-width
        // blend is a crisp line far away and a blurred band up close). 'fine' is added detail that shapes the edge
        // without widening it.
        float Crisp(float value, float fine, float edge)
        {
            return saturate((value + fine - edge) / max(fwidth(value) * 2.4, 0.035) + 0.5);
        }
        TEXTURE2D(_TrackNormal); SAMPLER(sampler_TrackNormal);
        TEXTURE2D(_TrackHeight); SAMPLER(sampler_TrackHeight);
        TEXTURE2D(_WindNormal); SAMPLER(sampler_WindNormal);
        TEXTURE2D(_WindHeight); SAMPLER(sampler_WindHeight);
        TEXTURE2D(_GrainNormal); SAMPLER(sampler_GrainNormal);
        TEXTURE2D(_GrainHeight); SAMPLER(sampler_GrainHeight);
        TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);
        TEXTURE2D(_BareTex); SAMPLER(sampler_BareTex);
        TEXTURE2D(_SandTex); SAMPLER(sampler_SandTex);

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float3 Hash33(float3 p)
        {
            p = frac(p * float3(0.1031, 0.1030, 0.0973));
            p += dot(p, p.yxz + 33.33);
            return frac((p.xxy + p.yxx) * p.zyx);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        // Noise in space, not on the map: the same on a cliff's face as on level ground, so nothing drawn with it
        // runs down a steep face in streaks
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

        // Wind-drift height: long soft swells with a finer ripple on top
        float Drift(float2 p)
        {
            return ValueNoise(p) * 0.65 + ValueNoise(p * 3.1 + 17.0) * 0.25 + ValueNoise(p * 9.7 + 5.0) * 0.10;
        }

        ENDHLSL

        Pass
        {
            // "ForwardOnly" is drawn by both of URP's rendering paths. With plain "UniversalForward" the game drew
            // nothing at all, which is what a deferred renderer does with a shader that has no G-buffer pass.
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "BwmSnowCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;      // red = 1 fresh snow ... 0 a trodden path (a mesh with no colours is all fresh)
                float2 uv : TEXCOORD0;    // x: needle litter under conifers; y: scree at a rock's foot
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float path : TEXCOORD3;
                float2 ground : TEXCOORD4;      // x bare ground, y sand
                float form : TEXCOORD5;         // vertex alpha: 0.5 level, less in a hollow, more on a crest
                float2 under : TEXCOORD6;       // x needle litter, y scree
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.path = 1.0 - v.color.r;
                o.ground = v.color.gb;
                o.form = v.color.a;
                o.under = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 positionWS = i.positionWS;
                float3 viewVector = GetWorldSpaceViewDir(positionWS);
                float viewDistance = length(viewVector);
                float3 v = viewVector / max(viewDistance, 1e-4);

                // Tracks: where players have pressed the snow down and heaped it aside. The eye's ray is followed
                // down into the trench (a short march through the height field), so its floor and far wall sit
                // below the surface instead of being painted on it.
                float trail = 0.0;              // metres at the point seen: below 0 pressed down, above 0 heaped
                float2 trailSlope = 0.0;        // its lean, to add to the normal
                float trailFade = _BwmTrail * (1.0 - smoothstep(_BwmTrailParams.y, _BwmTrailParams.z, viewDistance));
                if (trailFade > 0.0)
                {
                    const float deepest = 0.1;
                    float2 across = -v.xz / max(v.y, 0.22);      // metres sideways for each metre the ray goes down
                    float2 seen = positionWS.xz;
                    float here = TrailHeight(seen), far = TrailHeight(seen + across * deepest), mid = TrailHeight(seen + across * deepest * 0.5);
                    if (min(here, min(far, mid)) < -0.003)
                    {
                        float lastDepth = 0.0, lastGap = -here;      // gap: how far the floor is below the ray (negative once the ray is under it)
                        float hit = max(-here, 0.0);
                        for (int k = 1; k <= 6; k++)
                        {
                            float depth = deepest * k / 6.0;
                            float gap = -TrailHeight(seen + across * depth) - depth;
                            if (gap <= 0.0)
                            {
                                hit = lerp(lastDepth, depth, saturate(lastGap / max(lastGap - gap, 1e-5)));
                                break;
                            }
                            lastDepth = depth; lastGap = gap; hit = depth;
                        }
                        seen += across * hit;
                    }
                    float texel = _BwmTrailParams.w;
                    float h = TrailHeight(seen);
                    trail = h * trailFade;
                    trailSlope = float2(h - TrailHeight(seen + float2(texel, 0)), h - TrailHeight(seen + float2(0, texel))) / texel * trailFade;
                }
                float walked = smoothstep(0.052, 0.066, -trail);      // a walked trench is deeper than a slid groove

                // Paths: trodden snow, packed and covered in boot prints, without glints. The edge is ragged and
                // follows the prints themselves, so stray ones wander off it
                float2 trackUV = positionWS.xz / _TrackTile;
                float3 trackN = UnpackNormal(SAMPLE_TEXTURE2D(_TrackNormal, sampler_TrackNormal, trackUV));
                float trackCavity = SAMPLE_TEXTURE2D(_TrackHeight, sampler_TrackHeight, trackUV).g;      // 0 in a print
                float path = smoothstep(0.42, 0.58, i.path + (ValueNoise(positionWS.xz * 0.9) - 0.5) * 0.35 + (0.5 - trackCavity) * 0.25);
                path = max(path, walked);      // the floor of a walked trench is trodden snow too

                // The snow's surface, from the generated maps (tools/snowtex.py), laid over the ground from above:
                // the wind-carved map at its own size; the same map four times larger and blurred, for the shapes
                // of drifts further off, where the first has faded into its mips; and the crust, for the last metres
                float2 windUV = positionWS.xz / _WindTile;
                float3 windN = UnpackNormal(SAMPLE_TEXTURE2D(_WindNormal, sampler_WindNormal, windUV));
                float3 windH = SAMPLE_TEXTURE2D(_WindHeight, sampler_WindHeight, windUV).rgb;      // height, cavity, scoured flats
                float3 farN = UnpackNormal(SAMPLE_TEXTURE2D_BIAS(_WindNormal, sampler_WindNormal, windUV / 4.3 + float2(0.37, 0.61), 3.5));
                float3 grainN = UnpackNormal(SAMPLE_TEXTURE2D(_GrainNormal, sampler_GrainNormal, positionWS.xz / _GrainTile));
                float grainFade = saturate(1.0 - viewDistance / 10.0);

                // Slow swells from noise, which never repeats: tilt the normal by the slope of their height
                float2 dp = positionWS.xz * _DriftScale;
                float h0 = Drift(dp), hx = Drift(dp + float2(0.35, 0)), hz = Drift(dp + float2(0, 0.35));
                // Rock where the ground itself (not the snow's relief) is steep. The snow line is ragged at three
                // scales and follows the snow's own relief, and its edge is crisp: thin snow lies or it does not
                float3 ground = normalize(i.normalWS);
                float ragged = (Noise3(positionWS * 0.13 + 2.0) - 0.5) * 0.26 + (Noise3(positionWS * 0.45) - 0.5) * 0.16 + (Noise3(positionWS * 1.9) - 0.5) * 0.07;
                // (the snow's own relief and the crust map's grain shape the edge up close)
                float rock = 1.0 - Crisp(ground.y + ragged, (windH.g - 0.5) * 0.1 + grainN.y * 0.06, _RockLine);
                float strata = Noise3(positionWS * float3(0.11, 0.3, 0.11)) * 0.7 + Noise3(positionWS * 0.5 + 7.0) * 0.3;      // beds lying flat, as rock does
                // granite: the texture projected from all three sides, blended by how much the face looks each way
                // (one projection stretches on faces between them)
                float3 tw = pow(abs(ground), 4.0);
                tw /= tw.x + tw.y + tw.z;
                float3 rockTex = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.zy * _RockTiling).rgb * tw.x
                    + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xz * _RockTiling).rgb * tw.y
                    + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xy * _RockTiling).rgb * tw.z;
                // big-scale mottling and dark cracks, so a cliff reads as granite from a distance too
                float mottle = (ValueNoise(positionWS.zy * 0.05) * 0.6 + ValueNoise(positionWS.zy * 0.15 + 4.0) * 0.4) * tw.x
                    + (ValueNoise(positionWS.xz * 0.05) * 0.6 + ValueNoise(positionWS.xz * 0.15 + 4.0) * 0.4) * tw.y
                    + (ValueNoise(positionWS.xy * 0.05) * 0.6 + ValueNoise(positionWS.xy * 0.15 + 4.0) * 0.4) * tw.z;
                // joints as granite has them: long vertical cracks, and fainter horizontal sheeting, drawn on each
                // face from its own side (noise over plain height would draw contour lines)
                float2 faceX = positionWS.zy, faceZ = positionWS.xy;
                float jointX = smoothstep(0.9, 0.985, 1.0 - abs(ValueNoise(float2(faceX.x * 0.12, faceX.y * 0.035) + 3.0) * 2.0 - 1.0))
                    + 0.6 * smoothstep(0.92, 0.99, 1.0 - abs(ValueNoise(float2(faceX.x * 0.03, faceX.y * 0.14) + 8.0) * 2.0 - 1.0));
                float jointZ = smoothstep(0.9, 0.985, 1.0 - abs(ValueNoise(float2(faceZ.x * 0.12, faceZ.y * 0.035) + 5.0) * 2.0 - 1.0))
                    + 0.6 * smoothstep(0.92, 0.99, 1.0 - abs(ValueNoise(float2(faceZ.x * 0.03, faceZ.y * 0.14) + 1.0) * 2.0 - 1.0));
                float cracks = saturate((jointX * tw.x + jointZ * tw.z) / max(tw.x + tw.z, 0.001)) * saturate(1.0 - tw.y * 1.5);
                float3 rockColor = _RockColor.rgb * lerp(0.82, 1.1, strata) * rockTex * 1.6 * lerp(0.72, 1.18, mottle) * (1.0 - cracks * 0.45);
                float3 bareRock = rockColor;
                // snow caught on the rock's flatter parts, in patches with an edge (a soft dusting reads as a smear)
                float caught = Crisp(strata * 0.6 + Noise3(positionWS * 1.3 + 3.0) * 0.25 + Noise3(positionWS * 4.1) * 0.15, grainN.x * 0.08, 0.54);
                rockColor = lerp(rockColor, _Color.rgb * 0.92, saturate((ground.y - 0.3) * 2.5) * caught * 0.85);

                // bare ground and sand where the snow is thin
                float2 groundUV = positionWS.xz * _GroundTiling;
                // (three sizes of patch, so a bare place is a scatter of small ones with a ragged edge, not one blob)
                float patchy = Noise3(positionWS * 0.21) * 0.42 + Noise3(positionWS * 0.9 + 5.0) * 0.33 + Noise3(positionWS * 2.9 + 3.7) * 0.25;
                // where the snow is thin the ground shows first in the troughs between the wind's ridges
                float bare = Crisp(i.ground.x * 0.85 + (patchy - 0.5) * 0.5, (0.5 - windH.g) * 0.35 + grainN.x * 0.2, 0.65) * (1.0 - path * 0.45);
                float sand = smoothstep(0.3, 0.6, i.ground.y + (patchy - 0.5) * 0.25);
                // No snow at the waterline or under it: the sea keeps a ragged band of rock wet and bare (sand, in the
                // bays), and the seabed below is rock, darker the deeper it lies
                float tide = 1.0 - Crisp(positionWS.y + (patchy - 0.5) * 0.7, grainN.x * 0.05, 0.62);
                rock = max(rock, tide * (1.0 - sand));
                // wet rock above the water; under it the shallows are pale (the sea shows turquoise over them) and the
                // deep is dark
                rockColor = lerp(rockColor, bareRock * (positionWS.y > 0.0 ? 0.62 : lerp(0.95, 0.3, smoothstep(-0.3, -5.0, positionWS.y))), tide);
                // an ice rim: a ragged white line of ice where the water meets the land
                float iceRim = Crisp(0.14 - abs(positionWS.y - 0.04) + (ValueNoise(positionWS.xz * 0.8) - 0.5) * 0.12, 0.0, 0.0);
                rockColor = lerp(rockColor, float3(0.84, 0.93, 0.98), iceRim * 0.9);
                // (from three sides, like the rock: laid from above alone it smears down a slope)
                float3 bareTex = SAMPLE_TEXTURE2D(_BareTex, sampler_BareTex, positionWS.zy * _GroundTiling).rgb * tw.x
                    + SAMPLE_TEXTURE2D(_BareTex, sampler_BareTex, groundUV).rgb * tw.y
                    + SAMPLE_TEXTURE2D(_BareTex, sampler_BareTex, positionWS.xy * _GroundTiling).rgb * tw.z;
                // on a slope, and on the high ground, what shows through thin snow is the rock, not heath
                float3 heath = _BareColor.rgb * bareTex * 1.7;
                // under the conifers the ground is needle litter: dark, reddish, fine; at a rock's foot it is scree
                float3 litter = float3(0.40, 0.29, 0.19) * (0.8 + 0.4 * ValueNoise(positionWS.xz * 9.0)) * (0.85 + 0.3 * bareTex.g);      // (near-black reads as a hole in the snow)
                float3 scree = bareRock * (0.7 + 0.55 * step(0.5, ValueNoise(positionWS.xz * 7.0 + 2.0))) * (0.85 + 0.3 * ValueNoise(positionWS.xz * 23.0));
                heath = lerp(lerp(heath, scree, Crisp(i.under.y, (patchy - 0.5) * 0.5, 0.5)), litter, smoothstep(0.25, 0.7, i.under.x + (patchy - 0.5) * 0.4));
                float3 bareColor = lerp(heath, bareRock, max(smoothstep(0.93, 0.8, ground.y), smoothstep(30.0, 46.0, positionWS.y)) * (1.0 - smoothstep(0.3, 0.6, i.under.x)));
                // snow lying among the heath in small pockets, each with an edge (an even mix reads as grey plastic)
                float pockets = Crisp(ValueNoise(positionWS.xz * 2.7 + 11.0) * 0.5 + (patchy - 0.5) * 0.45, windH.g * 0.5 + grainN.y * 0.1, 0.55);
                bareColor = lerp(bareColor, _Color.rgb * 0.92, max(pockets * 0.85, 0.12 + 0.22 * Noise3(positionWS * 5.0)));      // and a thin frost over all of it
                float wet = 1.0 - smoothstep(0.15, 0.9, positionWS.y);
                // sand: the texture at two sizes (one alone is a blur at a walker's feet), darker where the last wave wet
                // it, with drifted snow lying on the upper beach in patches
                float3 sandTex = SAMPLE_TEXTURE2D(_SandTex, sampler_SandTex, groundUV * 0.7).rgb * (0.6 + 0.8 * SAMPLE_TEXTURE2D(_SandTex, sampler_SandTex, groundUV * 4.3).g);
                float3 sandColor = _SandColor.rgb * sandTex * 1.5 * lerp(1.0, 0.62, wet) * lerp(0.92, 1.06, windH.g);
                sandColor = lerp(sandColor, _Color.rgb * 0.95, Crisp(patchy + (positionWS.y - 1.2) * 0.25, windH.g * 0.3 + grainN.x * 0.1, 0.72) * (1.0 - wet) * 0.9);
                sandColor = lerp(sandColor, float3(0.84, 0.93, 0.98), iceRim * 0.9);
                float3 earth = lerp(bareColor, sandColor, sand);
                float earthy = saturate(max(bare, sand));
                float snowy = (1.0 - rock) * (1.0 - earthy * 0.8);
                // Ice: here and there in the hollows of level ground the wind has swept the snow off a sheet of blue ice
                float icy = Crisp(ValueNoise(positionWS.xz * 0.045 + 61.0) * 0.55 + ValueNoise(positionWS.xz * 0.19 + 7.0) * 0.3 + (0.5 - i.form) * 0.9, grainN.x * 0.03, 0.74)
                    * snowy * (1.0 - path) * smoothstep(0.965, 0.99, ground.y);
                // calmer and rougher stretches, so no two places look the same; packed paths are nearly flat
                float calm = lerp(0.55, 1.15, ValueNoise(positionWS.xz * 0.021 + 13.0));
                float pressed = smoothstep(0.008, 0.03, -trail), bank = smoothstep(0.004, 0.02, trail) * snowy;
                float relief = (1.0 - path * 0.85) * (1.0 - pressed * 0.8) * (1.0 - icy * 0.92) * snowy;
                float2 tilt = _DriftStrength * float2(h0 - hx, h0 - hz)
                    + _WindStrength * calm * (windN.xy + farN.xy * 0.5)
                    + _GrainStrength * grainN.xy * grainFade;
                tilt += grainN.xy * bank * 1.5;      // the heaped banks are loose and lumpy
                float3 n = normalize(ground + float3(tilt.x, 0, tilt.y) * relief + float3(windN.x + grainN.x * grainFade, 0, windN.y + grainN.y * grainFade) * (sand * 0.45)
                    + float3(trackN.x, 0, trackN.y) * (path * snowy) + float3(trailSlope.x, 0, trailSlope.y) * snowy);

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord, positionWS, half4(1, 1, 1, 1));

                // Light, shade, colour, sheen and glints are the island's shared snow (BwmSnowCommon.hlsl): the snow on
                // rocks, trees and roofs is lit by the same functions
                float lit = BwmSnowLight(n, sun.direction, windH.g, relief) * sun.shadowAttenuation;
                float3 ambient = SampleSH(n);
                float3 shade = BwmSnowShade(_ShadeColor.rgb, ambient);
                float3 snowAlbedo = BwmSnowAlbedo(_Color.rgb, positionWS, windH.g, windH.b);
                snowAlbedo = lerp(snowAlbedo, float3(0.56, 0.73, 0.87) * (0.9 + 0.1 * ValueNoise(positionWS.xz * 1.7)), icy * 0.85);
                // trodden snow: packed and a little greyer, each print bluer and darker than the snow round it
                float3 pathAlbedo = _PathColor.rgb * (0.94 + 0.06 * ValueNoise(positionWS.xz * 1.3)) * lerp(float3(0.89, 0.92, 0.98), float3(1.0, 1.0, 1.0), smoothstep(0.15, 0.5, trackCavity));
                float3 snowColor = lerp(lerp(lerp(snowAlbedo, pathAlbedo, path), earth, earthy), rockColor, rock);
                float3 earthShade = earth * _ShadeColor.rgb * (ambient * 1.1 + 0.16);
                shade = lerp(lerp(shade * lerp(1.0, 0.9, path), earthShade, earthy), rockColor * lerp(float3(1, 1, 1), _ShadeColor.rgb, 0.35) * (ambient * 1.1 + 0.16), rock);      // rock in shade stays grey, not snow-blue
                float3 color = lerp(shade, snowColor * sun.color, lit);

                // Hollows between drifts are a touch bluer; so is every dip in the land, and crests catch more light
                color = lerp(color, color * float3(0.90, 0.95, 1.0), saturate(0.55 - h0));
                // tracks: a slid groove is packed smooth and a little greyer; a trench lies in the shade of its own
                // walls, deeper is darker; the banks heaped beside it are fresh loose snow
                float groove = pressed * (1.0 - walked) * snowy;
                color *= lerp(float3(1, 1, 1), float3(0.84, 0.89, 0.97), groove)
                    * lerp(float3(1, 1, 1), float3(0.74, 0.81, 0.95), smoothstep(0.0, 0.09, -trail) * 0.75 * snowy)
                    * (1.0 + 0.07 * bank);
                // a trail is trodden down into the snow: the low wall along each side of it is in shade
                color *= lerp(float3(1, 1, 1), float3(0.80, 0.86, 0.97), path * (1.0 - path) * 4.0 * 0.7 * (1.0 - rock) * (1.0 - earthy));
                float hollow = saturate((0.5 - i.form) * 2.0), crest = saturate((i.form - 0.5) * 2.0);
                color *= lerp(float3(1, 1, 1), float3(0.80, 0.87, 0.98), hollow * (1.0 - rock)) * (1.0 + crest * 0.07);

                // Lamps
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light lamp = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
                        color += snowColor * lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation * saturate(dot(n, lamp.direction) * 0.7 + 0.3));
                    LIGHT_LOOP_END
                }
                #endif

                float snowOnly = (1.0 - path * 0.5) * (1.0 - rock) * (1.0 - earthy);
                color += BwmSnowSheen(n, v, sun.direction, saturate(windH.b + icy * 3.0)) * snowOnly * sun.color * sun.shadowAttenuation;
                color += BwmSnowGlints(positionWS, n, v, sun.direction, viewDistance, _SparkleScale, _SparkleAmount, _SparkleDistance)
                    * (1.0 - path) * (1.0 - rock) * (1.0 - earthy) * (1.0 - icy) * sun.color * sun.shadowAttenuation;

                color += pow(1.0 - saturate(dot(n, v)), 4.0) * _Rim * (ambient + 0.1) * (1.0 - earthy);
                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
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
