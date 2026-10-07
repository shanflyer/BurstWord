using System;
using System.Collections.Generic;
using System.Globalization;
using BurstWord.Baseline;
using TMPro;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore;

namespace BurstWord.BRG
{
    /// <summary>
    /// TMP font data -> glyph instances -> BRG. No GameObject, TMP component or Mesh per label.
    /// The only Mesh is one generated quad shared by all glyphs, regardless of text length.
    /// </summary>
    public sealed partial class BrgDamageTextRenderer : MonoBehaviour, IDamageTextBackend
    {
        [Min(1)] public int capacity = 10000;
        public TMP_FontAsset font;
        public Shader glyphShader;
        public Camera worldCamera;
        [Min(1)] public int fontSize = 28;
        [Min(0.01f)] public float lifetime = 1.5f;
        public float risePixels = 90;
        public Vector2 referenceResolution = new Vector2(1920, 1080);

        public string BackendName => $"TMP Font Asset + {ActiveBackend} (no label GameObjects)";
        public int Capacity => labels == null ? capacity : labels.Length;
        public int ActiveCount { get; private set; }
        public int CreatedCount => 0;
        public long EmittedCount { get; private set; }
        public long DroppedCount { get; private set; }
        public long MissingGlyphCount { get; private set; }
        public int ActiveGlyphCount { get; private set; }
        public int SubmittedGlyphCount { get; private set; }
        public int DrawCommandCount { get; private set; }
        public int UploadedBytesLastFrame { get; private set; }
        public int UploadCallsLastFrame { get; private set; }
        public int GlyphPageCount => glyphPages.Count;
        public int FontResourceCount => atlasBatches.Count;
        public int FragmentedLabelCount { get; private set; }
        public int FragmentedGlyphCount { get; private set; }
        public int GlyphSlotCapacity { get { int total = 0; foreach (var page in glyphPages) total += page.SlotCapacity; return total; } }
        internal bool useContiguousGlyphAllocation = true;
        // Diagnostic A/B switch; no rendering or ordering semantics change.
        [NonSerialized] public bool useContiguousIndexFastPath = true;
        public bool IsInitialized => quad != null && labels != null;

        private struct Label
        {
            public int animation;
            public float animationAmplitude;
            public float end, birth, duration, units, rise, drift;
            public int head, tail, activeIndex;
            public int firstGlyphSlot, glyphCount;
            public bool contiguousGlyphs;
            public bool active;
            public SpaceMode space;
            public Transform target;
            public TextPose pose;
            public Matrix4x4 localMatrix;
            public FollowTarget followTarget;
            public int previousFollow, nextFollow;
            public ulong generation, order;
        }
        private struct GlyphLink { public int group, slot, next; }
        private struct ResolvedGlyph
        {
            public TMP_FontAsset font;
            public TMP_Character character;
            public int group;
            public bool alternative;
        }
        private struct PositionedGlyph { public ResolvedGlyph glyph; public Vector4 rect, uv, style; public Color tint; }

        private Label[] labels;
        private int[] freeLabels;
        private int freeLabelCount;
        private GlyphLink[] links;
        private int[] freeLinks;
        private int freeLinkCount, linkHighWater;
        private readonly List<GlyphPage> glyphPages = new List<GlyphPage>();
        private readonly List<AtlasBatch> atlasBatches = new List<AtlasBatch>();
        private readonly Dictionary<uint, ResolvedGlyph> glyphCache = new Dictionary<uint, ResolvedGlyph>();
        private readonly List<PositionedGlyph> layout = new List<PositionedGlyph>();
        private readonly List<float> lineWidths = new List<float>();
        private BatchRendererGroup brg;
        private Mesh quad;
        private BatchMeshID meshId;
        private double epoch;
        private long cameraId;
        private bool warnedMissing;
        private static readonly ProfilerMarker GenerateMarker = new ProfilerMarker("BurstWord.BRG.GenerateTMPGlyphs");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("BurstWord.BRG.Upload");
        private static readonly ProfilerMarker CullMarker = new ProfilerMarker("BurstWord.BRG.DrawCommands");
        private static readonly ProfilerMarker LayoutMarker = new ProfilerMarker("BurstWord.BRG.Layout");
        private static readonly ProfilerMarker InstancesMarker = new ProfilerMarker("BurstWord.BRG.WriteInstances");
        private static readonly ProfilerMarker RetireMarker = new ProfilerMarker("BurstWord.BRG.Retire");
        private static readonly ProfilerMarker FollowMarker = new ProfilerMarker("BurstWord.BRG.Follow");
        private static readonly ProfilerMarker PoseUploadMarker = new ProfilerMarker("BurstWord.BRG.PoseUpload");
        private static readonly ProfilerMarker SortMarker = new ProfilerMarker("BurstWord.BRG.Sort");
        private static readonly ProfilerMarker VisibleMarker = new ProfilerMarker("BurstWord.BRG.VisibleIndices");
        private float Now =>
#if UNITY_EDITOR
            previewClock ? previewTime :
#endif
            (float)(Time.unscaledTimeAsDouble - epoch);

