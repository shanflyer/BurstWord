#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

TEXTURE2D(_BurstAtlas0);
TEXTURE2D(_BurstAtlas1);
TEXTURE2D(_BurstAtlas2);
TEXTURE2D(_BurstAtlas3);
TEXTURE2D(_BurstAtlas4);
TEXTURE2D(_BurstAtlas5);
TEXTURE2D(_BurstAtlas6);
TEXTURE2D(_BurstAtlas7);
TEXTURE2D(_BurstAtlas8);
TEXTURE2D(_BurstAtlas9);
TEXTURE2D(_BurstAtlas10);
TEXTURE2D(_BurstAtlas11);
TEXTURE2D(_BurstAtlas12);
TEXTURE2D(_BurstAtlas13);
TEXTURE2D(_BurstAtlas14);
TEXTURE2D(_BurstAtlas15);
SAMPLER(sampler_linear_clamp);
ByteAddressBuffer _BurstLabels;
StructuredBuffer<float4> _BurstResources;
StructuredBuffer<float4> _BurstAnimationLabels;
TEXTURE2D(_BurstAnimationCurves);
CBUFFER_START(UnityPerMaterial)
    float _BurstTime;
    float4 _BurstScreen;
    float4 _BurstAnimationInfo;
    float _BurstZTest, _BurstLabelCapacity, _BurstSortingMode, _BurstPlainSdfFastPath;
CBUFFER_END
half4 SampleAtlas(uint atlas, float2 uv)
{
    // Explicit bindings preserve the original texture resolution and Alpha8/RGBA formats.
    // No texture copies, resampling or giant RGBA texture arrays.
    [branch] switch(atlas)
    {
        case 0: return SAMPLE_TEXTURE2D(_BurstAtlas0, sampler_linear_clamp, uv);
        case 1: return SAMPLE_TEXTURE2D(_BurstAtlas1, sampler_linear_clamp, uv);
        case 2: return SAMPLE_TEXTURE2D(_BurstAtlas2, sampler_linear_clamp, uv);
        case 3: return SAMPLE_TEXTURE2D(_BurstAtlas3, sampler_linear_clamp, uv);
        case 4: return SAMPLE_TEXTURE2D(_BurstAtlas4, sampler_linear_clamp, uv);
        case 5: return SAMPLE_TEXTURE2D(_BurstAtlas5, sampler_linear_clamp, uv);
        case 6: return SAMPLE_TEXTURE2D(_BurstAtlas6, sampler_linear_clamp, uv);
        case 7: return SAMPLE_TEXTURE2D(_BurstAtlas7, sampler_linear_clamp, uv);
        case 8: return SAMPLE_TEXTURE2D(_BurstAtlas8, sampler_linear_clamp, uv);
        case 9: return SAMPLE_TEXTURE2D(_BurstAtlas9, sampler_linear_clamp, uv);
        case 10: return SAMPLE_TEXTURE2D(_BurstAtlas10, sampler_linear_clamp, uv);
        case 11: return SAMPLE_TEXTURE2D(_BurstAtlas11, sampler_linear_clamp, uv);
        case 12: return SAMPLE_TEXTURE2D(_BurstAtlas12, sampler_linear_clamp, uv);
        case 13: return SAMPLE_TEXTURE2D(_BurstAtlas13, sampler_linear_clamp, uv);
        case 14: return SAMPLE_TEXTURE2D(_BurstAtlas14, sampler_linear_clamp, uv);
        default: return SAMPLE_TEXTURE2D(_BurstAtlas15, sampler_linear_clamp, uv);
    }
}
#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _WorldBirth)
    UNITY_DOTS_INSTANCED_PROP(float4, _GlyphRect)
    UNITY_DOTS_INSTANCED_PROP(float4, _AtlasRect)
    UNITY_DOTS_INSTANCED_PROP(float4, _LifeMotion)
    UNITY_DOTS_INSTANCED_PROP(float4, _Tint)
    UNITY_DOTS_INSTANCED_PROP(float4, _GlyphStyle)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
#endif

struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct Varyings
{
    float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 tint : COLOR;
    float weight : TEXCOORD1; nointerpolation uint resource : TEXCOORD2;
    nointerpolation float4 plainFace : TEXCOORD3;
    nointerpolation float plainThreshold : TEXCOORD4;
    nointerpolation uint plainAtlas : TEXCOORD5;
};
Varyings Vert(Attributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    Varyings output;
    #ifdef UNITY_DOTS_INSTANCING_ENABLED
        float4 anchor = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _WorldBirth);
        float4 rect = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _GlyphRect);
        float4 atlas = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _AtlasRect);
        float4 motion = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _LifeMotion);
        float4 tint = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _Tint);
        float4 style = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _GlyphStyle);
    #else
        float4 anchor = 0, rect = 0, atlas = 0, motion = float4(1,0,0,0), tint = 0;
        float4 style = 0;
    #endif
    float t = saturate((_BurstTime - anchor.w) / max(motion.x, 0.001));
    float2 local = rect.xy + input.uv * rect.zw;
    local.x += style.y * (style.z + input.uv.y * rect.w);
    output.resource = (uint)anchor.x;
    uint label = (uint)max(0, style.w - 1);
    float alpha = 1 - smoothstep(0.5, 1, t);
    [branch] if (_BurstAnimationInfo.x > 0.5)
    {
        float4 animation = _BurstAnimationLabels[label];
        [branch] if (animation.x > 0.5)
        {
            float x = (t * (_BurstAnimationInfo.y - 1) + 0.5) / _BurstAnimationInfo.y;
            float row = (animation.x - 1) * 3;
            float4 geometry = SAMPLE_TEXTURE2D_LOD(_BurstAnimationCurves, sampler_linear_clamp, float2(x, (row + 0.5) / _BurstAnimationInfo.z), 0);
            float4 effects = SAMPLE_TEXTURE2D_LOD(_BurstAnimationCurves, sampler_linear_clamp, float2(x, (row + 1.5) / _BurstAnimationInfo.z), 0);
            float4 color = SAMPLE_TEXTURE2D_LOD(_BurstAnimationCurves, sampler_linear_clamp, float2(x, (row + 2.5) / _BurstAnimationInfo.z), 0);
            local *= geometry.zw;
            float sine, cosine; sincos(effects.x, sine, cosine);
            local = float2(cosine * local.x - sine * local.y, sine * local.x + cosine * local.y);
            local += geometry.xy * animation.y + float2(motion.z * t * effects.w, 0);
            tint.rgb *= color.rgb * effects.z;
            alpha = effects.y * color.a;
        }
        else local += float2(motion.z, motion.y) * t;
    }
    else local += float2(motion.z, motion.y) * t;
    anchor.xyz = asfloat(_BurstLabels.Load3(64 + label * 16));
    if (motion.w > 1.5)
    {
        uint block = (uint)_BurstLabelCapacity * 16;
        float3 right = asfloat(_BurstLabels.Load3(64 + block + label * 16));
        float3 up = asfloat(_BurstLabels.Load3(64 + block * 2 + label * 16));
        output.positionCS = TransformWorldToHClip(anchor.xyz + right * local.x + up * local.y);
    }
    else
    {
        float2 pixel = local * _BurstScreen.z;
        output.positionCS = TransformWorldToHClip(anchor.xyz);
        pixel.y *= _ProjectionParams.x;
        output.positionCS.xy += pixel * 2 / max(_BurstScreen.xy, 1) * output.positionCS.w;
    }
    float positiveW = step(0.0001, output.positionCS.w);
    output.uv = atlas.xy + input.uv * atlas.zw;
    output.tint = tint;
    output.weight = style.x;
    output.tint.a *= positiveW * alpha;
    output.plainFace = 0; output.plainThreshold = 0; output.plainAtlas = 16u;
    [branch] if (_BurstPlainSdfFastPath > 0.5)
    {
        uint baseIndex = output.resource * 10;
        float4 settings = _BurstResources[baseIndex + 4], face = _BurstResources[baseIndex + 5];
        float4 ratios = _BurstResources[baseIndex + 7], extra = _BurstResources[baseIndex + 9];
        [branch] if (settings.y <= 0.5 && face.y <= 0 && ratios.z <= 0.5 && extra.x <= 0.5)
        {
            output.plainFace = _BurstResources[baseIndex]; output.plainAtlas = (uint)settings.z;
            output.plainThreshold = 0.5 - settings.x - style.x * face.w - face.x * face.w * 0.5;
        }
    }
    return output;
}
half4 Frag(Varyings input) : SV_Target
{
    if (_BurstSortingMode > .5 && _BurstSortingMode < 1.5)
    {
        float opaqueDepth = SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
        #if UNITY_REVERSED_Z
            clip(input.positionCS.z - opaqueDepth + 0.0000001);
        #else
            clip(opaqueDepth - input.positionCS.z + 0.0000001);
        #endif
    }
    [branch] if (input.plainAtlas < 16u)
    {
        float distance = SampleAtlas(input.plainAtlas, input.uv).a;
        float edge = max(fwidth(distance), 0.001);
        float face = saturate((distance - input.plainThreshold) / edge + 0.5) * input.plainFace.a;
        return half4(input.plainFace.rgb * input.tint.rgb * face / max(face, 0.0001), face * input.tint.a);
    }
    uint baseIndex = input.resource * 10;
    float4 _FaceColor = _BurstResources[baseIndex];
    float4 settings = _BurstResources[baseIndex+4], faceSettings = _BurstResources[baseIndex+5];
    float4 ratios = _BurstResources[baseIndex+7], extra = _BurstResources[baseIndex+9];
    float _Weight=settings.x, _AtlasMode=settings.y, _GradientScale=settings.w;
    uint atlasIndex=(uint)settings.z;
    float _FaceDilate=faceSettings.x, _OutlineWidth=faceSettings.y, _OutlineSoftness=faceSettings.z, _ScaleRatioA=faceSettings.w;
    float _ScaleRatioB=ratios.x, _ScaleRatioC=ratios.y, _UseUnderlay=ratios.z, _InnerUnderlay=ratios.w;
    float _UseGlow=extra.x;
    float2 texelSize=extra.yz;
    half4 sample = SampleAtlas(atlasIndex,input.uv);
    if (_AtlasMode > 1.5) return sample * input.tint;
    if (_AtlasMode > 0.5) return half4(input.tint.rgb * _FaceColor.rgb, sample.a * input.tint.a * _FaceColor.a);
    float distance = sample.a;
    float edge = max(fwidth(distance), 0.001);
    float threshold = 0.5 - _Weight - input.weight * _ScaleRatioA - _FaceDilate * _ScaleRatioA * 0.5;
    float softness = max(edge, _OutlineSoftness * _ScaleRatioA * 0.5);
    float coverage = saturate((distance - threshold) / edge + 0.5);
    float face = coverage * _FaceColor.a;
    // Ordinary SDF text does not need effect colors, shadow metrics or glow math.
    // Keep the same unpremultiplication, including the small-alpha cutoff.
    [branch] if (_BurstPlainSdfFastPath > 0.5 && _OutlineWidth <= 0 && _UseUnderlay <= 0.5 && _UseGlow <= 0.5)
        return half4(_FaceColor.rgb * input.tint.rgb * face / max(face, 0.0001), face * input.tint.a);
    float4 _OutlineColor = _BurstResources[baseIndex+1];
    float4 _UnderlayColor = _BurstResources[baseIndex+2], _GlowColor = _BurstResources[baseIndex+3];
    float4 shadowSettings = _BurstResources[baseIndex+6], glowSettings = _BurstResources[baseIndex+8];
    float _UnderlayOffsetX=shadowSettings.x, _UnderlayOffsetY=shadowSettings.y, _UnderlayDilate=shadowSettings.z, _UnderlaySoftness=shadowSettings.w;
    float _GlowOffset=glowSettings.x, _GlowInner=glowSettings.y, _GlowOuter=glowSettings.z, _GlowPower=glowSettings.w;
    float outline = saturate((distance - threshold + _OutlineWidth * _ScaleRatioA * 0.5) / softness + 0.5);
    outline = _OutlineWidth > 0 ? max(0, outline - coverage) : 0;
    float alpha = face + (1 - face) * outline * _OutlineColor.a;
    float3 rgb = _FaceColor.rgb * input.tint.rgb * face + _OutlineColor.rgb * (1 - face) * outline * _OutlineColor.a;
    if (_UseUnderlay > 0.5)
    {
        float2 offset = float2(-_UnderlayOffsetX, -_UnderlayOffsetY) * _GradientScale * _ScaleRatioC * texelSize;
        float shadowDistance = SampleAtlas(atlasIndex,input.uv + offset).a;
        float shadow = saturate((shadowDistance - threshold + _UnderlayDilate * _ScaleRatioC * 0.5) / max(edge, _UnderlaySoftness * _ScaleRatioC * 0.5) + 0.5);
        if (_InnerUnderlay > 0.5)
        {
            float inner = (1 - shadow) * face * _UnderlayColor.a;
            rgb = lerp(rgb, _UnderlayColor.rgb * alpha, inner);
        }
        else
        {
            shadow *= (1 - alpha) * _UnderlayColor.a;
            rgb += _UnderlayColor.rgb * shadow; alpha += shadow;
        }
    }
    if (_UseGlow > 0.5)
    {
        float glowDistance = abs(distance - threshold + _GlowOffset * _ScaleRatioB * 0.5);
        float radius = max(edge, (_GlowOuter + _GlowInner) * _ScaleRatioB * 0.5);
        float glow = pow(saturate(1 - glowDistance / radius), max(0.01, _GlowPower)) * _GlowColor.a * (1 - alpha);
        rgb += _GlowColor.rgb * glow; alpha += glow;
    }
    return half4(rgb / max(alpha, 0.0001), alpha * input.tint.a);
}
