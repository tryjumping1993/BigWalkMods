// The light of the Winter Isle's Christmas lights on our own surfaces. Include after BwmLamps.hlsl.
//
// The bulbs themselves are only drawn bright (BWM/TinyLight on the island's stakes, BWM/FairyLight on the strings
// between posts). What they light comes from here: Winter Walk (IsleGlow.cs) hands the bulbs and stretches of string
// nearest the camera to our shaders as globals, up to 24 of them, and a surface lights itself from those. Each is a
// small light with a real light's falloff (the user, 2026-11-24: "a natural amount of illumination"):
//  - a bulb is a point: its light is its power over the distance squared, so a bulb 0.8 m up its stake lights a pool
//    of snow about a metre across and nothing much beyond two;
//  - a stretch of string is a line of bulbs from A to A + B: at distance d from it, its power over
//    d * sqrt(d * d + (length / 2) squared), which is 1 / d close under the string (a soft even band) and the same
//    1 / d squared as a point from far off. (That is the exact light of a line on a surface facing its middle.)
// Both fade to nothing at their range, as the engine's lights do. No shadows: the light of a string passes a wall.
//
// Beyond the nearest 24 the ground shows the same light from the island's baked map (BwmSnow.shader: _GlowMap, faded
// in by _BwmGlowReach as these fade out), so the far hillside and the snow at your feet are lit by one rule.
//
// Winter Walk also sets real point lights on the nearest strings, for the game's own surfaces (players, puzzle
// parts), which are lit deferred. BwmIsGlowLight tells those in a surface's own list of lights, so that a string is
// not counted twice: they all have one odd range, which no other light has.
#ifndef BWM_GLOW_INCLUDED
#define BWM_GLOW_INCLUDED

#define BWM_GLOW_MOST 24
float4 _BwmGlowA[BWM_GLOW_MOST];      // xyz: the bulb, or one end of the stretch (world); w: 1 / range squared
float4 _BwmGlowB[BWM_GLOW_MOST];      // xyz: from that end to the other (zero for a bulb); w: 1 / length squared (0 for a bulb)
float4 _BwmGlowC[BWM_GLOW_MOST];      // rgb: colour times power; a: half the length, squared
float _BwmGlowCount;                  // how many are set (0 by day)
float4 _BwmGlowReach;                 // x: how far from the camera they are lit this way (m), 0 when none are; y: the fade's length (m)
float4 _BwmGlowReal;                  // x: 1 / range squared of the real lights on the strings (0: none)

// The light on a surface at positionWS facing n; 'wrap' is how far round the light reaches (as BwmLampLight)
float3 BwmGlowLight(float3 positionWS, float3 n, float wrap)
{
    float3 sum = 0.0;
    int count = (int)min(_BwmGlowCount + 0.5, (float)BWM_GLOW_MOST);
    for (int k = 0; k < count; k++)
    {
        float4 a = _BwmGlowA[k], ab = _BwmGlowB[k], c = _BwmGlowC[k];
        float3 to = a.xyz + ab.xyz * saturate(dot(positionWS - a.xyz, ab.xyz) * ab.w) - positionWS;      // to the nearest point of the stretch
        float d2 = dot(to, to);
        float fade = d2 * a.w;                                    // (distance / range) squared
        if (fade < 1.0)                                           // (most of the 24 are out of a given point's range)
        {
            float window = 1.0 - fade * fade;
            float facing = saturate(dot(n, to) * rsqrt(max(d2, 1e-4)) * (1.0 - wrap) + wrap);
            d2 = max(d2, 0.3);                                    // (a bulb is a glass, not a point: nothing counts as nearer than half a metre)
            sum += c.rgb * (window * window * facing * rsqrt(d2 * (d2 + c.a)));
        }
    }
    return sum;
}

// Whether the light a LIGHT_LOOP hands a surface as 'lightIndex' is one of the real lights on the strings
bool BwmIsGlowLight(uint lightIndex)
{
    if (_BwmGlowReal.x <= 0.0) return false;
#if USE_CLUSTER_LIGHT_LOOP
    int visible = lightIndex;
#else
    int visible = GetPerObjectLightIndex(lightIndex);
#endif
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 at = _AdditionalLightsBuffer[visible].position;
    float4 attenuation = _AdditionalLightsBuffer[visible].attenuation;
#else
    float4 at = _AdditionalLightsPosition[visible];
    float4 attenuation = _AdditionalLightsAttenuation[visible];
#endif
    if (at.w < 0.5) return false;      // a directional light
    return abs(attenuation.x - _BwmGlowReal.x) < _BwmGlowReal.x * 0.0005;
}

#endif
