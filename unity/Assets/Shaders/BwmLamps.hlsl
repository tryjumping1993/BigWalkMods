// The Winter Isle's lamps on our own surfaces. Include after URP's Lighting.hlsl.
//
// The game lets a surface drawn forward (all of ours) take four lights at most: the per-object limit of its render
// pipeline asset, and the engine picks the four for the whole object. The island's ground is drawn in district-sized
// tiles, and its cabins, lanterns and platforms as meshes that span the island, so of the lamps near a player each
// such mesh took some four: a cabin stood black beside its own lit porch lamp, a platform under its lamp (2026-11-24).
// So Winter Walk (IsleLamps.cs) hands the eight lamps nearest the camera to our shaders itself, as globals, and a
// surface lights itself from those with BwmLampLight: every lamp within reach, however large the mesh.
//
// The real lights IsleLamps sets on the same lamps are for everything else (the game's rocks, plants, puzzle parts
// and players). BwmIsLampLight tells one of those in a surface's own list of lights from any other light (a torch, a
// room's lamp, the night-vision light), so that a lamp is not counted twice.
#ifndef BWM_LAMPS_INCLUDED
#define BWM_LAMPS_INCLUDED

float4 _BwmLampPos0, _BwmLampPos1, _BwmLampPos2, _BwmLampPos3, _BwmLampPos4, _BwmLampPos5, _BwmLampPos6, _BwmLampPos7;      // xyz: where (world); w: 1 / range squared
float4 _BwmLampCol0, _BwmLampCol1, _BwmLampCol2, _BwmLampCol3, _BwmLampCol4, _BwmLampCol5, _BwmLampCol6, _BwmLampCol7;      // rgb: colour times strength
float _BwmLampCount;       // how many of the eight are lit (0 by day)
float4 _BwmLampReach;      // x: how far from the camera lamps are lit this way (m), 0 when none are; y: the fade's length (m)

float3 BwmOneLamp(float4 at, float4 colour, float3 positionWS, float3 n, float wrap)
{
    float3 to = at.xyz - positionWS;
    float d2 = dot(to, to);
    float fade = d2 * at.w;                                   // (distance / range) squared
    float window = saturate(1.0 - fade * fade);               // URP's own falloff to nothing at the range
    float facing = saturate(dot(n, to * rsqrt(max(d2, 1e-4))) * (1.0 - wrap) + wrap);
    // (a lamp is a glass, not a point: nothing counts as nearer than 0.7 m, or the wall behind a porch lamp burns out)
    return colour.rgb * (window * window / max(d2, 0.5) * facing);
}

// The light of the island's lamps on a surface at positionWS facing n; 'wrap' is how far round the light reaches
// (0.3: a face turned square away still takes a little, as snow does)
float3 BwmLampLight(float3 positionWS, float3 n, float wrap)
{
    float3 sum = 0.0;
    if (_BwmLampCount > 0.5) sum += BwmOneLamp(_BwmLampPos0, _BwmLampCol0, positionWS, n, wrap);
    if (_BwmLampCount > 1.5) sum += BwmOneLamp(_BwmLampPos1, _BwmLampCol1, positionWS, n, wrap);
    if (_BwmLampCount > 2.5) sum += BwmOneLamp(_BwmLampPos2, _BwmLampCol2, positionWS, n, wrap);
    if (_BwmLampCount > 3.5) sum += BwmOneLamp(_BwmLampPos3, _BwmLampCol3, positionWS, n, wrap);
    if (_BwmLampCount > 4.5) sum += BwmOneLamp(_BwmLampPos4, _BwmLampCol4, positionWS, n, wrap);
    if (_BwmLampCount > 5.5) sum += BwmOneLamp(_BwmLampPos5, _BwmLampCol5, positionWS, n, wrap);
    if (_BwmLampCount > 6.5) sum += BwmOneLamp(_BwmLampPos6, _BwmLampCol6, positionWS, n, wrap);
    if (_BwmLampCount > 7.5) sum += BwmOneLamp(_BwmLampPos7, _BwmLampCol7, positionWS, n, wrap);
    return sum;
}

bool BwmLampAt(float3 at, float4 lamp) { float3 d = at - lamp.xyz; return dot(d, d) < 0.0025; }

// Whether the light a LIGHT_LOOP hands a surface as 'lightIndex' is the real light on one of those lamps
bool BwmIsLampLight(uint lightIndex)
{
    if (_BwmLampCount < 0.5) return false;
#if USE_CLUSTER_LIGHT_LOOP
    int visible = lightIndex;
#else
    int visible = GetPerObjectLightIndex(lightIndex);
#endif
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 at = _AdditionalLightsBuffer[visible].position;
#else
    float4 at = _AdditionalLightsPosition[visible];
#endif
    if (at.w < 0.5) return false;      // a directional light
    return BwmLampAt(at.xyz, _BwmLampPos0) || BwmLampAt(at.xyz, _BwmLampPos1) || BwmLampAt(at.xyz, _BwmLampPos2) || BwmLampAt(at.xyz, _BwmLampPos3)
        || BwmLampAt(at.xyz, _BwmLampPos4) || BwmLampAt(at.xyz, _BwmLampPos5) || BwmLampAt(at.xyz, _BwmLampPos6) || BwmLampAt(at.xyz, _BwmLampPos7);
}

#endif
