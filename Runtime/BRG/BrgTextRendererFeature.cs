using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BurstWord.BRG
{
    /// <summary>Draws modes 1/2 after scene transparency, in the exact whole-label order supplied by BRG.</summary>
    public sealed class BrgTextRendererFeature : ScriptableRendererFeature
    {
        private OverlayPass pass;
        private OpaqueDepthInput depthInput;
        public override void Create()
        {
            pass = new OverlayPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
            depthInput = new OpaqueDepthInput { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
            depthInput.ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (BrgDamageTextRenderer.NeedsOpaqueDepth(renderingData.cameraData.camera)) renderer.EnqueuePass(depthInput);
            if (BrgDamageTextRenderer.NeedsOverlay(renderingData.cameraData.camera)) renderer.EnqueuePass(pass);
        }
        // Declare the input before transparents. URP produces an opaque-only depth snapshot
        // even when its renderer asset normally copies depth after transparent rendering.
        private sealed class OpaqueDepthInput : ScriptableRenderPass
        {
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
        }
        private sealed class OverlayPass : ScriptableRenderPass
        {
            private static readonly ShaderTagId tag = new ShaderTagId("BurstWordOverlay");
            private FilteringSettings filter = new FilteringSettings(RenderQueueRange.transparent);
            private static readonly ProfilingSampler sampler = new ProfilingSampler("BurstWord.OrderedOverlay");
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var command = CommandBufferPool.Get();
                using (new ProfilingScope(command, sampler))
                {
                    context.ExecuteCommandBuffer(command); command.Clear();
                    // BRG has already sorted complete labels. Do not regroup by material or
                    // re-sort draw segments independently: that would break A -> B -> A.
                    var drawing = CreateDrawingSettings(tag, ref renderingData, SortingCriteria.None);
                    drawing.enableInstancing = true; drawing.perObjectData = PerObjectData.None;
                    context.DrawRenderers(renderingData.cullResults, ref drawing, ref filter);
                    BrgDamageTextRenderer.DrawInstancingOverlays(renderingData.cameraData.camera, command);
                }
                context.ExecuteCommandBuffer(command); CommandBufferPool.Release(command);
            }
        }
    }
}
