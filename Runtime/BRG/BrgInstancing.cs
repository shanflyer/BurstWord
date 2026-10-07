using UnityEngine;
using UnityEngine.Rendering;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        public enum RenderBackend { Auto, BRG, Instancing }
        // Runtime override for benchmarks. Normal use automatically selects the supported backend.
        [HideInInspector]
        public RenderBackend renderBackend = RenderBackend.Auto;
        public RenderBackend ActiveBackend { get; private set; }
        public string BackendReason { get; private set; }
        private bool UsingBrg => ActiveBackend == RenderBackend.BRG;
        // Ten float4 properties fit within the 16 KB uniform-block minimum of WebGL 2.
        private const int InstancingBatchSize = 64;
        private MaterialPropertyBlock instanceProperties;
        private Material instancingMaterial;
        private Vector4[][] instanceValues;
        private Matrix4x4[] instanceMatrices;
        private static readonly int[] InstancePropertyIds = {
            Shader.PropertyToID("_WorldBirth"), Shader.PropertyToID("_GlyphRect"),
            Shader.PropertyToID("_AtlasRect"), Shader.PropertyToID("_LifeMotion"),
            Shader.PropertyToID("_Tint"), Shader.PropertyToID("_GlyphStyle"),
            Shader.PropertyToID("_BurstPoseAnchor"), Shader.PropertyToID("_BurstPoseRight"),
            Shader.PropertyToID("_BurstPoseUp"), Shader.PropertyToID("_BurstAnimationLabel")
        };
        private static readonly int[] ResourcePropertyIds = {
            Shader.PropertyToID("_BurstResource0"), Shader.PropertyToID("_BurstResource1"),
            Shader.PropertyToID("_BurstResource2"), Shader.PropertyToID("_BurstResource3"),
            Shader.PropertyToID("_BurstResource4"), Shader.PropertyToID("_BurstResource5"),
            Shader.PropertyToID("_BurstResource6"), Shader.PropertyToID("_BurstResource7"),
            Shader.PropertyToID("_BurstResource8"), Shader.PropertyToID("_BurstResource9")
        };
        private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");

        private bool SelectBackend()
        {
            var api = SystemInfo.graphicsDeviceType;
            // Our BRG shader uses raw/structured storage, not the DOTS UBO path.
            bool storageBackend = api == GraphicsDeviceType.Direct3D11 || api == GraphicsDeviceType.Direct3D12 ||
                api == GraphicsDeviceType.Vulkan || api == GraphicsDeviceType.Metal;
            bool brgSupported = storageBackend && SystemInfo.supportsComputeShaders && SystemInfo.graphicsShaderLevel >= 45 &&
                Application.platform != RuntimePlatform.WebGLPlayer;
            Shader preferred = glyphShader != null && glyphShader.name != "BurstWord/Instanced TMP Glyph" ? glyphShader : Shader.Find("BurstWord/BRG TMP Glyph");
            bool chooseBrg = renderBackend != RenderBackend.Instancing && brgSupported && preferred != null && preferred.isSupported;
            ActiveBackend = chooseBrg ? RenderBackend.BRG : RenderBackend.Instancing;
            BackendReason = chooseBrg ? "BRG supported" : renderBackend == RenderBackend.Instancing ? "Instancing selected" : "BRG unavailable on this graphics device";
            glyphShader = chooseBrg ? preferred : Shader.Find("BurstWord/Instanced TMP Glyph");
            if (glyphShader == null || !glyphShader.isSupported || (!chooseBrg && !SystemInfo.supportsInstancing)) return false;
            if (!chooseBrg) InitializeInstancing();
            return true;
        }
        private void InitializeInstancing()
        {
            instanceProperties = new MaterialPropertyBlock();
            instancingMaterial = new Material(glyphShader) { name = "BurstWord shared instancing material", enableInstancing = true, hideFlags = HideFlags.HideAndDontSave };
            instanceValues = new Vector4[10][];
            for (int i = 0; i < instanceValues.Length; i++) instanceValues[i] = new Vector4[InstancingBatchSize];
            instanceMatrices = new Matrix4x4[InstancingBatchSize];
        }
        private void DisposeInstancing()
        {
            instanceProperties = null; instanceValues = null; instanceMatrices = null;
            DestroyGeneratedObject(instancingMaterial); instancingMaterial = null;
        }
        internal static void DrawInstancingOverlays(Camera camera, CommandBuffer command)
        {
            foreach (var renderer in spatialRenderers)
                if (renderer != null && renderer.isActiveAndEnabled && renderer.worldCamera == camera &&
                    renderer.IsInitialized && !renderer.UsingBrg && renderer.sortingMode != SortingMode.SceneTransparent)
                    renderer.DrawInstanced(command);
        }
