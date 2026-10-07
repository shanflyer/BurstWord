using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using BurstWord.Typography;
using BurstWord.Internal.RichTextKit;
using BurstWord.Internal.RichTextKit.Utils;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
#if BURSTWORD_UGUI_TMP || BURSTWORD_TEXTCORE_PAIRS
using GlyphPairRecord = UnityEngine.TextCore.LowLevel.GlyphPairAdjustmentRecord;
#else
using GlyphPairRecord = TMPro.TMP_GlyphPairAdjustmentRecord;
#endif

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        [Header("Typography")]
        public bool richText = true;
        public bool enableKerning = true;
        public bool enableLigatures = true;
        private ITextShaper runtimeShaper;
        private ITextShaper SelectedShaper => runtimeShaper;
        private bool ShapingEnabled => SelectedShaper != null && !shapingUnavailable;
        private bool UsesShapingCallback => runtimeShaper is CallbackTextShaper;
        public string ShaperName => ShapingEnabled ? SelectedShaper.Name : "TMP glyph data";
        /// <summary>Register optional application-owned shaping on the main thread. Null skips shaping.
        /// Clears live text and cached layouts. Re-register after changing callback settings.</summary>
        public void SetTextShaper(TextShapingCallback callback)
            => SetTextShaperProvider(callback == null ? null : new CallbackTextShaper(callback));

        /// <summary>Advanced font-session/Job adapter integration. Not required for a managed callback.
        /// Switch on the main thread; clears live text, sessions and all cached layouts.</summary>
        public void SetTextShaperProvider(ITextShaper provider)
        {
            bool restart = isActiveAndEnabled;
            if (restart) enabled = false;
            runtimeShaper = provider;
            if (restart) enabled = true;
        }
        [Tooltip("Width in reference-resolution pixels; zero disables automatic wrapping.")]
        [Min(0)] public float wrapWidth;
        [UnityEngine.Serialization.FormerlySerializedAs("additionalFonts")]
        [Tooltip("Font choices: index 0 is the default font, 1..N follow this list. Each font uses its own material and TMP fallback chain.")]
        public TMP_FontAsset[] fonts;
        public bool useSprites = true;
        public TMP_SpriteAsset spriteAsset;
        public TMP_SpriteAsset[] additionalSpriteAssets;
        public BrgFontSources fontSources;
        private BrgFontSources[] discoveredFontSources;
        [HideInInspector]
        public bool tightGlyphBounds = true;

        public int LastLayoutLineCount { get; private set; }
        public Vector2 LastLayoutSize { get; private set; }
        public bool LastLayoutUsedShaping { get; private set; }
        public int LastGlyphSubstitutionCount { get; private set; }
        public long UnavailableShapingCount { get; private set; }
        public long MissingSpriteCount { get; private set; }

        private struct TextStyle
        {
            public TMP_FontAsset font;
            public Color color;
            public float size, baseline, spacing;
            public bool bold, italic, underline, strike, noBreak;
        }
        private struct Token
        {
            public uint unicode;
            public TextStyle style;
            public ResolvedGlyph glyph;
            public TMP_SpriteAsset sprite;
            public TMP_SpriteCharacter spriteCharacter;
            public bool alternative;
            public int sourceIndex;
            public TMP_FontAsset requestedFont;
        }
        private struct StyleFrame { public string tag; public TextStyle previous; }
        private struct Run { public int first, count; public sbyte level; public float width; }
        private struct Shaped { public int tokenIndex, measuredIndex; public ResolvedGlyph glyph; public float advance, x, y; }
        private readonly List<Token> tokens = new List<Token>(64);
        private readonly List<StyleFrame> styleFrames = new List<StyleFrame>(16);
        private readonly List<Shaped> shaped = new List<Shaped>(64);
        private readonly List<Run> runs = new List<Run>(16);
        private readonly List<int> visualRuns = new List<int>(16);
        private readonly Dictionary<TMP_FontAsset, ShapingFace> shapingFaces = new Dictionary<TMP_FontAsset, ShapingFace>();
        private readonly Dictionary<(long, uint, bool, bool), ResolvedGlyph> styledGlyphs = new Dictionary<(long, uint, bool, bool), ResolvedGlyph>();
        private readonly Bidi bidi = new Bidi();
        private readonly BidiData bidiData = new BidiData();
        private readonly LineBreaker lineBreaker = new LineBreaker();
        private uint[] codePoints = new uint[128], scripts = new uint[128];
        private int[] bidiPoints = new int[128];
        private sbyte[] levels = new sbyte[128];
        private float[] advances = new float[128];
        private bool[] breakAfter = new bool[128];
        private readonly List<TextShapingGlyph> shapingOutput = new List<TextShapingGlyph>(128);
        private TextShapingFunctions shapingFunctions;
        private bool shapingUnavailable, warnedSource;
        private Texture2D solidTexture;
        private readonly Dictionary<(long, long, long), int> batchLookup = new Dictionary<(long, long, long), int>();
        private static readonly Dictionary<GlyphRenderMode, int> atlasModes = new Dictionary<GlyphRenderMode, int>();
        private readonly Dictionary<uint, uint> unicodeScripts = new Dictionary<uint, uint>();
        private readonly Dictionary<(long, uint), (TMP_SpriteAsset asset, int index)> spriteUnicodeCache =
            new Dictionary<(long, uint), (TMP_SpriteAsset asset, int index)>();
        private readonly struct GeometryKey : IEquatable<GeometryKey>
        {
            private readonly long asset, material;
            private readonly int flags;
            private readonly uint glyph;
            private readonly float size;
            public GeometryKey(long asset, uint glyph, long material, float size, bool bold, bool italic, bool alternative)
            { this.asset = asset; this.glyph = glyph; this.material = material; this.size = size; flags = (bold ? 1 : 0) | (italic ? 2 : 0) | (alternative ? 4 : 0); }
            public bool Equals(GeometryKey other) => asset == other.asset && glyph == other.glyph && material == other.material && size.Equals(other.size) && flags == other.flags;
            public override bool Equals(object other) => other is GeometryKey key && Equals(key);
            public override int GetHashCode() => unchecked((int)((((asset * 397 ^ (int)glyph) * 397 ^ material) * 397 ^ size.GetHashCode()) * 397 ^ flags));
        }
        private readonly Dictionary<GeometryKey, PositionedGlyph> geometry = new Dictionary<GeometryKey, PositionedGlyph>();
        private readonly Dictionary<(long, long, bool), float> materialPadding = new Dictionary<(long, long, bool), float>();
        private bool cachedTightGlyphBounds = true;
        private float RequiredPadding(TMP_FontAsset asset, Material material, bool syntheticBold)
        {
            if (!tightGlyphBounds) return asset.atlasPadding;
            var key = (BrgObjectIdentity.Of(asset), BrgObjectIdentity.Of(material), syntheticBold);
            if (materialPadding.TryGetValue(key, out float cached)) return cached;
            float Read(string property, float fallback = 0) => material.HasProperty(property) ? material.GetFloat(property) : fallback;
            float gradient = Read("_GradientScale", asset.atlasPadding + 1);
            float a = Read("_ScaleRatioA", 1), b = Read("_ScaleRatioB", 1), c = Read("_ScaleRatioC", 1);
            float bold = syntheticBold ? (asset.boldStyle - asset.normalStyle) / 4 : 0;
            float face = Mathf.Max(0, 2 * (Read("_WeightNormal") * a / 4 + bold * a) + Read("_FaceDilate") * a);
            float extent = face + Mathf.Max(0, Read("_OutlineWidth") + Read("_OutlineSoftness")) * a;
            if (material.IsKeywordEnabled("UNDERLAY_ON") || material.IsKeywordEnabled("UNDERLAY_INNER"))
                extent = Mathf.Max(extent, face + (Mathf.Max(Mathf.Abs(Read("_UnderlayOffsetX")), Mathf.Abs(Read("_UnderlayOffsetY"))) +
                    Mathf.Max(0, Read("_UnderlayDilate")) + Mathf.Max(0, Read("_UnderlaySoftness"))) * c);
            if (material.IsKeywordEnabled("GLOW_ON"))
                extent = Mathf.Max(extent, face + (Mathf.Abs(Read("_GlowOffset")) + Mathf.Max(0, Read("_GlowOuter")) + Mathf.Max(0, Read("_GlowInner"))) * b);
            float padding = Mathf.Min(asset.atlasPadding, Mathf.Ceil(1.25f + gradient * extent));
            materialPadding[key] = padding;
            return padding;
        }
        private sealed class PairCache
        {
            public int count = -1;
            public readonly Dictionary<ulong, GlyphPairRecord> records = new Dictionary<ulong, GlyphPairRecord>();
        }
        private readonly Dictionary<TMP_FontAsset, PairCache> pairCaches = new Dictionary<TMP_FontAsset, PairCache>();
        private bool Pair(TMP_FontAsset asset, uint left, uint right, out GlyphPairRecord record)
        {
            var table = asset.fontFeatureTable.glyphPairAdjustmentRecords;
            if (!pairCaches.TryGetValue(asset, out var cache)) { cache = new PairCache(); pairCaches.Add(asset, cache); }
            if (cache.count != table.Count)
            {
                cache.records.Clear(); cache.count = table.Count;
                foreach (var pair in table) cache.records[(ulong)pair.firstAdjustmentRecord.glyphIndex << 32 | pair.secondAdjustmentRecord.glyphIndex] = pair;
            }
            return cache.records.TryGetValue((ulong)left << 32 | right, out record);
        }

        private static int AtlasMode(TMP_FontAsset asset)
        {
            if (atlasModes.TryGetValue(asset.atlasRenderMode, out int value)) return value;
            string mode = asset.atlasRenderMode.ToString();
            value = mode.StartsWith("SDF", StringComparison.Ordinal) ? 0 : mode.StartsWith("COLOR", StringComparison.Ordinal) ? 2 : 1;
            atlasModes.Add(asset.atlasRenderMode, value);
            return value;
        }

        private static readonly string[] MaterialFloats = { "_FaceDilate", "_OutlineWidth", "_OutlineSoftness", "_UnderlayOffsetX", "_UnderlayOffsetY", "_UnderlayDilate", "_UnderlaySoftness", "_GlowOffset", "_GlowInner", "_GlowOuter", "_GlowPower", "_GradientScale", "_ScaleRatioA", "_ScaleRatioB", "_ScaleRatioC", "_WeightNormal", "_WeightBold" };
        private static readonly string[] MaterialColors = { "_FaceColor", "_OutlineColor", "_UnderlayColor", "_GlowColor" };
        private static void CopyFontMaterial(Material source, Material target)
        {
            foreach (string property in MaterialFloats) if (source.HasProperty(property)) target.SetFloat(property, source.GetFloat(property));
            foreach (string property in MaterialColors) if (source.HasProperty(property)) target.SetColor(property, source.GetColor(property));
            target.SetFloat("_UseUnderlay", source.IsKeywordEnabled("UNDERLAY_ON") || source.IsKeywordEnabled("UNDERLAY_INNER") ? 1 : 0);
            target.SetFloat("_InnerUnderlay", source.IsKeywordEnabled("UNDERLAY_INNER") ? 1 : 0);
            target.SetFloat("_UseGlow", source.IsKeywordEnabled("GLOW_ON") ? 1 : 0);
        }

        private int Batch(TMP_FontAsset asset, Texture texture, Material material, int mode)
        {
            var key = (ReferenceEquals(asset, null) ? 0 : BrgObjectIdentity.Of(asset), BrgObjectIdentity.Of(texture), ReferenceEquals(material, null) ? 0 : BrgObjectIdentity.Of(material));
            if (batchLookup.TryGetValue(key, out int group)) return group;
            group = atlasBatches.Count; atlasBatches.Add(new AtlasBatch(this, glyphShader, asset, texture, material, mode));
            batchLookup.Add(key, group);
            return group;
        }

        private bool BuildLayout(string text, Color color, bool preparedTokens = false)
        {
            captureMeasuredPlan = false;
            if (cachedTightGlyphBounds != tightGlyphBounds) { geometry.Clear(); wrappedLines.Clear(); wrappedLineGlyphCount = 0; measuredPlans.Clear(); measuredPlanGlyphCount = 0; cachedTightGlyphBounds = tightGlyphBounds; }
            LastLayoutUsedShaping = false;
            LastGlyphSubstitutionCount = 0;
            // Damage numbers avoid tag parsing, Unicode analysis and the native shaping call.
            bool numeric = wrapWidth <= 0 && CanUseNumericLayout;
            for (int i = 0; numeric && i < text.Length; i++) numeric = text[i] >= '0' && text[i] <= '9' || text[i] == '-' || text[i] == '+';
            if (numeric)
            {
                bool result = BuildBasicLayout(text, color);
                LastLayoutLineCount = 1;
                LastLayoutSize = new Vector2(lineWidths.Count > 0 ? lineWidths[0] : 0, LayoutFontSize);
                return result;
            }
            layout.Clear();
            if (!preparedTokens)
            {
                tokens.Clear(); styleFrames.Clear();
                using (LayoutTiming(0)) if (!ParseCached(text, color)) return false;
            }
            if (tokens.Count == 0) return false;
            if (wrapWidth <= 0 && BuildStyledNumbers()) return true;
            EnsureTextBuffers(tokens.Count + 1);
            for (int i = 0; i < tokens.Count; i++) { codePoints[i] = tokens[i].unicode; bidiPoints[i] = (int)codePoints[i]; }
            float y = 0, maxWidth = 0;
            int lines = 0, paragraphStart = 0;
            while (paragraphStart < tokens.Count)
            {
                int paragraphEnd = paragraphStart;
                while (paragraphEnd < tokens.Count && tokens[paragraphEnd].unicode != '\n') paragraphEnd++;
                int length = paragraphEnd - paragraphStart;
                if (length == 0) { y -= LayoutFontSize * 1.2f; lines++; paragraphStart = paragraphEnd + 1; continue; }
                using (LayoutTiming(1)) AnalyzeParagraphCached(paragraphStart, paragraphEnd);
                bool measureParagraph = wrapWidth > 0 || reusedBatchMeasurement != null || (enablePreparationJobs && useMeasuredLayout &&
                    paragraphStart == 0 && paragraphEnd == tokens.Count && activePreparedMessage != null &&
                    !preparationAttempted.Contains(PreparationKey()));
                if (measureParagraph)
                {
                    Array.Clear(advances, paragraphStart, length);
                    // Only wrapped paragraphs need a separate measurement pass.
                    bool restored;
                    using (LayoutTiming(2))
                    {
                        restored = TryRestoreBatchMeasurement(paragraphStart, paragraphEnd);
                        if (!restored && !ShapeRuns(paragraphStart, paragraphEnd, true)) return false;
                    }
                    if (useMeasuredLayout && paragraphStart == 0 && paragraphEnd == tokens.Count)
                    {
                        if (!restored && TryCompleteMeasuredLayout()) return true;
                        captureMeasuredPlan = true; measuredPlanEligible = true; measuredPatches.Clear(); measuredPlanBoundaries.Clear();
                    }
                }
                int lineStart = paragraphStart;
                while (lineStart < paragraphEnd)
                {
                    int lineEnd = paragraphEnd, lastBreak = -1;
                    float width = 0;
                    using (LayoutTiming(3)) if (wrapWidth > 0)
                    {
                        for (int i = lineStart; i < paragraphEnd; i++)
                        {
                            width += advances[i];
                            if (i > lineStart && breakAfter[i] && !tokens[i - 1].style.noBreak) lastBreak = i;
                            if (width > LayoutWrapWidth && i > lineStart)
                            {
                                // Emergency wrapping only at grapheme AND shaped-cluster boundaries.
                                if (lastBreak > lineStart) { lineEnd = lastBreak; break; }
                                if (advances[i] > 0 && GraphemeClusterAlgorithm.IsBoundary(new Slice<int>(bidiPoints, paragraphStart, length), i - paragraphStart) && !tokens[i].style.noBreak)
                                { lineEnd = i; break; }
                            }
                        }
                    }
                    int visibleEnd = lineEnd;
                    while (visibleEnd > lineStart && (tokens[visibleEnd - 1].unicode == ' ' || tokens[visibleEnd - 1].unicode == '\t')) visibleEnd--;
                    if (captureMeasuredPlan && (!measuredBoundaries[lineStart] || !measuredBoundaries[visibleEnd])) measuredPlanEligible = false;
                    if (captureMeasuredPlan) { measuredPlanBoundaries.Add(lineStart); measuredPlanBoundaries.Add(visibleEnd); }
                    (ParsedMessage, ParagraphAnalysis, int, int, int, float) lineKey = default;
                    bool cacheLine = wrapWidth > 0 && WrappedLineKey(lineStart, visibleEnd, out lineKey);
                    if (cacheLine && TryWrappedLine(lineKey, ref y, out float cachedWidth))
                    {
                        maxWidth = Mathf.Max(maxWidth, cachedWidth); lines++;
                        lineStart = lineEnd;
                        while (lineStart < paragraphEnd && tokens[lineStart].unicode == ' ') lineStart++;
                        continue;
                    }
                    int layoutStart = layout.Count, substitutions = LastGlyphSubstitutionCount;
                    float lineY = y;
                    bool previousShaping = LastLayoutUsedShaping;
                    LastLayoutUsedShaping = false;
                    using (LayoutTiming(4)) if (!(measureParagraph && TryMeasuredLine(lineStart, visibleEnd)) && !ShapeRuns(lineStart, visibleEnd, false)) return false;
                    bool lineShaping = LastLayoutUsedShaping;
                    LastLayoutUsedShaping |= previousShaping;
                    float lineWidth = 0, ascent = LayoutFontSize * 0.8f, descent = -LayoutFontSize * 0.2f;
                    foreach (var run in runs) lineWidth += run.width;
                    foreach (var run in runs)
                    {
                        if (run.count == 0) continue;
                        var style = tokens[shaped[run.first].tokenIndex].style;
                        var face = style.font.faceInfo;
                        float scale = style.size / face.pointSize * face.scale;
                        ascent = Mathf.Max(ascent, face.ascentLine * scale + style.baseline);
                        descent = Mathf.Min(descent, face.descentLine * scale + style.baseline);
                    }
                    y -= ascent;
                    float x = HorizontalStart(lineWidth);
                    ReorderRuns();
                    using (LayoutTiming(5)) foreach (int runIndex in visualRuns)
                    {
                        var run = runs[runIndex];
                        for (int i = run.first; i < run.first + run.count; i++)
                        {
                            var item = shaped[i];
                            AddPlaced(item, x + item.x, y + item.y);
                            x += item.advance;
                        }
                    }
                    y += descent - LayoutFontSize * 0.2f;
                    if (cacheLine) StoreWrappedLine(lineKey, layoutStart, lineY, y - lineY, lineWidth,
                        LastGlyphSubstitutionCount - substitutions, lineShaping);
                    maxWidth = Mathf.Max(maxWidth, lineWidth); lines++;
                    lineStart = lineEnd;
                    while (lineStart < paragraphEnd && tokens[lineStart].unicode == ' ') lineStart++;
                }
                paragraphStart = paragraphEnd + 1;
            }
            if (tokens[tokens.Count - 1].unicode == '\n') { y -= LayoutFontSize * 1.2f; lines++; }
            float center = VerticalOffset(-y);
            for (int i = 0; i < layout.Count; i++) { var item = layout[i]; item.rect.y += center; layout[i] = item; }
            LastLayoutLineCount = lines; LastLayoutSize = new Vector2(maxWidth, -y);
            if (captureMeasuredPlan && measuredPlanEligible) StoreCompleteMeasuredLayout(center);
            captureMeasuredPlan = false;
            return true;
        }

        private void EnsureTextBuffers(int count)
        {
            if (codePoints.Length >= count) return;
            int size = Mathf.NextPowerOfTwo(count);
            Array.Resize(ref codePoints, size); Array.Resize(ref bidiPoints, size); Array.Resize(ref scripts, size);
            Array.Resize(ref levels, size); Array.Resize(ref advances, size); Array.Resize(ref breakAfter, size);
        }

        private bool Parse(string text, Color color)
        {
            var style = new TextStyle { font = LayoutFont, size = LayoutFontSize, color = color };
            bool noParse = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (richText && text[i] == '<')
                {
                    int end = text.IndexOf('>', i + 1);
                    if (end >= 0)
                    {
                        string tag = text.Substring(i + 1, end - i - 1).Trim();
                        if (tag.Equals("/noparse", StringComparison.OrdinalIgnoreCase)) { noParse = false; i = end; continue; }
                        if (!noParse && tag.Equals("noparse", StringComparison.OrdinalIgnoreCase)) { noParse = true; i = end; continue; }
                        if (!noParse && ParseTag(tag, ref style)) { i = end; continue; }
                    }
                }
                uint unicode = text[i];
                if (unicode == '\r') continue;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) unicode = (uint)char.ConvertToUtf32(text[i], text[++i]);
                if (unicode == '\n') { tokens.Add(new Token { unicode = unicode, style = style }); continue; }
                if (TrySpriteUnicode(unicode, style)) continue;
                var token = new Token { unicode = unicode, style = style, sourceIndex = i, requestedFont = style.font };
                // Formatting controls are consumed by BiDi/shaping, without a missing-glyph box.
                if (IsControl(unicode)) { tokens.Add(token); continue; }
                if (!ResolveStyled(unicode == '\t' ? ' ' : unicode, style, out token.glyph, out token.alternative))
                {
                    MissingGlyphCount++;
                    if (!ResolveStyled(0xfffd, style, out token.glyph, out token.alternative) && !ResolveStyled('?', style, out token.glyph, out token.alternative)) return false;
                    token.unicode = token.glyph.character.unicode;
                }
                token.style.font = token.glyph.font;
                tokens.Add(token);
            }
            return true;
        }

        private static bool IsControl(uint c) => c == 0x200c || c == 0x200d || c == 0x200e || c == 0x200f || c >= 0x202a && c <= 0x202e || c >= 0x2066 && c <= 0x2069 || c >= 0xfe00 && c <= 0xfe0f || c == 0x00ad || c == 0x200b;
        private static string Value(string tag, string name)
        {
            int at = tag.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            at += name.Length + 1;
            if (at >= tag.Length) return "";
            if (tag[at] == '\"' || tag[at] == '\'') { char q = tag[at++]; int end = tag.IndexOf(q, at); return end < 0 ? tag.Substring(at) : tag.Substring(at, end - at); }
            int space = tag.IndexOf(' ', at); return space < 0 ? tag.Substring(at) : tag.Substring(at, space - at);
        }
        private static bool Number(string value, float relative, out float result)
        {
            result = 0; if (value == null) return false;
            value = value.Trim('"', '\'');
            bool percent = value.EndsWith("%", StringComparison.Ordinal), em = value.EndsWith("em", StringComparison.Ordinal);
            string number = percent ? value.Substring(0, value.Length - 1) : em ? value.Substring(0, value.Length - 2) : value.EndsWith("px", StringComparison.Ordinal) ? value.Substring(0, value.Length - 2) : value;
            if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out result) || float.IsNaN(result) || float.IsInfinity(result)) return false;
            if (percent) result *= relative * 0.01f; else if (em) result *= relative;
            return true;
        }
        private bool ParseTag(string tag, ref TextStyle style)
        {
            if (tag.StartsWith("/", StringComparison.Ordinal))
            {
                string close = tag.Substring(1).ToLowerInvariant();
                for (int i = styleFrames.Count - 1; i >= 0; i--)
                    if (styleFrames[i].tag == close) { style = styleFrames[i].previous; styleFrames.RemoveRange(i, styleFrames.Count - i); return true; }
                return false;
            }
            int split = tag.IndexOfAny(new[] { '=', ' ' });
            string name = (split < 0 ? tag : tag.Substring(0, split)).ToLowerInvariant();
            if (name == "br" || name == "br/") { tokens.Add(new Token { unicode = '\n', style = style }); return true; }
            if (name == "sprite") return ParseSprite(tag, style);
            if (tag.StartsWith("#", StringComparison.Ordinal)) { name = "color"; tag = "color=" + tag; }
            var previous = style;
            switch (name)
            {
                case "b": style.bold = true; break;
                case "i": style.italic = true; break;
                case "u": style.underline = true; break;
                case "s": style.strike = true; break;
                case "nobr": style.noBreak = true; break;
                case "color": if (!ColorUtility.TryParseHtmlString(Value(tag, name), out style.color)) return false; style.color.a *= previous.color.a; break;
                case "alpha": string a = Value(tag, name); if (a == null || !byte.TryParse(a.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alpha)) return false; style.color.a = alpha / 255f; break;
                case "size": if (!Number(Value(tag, name), style.size, out var size)) return false; style.size = Mathf.Max(0.01f, size); break;
                case "voffset": if (!Number(Value(tag, name), style.size, out style.baseline)) return false; break;
                case "cspace": if (!Number(Value(tag, name), style.size, out style.spacing)) return false; break;
                case "sup": style.baseline += style.size * 0.35f; style.size *= 0.6f; break;
                case "sub": style.baseline -= style.size * 0.2f; style.size *= 0.6f; break;
                case "font": style.font = FindFont(Value(tag, name)); if (style.font == null) return false; break;
                default: return false; // Unknown tags remain visible; they are not silently swallowed.
            }
            styleFrames.Add(new StyleFrame { tag = name, previous = previous });
            return true;
        }

        private TMP_FontAsset FindFont(string name)
        {
            if (LayoutFont != null && name == LayoutFont.name) return LayoutFont;
            if (name == font.name) return font;
            if (fonts != null) foreach (var asset in fonts) if (asset != null && asset.name == name) return asset;
            return null;
        }
        private TMP_SpriteAsset FindSpriteAsset(string name)
        {
            var active = ActiveSpriteAsset;
            if (active != null && active.name == name) return active;
            if (additionalSpriteAssets != null) foreach (var asset in additionalSpriteAssets) if (asset != null && asset.name == name) return asset;
            return null;
        }
        private readonly HashSet<TMP_SpriteAsset> searchedSpriteNames = new HashSet<TMP_SpriteAsset>();
        private TMP_SpriteAsset FindSpriteByName(TMP_SpriteAsset asset, string name, out int index)
        {
            searchedSpriteNames.Clear();
            var found = FindSpriteNameInFallbacks(asset, name, out index);
            if (found == null) found = FindSpriteNameInFallbacks(TMP_Settings.defaultSpriteAsset, name, out index);
            searchedSpriteNames.Clear();
            return found;
        }
        private TMP_SpriteAsset FindSpriteNameInFallbacks(TMP_SpriteAsset asset, string name, out int index)
        {
            index = -1;
            if (asset == null || !searchedSpriteNames.Add(asset)) return null;
            // Let the installed TMP version apply its own name hashing/case rules.
            index = asset.GetSpriteIndexFromName(name);
            if (index >= 0) return asset;
            if (asset.fallbackSpriteAssets != null)
                foreach (var fallback in asset.fallbackSpriteAssets)
                {
                    var found = FindSpriteNameInFallbacks(fallback, name, out index);
                    if (found != null) return found;
                }
            return null;
        }
        private bool ParseSprite(string tag, TextStyle style)
        {
            if (!useSprites) return false;
            var asset = spriteAsset != null ? spriteAsset : TMP_Settings.defaultSpriteAsset;
            string main = Value(tag, "sprite");
            if (main != null && !int.TryParse(main, out _)) asset = FindSpriteAsset(main);
            if (asset == null) { MissingSpriteCount++; return false; }
            string name = Value(tag, "name"), index = Value(tag, "index");
            int id = -1;
            if (name != null) asset = FindSpriteByName(asset, name, out id);
            else if (!int.TryParse(index ?? main, out id)) id = 0;
            string tint = Value(tag, "tint"), color = Value(tag, "color");
            if (tint != "1") style.color = new Color(1, 1, 1, style.color.a);
            if (color != null && ColorUtility.TryParseHtmlString(color, out var spriteColor)) style.color = spriteColor;
            return AddSpriteToken(asset, id, style);
        }
        private bool AddSpriteToken(TMP_SpriteAsset asset, int index, TextStyle style)
        {
            if (asset == null || asset.spriteSheet == null || index < 0 || index >= asset.spriteCharacterTable.Count) { MissingSpriteCount++; return false; }
            tokens.Add(new Token { unicode = 0xfffc, style = style, sprite = asset, spriteCharacter = asset.spriteCharacterTable[index] });
            return true;
        }
        private bool TrySpriteUnicode(uint unicode, TextStyle style)
        {
            if (!useSprites) return false;
            var asset = spriteAsset != null ? spriteAsset : TMP_Settings.defaultSpriteAsset;
            if (asset == null) return false;
            var key = (BrgObjectIdentity.Of(asset), unicode);
            if (!spriteUnicodeCache.TryGetValue(key, out var found))
            {
                var match = TMP_SpriteAsset.SearchForSpriteByUnicode(asset, unicode, true, out int id);
                found = (match, id);
                if (spriteUnicodeCache.Count >= 4096) spriteUnicodeCache.Clear();
                spriteUnicodeCache[key] = found;
            }
            style.color = new Color(1, 1, 1, style.color.a);
            return !ReferenceEquals(found.asset, null) && AddSpriteToken(found.asset, found.index, style);
        }
        private bool ResolveStyled(uint unicode, TextStyle style, out ResolvedGlyph resolved, out bool alternative)
        {
            alternative = false;
            var key = (BrgObjectIdentity.Of(style.font), unicode, style.bold, style.italic);
            if (styledGlyphs.TryGetValue(key, out resolved)) { alternative = resolved.alternative; return true; }
            FontStyles styles = (style.bold ? FontStyles.Bold : FontStyles.Normal) | (style.italic ? FontStyles.Italic : FontStyles.Normal);
            var character = LookupStyledCharacter(unicode, style.font, styles, style.bold ? FontWeight.Bold : FontWeight.Regular, out alternative);
            if (character == null && TMP_Settings.fallbackFontAssets != null)
                foreach (var candidate in TMP_Settings.fallbackFontAssets)
                {
                    if (candidate == null) continue;
                    character = LookupStyledCharacter(unicode, candidate, styles, style.bold ? FontWeight.Bold : FontWeight.Regular, out alternative);
                    if (character != null) break;
                }
            if (character == null) { resolved = default; return false; }
            var source = (TMP_FontAsset)character.textAsset;
            resolved = new ResolvedGlyph { font = source, character = character, alternative = alternative };
            styledGlyphs[key] = resolved;
            return true;
        }

        private static TMP_Character LookupStyledCharacter(uint unicode, TMP_FontAsset asset, FontStyles style, FontWeight weight, out bool alternative)
        {
            var character = TMP_FontAssetUtilities.GetCharacterFromFontAsset(unicode, asset, true, style, weight, out alternative);
            // New TMP returns null if no alternate bold/italic face is configured. The
            // source glyph remains valid for our shader's synthetic weight/slant.
            if (character == null && (style != FontStyles.Normal || weight != FontWeight.Regular))
                character = TMP_FontAssetUtilities.GetCharacterFromFontAsset(unicode, asset, true, FontStyles.Normal, FontWeight.Regular, out alternative);
            return character;
        }

        private void ResolveScripts(int start, int end)
        {
            const uint common = 0x5a797979, inherited = 0x5a696e68;
            var provider = ShapingEnabled ? SelectedShaper : null;
            uint last = common;
            for (int i = start; i < end; i++)
            {
                uint script = common;
                if (provider != null && !unicodeScripts.TryGetValue(codePoints[i], out script))
                {
                    try { script = provider.GetScript(codePoints[i]); }
                    catch (DllNotFoundException) { DisableUnavailableShaper(); provider = null; script = common; }
                    catch (EntryPointNotFoundException) { DisableUnavailableShaper(); provider = null; script = common; }
                    unicodeScripts[codePoints[i]] = script;
                }
                if (script != common && script != inherited) last = script;
                scripts[i] = script == common || script == inherited ? last : script;
            }
            last = common;
            for (int i = end - 1; i >= start; i--) { if (scripts[i] != common) last = scripts[i]; else scripts[i] = last; }
        }

        private void DisableUnavailableShaper()
        {
            if (!shapingUnavailable) Debug.LogWarning("The selected shaping adapter is unavailable; using TMP glyph data.", this);
            shapingUnavailable = true;
        }

        private ShapingFace Face(TMP_FontAsset asset)
        {
            if (!ShapingEnabled) return null;
            if (shapingFaces.TryGetValue(asset, out var face)) return face;
            TextAsset bytes = null;
            if (SelectedShaper.RequiresFontData)
            {
                bytes = fontSources != null ? fontSources.Find(asset) : null;
                if (bytes == null)
                {
                    if (discoveredFontSources == null) discoveredFontSources = Resources.LoadAll<BrgFontSources>("");
                    foreach (var catalog in discoveredFontSources)
                    {
                        bytes = catalog.Find(asset);
                        if (bytes != null) break;
                    }
                }
            }
            if (bytes == null && SelectedShaper.RequiresFontData)
            {
                UnavailableShapingCount++;
                if (!warnedSource) { Debug.LogWarning("The selected shaping adapter needs the original font data. Check its font-data integration.", this); warnedSource = true; }
                shapingFaces[asset] = null; return null;
            }
            try
            {
                var session = SelectedShaper.CreateFont(asset, bytes != null ? bytes.bytes : null);
                if (session == null) { UnavailableShapingCount++; shapingFaces[asset] = null; return null; }
                face = new ShapingFace(asset, bytes != null ? bytes.bytes : null, session);
                shapingFaces.Add(asset, face); return face;
            }
            catch (DllNotFoundException) { shapingUnavailable = true; Debug.LogWarning("The selected shaping adapter is unavailable; using TMP glyph data.", this); return null; }
            catch (EntryPointNotFoundException) { shapingUnavailable = true; Debug.LogWarning("The selected shaping adapter has an incompatible native library; using TMP glyph data.", this); return null; }
        }

        private static bool SameStyle(TextStyle a, TextStyle b) => ReferenceEquals(a.font, b.font) && a.size == b.size && a.baseline == b.baseline && a.spacing == b.spacing && a.bold == b.bold && a.italic == b.italic && a.underline == b.underline && a.strike == b.strike && a.color == b.color;
        private unsafe bool ShapeRuns(int start, int end, bool measure)
        {
            shaped.Clear(); runs.Clear();
            if (measure && useMeasuredLayout) BeginMeasurement(start, end);
            for (int first = start; first < end;)
            {
                var token = tokens[first];
                int last = first + 1;
                if (ReferenceEquals(token.sprite, null) && token.unicode != '\t')
                    while (last < end && ReferenceEquals(tokens[last].sprite, null) && tokens[last].unicode != '\t' && levels[last] == levels[first] && scripts[last] == scripts[first] && SameStyle(tokens[last].style, token.style)) last++;
                int initial = shaped.Count;
                int measuredInitial = measuredGlyphs.Count;
                var face = ReferenceEquals(token.sprite, null) && token.unicode != '\t' ? Face(token.style.font) : null;
                if (face != null)
                {
                    var native = ShapeNativeCached(face, start, end, first, last, out int count);
                    float scale = token.style.size / token.style.font.faceInfo.pointSize * token.style.font.faceInfo.scale;
                    bool cachedMeasurement = measure && useMeasuredLayout && TryCachedMeasurement(native, first, last);
                    for (int i = 0; !cachedMeasurement && i < count; i++)
                    {
                        int cluster = first + native[i].cluster;
                        if (!measure && face.Nominal(codePoints[cluster], out var nominal) && nominal != native[i].glyph) LastGlyphSubstitutionCount++;
                        if (native[i].glyph == 0 && IsControl(codePoints[cluster])) continue;
                        float advance = native[i].advance * scale;
                        // Character spacing applies once per shaped cluster, not once per combining mark.
                        if (i + 1 == count || native[i + 1].cluster != native[i].cluster) advance += token.style.spacing;
                        if (measure)
                        {
                            advances[cluster] += advance;
                            if (useMeasuredLayout)
                            {
                                if (i == 0 || native[i - 1].cluster != native[i].cluster) measuredBoundaries[cluster] = (native[i].flags & 1u) == 0;
                                else if ((native[i].flags & 1u) != 0) measuredBoundaries[cluster] = false;
                                measuredGlyphs.Add(new MeasuredGlyph { face = face, glyph = native[i].glyph, flags = native[i].flags, cluster = cluster,
                                    advance = advance, x = native[i].x * scale, y = native[i].y * scale });
                            }
                            continue;
                        }
                        if (!face.Resolve(native[i].glyph, out var resolved)) return false;
                        shaped.Add(new Shaped { tokenIndex = first, measuredIndex = -1, glyph = resolved, advance = advance, x = native[i].x * scale, y = native[i].y * scale });
                    }
                    if (measure && useMeasuredLayout && !cachedMeasurement) StoreCachedMeasurement(native, first, last, measuredInitial);
                    LastLayoutUsedShaping = true;
                }
                else
                {
                    for (int j = first; j < last; j++)
                    {
                        int index = (levels[first] & 1) != 0 ? last - 1 - (j - first) : j;
                        var t = tokens[index];
                        if (IsControl(t.unicode)) continue;
                        float advance;
                        if (t.sprite != null) advance = SpriteScale(t) * t.spriteCharacter.glyph.metrics.horizontalAdvance;
                        else
                        {
                            float scale = GlyphScale(t, t.glyph);
                            advance = t.glyph.character.glyph.metrics.horizontalAdvance * scale;
                            if (t.unicode == '\t') advance *= 4;
                        }
                        float offsetX = 0, offsetY = 0;
                        if (t.sprite == null && enableKerning)
                        {
                            float scale = GlyphScale(t, t.glyph);
                            if (index > first && tokens[index - 1].glyph.character != null && SameStyle(tokens[index - 1].style, t.style) && Pair(t.glyph.font,
                                tokens[index - 1].glyph.character.glyph.index, t.glyph.character.glyph.index, out var before))
                            { var v = before.secondAdjustmentRecord.glyphValueRecord; offsetX += v.xPlacement * scale; offsetY += v.yPlacement * scale; advance += v.xAdvance * scale; }
                            if (index + 1 < last && tokens[index + 1].glyph.character != null && SameStyle(tokens[index + 1].style, t.style) && Pair(t.glyph.font,
                                t.glyph.character.glyph.index, tokens[index + 1].glyph.character.glyph.index, out var after))
                            { var v = after.firstAdjustmentRecord.glyphValueRecord; offsetX += v.xPlacement * scale; offsetY += v.yPlacement * scale; advance += v.xAdvance * scale; }
                        }
                        advance += t.style.spacing;
                        if (measure)
                        {
                            advances[index] += advance;
                            if (useMeasuredLayout) { measuredBoundaries[index] = true; measuredGlyphs.Add(new MeasuredGlyph { resolved = t.glyph, cluster = index, advance = advance, x = offsetX, y = offsetY }); }
                        }
                        else shaped.Add(new Shaped { tokenIndex = index, measuredIndex = -1, glyph = t.glyph, advance = advance, x = offsetX, y = offsetY });
                    }
                }
                if (measure)
                {
                    if (useMeasuredLayout) measuredRuns.Add(new MeasuredRun { first = first, last = last, glyphStart = measuredInitial, glyphEnd = measuredGlyphs.Count, level = levels[first] });
                    first = last; continue;
                }
                float width = 0;
                for (int i = initial; i < shaped.Count; i++) width += shaped[i].advance;
                runs.Add(new Run { first = initial, count = shaped.Count - initial, level = levels[first], width = width });
                first = last;
            }
            if (measure && useMeasuredLayout)
            {
                ValidateTmpMeasuredBoundaries();
                // A style/font boundary inside a paragraph can still participate in
                // contextual shaping. Without an end-of-buffer flag, keep it conservative.
                for (int i = 1; i < measuredRuns.Count; i++)
                {
                    int boundary = measuredRuns[i].first;
                    if (codePoints[boundary - 1] != ' ' && codePoints[boundary] != ' ') measuredBoundaries[boundary] = false;
                }
                measuredBoundaries[start] = measuredBoundaries[end] = true;
            }
            return true;
        }

        private void ReorderRuns()
        {
            visualRuns.Clear(); int highest = 0, lowestOdd = int.MaxValue;
            for (int i = 0; i < runs.Count; i++) { visualRuns.Add(i); highest = Mathf.Max(highest, runs[i].level); if ((runs[i].level & 1) != 0) lowestOdd = Mathf.Min(lowestOdd, runs[i].level); }
            for (int level = highest; level >= lowestOdd; level--)
                for (int i = 0; i < visualRuns.Count;)
                {
                    if (runs[visualRuns[i]].level < level) { i++; continue; }
                    int start = i;
                    while (i < visualRuns.Count && runs[visualRuns[i]].level >= level) i++;
                    visualRuns.Reverse(start, i - start);
                }
        }

        private static float GlyphScale(Token token, ResolvedGlyph resolved) => token.style.size / resolved.font.faceInfo.pointSize * resolved.font.faceInfo.scale * resolved.character.scale * resolved.character.glyph.scale;
        private static float SpriteScale(Token token)
        {
            float height = token.spriteCharacter.glyph.metrics.height;
            return height > 0 ? token.style.size / height * token.spriteCharacter.scale * token.spriteCharacter.glyph.scale : 1;
        }
        private void AddPlaced(Shaped item, float x, float y)
        {
            int begin = layout.Count;
            AddPlacedCore(item, x, y);
            if (captureMeasuredPlan && item.measuredIndex >= 0 && codePoints[measuredGlyphs[item.measuredIndex].cluster] >= '0' && codePoints[measuredGlyphs[item.measuredIndex].cluster] <= '9')
                measuredPatches.Add(new MeasuredPatch { measuredIndex = item.measuredIndex, tokenIndex = item.tokenIndex, x = x, y = y, first = begin, count = layout.Count - begin,
                    alternative = tokens[item.tokenIndex].alternative, style = tokens[item.tokenIndex].style });
        }
        private void AddPlacedCore(Shaped item, float x, float y)
        {
            var token = tokens[item.tokenIndex];
            Glyph glyph; Texture texture; int group; float scale, padding;
            y += token.style.baseline;
            var resolved = item.glyph;
            bool sprite = !ReferenceEquals(token.sprite, null);
            var key = new GeometryKey(sprite ? BrgObjectIdentity.Of(token.sprite) : BrgObjectIdentity.Of(resolved.font),
                sprite ? token.spriteCharacter.glyph.index : resolved.character.glyph.index,
                resolved.font != null && resolved.font.material != null ? BrgObjectIdentity.Of(resolved.font.material) : 0,
                token.style.size, token.style.bold, token.style.italic, token.alternative);
            if (geometry.TryGetValue(key, out var cached))
            {
                if (cached.rect.z > 0 && cached.rect.w > 0)
                {
                    cached.rect.x += x; cached.rect.y += y; cached.tint = token.style.color;
                    layout.Add(cached);
                }
                if (token.style.underline) AddRule(x, y - token.style.size * 0.12f, item.advance, token.style);
                if (token.style.strike) AddRule(x, y + token.style.size * 0.3f, item.advance, token.style);
                return;
            }
            if (token.sprite != null)
            {
                glyph = token.spriteCharacter.glyph; texture = token.sprite.spriteSheet;
                scale = SpriteScale(token); padding = 0;
                group = Batch(null, texture, null, 2);
                resolved.group = group;
            }
            else
            {
                if (resolved.character == null) return;
                glyph = resolved.character.glyph;
                texture = resolved.group < 0 ? shapingFaces[resolved.font].Texture(glyph) : resolved.font.atlasTextures[glyph.atlasIndex];
                scale = GlyphScale(token, resolved);
                var material = resolved.font.material;
                padding = AtlasMode(resolved.font) == 0 ? RequiredPadding(resolved.font, material, token.style.bold && !token.alternative) : 0;
                group = Batch(resolved.font, texture, material, AtlasMode(resolved.font)); resolved.group = group;
            }
            var metrics = glyph.metrics;
            if (metrics.width > 0 && metrics.height > 0)
            {
                var r = glyph.glyphRect;
                // Trim the original quad in texture space. Raster glyph rectangles can differ
                // from fractional font metrics; rebuilding a smaller quad from metrics would
                // change the UV-to-screen scale and visibly distort the text.
                float fullPadding = !sprite && AtlasMode(resolved.font) == 0 ? resolved.font.atlasPadding : 0;
                float trimX = (fullPadding - padding) * (metrics.width + fullPadding * 2) / Mathf.Max(1, r.width + fullPadding * 2);
                float trimY = (fullPadding - padding) * (metrics.height + fullPadding * 2) / Mathf.Max(1, r.height + fullPadding * 2);
                float italic = token.style.italic && !token.alternative ? token.style.font.italicStyle * 0.01f : 0;
                float bold = token.style.bold && !token.alternative ? (resolved.font != null ? resolved.font.boldStyle - resolved.font.normalStyle : 0) / 4f : 0;
                var placed = new PositionedGlyph
                {
                    glyph = resolved, tint = token.style.color,
                    rect = new Vector4(x + (metrics.horizontalBearingX - fullPadding + trimX) * scale,
                        y + (metrics.horizontalBearingY - metrics.height - fullPadding + trimY) * scale,
                        (metrics.width + fullPadding * 2 - trimX * 2) * scale, (metrics.height + fullPadding * 2 - trimY * 2) * scale),
                    uv = new Vector4((r.x - padding) / (float)texture.width, (r.y - padding) / (float)texture.height, (r.width + padding * 2) / (float)texture.width, (r.height + padding * 2) / (float)texture.height),
                    style = new Vector4(bold, italic, (metrics.horizontalBearingY - metrics.height - fullPadding + trimY) * scale, 0)
                };
                layout.Add(placed);
                placed.rect.x -= x; placed.rect.y -= y;
                // Bound the cache when callers use continuously varying font sizes.
                if (geometry.Count >= 4096) geometry.Clear();
                geometry[key] = placed;
            }
            else geometry[key] = default;
            if (token.style.underline) AddRule(x, y - token.style.size * 0.12f, item.advance, token.style);
            if (token.style.strike) AddRule(x, y + token.style.size * 0.3f, item.advance, token.style);
        }
        private void AddRule(float x, float y, float width, TextStyle style)
        {
            if (width <= 0) return;
            if (solidTexture == null) { solidTexture = new Texture2D(1, 1) { name = "BRG text decoration" }; solidTexture.SetPixel(0, 0, Color.white); solidTexture.Apply(); }
            layout.Add(new PositionedGlyph { glyph = new ResolvedGlyph { group = Batch(null, solidTexture, null, 2) }, tint = style.color, rect = new Vector4(x, y, width, Mathf.Max(1, style.size * 0.05f)), uv = new Vector4(0, 0, 1, 1) });
        }

        private sealed unsafe class ShapingFace : IDisposable
        {
            public readonly IntPtr NativeFont;
            public TMP_FontAsset Font => font;
            private readonly TMP_FontAsset font;
            private readonly Dictionary<uint, ResolvedGlyph> glyphs = new Dictionary<uint, ResolvedGlyph>();
            private readonly Dictionary<uint, uint> nominalGlyphs = new Dictionary<uint, uint>();
            private readonly Dictionary<uint, Texture2D> glyphTextures = new Dictionary<uint, Texture2D>();
            private readonly List<Texture2D> textures = new List<Texture2D>();
            private List<GlyphRect> free, used;
            public readonly ITextShapingFont Session;
            private readonly FontEngineAtlasBridge.Face atlasFace;
            public ShapingFace(TMP_FontAsset asset, byte[] data, ITextShapingFont session)
            {
                font = asset; Session = session; atlasFace = new FontEngineAtlasBridge.Face(asset, data);
                NativeFont = new IntPtr(BrgObjectIdentity.Of(asset)); // Cache identity only; never a native ABI handle.
                foreach (var glyph in font.glyphTable) glyphs[glyph.index] = new ResolvedGlyph { font = font, character = new TMP_Character(0, font, glyph) };
            }
            public bool Resolve(uint index, out ResolvedGlyph resolved)
            {
                if (glyphs.TryGetValue(index, out resolved)) return true;
                if (!atlasFace.TryGetGlyph(index, out var glyph)) return false;
                if (glyph.metrics.width > 0 && glyph.metrics.height > 0)
                {
                    if (textures.Count == 0) NewAtlas();
                    if (!atlasFace.Add(index, font.atlasPadding, GlyphPackingMode.BestShortSideFit, free, used, font.atlasRenderMode, textures[textures.Count - 1], out glyph))
                    {
                        NewAtlas();
                        if (!atlasFace.Add(index, font.atlasPadding, GlyphPackingMode.BestShortSideFit, free, used, font.atlasRenderMode, textures[textures.Count - 1], out glyph)) return false;
                    }
                    textures[textures.Count - 1].Apply(false, false);
                    glyphTextures[index] = textures[textures.Count - 1];
                }
                resolved = new ResolvedGlyph { font = font, character = new TMP_Character(0, font, glyph), group = -1 };
                glyphs.Add(index, resolved); return true;
            }
            public bool Nominal(uint unicode, out uint glyph)
            {
                if (!nominalGlyphs.TryGetValue(unicode, out glyph))
                {
                    if (!Session.TryGetGlyphIndex(unicode, out glyph)) glyph = 0;
                    nominalGlyphs[unicode] = glyph;
                }
                return glyph != 0;
            }
            private void NewAtlas()
            {
                int width = Mathf.Max(512, font.atlasWidth), height = Mathf.Max(512, font.atlasHeight);
                var texture = new Texture2D(width, height, AtlasMode(font) == 2 ? TextureFormat.RGBA32 : TextureFormat.Alpha8, false) { name = font.name + " shaped glyph atlas", filterMode = FilterMode.Bilinear };
                FontEngineAtlasBridge.Reset(texture);
                textures.Add(texture);
                free = new List<GlyphRect> { new GlyphRect(0, 0, width - 1, height - 1) }; used = new List<GlyphRect>();
            }
            public Texture Texture(Glyph glyph) => glyphTextures.TryGetValue(glyph.index, out var texture) ? texture : font.atlasTextures[glyph.atlasIndex];
            public void Dispose() { Session.Dispose(); atlasFace.Dispose(); foreach (var texture in textures) DestroyGeneratedObject(texture); }
        }

        private void ValidateTmpMeasuredBoundaries()
        {
            foreach (var measuredRun in measuredRuns)
            {
                if (!enableKerning || measuredRun.glyphStart == measuredRun.glyphEnd || measuredGlyphs[measuredRun.glyphStart].face != null) continue;
                for (int boundary = measuredRun.first + 1; boundary < measuredRun.last; boundary++)
                {
                    var left = tokens[boundary - 1]; var right = tokens[boundary];
                    if (left.sprite != null || right.sprite != null || left.glyph.character == null || right.glyph.character == null ||
                        !SameStyle(left.style, right.style) || !Pair(left.glyph.font, left.glyph.character.glyph.index, right.glyph.character.glyph.index, out var pair)) continue;
                    var a = pair.firstAdjustmentRecord.glyphValueRecord; var b = pair.secondAdjustmentRecord.glyphValueRecord;
                    if (a.xAdvance != 0 || a.xPlacement != 0 || a.yPlacement != 0 || b.xAdvance != 0 || b.xPlacement != 0 || b.yPlacement != 0)
                        measuredBoundaries[boundary] = false;
                }
            }
        }

        private void DisposeTypography()
        {
            DisposePreparationJobs();
            discoveredFontSources = null;
            foreach (var face in shapingFaces.Values) face?.Dispose(); shapingFaces.Clear(); styledGlyphs.Clear(); pairCaches.Clear();
            batchLookup.Clear(); geometry.Clear(); unicodeScripts.Clear(); spriteUnicodeCache.Clear();
            materialPadding.Clear();
            parsedMessages.Clear(); parsedTokenCount = 0;
            nativeShapes.Clear(); nativeShapeGlyphCount = 0;
            measuredPlans.Clear(); measuredPlanGlyphCount = 0;
            measuredRunCache.Clear(); measuredRunGlyphCount = 0;

            paragraphAnalyses.Clear();
            wrappedLines.Clear(); wrappedLineGlyphCount = 0;
            activePreparedMessage = null; activeParagraphAnalysis = null;
            shapingOutput.Clear(); shapingFunctions = default;
            DestroyGeneratedObject(solidTexture);
            tokens.Clear(); shaped.Clear(); runs.Clear(); styleFrames.Clear();
            warnedSource = shapingUnavailable = false;
        }
    }
}
