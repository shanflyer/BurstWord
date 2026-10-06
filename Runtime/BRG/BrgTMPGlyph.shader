Shader "BurstWord/BRG TMP Glyph"
{
    Properties
    {
        [HideInInspector] _BurstZTest("Depth test", Float) = 8
        [HideInInspector] _BurstSortingMode("Sorting mode", Float) = 0
        [HideInInspector] _BurstPlainSdfFastPath("Plain SDF fast path", Float) = 1
        [HideInInspector] _BurstLabelCapacity("Label capacity", Float) = 1
        [HideInInspector] _BurstAtlas0("Atlas 0", 2D) = "white" {}
        [HideInInspector] _BurstAtlas1("Atlas 1", 2D) = "white" {}
        [HideInInspector] _BurstAtlas2("Atlas 2", 2D) = "white" {}
        [HideInInspector] _BurstAtlas3("Atlas 3", 2D) = "white" {}
        [HideInInspector] _BurstAtlas4("Atlas 4", 2D) = "white" {}
        [HideInInspector] _BurstAtlas5("Atlas 5", 2D) = "white" {}
        [HideInInspector] _BurstAtlas6("Atlas 6", 2D) = "white" {}
        [HideInInspector] _BurstAtlas7("Atlas 7", 2D) = "white" {}
        [HideInInspector] _BurstAtlas8("Atlas 8", 2D) = "white" {}
        [HideInInspector] _BurstAtlas9("Atlas 9", 2D) = "white" {}
        [HideInInspector] _BurstAtlas10("Atlas 10", 2D) = "white" {}
        [HideInInspector] _BurstAtlas11("Atlas 11", 2D) = "white" {}
        [HideInInspector] _BurstAtlas12("Atlas 12", 2D) = "white" {}
        [HideInInspector] _BurstAtlas13("Atlas 13", 2D) = "white" {}
        [HideInInspector] _BurstAtlas14("Atlas 14", 2D) = "white" {}
        [HideInInspector] _BurstAtlas15("Atlas 15", 2D) = "white" {}
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
            #pragma target 4.5
            #pragma only_renderers d3d11 metal vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "BrgTMPGlyph.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "OrderedOverlay"
            Tags { "LightMode"="BurstWordOverlay" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 metal vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "BrgTMPGlyph.hlsl"
            ENDHLSL
        }
    }
}
