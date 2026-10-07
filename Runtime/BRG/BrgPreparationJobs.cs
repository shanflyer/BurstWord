using System;
using System.Collections.Generic;
using Unity.Burst;
using BurstWord.Typography;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using TMPro;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        // Enabled by default; keep the code override for preparation comparisons.
        [HideInInspector]
        public bool enablePreparationJobs = true;
        public int JobPreparedLastBatch { get; private set; }
        public int JobFallbackLastBatch { get; private set; }
        public long JobPreparedCount { get; private set; }
        public long JobFallbackCount { get; private set; }
        public double PreparationJobWaitMilliseconds { get; private set; }
        public double InstanceJobWaitMilliseconds { get; private set; }
        public int PreparationTemplateCount => preparationTemplates.Count;
        public int PreparationVariantCount => preparationVariantCount;
        public int PreparationBatchFrame { get; private set; } = -1;
        public long FailedLayoutCount { get; private set; }

        /// <summary>Font/FontIndex, FontSize and Animation/AnimationIndex can override the manager per request.
        /// Font takes priority; otherwise FontIndex 0 selects the default and 1..N the font list.
        /// A target pose is local to Target; a pose without Target is in world space.
        /// Duration is the positive lifetime in seconds (default 1.5), independent of animation clip length.
        /// WrapWidth is per request. Requests are committed in input order in this frame.</summary>
        public struct TextEmission
        {
            public string Text;
            public Color Color;
            public Transform Target;
            public TextPose Pose;
            public float WrapWidth, HorizontalDrift, Duration;
            public BrgTextAnimation Animation;
            public Material EffectMaterial;
            public int AnimationIndex, EffectIndex;
            public Vector4 EffectParameters;
            public float AnimationAmplitude;
            public bool UseLegacyAnimation;
            public TMP_FontAsset Font;
            public int FontIndex;
            public int FontSize;
            public TextAnchor? Alignment;
            public Vector2? TextAreaSize;
            public TextEmission(string text, Color color, TextPose pose, float wrapWidth,
                float horizontalDrift, float duration, Transform target,
                BrgTextAnimation animation, float animationAmplitude, bool useLegacyAnimation)
                : this(text, color, pose, wrapWidth, horizontalDrift, duration, target,
                    animation, animationAmplitude, useLegacyAnimation, null, 0) { }

            public TextEmission(string text, Color color, TextPose pose, float wrapWidth = 0,
                float horizontalDrift = 0, float duration = 1.5f, Transform target = null,
                BrgTextAnimation animation = null, float animationAmplitude = 1, bool useLegacyAnimation = false,
                TMP_FontAsset font = null, int fontSize = 0,
                TextAnchor? alignment = null, Vector2? textAreaSize = null, int fontIndex = 0, int animationIndex = 0, int effectIndex = 0, Vector4 effectParameters = default, Material effectMaterial = null)
            {
                Text = text; Color = color; Pose = pose; Target = target; WrapWidth = wrapWidth;
                HorizontalDrift = horizontalDrift; Duration = duration;
                Animation = animation; AnimationIndex = animationIndex; AnimationAmplitude = animationAmplitude; UseLegacyAnimation = useLegacyAnimation;
                Font = font; FontIndex = fontIndex; FontSize = fontSize;
                Alignment = alignment; TextAreaSize = textAreaSize;
                EffectIndex = effectIndex; EffectParameters = effectParameters; EffectMaterial = effectMaterial;
            }
        }

        private struct PreparedGlyph
        {
            public int group;
            public Vector4 rect, uv, style;
            public Color tint;
            public static PreparedGlyph From(PositionedGlyph glyph) => new PreparedGlyph
            { group = glyph.glyph.group, rect = glyph.rect, uv = glyph.uv, style = glyph.style, tint = glyph.tint };
        }
        private struct TmpPreparationMetric
        {
            public float advance, scale, spacing;
            public int before, after, choices, nextChoices;
        }
        private struct TmpPreparationPair { public Vector3 first, second; }
        private struct PreparationSignature { public uint glyph, flags; public int cluster; public float advance, x, y; }
        private struct PreparationRun
        {
            public IntPtr font;
            public int first, last, glyphStart, glyphCount, direction, tmp;
            public uint script;
            public float scale, spacing;
        }
        private struct PreparationPatch { public int point, first, count, replacements; }
        private unsafe struct PreparationTemplateData
        {
            public PreparedGlyph* glyphs;
            public PreparedGlyph* replacements;
            public PreparationRun* runs;
            public PreparationSignature* signature;
            public TmpPreparationMetric* tmpMetrics;
            public TmpPreparationPair* tmpPairs;
            public PreparationPatch* patches;
            public uint* nominal;
            public PreparationTemplateData* alternatives;
            public int alternativeCount;
            public int glyphCount, runCount, patchCount, pointCount, signatureCount, features;
        }
        private sealed class PreparationTemplate : IDisposable
        {
            public MeasuredPlan plan;
            public ParsedMessage message;
            public ParagraphAnalysis analysis;
            public Token[] source;
            public int[] digits;
            public LinkedListNode<PreparationTemplate> cacheNode;
            public (ParsedMessage, ParagraphAnalysis, float, int, Vector2) key;
            public NativeArray<PreparedGlyph> glyphs, replacements;
            public NativeArray<PreparationRun> runs;
            public NativeArray<PreparationSignature> signature;
            public NativeArray<TmpPreparationMetric> tmpMetrics;
            public NativeArray<TmpPreparationPair> tmpPairs;
            public NativeArray<PreparationPatch> patches;
            public NativeArray<uint> nominal;
            public NativeArray<PreparationTemplateData> alternatives;
            public readonly List<PreparationTemplate> alternativeTemplates = new List<PreparationTemplate>();
            public unsafe PreparationTemplateData Data => new PreparationTemplateData
            {
                glyphs = (PreparedGlyph*)glyphs.GetUnsafeReadOnlyPtr(), replacements = (PreparedGlyph*)replacements.GetUnsafeReadOnlyPtr(),
                runs = (PreparationRun*)runs.GetUnsafeReadOnlyPtr(), signature = (PreparationSignature*)signature.GetUnsafeReadOnlyPtr(),
                patches = (PreparationPatch*)patches.GetUnsafeReadOnlyPtr(), nominal = (uint*)nominal.GetUnsafeReadOnlyPtr(),
                tmpMetrics = (TmpPreparationMetric*)tmpMetrics.GetUnsafeReadOnlyPtr(), tmpPairs = (TmpPreparationPair*)tmpPairs.GetUnsafeReadOnlyPtr(),
                glyphCount = glyphs.Length, runCount = runs.Length, patchCount = patches.Length, pointCount = source.Length, signatureCount = signature.Length,
                alternatives = alternatives.IsCreated ? (PreparationTemplateData*)alternatives.GetUnsafeReadOnlyPtr() : null,
                alternativeCount = alternativeTemplates.Count + 1
            };
            public void Dispose()
            {
                glyphs.Dispose(); replacements.Dispose(); runs.Dispose(); signature.Dispose(); patches.Dispose(); nominal.Dispose(); tmpMetrics.Dispose(); tmpPairs.Dispose();
                if (alternatives.IsCreated) alternatives.Dispose();
                foreach (var item in alternativeTemplates) item.Dispose();
            }
        }
        private struct PreparationRequest { public PreparationTemplateData template; public int points, output, measurement, numericCount; public Color color; public TextAnchor alignment; public Vector2 area; }
        private struct PreparationResult { public int valid, nativeCalls, measured, alternative; public Vector2 size; }
        private struct QueuedPreparation
        {
            public TextEmission request;
            public PreparationTemplate template;
            public int output, count, measurement;
            public int lines, substitutions;
            public Vector2 size;
            public bool shaping, ready, numeric;
        }
        private readonly Dictionary<(ParsedMessage, ParagraphAnalysis, float, int, Vector2), PreparationTemplate> preparationTemplates =
            new Dictionary<(ParsedMessage, ParagraphAnalysis, float, int, Vector2), PreparationTemplate>();
        private readonly HashSet<(ParsedMessage, ParagraphAnalysis, float, int, Vector2)> preparationAttempted =
            new HashSet<(ParsedMessage, ParagraphAnalysis, float, int, Vector2)>();
        private readonly Dictionary<(ParsedMessage, float, int, Vector2), PreparationTemplate> compiledPreparations =
            new Dictionary<(ParsedMessage, float, int, Vector2), PreparationTemplate>();
        private readonly LinkedList<PreparationTemplate> preparationLru = new LinkedList<PreparationTemplate>();
        private readonly List<PreparationTemplate> retiredPreparations = new List<PreparationTemplate>();
        private int preparationVariantCount;
        private const int MaxPreparationAlternatives = 8;
        private int PreparationFlags => (ShapingEnabled ? 1 : 0) | (enableKerning ? 2 : 0) |
            (enableLigatures ? 4 : 0) | (tightGlyphBounds ? 8 : 0) | AlignmentFlags;
        private void TouchPreparation(PreparationTemplate template)
        { preparationLru.Remove(template.cacheNode); preparationLru.AddLast(template.cacheNode); }
        private void RetirePreparation(PreparationTemplate template)
        {
            preparationTemplates.Remove(template.key); preparationAttempted.Remove(template.key);
            compiledPreparations.Remove((template.message, template.key.Item3, template.key.Item4, template.key.Item5));
            preparationLru.Remove(template.cacheNode);
            preparationVariantCount -= template.alternativeTemplates.Count + 1;
            // A queued request holds raw pointers until its worker and commit finish.
            if (preparingBatch) retiredPreparations.Add(template); else template.Dispose();
        }
        private void InvalidatePreparationTemplates()
        {
            while (preparationLru.First != null) RetirePreparation(preparationLru.First.Value);
            preparationAttempted.Clear();
        }
        private void DisposeRetiredPreparations()
        { foreach (var item in retiredPreparations) item.Dispose(); retiredPreparations.Clear(); }
        private bool TryCompiledPreparation(string text, Color color, out PreparationTemplate template)
        {
            template = null;
            if (!useCompiledPreparationFastPath || !FindParsedMessage(text, color, out var entry) || entry.digitChoices == null ||
                !ReferenceEquals(entry.digitSprite, ActiveSpriteAsset) ||
                !compiledPreparations.TryGetValue((entry, wrapWidth, PreparationFlags, LayoutTextArea), out template)) return false;
            for (int slot = 0; slot < entry.digitTokens.Length; slot++)
            {
                int index = entry.digitTokens[slot]; var token = template.source[index];
                var choice = entry.digitChoices[slot][text[token.sourceIndex] - '0'];
                if (!ReferenceEquals(choice.glyph.font, token.style.font) || choice.alternative != token.alternative)
                { template = null; return false; }
            }
            TouchPreparation(template); ParsedCacheHits++; UnicodeCacheHits++; return true;
        }
        private NativeArray<PreparationRequest> preparationRequests;
        private NativeArray<PreparationResult> preparationResults;
        private NativeArray<uint> preparationPoints;
        private NativeArray<PreparedGlyph> preparationOutput;
        private NativeArray<PreparationSignature> preparationMeasurements;
        private NativeArray<IntPtr> preparationBuffers;
        private QueuedPreparation[] preparationQueue = new QueuedPreparation[128];
        private bool preparingBatch;
        internal bool useCompiledPreparationFastPath = true, useInstanceWriteJobs = true, usePreparationAlternatives = true;
        private bool queueInstanceWrites;
        private int instanceWriteCount;
        private NativeArray<InstanceLabelWrite> instanceLabelWrites;
        private NativeArray<int> instanceWriteSlots;
        private NativeArray<InstanceGroupWrite> instanceWriteGroups;
        private NativeArray<InstancePageWrite> instanceWritePages;
        private ulong[] instancePinHandles = new ulong[24];
        private struct InstanceLabelWrite { public int first, count, label; public Vector4 anchor, motion; }
        private struct InstanceGroupWrite { public int page, resource; }
        private unsafe struct InstancePageWrite { public Vector4* anchor, rect, uv, motion, tint, style; }
        private PreparationTemplate reusedBatchMeasurement;
        private int reusedBatchMeasurementOffset;
        private static readonly ProfilerMarker JobPrepareMarker = new ProfilerMarker("BurstWord.BRG.JobPreparation");
        private static readonly ProfilerMarker JobWaitMarker = new ProfilerMarker("BurstWord.BRG.JobWait");

        private (ParsedMessage, ParagraphAnalysis, float, int, Vector2) PreparationKey()
        {
            var key = MeasuredPlanKey();
            return (key.Item1, key.Item2, key.Item3, key.Item4 | (tightGlyphBounds ? 8 : 0), key.Item5);
        }
        private static void EnsurePreparationCapacity<T>(ref NativeArray<T> buffer, int count) where T : struct
        {
            if (buffer.IsCreated && buffer.Length >= count) return;
            var replacement = new NativeArray<T>(Mathf.NextPowerOfTwo(Mathf.Max(count, 128)), Allocator.Persistent);
            if (buffer.IsCreated) { NativeArray<T>.Copy(buffer, replacement, buffer.Length); buffer.Dispose(); }
            buffer = replacement;
        }

        /// <summary>Bulk API. Optional handles receive the same value handles as EmitText.
        /// Does not defer visibility by a frame or drop work to satisfy a time budget.</summary>
        public void EmitBatch(TextEmission[] requests, int count, TextHandle[] handles = null)
        {
            if (requests == null || count < 0 || count > requests.Length || (handles != null && handles.Length < count))
                throw new ArgumentException("Invalid text batch range.");
            if (preparingBatch) throw new InvalidOperationException("Nested text batches are not supported.");
            // Validate the whole range before committing any text.
            for (int i = 0; i < count; i++)
            {
                ValidateDuration(requests[i].Duration);
                ResolveEffect(requests[i].EffectIndex, requests[i].EffectParameters, requests[i].EffectMaterial);
                if (requests[i].Font == null) GetFont(requests[i].FontIndex);
                ResolveAnimation(requests[i].Animation, requests[i].AnimationIndex, requests[i].UseLegacyAnimation);
            }
            JobPreparedLastBatch = JobFallbackLastBatch = 0; PreparationJobWaitMilliseconds = InstanceJobWaitMilliseconds = 0;
            PreparationBatchFrame = Time.frameCount;
            float savedWidth = wrapWidth;
            try
            {
                bool legacyNumbers = !useNumericGeometryCache;
                for (int i = 0; legacyNumbers && i < count; i++) legacyNumbers = requests[i].Font == null &&
                    requests[i].FontIndex == 0 && requests[i].FontSize <= 0 && requests[i].WrapWidth <= 0 && IsBasicNumber(requests[i].Text);
                if (!enablePreparationJobs || !useMeasuredLayout || count < 16 || legacyNumbers)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var request = requests[i]; wrapWidth = request.WrapWidth;
                        var handle = EmitSpatial(request.Text, request.Color, request.Target, request.Pose, request.HorizontalDrift, request.Duration,
                            ResolveAnimation(request.Animation, request.AnimationIndex, request.UseLegacyAnimation), request.AnimationAmplitude, true,
                            request.Font, request.FontSize, request.Alignment, request.TextAreaSize, request.FontIndex, request.EffectIndex, request.EffectParameters, request.EffectMaterial);
                        if (handles != null) handles[i] = handle;
                    }
                    return;
                }
                if (!isActiveAndEnabled || !IsInitialized) { if (handles != null) Array.Clear(handles, 0, count); return; }
                preparingBatch = true;
                using var generateScope = GenerateMarker.Auto();
                EnsurePreparationCapacity(ref preparationRequests, count);
                EnsurePreparationCapacity(ref preparationResults, count);
                if (preparationQueue.Length < count) Array.Resize(ref preparationQueue, Mathf.NextPowerOfTwo(count));
                int output = 0, points = 0, measured = 0, accepted = 0, jobs = 0;
                using (LayoutMarker.Auto()) using (JobPrepareMarker.Auto())
                {
                    for (int i = 0; i < count; i++)
                    {
                        var request = requests[i]; wrapWidth = request.WrapWidth;
                        using var appearance = new EmissionAppearanceScope(this, request.Font, request.FontSize, request.Alignment, request.TextAreaSize, request.FontIndex);
                        preparationQueue[i] = new QueuedPreparation { request = request };
                        preparationRequests[i] = default; preparationResults[i] = default;
                        if (handles != null) handles[i] = default;
                        if (string.IsNullOrEmpty(request.Text)) continue;
                        if (accepted == freeLabelCount) { DroppedCount++; continue; }
                        if (PrepareNumericJob(request.Text, out int numberGlyphs))
                        {
                            EnsurePreparationCapacity(ref preparationOutput,output+numberGlyphs);
                            EnsurePreparationCapacity(ref preparationPoints,points+request.Text.Length);
                            for (int j=0;j<request.Text.Length;j++) preparationPoints[points+j]=request.Text[j];
                            preparationRequests[i]=new PreparationRequest { numericCount=request.Text.Length, points=points, output=output, color=request.Color, alignment=LayoutAlignment, area=LayoutTextArea };
                            preparationQueue[i]=new QueuedPreparation { request=request, output=output, count=numberGlyphs, ready=true, numeric=true, lines=1 };
                            points+=request.Text.Length; output+=numberGlyphs; accepted++; jobs++; continue;
                        }
                        PreparationTemplate template = null;
                        bool builtOnMain = false, parsedOnMain = false;
                        bool direct = TryCompiledPreparation(request.Text, request.Color, out template);
                        bool fast = direct || (!(wrapWidth <= 0 && CanUseNumericLayout && IsBasicNumber(request.Text)) &&
                            TryPreparationTemplate(request.Text, request.Color, out template, out builtOnMain, out parsedOnMain));
                        if (!fast && !builtOnMain && !BuildLayout(request.Text, request.Color, parsedOnMain)) { FailedLayoutCount++; continue; }
                        int glyphCount = fast ? template.glyphs.Length : layout.Count;
                        EnsurePreparationCapacity(ref preparationOutput, output + glyphCount);
                        var queued = new QueuedPreparation { request = request, template = fast ? template : null,
                            output = output, count = glyphCount, measurement = measured, ready = true,
                            lines = fast ? template.plan.lines : LastLayoutLineCount,
                            size = fast ? template.plan.size : LastLayoutSize,
                            substitutions = fast ? template.plan.substitutions : LastGlyphSubstitutionCount,
                            shaping = fast ? template.plan.shaping : LastLayoutUsedShaping };
                        if (fast)
                        {
                            int pointCount = template.source.Length;
                            EnsurePreparationCapacity(ref preparationPoints, points + pointCount);
                            EnsurePreparationCapacity(ref preparationMeasurements, measured + template.signature.Length);
                            if (direct)
                            {
                                NativeArray<uint>.Copy(template.message.points, 0, preparationPoints, points, pointCount);
                                foreach (int j in template.digits) preparationPoints[points + j] = request.Text[template.source[j].sourceIndex];
                            }
                            else for (int j = 0; j < pointCount; j++) preparationPoints[points + j] = tokens[j].unicode;
                            var data = template.Data; data.features = PreparationFlags & 7;
                            if (!usePreparationAlternatives) data.alternativeCount = 1;
                            preparationRequests[i] = new PreparationRequest { template = data, points = points, output = output, measurement = measured };
                            points += pointCount; measured += template.signature.Length; jobs++;
                        }
                        else for (int j = 0; j < glyphCount; j++) preparationOutput[output + j] = PreparedGlyph.From(layout[j]);
                        preparationQueue[i] = queued; output += glyphCount; accepted++;
                    }
                    if (jobs > 0)
                    {
                        if (!preparationBuffers.IsCreated) preparationBuffers = new NativeArray<IntPtr>(JobsUtility.ThreadIndexCount, Allocator.Persistent);
                        EnsureNumericJobStorage(); EnsurePreparationCapacity(ref preparationMeasurements,Math.Max(1,measured));
                        var handle = new PrepareTemplatesJob { requests = preparationRequests, results = preparationResults,
                            points = preparationPoints, output = preparationOutput, measurements = preparationMeasurements, buffers = preparationBuffers, shaping = shapingFunctions,
                            numericGlyphs=numericJobGlyphs, numericFirstPairs=numericJobFirstPairs, numericSecondPairs=numericJobSecondPairs,
                            numericKerning=enableKerning, numericSize=fontSize }.Schedule(count, 16);
                        long began = collectLayoutTimings ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                        using (JobWaitMarker.Auto()) handle.Complete();
                        if (collectLayoutTimings) PreparationJobWaitMilliseconds =
                            (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    }
                }
                // Commit in emission order. All worker memory is independent of the slot allocator.
                EnsurePreparationCapacity(ref instanceLabelWrites, count);
                EnsurePreparationCapacity(ref instanceWriteSlots, output);
                instanceWriteCount = 0; queueInstanceWrites = useInstanceWriteJobs;
                for (int i = 0; i < count; i++)
                {
                    var queued = preparationQueue[i]; if (!queued.ready) continue;
                    var request = queued.request; wrapWidth = request.WrapWidth;
                    using var appearance = new EmissionAppearanceScope(this, request.Font, request.FontSize, request.Alignment, request.TextAreaSize, request.FontIndex);
                    TextHandle handle;
                    if (queued.template != null && preparationResults[i].valid == 0)
                    {
                        NativeShapeCalls += preparationResults[i].nativeCalls;
                        JobFallbackLastBatch++; JobFallbackCount++;
                        reusedBatchMeasurement = preparationResults[i].measured != 0 ? queued.template : null;
                        reusedBatchMeasurementOffset = queued.measurement;
                        try { handle = EmitSpatial(request.Text, request.Color, request.Target, request.Pose, request.HorizontalDrift, request.Duration,
                            ResolveAnimation(request.Animation, request.AnimationIndex, request.UseLegacyAnimation), request.AnimationAmplitude, true,
                            request.Font, request.FontSize, request.Alignment, request.TextAreaSize, request.FontIndex, request.EffectIndex, request.EffectParameters, request.EffectMaterial); }
                        finally { reusedBatchMeasurement = null; }
                    }
                    else
                    {
                        if (queued.numeric) { JobPreparedLastBatch++; JobPreparedCount++; queued.size=preparationResults[i].size; }
                        if (queued.template != null)
                        {
                            NativeShapeCalls += preparationResults[i].nativeCalls; JobPreparedLastBatch++; JobPreparedCount++;
                            int alternative = preparationResults[i].alternative;
                            if (alternative > 0)
                            {
                                var plan = queued.template.alternativeTemplates[alternative - 1].plan;
                                queued.lines = plan.lines; queued.size = plan.size; queued.substitutions = plan.substitutions; queued.shaping = plan.shaping;
                            }
                        }
                        LastLayoutLineCount = queued.lines; LastLayoutSize = queued.size;
                        LastGlyphSubstitutionCount = queued.substitutions; LastLayoutUsedShaping = queued.shaping;
                        handle = CommitSpatial(request.Target, request.Pose, request.HorizontalDrift, request.Duration,
                            preparationOutput, queued.output, queued.count, ResolveAnimation(request.Animation, request.AnimationIndex, request.UseLegacyAnimation), request.AnimationAmplitude, ResolveEffect(request.EffectIndex, request.EffectParameters, request.EffectMaterial), request.EffectParameters);
                    }
                    if (handles != null) handles[i] = handle;
                }
                queueInstanceWrites = false;
                if (instanceWriteCount > 0) FlushInstanceWrites();
            }
            finally
            {
                wrapWidth = savedWidth; preparingBatch = queueInstanceWrites = false;
                // Reuse the backing arrays without retaining caller strings/Transforms.
                Array.Clear(preparationQueue, 0, Math.Min(count, preparationQueue.Length));
                DisposeRetiredPreparations();
            }
        }

        private unsafe void FlushInstanceWrites()
        {
            using var scope = InstancesMarker.Auto();
            EnsurePreparationCapacity(ref instanceWriteGroups, atlasBatches.Count);
            EnsurePreparationCapacity(ref instanceWritePages, glyphPages.Count);
            if (instancePinHandles.Length < glyphPages.Count * 6) Array.Resize(ref instancePinHandles, Mathf.NextPowerOfTwo(glyphPages.Count * 6));
            int pinned = 0;
            try
            {
                for (int i = 0; i < atlasBatches.Count; i++)
                    instanceWriteGroups[i] = new InstanceGroupWrite { page = atlasBatches[i].Page.Index, resource = atlasBatches[i].Resource };
                // All slot allocation and buffer growth has finished before pinning upload storage.
                var pointers = stackalloc Vector4*[6];
                for (int i = 0; i < glyphPages.Count; i++)
                {
                    var page = glyphPages[i];
                    for (int field = 0; field < 6; field++)
                    {
                        pointers[field] = (Vector4*)UnsafeUtility.PinGCArrayAndGetDataAddress(page.values[field], out instancePinHandles[pinned]);
                        pinned++;
                    }
                    instanceWritePages[i] = new InstancePageWrite { anchor = pointers[0], rect = pointers[1], uv = pointers[2], motion = pointers[3], tint = pointers[4], style = pointers[5] };
                }
                var handle = new WriteInstanceLabelsJob { labels = instanceLabelWrites, slots = instanceWriteSlots,
                    groups = instanceWriteGroups, pages = instanceWritePages, glyphs = preparationOutput }.Schedule(instanceWriteCount, 16);
                long began = collectLayoutTimings ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                handle.Complete();
                if (collectLayoutTimings) InstanceJobWaitMilliseconds =
                    (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
            finally { for (int i = 0; i < pinned; i++) UnsafeUtility.ReleaseGCObject(instancePinHandles[i]); }
        }
        [BurstCompile]
        private unsafe struct WriteInstanceLabelsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InstanceLabelWrite> labels;
            [ReadOnly] public NativeArray<int> slots;
            [ReadOnly] public NativeArray<InstanceGroupWrite> groups;
            [ReadOnly] public NativeArray<InstancePageWrite> pages;
            [ReadOnly] public NativeArray<PreparedGlyph> glyphs;
            public void Execute(int index)
            {
                var label = labels[index];
                for (int i = label.first, end = label.first + label.count; i < end; i++)
                {
                    var glyph = glyphs[i]; var group = groups[glyph.group]; var page = pages[group.page]; int slot = slots[i];
                    var anchor = label.anchor; anchor.x = group.resource;
                    var style = glyph.style; style.w = label.label + 1;
                    page.anchor[slot] = anchor; page.rect[slot] = glyph.rect; page.uv[slot] = glyph.uv;
                    page.motion[slot] = label.motion; page.tint[slot] = glyph.tint; page.style[slot] = style;
                }
            }
        }
        private bool TryRestoreBatchMeasurement(int start, int end)
        {
            var template = reusedBatchMeasurement;
            if (template == null || start != 0 || end != template.source.Length ||
                !ReferenceEquals(template.message, activePreparedMessage) || !ReferenceEquals(template.analysis, activeParagraphAnalysis)) return false;
            BeginMeasurement(start, end); measuredRuns.AddRange(template.plan.runs);
            for (int i = 0; i < template.plan.signature.Length; i++)
            {
                var item = template.plan.signature[i]; var actual = preparationMeasurements[reusedBatchMeasurementOffset + i];
                if (item.face == null) item.resolved = tokens[item.cluster].glyph;
                item.glyph = actual.glyph; item.flags = actual.flags; item.advance = actual.advance; item.x = actual.x; item.y = actual.y;
                advances[item.cluster] += item.advance; measuredGlyphs.Add(item);
            }
            // Different kerning/ligature decisions can change UNSAFE_TO_BREAK flags even
            // when the glyph count and cluster sequence stay identical.
            Array.Clear(measuredBoundaries, 0, end + 1);
            foreach (var run in measuredRuns)
                for (int i = run.glyphStart; i < run.glyphEnd; i++)
                {
                    var item = measuredGlyphs[i];
                    if (item.face == null) measuredBoundaries[item.cluster] = true;
                    else if (i == run.glyphStart || measuredGlyphs[i - 1].cluster != item.cluster) measuredBoundaries[item.cluster] = (item.flags & 1u) == 0;
                    else if ((item.flags & 1u) != 0) measuredBoundaries[item.cluster] = false;
                }
            ValidateTmpMeasuredBoundaries();
            for (int i = 1; i < measuredRuns.Count; i++)
            { int boundary = measuredRuns[i].first; if (codePoints[boundary - 1] != ' ' && codePoints[boundary] != ' ') measuredBoundaries[boundary] = false; }
            measuredBoundaries[0] = measuredBoundaries[end] = true;
            LastLayoutUsedShaping = template.plan.shaping; return true;
        }

        private static bool IsBasicNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++) if (!(text[i] >= '0' && text[i] <= '9' || text[i] == '-' || text[i] == '+')) return false;
            return true;
        }
        private bool TryPreparationTemplate(string text, Color color, out PreparationTemplate template, out bool builtOnMain, out bool parsedOnMain)
        {
            template = null; builtOnMain = false; parsedOnMain = false; tokens.Clear(); styleFrames.Clear(); layout.Clear();
            LastLayoutUsedShaping = false; LastGlyphSubstitutionCount = 0;
            using (LayoutTiming(0)) if (!ParseCached(text, color)) return false;
            parsedOnMain = true;
            if (activePreparedMessage == null || tokens.Count == 0) return false;
            if (wrapWidth <= 0 && BuildStyledNumbers()) { builtOnMain = true; return false; }
            EnsureTextBuffers(tokens.Count + 1);
            for (int i = 0; i < tokens.Count; i++)
            { if (tokens[i].unicode == '\n') return false; codePoints[i] = tokens[i].unicode; bidiPoints[i] = (int)codePoints[i]; }
            using (LayoutTiming(1)) AnalyzeParagraphCached(0, tokens.Count);
            if (!preparationTemplates.TryGetValue(PreparationKey(), out template)) return false;
            // Parsing can select a different fallback font or alternate typeface for a digit.
            foreach (int i in template.digits)
                if (!ReferenceEquals(tokens[i].style.font, template.source[i].style.font) || tokens[i].alternative != template.source[i].alternative)
                { template = null; return false; }
            TouchPreparation(template);
            return true;
        }

        private unsafe void StorePreparationTemplate(MeasuredPlan plan)
        {
            // Managed callbacks own arbitrary context-dependent behavior and cannot execute in Burst.
            // Keep exact request/layout caches and instance-write jobs without approximating their output.
            if (UsesShapingCallback) return;
            if (!enablePreparationJobs || !useMeasuredLayout || activePreparedMessage == null || activeParagraphAnalysis == null) return;
            preparationTemplates.TryGetValue(PreparationKey(), out var existing);
            if (existing != null && (!usePreparationAlternatives || !CanAddPreparationAlternative(existing, plan.signature, plan.runs, plan.glyphs.Length))) return;
            if (preparationAttempted.Count < 512) preparationAttempted.Add(PreparationKey());
            // Fixed runs can only reuse their contextual result at the same safe word boundaries
            // used by the native shaping cache. Dynamic/contextual runs are re-shaped in the job.
            var jobRuns = new List<PreparationRun>();
            var nominal = new uint[tokens.Count * 10];
            var tmpMetrics = new TmpPreparationMetric[tokens.Count * 10];
            var tmpPairs = new List<TmpPreparationPair>();
            foreach (var run in plan.runs)
            {
                if (run.glyphStart == run.glyphEnd) continue;
                bool dynamic = activePreparedMessage.digitPrefix[run.last] != activePreparedMessage.digitPrefix[run.first];
                bool isolated = (run.first == 0 || codePoints[run.first - 1] == ' ') && (run.last == tokens.Count || codePoints[run.last] == ' ');
                if (!dynamic && isolated) continue;
                var token = tokens[run.first];
                var face = plan.signature[run.glyphStart].face;
                if (face == null)
                {
                    if (!dynamic) continue;
                    if (!BuildTmpPreparationRun(run, tmpMetrics, tmpPairs, nominal)) return;
                    jobRuns.Add(new PreparationRun { first = run.first, last = run.last,
                        glyphStart = run.glyphStart, glyphCount = run.glyphEnd - run.glyphStart, tmp = 1 });
                    continue;
                }
                if (!(face.Session is IJobTextShapingFont jobFont) || !jobFont.JobFunctions.IsCreated) return;
                var functions = jobFont.JobFunctions;
                if (shapingFunctions.IsCreated && (shapingFunctions.Shape.Value != functions.Shape.Value ||
                    shapingFunctions.Release.Value != functions.Release.Value)) return;
                shapingFunctions = functions;
                jobRuns.Add(new PreparationRun { font = jobFont.JobHandle, first = run.first, last = run.last,
                    glyphStart = run.glyphStart, glyphCount = run.glyphEnd - run.glyphStart,
                    direction = (run.level & 1) != 0 ? 5 : 4, script = scripts[run.first],
                    scale = token.style.size / token.style.font.faceInfo.pointSize * token.style.font.faceInfo.scale,
                    spacing = token.style.spacing });
                for (int i = run.first; i < run.last; i++)
                    if (codePoints[i] >= '0' && codePoints[i] <= '9')
                        for (uint digit = 0; digit < 10; digit++) if (!face.Nominal('0' + digit, out nominal[i * 10 + digit])) return;
            }
            var replacements = new List<PreparedGlyph>();
            var patches = new PreparationPatch[plan.patches.Length];
            int originalCount = layout.Count;
            try
            {
                for (int i = 0; i < patches.Length; i++)
                {
                    var patch = plan.patches[i]; var signature = plan.signature[patch.measuredIndex];

                    patches[i] = new PreparationPatch { point = signature.cluster, first = patch.first, count = patch.count, replacements = replacements.Count };
                    for (uint digit = 0; digit < 10; digit++)
                    {
                        uint glyph = nominal[signature.cluster * 10 + digit];
                        ResolvedGlyph resolved;
                        if (signature.face != null)
                        { if (glyph == 0 || !signature.face.Resolve(glyph, out resolved)) return; }
                        else
                        {
                            var choice = TmpPreparationChoice(signature.cluster, (int)digit);
                            if (choice.character == null) return;
                            resolved = choice;
                        }
                        AddPlacedCore(new Shaped { tokenIndex = patch.tokenIndex, glyph = resolved, advance = signature.advance,
                            x = signature.x, y = signature.y }, patch.x, patch.y);
                        if (layout.Count - originalCount != patch.count) return;
                        for (int j = originalCount; j < layout.Count; j++) replacements.Add(PreparedGlyph.From(layout[j]));
                        layout.RemoveRange(originalCount, layout.Count - originalCount);
                    }
                }
                var glyphs = new PreparedGlyph[plan.glyphs.Length];
                for (int i = 0; i < glyphs.Length; i++) glyphs[i] = PreparedGlyph.From(plan.glyphs[i]);
                var signatureData = new PreparationSignature[plan.signature.Length];
                for (int i = 0; i < signatureData.Length; i++)
                { var item = plan.signature[i]; signatureData[i] = new PreparationSignature { glyph = item.glyph, flags = item.flags, cluster = item.cluster, advance = item.advance, x = item.x, y = item.y }; }
                if (existing == null) while (preparationTemplates.Count >= 128) RetirePreparation(preparationLru.First.Value);
                var template = new PreparationTemplate { plan = plan, message = activePreparedMessage, analysis = activeParagraphAnalysis, key = PreparationKey(),
                    source = tokens.ToArray(), digits = activePreparedMessage.digitTokens,
                    glyphs = new NativeArray<PreparedGlyph>(glyphs, Allocator.Persistent), replacements = new NativeArray<PreparedGlyph>(replacements.ToArray(), Allocator.Persistent),
                    runs = new NativeArray<PreparationRun>(jobRuns.ToArray(), Allocator.Persistent), signature = new NativeArray<PreparationSignature>(signatureData, Allocator.Persistent),
                    tmpMetrics = new NativeArray<TmpPreparationMetric>(tmpMetrics, Allocator.Persistent), tmpPairs = new NativeArray<TmpPreparationPair>(tmpPairs.ToArray(), Allocator.Persistent),
                    patches = new NativeArray<PreparationPatch>(patches, Allocator.Persistent), nominal = new NativeArray<uint>(nominal, Allocator.Persistent) };
                if (existing == null)
                {
                    template.alternatives = new NativeArray<PreparationTemplateData>(MaxPreparationAlternatives, Allocator.Persistent);
                    template.alternatives[0] = template.Data;
                    preparationTemplates.Add(template.key, template);
                    compiledPreparations[(template.message, template.key.Item3, template.key.Item4, template.key.Item5)] = template;
                    template.cacheNode = preparationLru.AddLast(template);
                    preparationVariantCount++;
                }
                else
                {
                    existing.alternativeTemplates.Add(template);
                    existing.alternatives[existing.alternativeTemplates.Count] = template.Data;
                    preparationVariantCount++;
                    TouchPreparation(existing);
                }
            }
            finally { if (layout.Count > originalCount) layout.RemoveRange(originalCount, layout.Count - originalCount); }
        }

        private int TmpPreparationChoiceCount(int point) => codePoints[point] >= '0' && codePoints[point] <= '9' ? 10 : 1;
        private ResolvedGlyph TmpPreparationChoice(int point, int digit)
        {
            if (TmpPreparationChoiceCount(point) == 1) return tokens[point].glyph;
            for (int slot = 0; slot < activePreparedMessage.digitTokens.Length; slot++)
                if (activePreparedMessage.digitTokens[slot] == point && activePreparedMessage.digitChoices != null)
                    return activePreparedMessage.digitChoices[slot][digit].glyph;
            return default;
        }
        // Copy TMP metrics into immutable job data once. No TMP object/API is accessed by a worker.
        private bool BuildTmpPreparationRun(MeasuredRun run, TmpPreparationMetric[] metrics,
            List<TmpPreparationPair> pairs, uint[] nominal)
        {
            var boundaries = new int[run.last - run.first];
            for (int i = run.first; i < run.last; i++)
            {
                boundaries[i - run.first] = pairs.Count;
                if (i + 1 == run.last) continue;
                int leftCount = TmpPreparationChoiceCount(i), rightCount = TmpPreparationChoiceCount(i + 1);
                for (int left = 0; left < leftCount; left++) for (int right = 0; right < rightCount; right++)
                {
                    var a = TmpPreparationChoice(i, left); var b = TmpPreparationChoice(i + 1, right);
                    TmpPreparationPair item = default;
                    if (enableKerning && tokens[i].sprite == null && tokens[i + 1].sprite == null &&
                        SameStyle(tokens[i].style, tokens[i + 1].style) && a.character != null && b.character != null &&
                        Pair(a.font, a.character.glyph.index, b.character.glyph.index, out var record))
                    {
                        var first = record.firstAdjustmentRecord.glyphValueRecord;
                        var second = record.secondAdjustmentRecord.glyphValueRecord;
                        item.first = new Vector3(first.xPlacement, first.yPlacement, first.xAdvance);
                        item.second = new Vector3(second.xPlacement, second.yPlacement, second.xAdvance);
                    }
                    pairs.Add(item);
                }
            }
            for (int i = run.first; i < run.last; i++)
            {
                var token = tokens[i]; int count = TmpPreparationChoiceCount(i);
                for (int digit = 0; digit < count; digit++)
                {
                    var choice = TmpPreparationChoice(i, digit);
                    if (token.sprite == null && !IsControl(token.unicode) && choice.character == null) return false;
                    if (count == 10 && (!ReferenceEquals(choice.font, token.style.font) || choice.alternative != token.alternative)) return false;
                    float scale = token.sprite != null ? SpriteScale(token) : choice.character != null ? GlyphScale(token, choice) : 0;
                    float advance = token.sprite != null ? token.spriteCharacter.glyph.metrics.horizontalAdvance * scale :
                        choice.character != null ? choice.character.glyph.metrics.horizontalAdvance * scale : 0;
                    if (token.unicode == '\t') advance *= 4;
                    metrics[i * 10 + digit] = new TmpPreparationMetric { advance = advance, scale = scale, spacing = token.style.spacing,
                        before = i > run.first ? boundaries[i - run.first - 1] : -1,
                        after = i + 1 < run.last ? boundaries[i - run.first] : -1,
                        choices = count, nextChoices = i + 1 < run.last ? TmpPreparationChoiceCount(i + 1) : 1 };
                    nominal[i * 10 + digit] = choice.character != null ? choice.character.glyph.index : 0;
                }
            }
            return true;
        }

        private bool CanAddPreparationAlternative(PreparationTemplate existing, IList<MeasuredGlyph> signature, IList<MeasuredRun> runs, int glyphCount)
        {
            if (existing.alternativeTemplates.Count >= MaxPreparationAlternatives - 1 || glyphCount != existing.glyphs.Length ||
                signature.Count != existing.plan.signature.Length || runs.Count != existing.plan.runs.Length) return false;
            for (int i = 0; i < runs.Count; i++)
            {
                var a = runs[i]; var b = existing.plan.runs[i];
                if (a.first != b.first || a.last != b.last || a.glyphStart != b.glyphStart || a.glyphEnd != b.glyphEnd || a.level != b.level) return false;
            }
            for (int i = 0; i < signature.Count; i++)
                if (signature[i].face != existing.plan.signature[i].face || signature[i].cluster != existing.plan.signature[i].cluster) return false;
            for (int variant = 0; variant <= existing.alternativeTemplates.Count; variant++)
            {
                var previous = variant == 0 ? existing.plan : existing.alternativeTemplates[variant - 1].plan;
                bool same = true;
                for (int i = 0; same && i < signature.Count; i++)
                {
                    var a = signature[i]; var b = previous.signature[i];
                    same = a.flags == b.flags && a.advance == b.advance && a.x == b.x && a.y == b.y;
                }
                if (same) return false;
            }
            return true;
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        private unsafe struct PrepareTemplatesJob : IJobParallelFor
        {
            private static readonly ProfilerMarker WorkerPrepareMarker = new ProfilerMarker("BurstWord.BRG.PrepareWorker");
            [ReadOnly] public NativeArray<PreparationRequest> requests;
            [ReadOnly] public NativeArray<uint> points;
            [WriteOnly] public NativeArray<PreparationResult> results;
            [NativeDisableParallelForRestriction] public NativeArray<PreparedGlyph> output;
            [NativeDisableParallelForRestriction] public NativeArray<PreparationSignature> measurements;
            [NativeDisableParallelForRestriction] public NativeArray<IntPtr> buffers;
            [NativeSetThreadIndex] private int threadIndex;
            [ReadOnly] public NativeArray<NumericPreparedGlyph> numericGlyphs;
            [ReadOnly] public NativeArray<Vector3> numericFirstPairs, numericSecondPairs;
            public TextShapingFunctions shaping;
            public bool numericKerning;
            public int numericSize;
            public void Execute(int index)
            {
                var request = requests[index]; var template = request.template;
                if (request.numericCount>0) { PrepareNumber(index,request); return; }
                if (template.glyphs == null) return;
                using var scope = WorkerPrepareMarker.Auto();
                var text = (uint*)points.GetUnsafeReadOnlyPtr() + request.points;
                int calls = 0;
                bool compatible = true;
                var measured = (PreparationSignature*)measurements.GetUnsafePtr() + request.measurement;
                // Static runs keep their exact signature; contextual runs overwrite it below.
                UnsafeUtility.MemCpy(measured, template.signature, (long)template.signatureCount * UnsafeUtility.SizeOf<PreparationSignature>());
                IntPtr buffer = buffers[threadIndex];
                for (int r = 0; r < template.runCount; r++)
                {
                    var run = template.runs[r];
                    if (run.tmp != 0)
                    {
                        for (int i = 0; i < run.glyphCount; i++)
                        {
                            int at = run.glyphStart + i; var item = measured[at]; int point = item.cluster;
                            int digit = text[point] >= '0' && text[point] <= '9' ? (int)text[point] - '0' : 0;
                            var metric = template.tmpMetrics[point * 10 + digit];
                            float advance = metric.advance, x = 0, y = 0;
                            if (metric.before >= 0)
                            {
                                int previous = text[point - 1] >= '0' && text[point - 1] <= '9' ? (int)text[point - 1] - '0' : 0;
                                var value = template.tmpPairs[metric.before + previous * metric.choices + digit].second;
                                x += value.x * metric.scale; y += value.y * metric.scale; advance += value.z * metric.scale;
                            }
                            if (metric.after >= 0)
                            {
                                int next = text[point + 1] >= '0' && text[point + 1] <= '9' ? (int)text[point + 1] - '0' : 0;
                                var value = template.tmpPairs[metric.after + digit * metric.nextChoices + next].first;
                                x += value.x * metric.scale; y += value.y * metric.scale; advance += value.z * metric.scale;
                            }
                            item.advance = advance + metric.spacing; item.x = x; item.y = y; measured[at] = item;
                        }
                        continue;
                    }
                    TextShapingJobResult shapedResult = default;
                    shaping.Shape.Invoke(run.font, ref buffer, text, template.pointCount, run.first, run.last - run.first,
                        run.script, (template.features & 6) | (run.direction == 5 ? 1 : 0), ref shapedResult);
                    buffers[threadIndex] = buffer; calls++;
                    int count = shapedResult.Count;
                    var glyphs = shapedResult.Glyphs;
                    if (count != run.glyphCount) { results[index] = new PreparationResult { nativeCalls = calls }; return; }
                    for (int i = 0; i < count; i++)
                    {
                        var original = template.signature[run.glyphStart + i];
                        int cluster = (int)glyphs[i].Cluster;
                        if (cluster != original.cluster) { results[index] = new PreparationResult { nativeCalls = calls }; return; }
                        uint point = text[cluster];
                        uint expected = point >= '0' && point <= '9' ? template.nominal[cluster * 10 + point - '0'] : original.glyph;
                        float advance = glyphs[i].Advance * run.scale;
                        if (i + 1 == count || glyphs[i + 1].Cluster != glyphs[i].Cluster) advance += run.spacing;
                        compatible &= glyphs[i].GlyphId == expected;
                        float x = glyphs[i].OffsetX * run.scale, y = glyphs[i].OffsetY * run.scale;
                        original.glyph = glyphs[i].GlyphId; original.flags = glyphs[i].Flags; original.advance = advance; original.x = x; original.y = y;
                        measured[run.glyphStart + i] = original;
                    }
                }
                if (!compatible) { results[index] = new PreparationResult { nativeCalls = calls, measured = 1 }; return; }
                int selected = -1;
                for (int variant = 0; variant < template.alternativeCount; variant++)
                {
                    var candidate = template.alternatives[variant]; bool same = true;
                    for (int j = 0; same && j < template.signatureCount; j++)
                    {
                        var actual = measured[j]; var expected = candidate.signature[j];
                        same = actual.flags == expected.flags && actual.advance == expected.advance && actual.x == expected.x && actual.y == expected.y;
                    }
                    if (same) { selected = variant; template = candidate; break; }
                }
                if (selected < 0) { results[index] = new PreparationResult { nativeCalls = calls, measured = 1 }; return; }
                var destination = (PreparedGlyph*)output.GetUnsafePtr() + request.output;
                UnsafeUtility.MemCpy(destination, template.glyphs, (long)template.glyphCount * UnsafeUtility.SizeOf<PreparedGlyph>());
                for (int i = 0; i < template.patchCount; i++)
                {
                    var patch = template.patches[i]; int digit = (int)text[patch.point] - '0';
                    UnsafeUtility.MemCpy(destination + patch.first, template.replacements + patch.replacements + digit * patch.count,
                        (long)patch.count * UnsafeUtility.SizeOf<PreparedGlyph>());
                }
                results[index] = new PreparationResult { valid = 1, nativeCalls = calls, alternative = selected };
            }
            private void PrepareNumber(int index, PreparationRequest request)
            {
                using var scope = WorkerPrepareMarker.Auto();
                var text=(uint*)points.GetUnsafeReadOnlyPtr()+request.points;
                var glyphs=(NumericPreparedGlyph*)numericGlyphs.GetUnsafeReadOnlyPtr();
                var first=(Vector3*)numericFirstPairs.GetUnsafeReadOnlyPtr(); var second=(Vector3*)numericSecondPairs.GetUnsafeReadOnlyPtr();
                var destination=(PreparedGlyph*)output.GetUnsafePtr()+request.output;
                float x=0, ascent=float.MinValue, descent=float.MaxValue; int count=0;
                for (int i=0;i<request.numericCount;i++)
                {
                    int at=NumericIndex((char)text[i]); var glyph=glyphs[at]; Vector3 adjustment=default;
                    if (numericKerning)
                    {
                        if (i>0) adjustment+=second[NumericIndex((char)text[i-1])*12+at];
                        if (i+1<request.numericCount) adjustment+=first[at*12+NumericIndex((char)text[i+1])];
                    }
                    ascent=Mathf.Max(ascent,glyph.ascent); descent=Mathf.Min(descent,glyph.descent);
                    if (glyph.visible!=0)
                    {
                        var placed=glyph.geometry; placed.tint=request.color;
                        placed.rect.x=x+(placed.rect.x+adjustment.x)*glyph.scale;
                        placed.rect.y=glyph.baseline+(placed.rect.y+adjustment.y)*glyph.scale;
                        destination[count++]=placed;
                    }
                    x+=(glyph.advance+adjustment.z)*glyph.scale;
                }
                float offsetX=HorizontalStart(x,request.alignment,request.area);
                float offsetY=VerticalOffset(ascent-descent,request.alignment,request.area)-ascent;
                for (int i=0;i<count;i++) { destination[i].rect.x+=offsetX; destination[i].rect.y+=offsetY; }
                results[index]=new PreparationResult { valid=1, size=new Vector2(x,numericSize) };
            }
        }

        private void DisposePreparationJobs()
        {
            InvalidatePreparationTemplates(); DisposeRetiredPreparations();
            if (preparationBuffers.IsCreated)
            { foreach (var buffer in preparationBuffers) if (buffer != IntPtr.Zero && shapingFunctions.IsCreated) shapingFunctions.Release.Invoke(buffer); preparationBuffers.Dispose(); }
            if (preparationRequests.IsCreated) preparationRequests.Dispose(); if (preparationResults.IsCreated) preparationResults.Dispose();
            if (preparationPoints.IsCreated) preparationPoints.Dispose(); if (preparationOutput.IsCreated) preparationOutput.Dispose();
            if (preparationMeasurements.IsCreated) preparationMeasurements.Dispose();
            if (instanceLabelWrites.IsCreated) instanceLabelWrites.Dispose(); if (instanceWriteSlots.IsCreated) instanceWriteSlots.Dispose();
            if (instanceWriteGroups.IsCreated) instanceWriteGroups.Dispose(); if (instanceWritePages.IsCreated) instanceWritePages.Dispose();
            if (numericJobGlyphs.IsCreated) numericJobGlyphs.Dispose(); if (numericJobFirstPairs.IsCreated) numericJobFirstPairs.Dispose();
            if (numericJobSecondPairs.IsCreated) numericJobSecondPairs.Dispose(); nativeNumericVersion=-1;
            Array.Clear(preparationQueue, 0, preparationQueue.Length);
        }
    }
}
