// Include before URP's Lighting.hlsl: our shaders always take URP's cascaded sun-shadow path with plain sampling
// (why: BwmSunShadow.hlsl, which is included after Lighting.hlsl). Unity includes a file only once per pass, so the
// keyword and the function live in two files.
#ifndef BWM_SUN_SHADOW_KEYWORDS
#define BWM_SUN_SHADOW_KEYWORDS
#define _MAIN_LIGHT_SHADOWS_CASCADE 1
#endif
