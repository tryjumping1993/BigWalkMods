// What our solid shaders write in the game's GBuffer pass (the "GBufferDepth" pass of each).
//
// The game renders deferred, and our lit passes are forward (UniversalForwardOnly), drawn after it. This pass puts our
// surfaces into the GBuffer so that what the game works out from it before our forward pass draws is about OUR
// surface, not about whatever lies behind it:
//   - depth (the depth buffer itself): fog, the fog box, anything that asks how far a pixel is;
//   - the normal (target 2) and a plain albedo (target 0): the game's ambient occlusion (HBAO) reads the GBuffer's
//     normals. Until 2026-11-24 this pass wrote depth only (ColorMask 0 on every target), so the normals under our
//     pixels were those of the game's own things behind them, and the occlusion drew their outlines on our walls and
//     snow: players, puzzle parts and the mine's tunnels showed faintly through the ground ("can still see through
//     the snow", players, with several of them in a walk).
// Target 3 is the camera's colour: left alone (ColorMask 0 3 in the pass), the forward pass draws it.
//
// The normal's packing follows the game's renderer ("accurate G-buffer normals" packs it into an octahedron): Winter
// Walk sets _BwmGBufferOct from the renderer in use (Plugin.cs, SunShadows). A keyword would need the variant in the
// pack, which our pipeline asset does not keep.
#ifndef BWM_GBUFFER_INCLUDED
#define BWM_GBUFFER_INCLUDED

float _BwmGBufferOct;

struct BwmGBufferAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct BwmGBufferVaryings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD7; };
struct BwmGBufferOut { half4 g0 : SV_Target0; half4 g1 : SV_Target1; float4 g2 : SV_Target2; };

BwmGBufferVaryings BwmGBufferVert(BwmGBufferAttributes v)
{
    BwmGBufferVaryings o;
    UNITY_SETUP_INSTANCE_ID(v);
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    return o;
}

BwmGBufferOut BwmGBuffer(float3 normalWS, bool front)
{
    float3 n = normalize(normalWS);
    if (!front) n = -n;
    BwmGBufferOut o;
    o.g0 = half4(0.8, 0.8, 0.8, 0);      // albedo, no material flags
    o.g1 = half4(0, 0, 0, 1);            // no specular, no occlusion
    if (_BwmGBufferOct > 0.5)
    {
        float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
        o.g2 = float4(PackFloat2To888(oct), 0);
    }
    else o.g2 = float4(n, 0);            // smoothness 0
    return o;
}
#endif
