using System;
using System.Collections.Generic;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        // Disabling restores reference parsing + shaping for same-process A/B tests.
        [NonSerialized] public bool useMeasuredLayout = true;
        // Optional benchmark instrumentation. Production renderers pay no clock calls.
        [NonSerialized] public bool collectLayoutTimings;
        internal readonly long[] layoutTimingTicks = new long[7];
        private LayoutTimingScope LayoutTiming(int stage) => new LayoutTimingScope(this, stage);
        private readonly struct LayoutTimingScope : IDisposable
        {
            private readonly BrgDamageTextRenderer owner;
            private readonly int stage;
            private readonly long start;
            public LayoutTimingScope(BrgDamageTextRenderer renderer, int stage)
            {
                owner = renderer.collectLayoutTimings ? renderer : null; this.stage = stage;
                start = ReferenceEquals(owner, null) ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
            }
            public void Dispose()
            {
                if (!ReferenceEquals(owner, null)) owner.layoutTimingTicks[stage] += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            }
        }
        public long ParsedCacheHits { get; private set; }
        public long ParsedCacheMisses { get; private set; }
        public long UnicodeCacheHits { get; private set; }
        public long UnicodeCacheMisses { get; private set; }
        public long FixedLineCacheHits { get; private set; }
        public long NativeCacheHits { get; private set; }
        public long NativeShapeCalls { get; private set; }
        public long MeasuredLineHits { get; private set; }
        public long MeasuredLineFallbacks { get; private set; }
        public long MeasuredPlanHits { get; private set; }
        public long MeasuredRunCacheHits { get; private set; }

        private struct MeasuredGlyph
        {
            public ShapingFace face;
            public ResolvedGlyph resolved;
            public uint glyph, flags;
            public int cluster;
            public float advance, x, y;
        }
        private struct MeasuredRun { public int first, last, glyphStart, glyphEnd; public sbyte level; }
        private readonly List<MeasuredGlyph> measuredGlyphs = new List<MeasuredGlyph>(128);
        private readonly List<MeasuredRun> measuredRuns = new List<MeasuredRun>(32);
        private bool[] measuredBoundaries = new bool[128];
        private int measuredStart, measuredEnd;
        private sealed class MeasuredRunCache
        {
            public float[] advances;
            public bool[] boundaries;
            public MeasuredGlyph[] glyphs;
        }
        private readonly Dictionary<(ParsedMessage, NativeShapeGlyph[], int, int), MeasuredRunCache> measuredRunCache =
            new Dictionary<(ParsedMessage, NativeShapeGlyph[], int, int), MeasuredRunCache>();
        private int measuredRunGlyphCount;
        private bool TryCachedMeasurement(NativeShapeGlyph[] native, int first, int last)
        {
            if (activePreparedMessage == null || ReferenceEquals(native, nativeScratch) ||
                !measuredRunCache.TryGetValue((activePreparedMessage, native, first, last), out var entry)) return false;
            Array.Copy(entry.advances, 0, advances, first, last - first);
            Array.Copy(entry.boundaries, 0, measuredBoundaries, first, last - first);
            measuredGlyphs.AddRange(entry.glyphs); MeasuredRunCacheHits++; return true;
        }
        private void StoreCachedMeasurement(NativeShapeGlyph[] native, int first, int last, int initial)
        {
            if (activePreparedMessage == null || ReferenceEquals(native, nativeScratch) || last - first > 256) return;
            var key = (activePreparedMessage, native, first, last);
            if (measuredRunCache.ContainsKey(key)) return;
            int count = measuredGlyphs.Count - initial;
            if (measuredRunCache.Count >= 256 || measuredRunGlyphCount + count > 8192) { measuredRunCache.Clear(); measuredRunGlyphCount = 0; }
            var entry = new MeasuredRunCache { advances = new float[last - first], boundaries = new bool[last - first], glyphs = new MeasuredGlyph[count] };
            Array.Copy(advances, first, entry.advances, 0, entry.advances.Length); Array.Copy(measuredBoundaries, first, entry.boundaries, 0, entry.boundaries.Length);
            measuredGlyphs.CopyTo(initial, entry.glyphs, 0, count); measuredRunCache[key] = entry; measuredRunGlyphCount += count;
        }

        private void BeginMeasurement(int start, int end)
        {
            measuredGlyphs.Clear(); measuredRuns.Clear(); measuredStart = start; measuredEnd = end;
            if (measuredBoundaries.Length <= end) Array.Resize(ref measuredBoundaries, UnityEngine.Mathf.NextPowerOfTwo(end + 1));
            Array.Clear(measuredBoundaries, start, end - start + 1);
            measuredBoundaries[start] = measuredBoundaries[end] = true;
        }
        private bool TryMeasuredLine(int start, int end)
        {
            // HarfBuzz UNSAFE_TO_BREAK is produced by default. Safe boundaries preserve
            // exactly the glyphs/positions of shaping the two pieces independently.
            if (!useMeasuredLayout || start < measuredStart || end > measuredEnd || !measuredBoundaries[start] || !measuredBoundaries[end])
            { MeasuredLineFallbacks++; return false; }
            shaped.Clear(); runs.Clear();
            foreach (var run in measuredRuns)
            {
                if (run.last <= start || run.first >= end) continue;
                int initial = shaped.Count; float width = 0;
                for (int i = run.glyphStart; i < run.glyphEnd; i++)
                {
                    var item = measuredGlyphs[i];
                    if (item.cluster < start || item.cluster >= end) continue;
                    var resolved = item.resolved;
                    if (item.face != null)
                    {
                        if (item.face.Nominal(codePoints[item.cluster], out var nominal) && nominal != item.glyph) LastGlyphSubstitutionCount++;
                        if (!item.face.Resolve(item.glyph, out resolved)) return false;
                        LastLayoutUsedShaping = true;
                    }
                    int tokenIndex = item.face == null ? item.cluster : Math.Max(run.first, start);
                    shaped.Add(new Shaped { tokenIndex = tokenIndex, measuredIndex = i, glyph = resolved, advance = item.advance, x = item.x, y = item.y });
                    width += item.advance;
                }
                runs.Add(new Run { first = initial, count = shaped.Count - initial, level = run.level, width = width });
            }
            MeasuredLineHits++; return true;
        }

        private struct MeasuredPatch
        {
            public int measuredIndex, tokenIndex, first, count;
            public float x, y;
            public bool alternative;
            public TextStyle style;
        }
        private sealed class MeasuredPlan
        {
            public MeasuredGlyph[] signature;
            public MeasuredRun[] runs;
            public PositionedGlyph[] glyphs;
            public MeasuredPatch[] patches;
            public int[] boundaries;
            public int lines, substitutions;
            public UnityEngine.Vector2 size;
            public bool shaping;
        }
        private readonly Dictionary<(ParsedMessage, ParagraphAnalysis, float, int), MeasuredPlan> measuredPlans =
            new Dictionary<(ParsedMessage, ParagraphAnalysis, float, int), MeasuredPlan>();
        private readonly List<MeasuredPatch> measuredPatches = new List<MeasuredPatch>(16);
        private readonly List<int> measuredPlanBoundaries = new List<int>(16);
        private int measuredPlanGlyphCount;
        private bool captureMeasuredPlan, measuredPlanEligible;
        private (ParsedMessage, ParagraphAnalysis, float, int) MeasuredPlanKey() => (activePreparedMessage, activeParagraphAnalysis, wrapWidth,
            (enableShaping && !shapingUnavailable ? 1 : 0) | (enableKerning ? 2 : 0) | (enableLigatures ? 4 : 0));

        private bool TryCompleteMeasuredLayout()
        {
            if (activePreparedMessage == null || activeParagraphAnalysis == null || !measuredPlans.TryGetValue(MeasuredPlanKey(), out var plan) ||
                measuredGlyphs.Count != plan.signature.Length || measuredRuns.Count != plan.runs.Length) return false;
            foreach (int boundary in plan.boundaries) if (!measuredBoundaries[boundary]) return false;
            for (int i = 0; i < measuredRuns.Count; i++)
            {
                var a = measuredRuns[i]; var b = plan.runs[i];
                if (a.first != b.first || a.last != b.last || a.glyphStart != b.glyphStart || a.glyphEnd != b.glyphEnd || a.level != b.level) return false;
            }
            // Verify the actual shaped metrics on EVERY emission. Equal digit count alone
            // is insufficient for proportional digits, kerning, ligatures or contextual fonts.
            for (int i = 0; i < measuredGlyphs.Count; i++)
            {
                var a = measuredGlyphs[i]; var b = plan.signature[i];
                if (a.face != b.face || a.cluster != b.cluster || a.advance != b.advance || a.x != b.x || a.y != b.y) return false;
                if (codePoints[a.cluster] >= '0' && codePoints[a.cluster] <= '9' && a.face != null &&
                    (!a.face.Nominal(codePoints[a.cluster], out var digitNominal) || digitNominal != a.glyph)) return false;
                bool changed = a.face != null ? a.glyph != b.glyph : !ReferenceEquals(a.resolved.character, b.resolved.character);
                if (!changed) continue;
                if (codePoints[a.cluster] < '0' || codePoints[a.cluster] > '9') return false;
                if (a.face != null && (!a.face.Nominal(codePoints[a.cluster], out var nominal) || nominal != a.glyph)) return false;
                if (a.face == null && !ReferenceEquals(a.resolved.font, b.resolved.font)) return false;
                bool patchable = false;
                foreach (var patch in plan.patches) if (patch.measuredIndex == i) { patchable = true; break; }
                if (!patchable) return false;
            }
            foreach (var patch in plan.patches)
                if (patch.alternative != tokens[patch.tokenIndex].alternative || !SameStyle(patch.style, tokens[patch.tokenIndex].style)) return false;
            layout.AddRange(plan.glyphs);
            foreach (var patch in plan.patches)
            {
                var item = measuredGlyphs[patch.measuredIndex]; var original = plan.signature[patch.measuredIndex];
                if (item.face != null ? item.glyph == original.glyph : ReferenceEquals(item.resolved.character, original.resolved.character)) continue;
                var resolved = item.resolved;
                if (item.face != null && !item.face.Resolve(item.glyph, out resolved)) { layout.Clear(); return false; }
                int begin = layout.Count;
                AddPlacedCore(new Shaped { tokenIndex = patch.tokenIndex, glyph = resolved, advance = item.advance, x = item.x, y = item.y }, patch.x, patch.y);
                int added = layout.Count - begin;
                if (added != patch.count) { layout.Clear(); return false; }
                for (int j = 0; j < added; j++) layout[patch.first + j] = layout[begin + j];
                layout.RemoveRange(begin, added);
            }
            LastLayoutLineCount = plan.lines; LastLayoutSize = plan.size;
            LastGlyphSubstitutionCount = plan.substitutions; LastLayoutUsedShaping = plan.shaping;
            StorePreparationTemplate(plan);
            MeasuredPlanHits++; return true;
        }
        private void StoreCompleteMeasuredLayout(float center)
        {
            if (activePreparedMessage == null || activeParagraphAnalysis == null || measuredGlyphs.Count > 256 || layout.Count > 256) return;
            foreach (var item in measuredGlyphs) if (codePoints[item.cluster] >= '0' && codePoints[item.cluster] <= '9' && item.face != null &&
                (!item.face.Nominal(codePoints[item.cluster], out var nominal) || nominal != item.glyph)) return;
            var key = MeasuredPlanKey();
            // Retain one measured signature per template/width instead of churning the
            // cache when arbitrary proportional values continuously change their widths.
            bool exists = measuredPlans.ContainsKey(key);
            if (exists && (!enablePreparationJobs || !usePreparationAlternatives || !preparationTemplates.TryGetValue(PreparationKey(), out var existing) ||
                !CanAddPreparationAlternative(existing, measuredGlyphs, measuredRuns, layout.Count))) return;
            if (!exists && (measuredPlans.Count >= 128 || measuredPlanGlyphCount + layout.Count > 8192)) { measuredPlans.Clear(); measuredPlanGlyphCount = 0; }
            var patches = measuredPatches.ToArray();
            for (int i = 0; i < patches.Length; i++) patches[i].y += center;
            var plan = new MeasuredPlan { signature = measuredGlyphs.ToArray(), runs = measuredRuns.ToArray(), glyphs = layout.ToArray(), patches = patches, boundaries = measuredPlanBoundaries.ToArray(),
                lines = LastLayoutLineCount, size = LastLayoutSize, substitutions = LastGlyphSubstitutionCount, shaping = LastLayoutUsedShaping };
            if (!exists) { measuredPlans[key] = plan; measuredPlanGlyphCount += layout.Count; }
            StorePreparationTemplate(plan);
        }
    }
}
