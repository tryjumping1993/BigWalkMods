// The sun's shadow for our shaders, the same at every one of the game's quality settings.
//
// Why not URP's shadow keywords: a pack carries only the shader variants our build's pipeline asset keeps (4 cascades,
// soft shadows at one quality), while the game switches between no sun shadows (Very Low, Low), soft Low, and soft
// High. Missing a variant, the game ran the nearest one it had, which took everything outside the shadow map (all of
// it on Low, everything past 50-100 m above) as in shade: the snow was blue except for a sunlit, cream circle round
// the player at Medium and up, which read as seeing through the snow (2026-11-21).
//
// So these shaders always compile URP's cascaded path with plain (hard) sampling, which reads any of the game's
// settings correctly, and Winter Walk says whether the quality level in use draws sun shadows at all
// (_BwmSunShadows: 1 yes, 0 no; unset, as in the editor, is no). Past the shadow map URP's own cascade code gives
// full sun, and the shadow fades out towards the shadow distance.
//
// In a lit pass: #include "BwmSunShadowKeywords.hlsl" before URP's Lighting.hlsl, and this file after it.
#ifndef BWM_SUN_SHADOW_INCLUDED
#define BWM_SUN_SHADOW_INCLUDED
float _BwmSunShadows;

// The sun at a point, with its shadow there (1 in full sun)
Light BwmSun(float3 positionWS)
{
    Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, half4(1, 1, 1, 1));
    sun.shadowAttenuation = lerp(1.0, sun.shadowAttenuation, saturate(_BwmSunShadows));
    return sun;
}
#endif