        private void OnEnable() => Initialize();

        public void Initialize()
        {
            if (IsInitialized) return;
            if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (worldCamera == null) worldCamera = Camera.main;
            if (font == null || worldCamera == null || GraphicsSettings.currentRenderPipeline == null || !SelectBackend())
            {
                Debug.LogError($"Damage text initialization failed: font={font != null}, camera={worldCamera != null}, shader={glyphShader != null}, supported={glyphShader != null && glyphShader.isSupported}, SRP={GraphicsSettings.currentRenderPipeline != null}, instancing={SystemInfo.supportsInstancing}.", this);
                enabled = false;
                return;
            }
            if (UsingBrg)
            {
                try { brg = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero); }
                catch (Exception error)
                {
                    ActiveBackend = RenderBackend.Instancing;
                    BackendReason = "BRG initialization failed: " + error.Message;
                    glyphShader = Shader.Find("BurstWord/Instanced TMP Glyph");
                    if (!SystemInfo.supportsInstancing || glyphShader == null || !glyphShader.isSupported)
                    { Debug.LogError(BackendReason, this); enabled = false; return; }
                    InitializeInstancing();
                }
            }
            capacity = Mathf.Max(1, capacity);
            fontSize = Mathf.Max(1, fontSize);
            labels = new Label[capacity];
            freeLabels = new int[capacity];
            for (int i = 0; i < capacity; i++) freeLabels[i] = capacity - i - 1;
            freeLabelCount = capacity;
            InitializeSpatial();
            InitializeAnimations();
            links = new GlyphLink[Mathf.Max(128, capacity)];
            visibleLinks = new NativeArray<VisibleLink>(links.Length, Allocator.Persistent);
            visibleLabels = new NativeArray<VisibleLabel>(capacity, Allocator.Persistent);
            freeLinks = new int[links.Length];
            epoch = Time.unscaledTimeAsDouble;
            cameraId = BrgObjectIdentity.Of(worldCamera);
            quad = new Mesh { name = "BurstWord shared generated glyph quad", hideFlags = HideFlags.HideAndDontSave };
            quad.vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) };
            quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            // Match BRG's conservative visibility: shader expansion and GPU motion are not
            // represented by the quad's vertices. Keep the center correct for scene sorting.
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * (UsingBrg ? 1 : worldCamera.farClipPlane * 2 + 100));
            quad.UploadMeshData(true);
            if (UsingBrg)
            {
                brg.SetEnabledViewTypes(new[] { BatchCullingViewType.Camera });
                meshId = brg.RegisterMesh(quad);
            }
            // The shader expands world anchors in screen space. No extra distance/visibility drops
            // are introduced relative to the UGUI baseline. Only the benchmark camera renders it.
            UpdateBounds();
        }

        // Preserve the original public signature for precompiled callers and method groups.
        public bool Emit(Vector3 worldPosition, int damage, Color color, float horizontalDrift, float durationScale)
            => Emit(worldPosition, damage, color, horizontalDrift, durationScale, font: null);

        public bool Emit(Vector3 worldPosition, int damage, Color color, float horizontalDrift = 0, float durationScale = 1,
            BrgTextAnimation animation = null, float animationAmplitude = 1, TMP_FontAsset font = null,
            Material material = null, int fontSize = 0, bool useLegacyAnimation = false)
        {
            if (!isActiveAndEnabled || !IsInitialized) return false;
            if (freeLabelCount == 0) { DroppedCount++; return false; }
            // Keep the same integer-to-string work as the baseline for this first comparison.
            return EmitText(worldPosition, damage.ToString(CultureInfo.InvariantCulture), color, horizontalDrift,
                durationScale, animation, animationAmplitude, font, material, fontSize, useLegacyAnimation);
        }

        public bool EmitText(Vector3 worldPosition, string text, Color color, float horizontalDrift, float durationScale)
            => EmitText(worldPosition, text, color, horizontalDrift, durationScale, font: null);

        public bool EmitText(Vector3 worldPosition, string text, Color color, float horizontalDrift = 0, float durationScale = 1,
            BrgTextAnimation animation = null, float animationAmplitude = 1, TMP_FontAsset font = null,
            Material material = null, int fontSize = 0, bool useLegacyAnimation = false)
            => EmitSpatial(text, color, null, new TextPose(worldPosition, Quaternion.identity, Vector3.one),
                horizontalDrift, durationScale, useLegacyAnimation ? null : animation ?? ActiveDefaultAnimation,
                animationAmplitude, true, font, material, fontSize).IsAlive;

        private bool BuildBasicLayout(string text, Color color)
        {
            if (useNumericGeometryCache && IsBasicNumber(text) && BuildCachedNumbers(text, color)) return true;
            layout.Clear();
            lineWidths.Clear();
            float x = 0;
            int line = 0;
            float lineHeight = font.faceInfo.lineHeight * fontSize / font.faceInfo.pointSize * font.faceInfo.scale;
            // Center the visible ascender/descender extent, as TMP's center alignment does.
            float ascender = float.MinValue, descender = float.MaxValue;
            for (int i = 0; i < text.Length; i++)
            {
                uint unicode = text[i];
                if (unicode == '\r') continue;
                if (unicode == '\n') { lineWidths.Add(x); x = 0; line++; continue; }
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    unicode = (uint)char.ConvertToUtf32(text[i], text[++i]);
                if (!Resolve(unicode, out var resolved))
                {
                    MissingGlyphCount++;
                    if (!Resolve(0xfffd, out resolved) && !Resolve('?', out resolved))
                    {
                        if (!warnedMissing) { Debug.LogWarning("TMP font/fallbacks have no replacement glyph for missing characters.", this); warnedMissing = true; }
                        return false;
                    }
                }
                var character = resolved.character;
                var glyph = character.glyph;
                var face = resolved.font.faceInfo;
                float scale = (float)fontSize / face.pointSize * face.scale * character.scale * glyph.scale;
                var metrics = glyph.metrics;
                float baseline = face.baseline * scale - line * lineHeight;
                float adjustX = 0, adjustY = 0, adjustAdvance = 0;
                if (enableKerning)
                {
                    if (i > 0 && Resolve(text[i - 1], out var previous) && previous.font == resolved.font &&
                        Pair(resolved.font, previous.character.glyph.index, glyph.index, out var before))
                    {
                        var value = before.secondAdjustmentRecord.glyphValueRecord;
                        adjustX += value.xPlacement; adjustY += value.yPlacement; adjustAdvance += value.xAdvance;
                    }
                    if (i + 1 < text.Length && Resolve(text[i + 1], out var next) && next.font == resolved.font &&
                        Pair(resolved.font, glyph.index, next.character.glyph.index, out var after))
                    {
                        var value = after.firstAdjustmentRecord.glyphValueRecord;
                        adjustX += value.xPlacement; adjustY += value.yPlacement; adjustAdvance += value.xAdvance;
                    }
                }
                ascender = Mathf.Max(ascender, baseline + face.ascentLine * scale);
                descender = Mathf.Min(descender, baseline + face.descentLine * scale);
                if (metrics.width > 0 && metrics.height > 0)
                {
                    float padding = Mathf.Min(1, resolved.font.atlasPadding);
                    var atlas = resolved.font.atlasTextures[glyph.atlasIndex];
                    var r = glyph.glyphRect;
                    layout.Add(new PositionedGlyph
                    {
                        glyph = resolved,
                        tint = color,
                        rect = new Vector4(x + (metrics.horizontalBearingX - padding + adjustX) * scale,
                            baseline + (metrics.horizontalBearingY - metrics.height - padding + adjustY) * scale,
                            (metrics.width + padding * 2) * scale, (metrics.height + padding * 2) * scale),
                        uv = new Vector4((r.x - padding) / atlas.width, (r.y - padding) / atlas.height,
                            (r.width + padding * 2) / atlas.width, (r.height + padding * 2) / atlas.height)
                    });
                }
                x += (metrics.horizontalAdvance + adjustAdvance) * scale;
            }
            lineWidths.Add(x);
            // Each glyph stores its line's center adjustment in a second pass over the string.
            int positioned = 0;
            line = 0;
            for (int i = 0; i < text.Length; i++)
            {
                uint unicode = text[i];
                if (unicode == '\r') continue;
                if (unicode == '\n') { line++; continue; }
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    unicode = (uint)char.ConvertToUtf32(text[i], text[++i]);
                if (!Resolve(unicode, out var resolved) && !Resolve(0xfffd, out resolved)) Resolve('?', out resolved);
                if (resolved.character.glyph.metrics.width <= 0 || resolved.character.glyph.metrics.height <= 0) continue;
                var item = layout[positioned];
                item.rect.x -= lineWidths[line] * 0.5f;
                item.rect.y -= (ascender + descender) * 0.5f;
                layout[positioned++] = item;
            }
            return true;
        }

        private bool Resolve(uint unicode, out ResolvedGlyph resolved)
        {
            if (glyphCache.TryGetValue(unicode, out resolved)) return true;
            var character = TMP_FontAssetUtilities.GetCharacterFromFontAsset(unicode, font, true, FontStyles.Normal,
                FontWeight.Regular, out _);
            if (character == null && useAdditionalFonts && additionalFonts != null)
                foreach (var candidate in additionalFonts)
                {
                    if (candidate == null) continue;
                    character = TMP_FontAssetUtilities.GetCharacterFromFontAsset(unicode, candidate, true,
                        FontStyles.Normal, FontWeight.Regular, out _);
                    if (character != null) break;
                }
            if (character == null && TMP_Settings.fallbackFontAssets != null)
                character = TMP_FontAssetUtilities.GetCharacterFromFontAssets(unicode, font, TMP_Settings.fallbackFontAssets,
                    true, FontStyles.Normal, FontWeight.Regular, out _);
            if (character == null) return false;
            var source = (TMP_FontAsset)character.textAsset;
            var texture = source.atlasTextures[character.glyph.atlasIndex];
            int groupId = Batch(source, texture, source.material, AtlasMode(source));
            resolved = new ResolvedGlyph { font = source, character = character, group = groupId };
            glyphCache.Add(unicode, resolved);
            return true;
        }

        private int AllocateLink()
        {
            if (freeLinkCount > 0) return freeLinks[--freeLinkCount];
            if (linkHighWater == links.Length)
            {
                Array.Resize(ref links, links.Length * 2);
                Array.Resize(ref freeLinks, links.Length);
                EnsurePreparationCapacity(ref visibleLinks, links.Length);
            }
            return linkHighWater++;
        }

        private void ReturnLabel(int id)
        {
            CompleteVisibilityWork();
            if (!labels[id].contiguousGlyphs) { FragmentedLabelCount--; FragmentedGlyphCount -= labels[id].glyphCount; }
            int linkId = labels[id].head;
            while (linkId >= 0)
            {
                var link = links[linkId];
                glyphPages[link.group].Remove(link.slot);
                freeLinks[freeLinkCount++] = linkId;
                ActiveGlyphCount--;
                linkId = link.next;
            }
            var removed = labels[id];
            int lastId = activeLabels[ActiveCount - 1];
            activeLabels[removed.activeIndex] = lastId;
            labels[lastId].activeIndex = removed.activeIndex;
            if (removed.space == SpaceMode.WorldFollow && (removed.animation != 0 || removed.rise != 0 || removed.drift != 0)) animatedWorldLabels--;
            if (removed.animation != 0 || removed.rise != 0 || removed.drift != 0) animatedLabels--;
            if (removed.animation != 0) activeAnimationLabels--;
            DetachFollow(id);
            labels[id].active = false; labels[id].target = null;
            liveLabelOrders[id] = 0;
            orderDirty = true;
            freeLabels[freeLabelCount++] = id;
            ActiveCount--;
        }

        private void LateUpdate()
        {
            if (!IsInitialized) return;
            float now = Now;
            UpdateAnimations();
            using (RetireMarker.Auto())
                for (int i = ActiveCount - 1; i >= 0; i--)
                    if (labels[activeLabels[i]].end <= now) ReturnLabel(activeLabels[i]);
            UpdateSpatial();
            UploadedBytesLastFrame = 0;
            UploadCallsLastFrame = 0;
            using (UploadMarker.Auto())
                foreach (var batch in glyphPages)
                {
                    UploadedBytesLastFrame += batch.Upload();
                    UploadCallsLastFrame += batch.UploadCalls;
                }
            UpdateScreenParameters();
            UpdateBounds();
        }

        private void UpdateBounds()
        {
            if (brg != null && worldCamera != null)
                brg.SetGlobalBounds(new Bounds(worldCamera.transform.position, Vector3.one * (worldCamera.farClipPlane * 2 + 100)));
        }

        private JobHandle OnPerformCulling(BatchRendererGroup group, BatchCullingContext context,
            BatchCullingOutput output, IntPtr userContext) => CullSorted(context, output);

        public void Clear()
        {
            if (labels != null)
                while (ActiveCount > 0) ReturnLabel(activeLabels[ActiveCount - 1]);
            SubmittedGlyphCount = DrawCommandCount = 0;
        }

        public void ResetCounters() { EmittedCount = DroppedCount = MissingGlyphCount = MissingSpriteCount = UnavailableShapingCount = FailedLayoutCount = 0; }

        private void OnDisable()
        {
            CompleteVisibilityWork();
            Clear();
            foreach (var batch in atlasBatches) batch.Dispose();
            atlasBatches.Clear();
            foreach (var page in glyphPages) page.Dispose();
            glyphPages.Clear();
            glyphCache.Clear();
            numericGlyphs = null;
            DisposeTypography();
            DisposeSpatial();
            DisposeAnimations();
            DisposeInstancing();
            if (brg != null) { brg.Dispose(); brg = null; }
            DestroyGeneratedObject(quad); quad = null;
            labels = null; freeLabels = null; links = null; freeLinks = null;
            freeLinkCount = linkHighWater = 0;
            if (visibleLinks.IsCreated) visibleLinks.Dispose();
            if (visibleLabels.IsCreated) visibleLabels.Dispose();
        }

        // A typography resource refers to a page; it does not own a separate draw material/buffer.
        private static void DestroyGeneratedObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        private sealed class AtlasBatch : IDisposable
        {
            public readonly TMP_FontAsset Font;
            public readonly Texture Texture;
            public readonly Material SourceMaterial;
            public readonly GlyphPage Page;
            public readonly int Resource;
            public AtlasBatch(BrgDamageTextRenderer renderer, Shader shader, TMP_FontAsset font, Texture texture, Material source, int mode)
            {
                Font=font; Texture=texture; SourceMaterial=source;
                foreach(var candidate in renderer.glyphPages)
                    if(candidate.CanBind(texture)) { Page=candidate; break; }
                if(Page==null) { Page=new GlyphPage(renderer); renderer.glyphPages.Add(Page); }
                Resource=Page.AddResource(texture,source,mode);
            }
            public void Dispose() { } // Page lifetime is owned by the renderer.
        }

        private sealed class GlyphPage : IDisposable
        {
            public readonly Material Material;
            public BatchMaterialID MaterialId { get; private set; }
            public BatchID BatchId { get; private set; }
            public int Count { get; private set; }
            public int[] LiveSlots { get; private set; }
            private readonly BatchRendererGroup owner;
            private static readonly string[] PropertyNames = { "_WorldBirth", "_GlyphRect", "_AtlasRect", "_LifeMotion", "_Tint", "_GlyphStyle" };
            internal readonly Vector4[][] values = new Vector4[6][];
            private int[] liveIndex;
            private int cursor, highWater, allocated, peakCount, dirtyFirst = -1, dirtyLast, dirtyPrevious = -1, dirtyWraps;
            private bool dirtyFull;
            private GraphicsBuffer buffer;

            public readonly int Index;
            private readonly List<Texture> textures = new List<Texture>();
            private readonly List<Vector4> resources = new List<Vector4>();
            public void BindInstancedResource(MaterialPropertyBlock block, int id)
            {
                var settings = resources[id * 10 + 4];
                block.SetTexture(MainTextureId, textures[(int)settings.z]);
                for (int field = 0; field < 10; field++) block.SetVector(ResourcePropertyIds[field], resources[id * 10 + field]);
                settings.z = 0; block.SetVector(ResourcePropertyIds[4], settings);
            }
            private GraphicsBuffer resourceBuffer;
            private int resourceCapacity;
            public GlyphPage(BrgDamageTextRenderer renderer)
            {
                owner = renderer.brg; Index = renderer.glyphPages.Count;
                Material = new Material(renderer.glyphShader) { name = "BurstWord shared glyph page " + Index, enableInstancing = true, hideFlags = HideFlags.HideAndDontSave };
                renderer.ConfigureSpatialMaterial(Material);
                Grow(128);
            }
            public bool CanBind(Texture texture) => textures.Count < 16 || textures.Contains(texture);
            public int AddResource(Texture texture, Material source, int mode)
            {
                int textureId = textures.IndexOf(texture);
                if (textureId < 0)
                {
                    textureId = textures.Count; textures.Add(texture);
                    if (owner != null) Material.SetTexture("_BurstAtlas" + textureId, texture);
                }
                // Match the original shader defaults and TMP keyword interpretation exactly.
                float F(string name, float fallback = 0) => source != null && source.HasProperty(name) ? source.GetFloat(name) : fallback;
                Color C(string name, Color fallback)
                {
                    var value=source != null && source.HasProperty(name) ? source.GetColor(name) : fallback;
                    // Material Color properties are converted by Unity for a linear project;
                    // structured buffers need the equivalent conversion explicitly.
                    return QualitySettings.activeColorSpace == ColorSpace.Linear ? value.linear : value;
                }
                bool underlay = source != null && (source.IsKeywordEnabled("UNDERLAY_ON") || source.IsKeywordEnabled("UNDERLAY_INNER"));
                int id = resources.Count / 10;
                resources.Add(C("_FaceColor", Color.white));
                resources.Add(C("_OutlineColor", Color.black));
                resources.Add(C("_UnderlayColor", new Color(0,0,0,.5f)));
                resources.Add(C("_GlowColor", new Color(0,1,0,.5f)));
                resources.Add(new Vector4(F("_WeightNormal") * F("_ScaleRatioA",1) / 4, mode, textureId, F("_GradientScale",10)));
                resources.Add(new Vector4(F("_FaceDilate"),F("_OutlineWidth"),F("_OutlineSoftness"),F("_ScaleRatioA",1)));
                resources.Add(new Vector4(F("_UnderlayOffsetX"),F("_UnderlayOffsetY"),F("_UnderlayDilate"),F("_UnderlaySoftness")));
                resources.Add(new Vector4(F("_ScaleRatioB",1),F("_ScaleRatioC",1),underlay?1:0,source != null && source.IsKeywordEnabled("UNDERLAY_INNER")?1:0));
                resources.Add(new Vector4(F("_GlowOffset"),F("_GlowInner"),F("_GlowOuter"),F("_GlowPower",.75f)));
                resources.Add(new Vector4(source != null && source.IsKeywordEnabled("GLOW_ON")?1:0,1f/texture.width,1f/texture.height,0));
                if (owner == null) return id;
                if (resourceBuffer == null || resources.Count > resourceCapacity)
                {
                    resourceBuffer?.Dispose(); resourceCapacity = Mathf.NextPowerOfTwo(Mathf.Max(160,resources.Count));
                    resourceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,resourceCapacity,16);
                    Material.SetBuffer("_BurstResources",resourceBuffer);
                    resourceBuffer.SetData(resources);
                }
                else resourceBuffer.SetData(resources,id*10,id*10,10);
                if (id == 0) MaterialId = owner.RegisterMaterial(Material);
                return id;
            }

            private void Grow(int size)
            {
                int previousSize = allocated;
                if (buffer != null) { owner.RemoveBatch(BatchId); buffer.Dispose(); }
                allocated = size;
                for (int i = 0; i < values.Length; i++) Array.Resize(ref values[i], size);
                Array.Resize(ref liveIndex, size);
                for (int i = previousSize; i < size; i++) liveIndex[i] = -1;
                var live = LiveSlots;
                Array.Resize(ref live, size);
                LiveSlots = live;
                if (owner == null) { cursor = previousSize; dirtyFull = true; return; }
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (64 + size * 16 * values.Length) / 4, 4);
                buffer.SetData(new Vector4[4]);
                var metadata = new NativeArray<MetadataValue>(values.Length, Allocator.Temp);
                try
                {
                    for (int i = 0; i < values.Length; i++)
                        metadata[i] = new MetadataValue { NameID = Shader.PropertyToID(PropertyNames[i]), Value = 0x80000000u | (uint)(64 + i * size * 16) };
                    BatchId = owner.AddBatch(metadata, buffer.bufferHandle);
                }
                finally { metadata.Dispose(); }
                cursor = previousSize;
                dirtyFull = true;
            }

            public int Add(Vector4 anchor, Vector4 rect, Vector4 uv, Vector4 motion, Vector4 tint, Vector4 style)
            {
                int slot = ReserveSlot();
                Write(slot, anchor, rect, uv, motion, tint, style);
                return slot;
            }
            public void Write(int slot, Vector4 anchor, Vector4 rect, Vector4 uv, Vector4 motion, Vector4 tint, Vector4 style)
            {
                values[0][slot] = anchor; values[1][slot] = rect; values[2][slot] = uv;
                values[3][slot] = motion; values[4][slot] = tint; values[5][slot] = style;
            }
            public int SlotCapacity => allocated;
            public int ReserveRange(int count)
            {
                // Keep headroom for varying lifetimes. Reserving a whole label/run prevents
                // its glyphs from being scattered among holes left by unrelated labels.
                peakCount = Math.Max(peakCount, checked(Count + count));
                int limit = Mathf.NextPowerOfTwo(Math.Max(128, checked(peakCount * 2)));
                while (count > allocated - Count || Count + count > allocated - allocated / 4) Grow(checked(allocated * 2));
                int inspected = 0;
                while (true)
                {
                    if (cursor + count > allocated) cursor = 0;
                    int first = cursor, i = 0;
                    for (; i < count; i++) if (liveIndex[first + i] >= 0) break;
                    if (i == count)
                    {
                        for (int j = first; j < first + count; j++) { liveIndex[j] = Count; LiveSlots[Count++] = j; }
                        cursor = first + count; highWater = Math.Max(highWater, cursor);
                        if (dirtyFirst < 0) dirtyFirst = first;
                        if (dirtyPrevious >= 0 && first < dirtyPrevious) dirtyWraps++;
                        dirtyPrevious = cursor - 1; dirtyLast = cursor;
                        return first;
                    }
                    cursor = first + i + 1; inspected += i + 1;
                    if (inspected >= Math.Min(allocated, Math.Max(1024, count * 64)))
                    {
                        // Rare pathological lifetime/size mixtures use the linked Burst path
                        // instead of scanning forever or growing storage without a bound.
                        if (allocated >= limit) return -1;
                        Grow(checked(allocated * 2)); inspected = 0;
                    }
                }
            }
            public int ReserveSlot()
            {
                if (Count == allocated) Grow(allocated * 2);
                // Write in GPU address order. A long-lived slot is skipped, never overwritten.
                if (cursor == allocated) cursor = 0;
                while (liveIndex[cursor] >= 0) { if (++cursor == allocated) cursor = 0; }
                int slot = cursor++;
                if (slot >= highWater) highWater = slot + 1;
                liveIndex[slot] = Count;
                LiveSlots[Count++] = slot;
                peakCount = Math.Max(peakCount, Count);
                if (dirtyFirst < 0) dirtyFirst = slot;
                if (dirtyPrevious >= 0 && slot < dirtyPrevious) dirtyWraps++;
                dirtyPrevious = slot; dirtyLast = slot + 1;
                return slot;
            }

            public void Remove(int slot)
            {
                int index = liveIndex[slot];
                int last = LiveSlots[--Count];
                LiveSlots[index] = last;
                liveIndex[last] = index;
                liveIndex[slot] = -1;
            }

            public int Upload()
            {
                UploadCalls = 0;
                if (dirtyFirst < 0 && !dirtyFull) return 0;
                int bytes;
                if (dirtyFull || dirtyWraps > 1) bytes = UploadSpan(0, highWater);
                else if (dirtyWraps == 1) bytes = UploadSpan(dirtyFirst, highWater) + UploadSpan(0, dirtyLast);
                else bytes = UploadSpan(dirtyFirst, dirtyLast);
                dirtyFirst = dirtyPrevious = -1; dirtyLast = dirtyWraps = 0; dirtyFull = false;
                return bytes;
            }
            public int UploadCalls { get; private set; }
            private int UploadSpan(int first, int last)
            {
                int count = last - first;
                if (count <= 0 || buffer == null) return 0;
                for (int i = 0; i < values.Length; i++)
                    buffer.SetData(values[i], first, 4 + i * allocated + first, count);
                UploadCalls += values.Length;
                return count * 16 * values.Length;
            }

            public void Dispose()
            {
                if (owner != null) { owner.RemoveBatch(BatchId); owner.UnregisterMaterial(MaterialId); }
                buffer?.Dispose();
                resourceBuffer?.Dispose();
                DestroyGeneratedObject(Material);
            }
        }
    }
}

