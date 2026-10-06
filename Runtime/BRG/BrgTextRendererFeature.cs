using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if BURSTWORD_URP_RENDER_GRAPH
using UnityEngine.Rendering.RenderGraphModule;
#endif

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
#if !UNITY_6000_3_OR_NEWER
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#endif
#if BURSTWORD_URP_RENDER_GRAPH
            // ConfigureInput is consumed by URP before recording its depth-copy passes.
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) { }
#endif
        }
        private sealed class OverlayPass : ScriptableRenderPass
        {
            private static readonly ShaderTagId tag = new ShaderTagId("BurstWordOverlay");
            private FilteringSettings filter = new FilteringSettings(RenderQueueRange.transparent);
            private static readonly ProfilingSampler sampler = new ProfilingSampler("BurstWord.OrderedOverlay");
#if BURSTWORD_URP_RENDER_GRAPH
            private sealed class PassData
            {
                public RendererListHandle renderers;
                public Camera camera;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var rendering = frameData.Get<UniversalRenderingData>();
                var camera = frameData.Get<UniversalCameraData>();
                var lights = frameData.Get<UniversalLightData>();
                var resources = frameData.Get<UniversalResourceData>();
                using (var builder = graph.AddRasterRenderPass<PassData>("BurstWord.OrderedOverlay", out var data, sampler))
                {
                    var drawing = RenderingUtils.CreateDrawingSettings(tag, rendering, camera, lights, SortingCriteria.None);
                    drawing.enableInstancing = true; drawing.perObjectData = PerObjectData.None;
                    data.renderers = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filter));
                    data.camera = camera.camera;
                    builder.UseRendererList(data.renderers);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    if (resources.cameraDepthTexture.IsValid() && !resources.cameraDepthTexture.Equals(resources.activeDepthTexture))
                        builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.AllowPassCulling(false); // Instancing submissions are intentionally outside the renderer list.
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((PassData passData, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(passData.renderers);
                        BrgDamageTextRenderer.DrawInstancingOverlays(passData.camera, context.cmd);
                    });
                }
            }
#endif
#if !UNITY_6000_3_OR_NEWER
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
#endif
        }
    }
}