#if BURSTWORD_URP_RENDER_GRAPH
        internal static void DrawInstancingOverlays(Camera camera, RasterCommandBuffer command)
        {
            foreach (var renderer in spatialRenderers)
                if (renderer != null && renderer.isActiveAndEnabled && renderer.worldCamera == camera &&
                    renderer.IsInitialized && !renderer.UsingBrg && renderer.sortingMode != SortingMode.SceneTransparent)
                    renderer.DrawInstanced(new InstancingCommand(command));
        }
#endif
        private readonly struct InstancingCommand
        {
            private readonly CommandBuffer legacy;
#if BURSTWORD_URP_RENDER_GRAPH
            private readonly RasterCommandBuffer graph;
            public InstancingCommand(RasterCommandBuffer command) { legacy = null; graph = command; }
#endif
            public InstancingCommand(CommandBuffer command)
            {
                legacy = command;
#if BURSTWORD_URP_RENDER_GRAPH
                graph = null;
#endif
            }
            public bool IsOverlay => legacy != null
#if BURSTWORD_URP_RENDER_GRAPH
                || graph != null
#endif
                ;
            public void Draw(Mesh mesh, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties)
            {
#if BURSTWORD_URP_RENDER_GRAPH
                if (graph != null) { graph.DrawMeshInstanced(mesh, 0, material, 1, matrices, count, properties); return; }
#endif
                legacy.DrawMeshInstanced(mesh, 0, material, 1, matrices, count, properties);
            }
        }
        private void DrawInstanced(CommandBuffer command) => DrawInstanced(new InstancingCommand(command));
        private void DrawInstanced(InstancingCommand command)
        {
            DrawCommandCount = SubmittedGlyphCount = 0;
            if (ActiveGlyphCount == 0 || worldCamera == null) return;
            using (SortMarker.Auto()) SortLabels();
            ConfigureSpatialMaterial(instancingMaterial);
            GlyphPage previousPage = null;
            int previousResource = -1, count = 0;
            for (int i = 0; i < sortedCount; i++)
            {
                int id = sortedLabels[i].id;
                // Scene-transparent draws need one bounds center per complete label, just as a TMP renderer.
                if (!command.IsOverlay && count != 0) { SubmitInstances(previousPage, previousResource, count, command); count = 0; }
                for (int link = labels[id].head; link >= 0; link = links[link].next)
                {
                    var item = links[link]; var page = glyphPages[item.group];
                    int resource = (int)page.values[0][item.slot].x;
                    if (count != 0 && (count == InstancingBatchSize || page != previousPage || resource != previousResource))
                    { SubmitInstances(previousPage, previousResource, count, command); count = 0; }
                    previousPage = page; previousResource = resource;
                    for (int field = 0; field < 6; field++) instanceValues[field][count] = page.values[field][item.slot];
                    for (int field = 0; field < 3; field++) instanceValues[field + 6][count] = labelValues[field][id];
                    instanceValues[9][count] = animationLabels[id];
                    // Position the engine's group bounds at the animated label center. The shader uses explicit poses.
                    instanceMatrices[count] = Matrix4x4.Translate(sortedLabels[i].position);
                    count++;
                }
            }
            if (count != 0) SubmitInstances(previousPage, previousResource, count, command);
        }
        private void SubmitInstances(GlyphPage page, int resource, int count, InstancingCommand command)
        {
            // Use one material across all resource segments, including A -> B -> A.
            // Changing materials can regroup equal-depth transparent segments by state.
            page.BindInstancedResource(instanceProperties, resource);
            for (int field = 0; field < instanceValues.Length; field++)
                instanceProperties.SetVectorArray(InstancePropertyIds[field], instanceValues[field]);
            if (command.IsOverlay) command.Draw(quad, instancingMaterial, instanceMatrices, count, instanceProperties);
            else Graphics.DrawMeshInstanced(quad, 0, instancingMaterial, instanceMatrices, count, instanceProperties,
                ShadowCastingMode.Off, false, gameObject.layer, worldCamera, LightProbeUsage.Off);
            DrawCommandCount++; SubmittedGlyphCount += count;
            UploadedBytesLastFrame += count * (10 * 16 + 64); UploadCallsLastFrame++;
        }
    }
}
