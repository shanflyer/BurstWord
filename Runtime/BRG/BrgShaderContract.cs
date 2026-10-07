using UnityEngine;
using UnityEngine.Rendering;

namespace BurstWord.BRG
{
    /// <summary>Public ABI for user-authored BurstWord shaders. Tags declare backend support;
    /// they cannot prove the behavior of arbitrary shader code.</summary>
    public static class BrgShaderContract
    {
        public const string Version = "1";
        private static readonly string[] RequiredProperties = {
            "_BurstZTest", "_BurstSortingMode", "_BurstPlainSdfFastPath", "_BurstLabelCapacity",
            "_BurstTime", "_BurstScreen", "_BurstAnimationInfo"
        };

        public static bool Validate(Shader shader, out bool brg, out bool instancing, out string reason)
        {
            brg = instancing = false;
            if (shader == null) { reason = "No shader assigned (uses the built-in effect)."; return false; }
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (material.GetTag("BurstWordContract", false, "") != Version)
                { reason = "Missing BurstWordContract=1 tag. Create a BurstWord effect template or implement the documented shader ABI."; return false; }
                brg = string.Equals(material.GetTag("BurstWordBRG", false, ""), "True", System.StringComparison.OrdinalIgnoreCase);
                instancing = string.Equals(material.GetTag("BurstWordInstancing", false, ""), "True", System.StringComparison.OrdinalIgnoreCase);
                if (!brg && !instancing) { reason = "Shader declares neither BRG nor Instancing support."; return false; }
                if (brg && !shader.keywordSpace.FindKeyword("DOTS_INSTANCING_ON").isValid)
                { reason = "Shader declares BRG support but has no DOTS_INSTANCING_ON variant."; return false; }
                if (instancing && !shader.keywordSpace.FindKeyword("INSTANCING_ON").isValid)
                { reason = "Shader declares Instancing support but has no INSTANCING_ON variant."; return false; }
                if (material.GetTag("RenderPipeline", false, "") != "UniversalPipeline")
                { reason = "BurstWord effects require a UniversalPipeline SubShader."; return false; }
                // Stable pass indices are also used by command-buffer fallback submissions.
                if (material.FindPass("Scene") != 0 || material.FindPass("OrderedOverlay") != 1)
                { reason = "Pass 0 must be Scene and pass 1 must be OrderedOverlay."; return false; }
                string sceneTag = shader.FindPassTagValue(0, 0, new ShaderTagId("LightMode")).name;
                string overlayTag = shader.FindPassTagValue(0, 1, new ShaderTagId("LightMode")).name;
                if (!string.Equals(sceneTag, "SRPDefaultUnlit", System.StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(overlayTag, "BurstWordOverlay", System.StringComparison.OrdinalIgnoreCase))
                { reason = "Scene / OrderedOverlay LightMode must be SRPDefaultUnlit / BurstWordOverlay (got '" + sceneTag + "' / '" + overlayTag + "')."; return false; }
                foreach (string property in RequiredProperties)
                    if (!material.HasProperty(property)) { reason = "Missing required shader property: " + property; return false; }
                if (brg)
                    for (int i = 0; i < 16; i++)
                        if (!material.HasProperty("_BurstAtlas" + i)) { reason = "Missing BRG atlas binding _BurstAtlas" + i; return false; }
                if (instancing)
                {
                    if (!material.HasProperty("_MainTex")) { reason = "Missing Instancing atlas _MainTex."; return false; }
                    for (int i = 0; i < 10; i++)
                        if (!material.HasProperty("_BurstResource" + i)) { reason = "Missing Instancing resource _BurstResource" + i; return false; }
                }
                reason = "Contract valid. BRG: " + (brg ? "supported" : "NOT supported") +
                    "; Instancing: " + (instancing ? "supported" : "NOT supported") + ".";
                return true;
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material);
            }
        }
    }
}
