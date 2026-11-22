// The one snow of the Winter Isle. Every shader that draws snow (the ground, the caps on the game's rocks, our
// trees, props and walls) lights it with these functions and takes its surface from the same generated maps
// (tools/snowtex.py), so a drift on a bough matches the drift under it.
//
// Include it inside a pass, after URP's Lighting.hlsl.
//
// The ground and the rock caps carry the maps on their own materials. Everything else reads them as globals,
// which the Winter Walk plugin sets from the island's materials once the island is loaded (_BwmSnowMaps is 1
// from then on); until then, and in the editor, such snow is drawn smooth.
#ifndef BWM_SNOW_COMMON_INCLUDED
#define BWM_SNOW_COMMON_INCLUDED

TEXTURE2D(_BwmWindNormal); SAMPLER(sampler_BwmWindNormal);
TEXTURE2D(_BwmWindHeight); SAMPLER(sampler_BwmWindHeight);
TEXTURE2D(_BwmGrainNormal); SAMPLER(sampler_BwmGrainNormal);
float _BwmSnowMaps;

float BwmHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float3 BwmHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

float BwmValueNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = BwmHash21(i), b = BwmHash21(i + float2(1, 0)), c = BwmHash21(i + float2(0, 1)), d = BwmHash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// What the maps say about the snow's surface at a point
struct BwmSnowDetail
{
    float2 tilt;      // how the surface leans, to add to the normal's x and z
    float cavity;     // 0 in a trough between the wind's ridges, 0.5 level, 1 on a crest
    float flats;      // 1 on scoured, rippled wind crust
};

// The surface of snow lying on something that is not the ground: the same maps laid from above at a smaller
// size (a bough or a cap is a metre or two across), shifted with height so stacked boughs do not repeat
BwmSnowDetail BwmSnowDetailOn(float3 positionWS, float viewDistance, float tile)
{
    float2 uv = (positionWS.xz + positionWS.y * float2(0.37, 0.61)) / tile;
    float3 windN = UnpackNormal(SAMPLE_TEXTURE2D(_BwmWindNormal, sampler_BwmWindNormal, uv));
    float3 windH = SAMPLE_TEXTURE2D(_BwmWindHeight, sampler_BwmWindHeight, uv).rgb;
    // (the crust map spans 3.2 m: tools/snowtex.py)
    float3 grainN = UnpackNormal(SAMPLE_TEXTURE2D(_BwmGrainNormal, sampler_BwmGrainNormal, uv * (tile / 3.2)));
    BwmSnowDetail d;
    d.tilt = (windN.xy + grainN.xy * saturate(1.0 - viewDistance / 10.0)) * _BwmSnowMaps;
    d.cavity = lerp(0.5, windH.g, _BwmSnowMaps);
    d.flats = windH.b * _BwmSnowMaps;
    return d;
}

// Snow is not one white: faintly bluer and greyer crusts, warmer fresh patches; the troughs between the wind's
// ridges a little bluer and darker, their crests brightest; scoured flats a touch greyer than fresh snow
float3 BwmSnowAlbedo(float3 snowColor, float3 positionWS, float cavity, float flats)
{
    float crust = BwmValueNoise(positionWS.xz * 0.07 + 3.0) * 0.6 + BwmValueNoise(positionWS.xz * 0.35 + 9.0) * 0.4;
    float3 albedo = snowColor * lerp(float3(0.9, 0.93, 0.98), float3(1.0, 1.0, 1.0), smoothstep(0.25, 0.75, crust));
    return albedo * lerp(float3(0.86, 0.91, 0.99), float3(1.0, 1.0, 1.0), smoothstep(0.2, 0.6, cavity)) * (1.0 - 0.04 * flats);
}

// Wrapped diffuse keeps slopes facing away from the sun soft; a low sun leaves the troughs in shade
float BwmSnowLight(float3 n, float3 sunDirection, float cavity, float relief)
{
    float lit = saturate(dot(n, sunDirection) * 0.82 + 0.18);
    return lit * lerp(1.0, smoothstep(0.1, 0.5, cavity), (1.0 - saturate(sunDirection.y * 1.5)) * 0.5 * relief);
}

// Unlit snow goes blue instead of black, as it does under an open sky
float3 BwmSnowShade(float3 shadeColor, float3 ambient)
{
    return shadeColor * (ambient * 1.1 + 0.18);
}

// A soft sheen where the surface mirrors the sun, strongest on wind crust
float BwmSnowSheen(float3 n, float3 v, float3 sunDirection, float flats)
{
    return pow(saturate(dot(n, normalize(sunDirection + v))), 48.0) * (0.05 + 0.15 * flats);
}

// Glints: each small cell of the surface has a random facet that flashes when it mirrors the sun. Near the eye
// only, so they never shimmer far away.
float BwmSnowGlints(float3 positionWS, float3 n, float3 v, float3 sunDirection, float viewDistance, float scale, float amount, float reach)
{
    float fade = saturate(1.0 - viewDistance / reach);
    if (fade <= 0.0) return 0.0;
    float3 scaled = positionWS * scale;
    float3 random = BwmHash33(floor(scaled));
    float3 facet = normalize(n + (random * 2.0 - 1.0) * 0.75);
    float glint = pow(saturate(dot(reflect(-sunDirection, facet), v)), 90.0);
    // a round grain in the middle of the cell, and only some cells hold one
    float grain = smoothstep(0.42, 0.12, length(frac(scaled) - 0.5)) * step(0.55, random.x);
    return glint * grain * amount * fade * fade;
}

// Lit snow lying on something (not the ground, which mixes in paths, rock and earth as it goes): colour from
// the surface's own normal n, with the maps' detail added by how much it faces up. occlusion darkens the
// shade where the snow is tucked inside something (a fir's crown).
float3 BwmSnowOn(float3 snowColor, float3 shadeColor, float3 positionWS, float3 n, float3 v, float viewDistance,
    BwmSnowDetail detail, float3 sunDirection, float3 sunColor, float sunShadow, float occlusion)
{
    float up = saturate(n.y);
    float3 ns = normalize(n + float3(detail.tilt.x, 0, detail.tilt.y) * up);
    float3 albedo = BwmSnowAlbedo(snowColor, positionWS, detail.cavity, detail.flats);
    float lit = BwmSnowLight(ns, sunDirection, detail.cavity, up) * sunShadow;
    float3 ambient = SampleSH(ns);
    float3 color = BwmSnowShade(shadeColor, ambient) * occlusion + albedo * sunColor * lit;      // sunlight adds to the sky's light
    color += (BwmSnowSheen(ns, v, sunDirection, detail.flats) + BwmSnowGlints(positionWS, ns, v, sunDirection, viewDistance, 70.0, 2.4, 22.0)) * up * sunColor * sunShadow;
    color += pow(1.0 - saturate(dot(ns, v)), 4.0) * 0.18 * (ambient + 0.1) * occlusion;
    return color;
}

#endif
