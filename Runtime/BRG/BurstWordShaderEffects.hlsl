#ifndef BURSTWORD_SHADER_EFFECTS_INCLUDED
#define BURSTWORD_SHADER_EFFECTS_INCLUDED

// Include this file in a shader compiled with both DOTS_INSTANCING_ON and ordinary
// instancing variants. Implement the two functions below in your shader file.
#define BURSTWORD_CUSTOM_EFFECT 1
#if !defined(DOTS_INSTANCING_ON)
    #define BURST_CLASSIC_INSTANCING 1
#endif
#define Vert BurstWordBaseVert
#define Frag BurstWordBaseFrag
#include "BrgTMPGlyph.hlsl"
#undef Vert
#undef Frag

struct BurstWordEffectVertex
{
    float4 positionCS;
    float2 atlasUV;
    float2 glyphUV;
    half4 color;
    float4 parameters;
    float normalizedAge;
    float elapsedSeconds;
};
struct BurstWordEffectFragment
{
    float4 positionCS;
    float2 atlasUV;
    float2 glyphUV;
    float4 parameters;
    float normalizedAge;
    float elapsedSeconds;
};

void BurstWordModifyVertex(inout BurstWordEffectVertex vertex);
void BurstWordModifyFragment(inout half4 color, BurstWordEffectFragment fragment);

Varyings BurstWordEffectVert(Attributes input)
{
    Varyings output = BurstWordBaseVert(input);
    BurstWordEffectVertex vertex;
    vertex.positionCS = output.positionCS;
    vertex.atlasUV = output.uv;
    vertex.glyphUV = output.effectContext.xy;
    vertex.color = output.tint;
    vertex.parameters = output.effectParameters;
    vertex.normalizedAge = output.effectContext.z;
    vertex.elapsedSeconds = output.effectContext.w;
    BurstWordModifyVertex(vertex);
    output.positionCS = vertex.positionCS;
    output.uv = vertex.atlasUV;
    output.effectContext.xy = vertex.glyphUV;
    output.tint = vertex.color;
    return output;
}

half4 BurstWordEffectFrag(Varyings input) : SV_Target
{
    // Base shading performs SDF / Sprite rendering, TMP effects and opaque-depth
    // rejection before invoking the final pixel hook. Color uses straight alpha.
    half4 color = BurstWordBaseFrag(input);
    BurstWordEffectFragment fragment;
    fragment.positionCS = input.positionCS;
    fragment.atlasUV = input.uv;
    fragment.glyphUV = input.effectContext.xy;
    fragment.parameters = input.effectParameters;
    fragment.normalizedAge = input.effectContext.z;
    fragment.elapsedSeconds = input.effectContext.w;
    BurstWordModifyFragment(color, fragment);
    return color;
}
#endif
