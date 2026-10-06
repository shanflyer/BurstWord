Shader "BurstWord/Instanced TMP Glyph"
{
    Properties
    {
        [HideInInspector] _BurstZTest("Depth test", Float) = 8
        [HideInInspector] _BurstSortingMode("Sorting mode", Float) = 0
        [HideInInspector] _BurstPlainSdfFastPath("Plain SDF fast path", Float) = 1
        [HideInInspector] _BurstLabelCapacity("Label capacity", Float) = 1
        [HideInInspector] _BurstResource0("Resource 0", Vector) = (0,0,0,0)
        [HideInInspector] _BurstResource1("Resource 1", Vector) = (0,0,0,0)
        [HideInInspector] _BurstResource2("Resource 2", Vector) = (0,0,0,0)
        [HideInInspector] _BurstResource3("Resource 3", Vector) = (0,0,0,0)
        [HideInInspector] _BurstResource4("Resource 4", Vector) = (0,0,0,0)
        [HideInInspector] _BurstResource5("Resource 5", Vector) = (0,0,0,0)
        [HideInInspector] _BurstResource6("Resource 6", Vector) = (0,0,0,0)
        [HideInInspector] _BurstResource7("Resource 7", Vector) = (0,0,0,0)
        [HideInInspector] _BurstResource8("Resource 8", Vector) = (0,0,0,0)
        [HideInInspector] _BurstResource9("Resource 9", Vector) = (0,0,0,0)
        [HideInInspector] _BurstPoseAnchor("_BurstPoseAnchor", Vector) = (0,0,0,0)
        [HideInInspector] _BurstPoseRight("_BurstPoseRight", Vector) = (0,0,0,0)
        [HideInInspector] _BurstPoseUp("_BurstPoseUp", Vector) = (0,0,0,0)
        [HideInInspector] _BurstAnimationLabel("_BurstAnimationLabel", Vector) = (0,0,0,0)
        _MainTex("TMP SDF Atlas", 2D) = "white" {}
        _Weight("SDF normal weight", Float) = 0
        _AtlasMode("Atlas mode: SDF / bitmap / RGBA", Float) = 0
        _FaceColor("Face Color", Color) = (1,1,1,1)
        _FaceDilate("Face Dilate", Range(-1,1)) = 0
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth("Outline Width", Range(0,1)) = 0
        _OutlineSoftness("Outline Softness", Range(0,1)) = 0
        _UnderlayColor("Underlay Color", Color) = (0,0,0,0.5)
        _UnderlayOffsetX("Underlay Offset X", Float) = 0
        _UnderlayOffsetY("Underlay Offset Y", Float) = 0
        _UnderlayDilate("Underlay Dilate", Float) = 0
        _UnderlaySoftness("Underlay Softness", Float) = 0
        _GlowColor("Glow Color", Color) = (0,1,0,0.5)
        _GlowOffset("Glow Offset", Float) = 0
        _GlowInner("Glow Inner", Float) = 0
        _GlowOuter("Glow Outer", Float) = 0
        _GlowPower("Glow Power", Float) = 0.75
        _GradientScale("Gradient Scale", Float) = 10
        _ScaleRatioA("Scale Ratio A", Float) = 1
        _ScaleRatioB("Scale Ratio B", Float) = 1
        _ScaleRatioC("Scale Ratio C", Float) = 1
        _WeightNormal("Normal Weight", Float) = 0
        _WeightBold("Bold Weight", Float) = 0.75
        [HideInInspector] _UseUnderlay("Use Underlay", Float) = 0
        [HideInInspector] _InnerUnderlay("Inner Underlay", Float) = 0
        [HideInInspector] _UseGlow("Use Glow", Float) = 0
        [HideInInspector] _BurstTime("Animation time", Float) = 0
        [HideInInspector] _BurstAnimationInfo("Animation table dimensions", Vector) = (0,256,1,0)
        [HideInInspector] _BurstScreen("Viewport and Canvas scale", Vector) = (1920,1080,1,0)
        [HideInInspector] _WorldBirth("World anchor and birth", Vector) = (0,0,0,0)
        [HideInInspector] _GlyphRect("Glyph rectangle", Vector) = (0,0,0,0)
        [HideInInspector] _AtlasRect("Atlas rectangle", Vector) = (0,0,0,0)
        [HideInInspector] _LifeMotion("Life and motion", Vector) = (1,0,0,0)
        [HideInInspector] _Tint("Tint", Color) = (1,1,1,1)
        [HideInInspector] _GlyphStyle("Weight, shear and baseline", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Overlay" }
        Cull Off
        ZWrite Off
        ZTest [_BurstZTest]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "Scene"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma instancing_options forcemaxcount:64
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #define BURST_CLASSIC_INSTANCING 1
            #include "BrgTMPGlyph.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "OrderedOverlay"
            Tags { "LightMode"="BurstWordOverlay" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma instancing_options forcemaxcount:64
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #define BURST_CLASSIC_INSTANCING 1
            #include "BrgTMPGlyph.hlsl"
            ENDHLSL
        }
    }
}
