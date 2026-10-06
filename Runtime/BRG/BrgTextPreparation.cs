using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Topten.RichTextKit;
using Topten.RichTextKit.Utils;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        private sealed class ParsedMessage
        {
            public string sample;
            public Token[] tokens;
            public TMP_FontAsset font;
            public Material material;
            public Color color;
            public float size;
            public bool rich;
            public int[] digitPrefix;
            public int[] digitTokens;
            public DigitChoice[][] digitChoices;
            public TMP_SpriteAsset digitSprite;
            public uint[] points;
        }
        private struct DigitChoice { public ResolvedGlyph glyph; public bool alternative; }
        private void PrepareDigitChoices(ParsedMessage entry)
        {
            var source = spriteAsset != null ? spriteAsset : TMP_Settings.defaultSpriteAsset;
            entry.digitSprite = source;
            if (source != null)
                for (uint digit = '0'; digit <= '9'; digit++)
                    if (TMP_SpriteAsset.SearchForSpriteByUnicode(source, digit, true, out _) != null) return;
            var choices = new DigitChoice[entry.digitTokens.Length][];
            for (int slot = 0; slot < choices.Length; slot++)
            {
                var token = entry.tokens[entry.digitTokens[slot]];
                for (int earlier = 0; earlier < slot; earlier++)
                {
                    var previous = entry.tokens[entry.digitTokens[earlier]];
                    if (ReferenceEquals(token.requestedFont, previous.requestedFont) && token.style.bold == previous.style.bold && token.style.italic == previous.style.italic)
                    { choices[slot] = choices[earlier]; break; }
                }
                if (choices[slot] != null) continue;
                var style = token.style; style.font = token.requestedFont;
                var digits = new DigitChoice[10];
                for (int i = 0; i < 10; i++) if (!ResolveStyled((uint)('0' + i), style, out digits[i].glyph, out digits[i].alternative)) return;
                choices[slot] = digits;
            }
            entry.digitChoices = choices;
        }
        private readonly Dictionary<ulong, ParsedMessage> parsedMessages = new Dictionary<ulong, ParsedMessage>();
        private int parsedTokenCount;
        private ParsedMessage activePreparedMessage;

        private ulong ParsedHash(string text, Color color)
        {
            ulong hash = 14695981039346656037UL; bool tag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i]; if (c == '<') tag = true;
                hash = unchecked((hash ^ (uint)(!tag && c >= '0' && c <= '9' ? '0' : c)) * 1099511628211UL);
                if (c == '>') tag = false;
            }
            hash = unchecked((hash ^ (uint)font.GetInstanceID()) * 1099511628211UL);
            hash = unchecked((hash ^ (uint)fontSize.GetHashCode()) * 1099511628211UL);
            return unchecked((hash ^ (uint)color.GetHashCode()) * 1099511628211UL);
        }
        private bool FindParsedMessage(string text, Color color, out ParsedMessage entry)
        {
            entry = null;
            return text.Length <= 512 && parsedMessages.TryGetValue(ParsedHash(text, color), out entry) &&
                ReferenceEquals(entry.font, font) && ReferenceEquals(entry.material, fontMaterial) &&
                entry.size == fontSize && entry.color.Equals(color) && entry.rich == richText && MatchesPrepared(entry.sample, text);
        }

        // Cache formatting and static characters, not damage values or final layouts.
        // Digits inside tags stay exact: <size=20> and <size=30> are different messages.
        private bool ParseCached(string text, Color color)
        {
            activePreparedMessage = null;
            if (text.Length > 512) return Parse(text, color);
            if (spriteSequences != null)
                foreach (var sequence in spriteSequences)
                    if (sequence.text != null)
                        foreach (char c in sequence.text)
                            if (c >= '0' && c <= '9') return Parse(text, color);
            ulong hash = ParsedHash(text, color);
            if (parsedMessages.TryGetValue(hash, out var entry) && ReferenceEquals(entry.font, font) &&
                ReferenceEquals(entry.material, fontMaterial) && entry.size == fontSize && entry.color.Equals(color) &&
                entry.rich == richText && MatchesPrepared(entry.sample, text))
            {
                if (!useMeasuredLayout)
                {
                    // Complete reference preparation path for benchmark A/B verification.
                    foreach (var original in entry.tokens)
                    {
                        var token = original;
                        if (token.unicode >= '0' && token.unicode <= '9')
                        {
                            token.unicode = text[token.sourceIndex];
                            if (TrySpriteUnicode(token.unicode, token.style)) return Reparse(text, color);
                            var style = token.style; style.font = token.requestedFont;
                            if (!ResolveStyled(token.unicode, style, out token.glyph, out token.alternative)) return Reparse(text, color);
                            token.style.font = token.glyph.font;
                        }
                        tokens.Add(token);
                    }
                    activePreparedMessage = entry; ParsedCacheHits++; return true;
                }
                // Bulk-copy the fixed token data. Only numeric slots need resolution;
                // copying a large Token struct character by character was a hot path.
                tokens.AddRange(entry.tokens);
                bool preparedDigits = entry.digitChoices != null && ReferenceEquals(entry.digitSprite, spriteAsset != null ? spriteAsset : TMP_Settings.defaultSpriteAsset);
                for (int slot = 0; slot < entry.digitTokens.Length; slot++)
                {
                    int index = entry.digitTokens[slot];
                    var token = tokens[index];
                    token.unicode = text[token.sourceIndex];
                    if (preparedDigits)
                    {
                        var choice = entry.digitChoices[slot][token.unicode - '0'];
                        token.glyph = choice.glyph; token.alternative = choice.alternative; token.style.font = choice.glyph.font;
                    }
                    else
                    {
                        if (TrySpriteUnicode(token.unicode, token.style)) return Reparse(text, color);
                        var style = token.style; style.font = token.requestedFont;
                        if (!ResolveStyled(token.unicode, style, out token.glyph, out token.alternative))
                            return Reparse(text, color);
                        token.style.font = token.glyph.font;
                    }
                    tokens[index] = token;
                }
                activePreparedMessage = entry;
                ParsedCacheHits++;
                return true;
            }
            ParsedCacheMisses++;
            long missingGlyphs = MissingGlyphCount, missingSprites = MissingSpriteCount;
            if (!Parse(text, color)) return false;
            // A missing glyph must still be reported on every emission.
            if (MissingGlyphCount != missingGlyphs || MissingSpriteCount != missingSprites) return true;
            foreach (var token in tokens)
                if (token.spriteCharacter != null && token.spriteCharacter.unicode >= '0' && token.spriteCharacter.unicode <= '9') return true;
            if (parsedMessages.Count >= 512 || parsedTokenCount + tokens.Count > 32768)
            { InvalidatePreparationTemplates(); parsedMessages.Clear(); parsedTokenCount = 0; wrappedLines.Clear(); wrappedLineGlyphCount = 0; measuredPlans.Clear(); measuredPlanGlyphCount = 0; measuredRunCache.Clear(); measuredRunGlyphCount = 0; }
            if (parsedMessages.TryGetValue(hash, out entry)) parsedTokenCount -= entry.tokens.Length;
            entry = new ParsedMessage { sample = text, tokens = tokens.ToArray(), font = font,
                material = fontMaterial, color = color, size = fontSize, rich = richText, digitPrefix = new int[tokens.Count + 1], points = new uint[tokens.Count] };
            for (int i = 0; i < tokens.Count; i++) entry.points[i] = tokens[i].unicode;
            for (int i = 0; i < tokens.Count; i++) entry.digitPrefix[i + 1] = entry.digitPrefix[i] +
                (tokens[i].unicode >= '0' && tokens[i].unicode <= '9' ? 1 : 0);
            entry.digitTokens = new int[entry.digitPrefix[tokens.Count]];
            for (int i = 0, at = 0; i < tokens.Count; i++) if (entry.digitPrefix[i + 1] != entry.digitPrefix[i]) entry.digitTokens[at++] = i;
            PrepareDigitChoices(entry);
            parsedMessages[hash] = activePreparedMessage = entry;
            parsedTokenCount += tokens.Count;
            return true;
        }
        private bool Reparse(string text, Color color)
        {
            tokens.Clear(); styleFrames.Clear(); return Parse(text, color);
        }
        private static bool MatchesPrepared(string sample, string text)
        {
            if (sample.Length != text.Length) return false;
            bool tag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i], previous = sample[i];
                if (c == '<') tag = true;
                if (c != previous && (tag || c < '0' || c > '9' || previous < '0' || previous > '9')) return false;
                if (c == '>') tag = false;
            }
            return true;
        }

        private bool BuildStyledNumbers()
        {
            foreach (var token in tokens)
                if (!ReferenceEquals(token.sprite, null) || !(token.unicode >= '0' && token.unicode <= '9' || token.unicode == '-' || token.unicode == '+')) return false;
            shaped.Clear();
            float width = 0, ascent = fontSize * 0.8f, descent = -fontSize * 0.2f;
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                float scale = GlyphScale(token, token.glyph), x = 0, y = 0;
                float advance = token.glyph.character.glyph.metrics.horizontalAdvance * scale + token.style.spacing;
                if (enableKerning)
                {
                    if (i > 0 && SameStyle(tokens[i - 1].style, token.style) && Pair(token.glyph.font,
                        tokens[i - 1].glyph.character.glyph.index, token.glyph.character.glyph.index, out var before))
                    { var adjustment = before.secondAdjustmentRecord.glyphValueRecord; x += adjustment.xPlacement * scale; y += adjustment.yPlacement * scale; advance += adjustment.xAdvance * scale; }
                    if (i + 1 < tokens.Count && SameStyle(tokens[i + 1].style, token.style) && Pair(token.glyph.font,
                        token.glyph.character.glyph.index, tokens[i + 1].glyph.character.glyph.index, out var after))
                    { var adjustment = after.firstAdjustmentRecord.glyphValueRecord; x += adjustment.xPlacement * scale; y += adjustment.yPlacement * scale; advance += adjustment.xAdvance * scale; }
                }
                shaped.Add(new Shaped { tokenIndex = i, glyph = token.glyph, advance = advance, x = x, y = y });
                width += advance;
                var face = token.style.font.faceInfo;
                float faceScale = token.style.size / face.pointSize * face.scale;
                ascent = Mathf.Max(ascent, face.ascentLine * faceScale + token.style.baseline);
                descent = Mathf.Min(descent, face.descentLine * faceScale + token.style.baseline);
            }
            float height = ascent - descent + fontSize * 0.2f;
            float baseline = -ascent + height * 0.5f, cursor = -width * 0.5f;
            foreach (var item in shaped)
            {
                AddPlaced(item, cursor + item.x, baseline + item.y); cursor += item.advance;
            }
            LastLayoutLineCount = 1; LastLayoutSize = new Vector2(width, height);
            return true;
        }

        // Cache only exact, digit-free lines after contextual shaping and positioning.
        // Paragraph measurement and break decisions still use the actual damage value.
        // Levels/scripts are part of the key because identical text can occur in different
        // bidi paragraphs. Each line is shaped with precisely the same context as before.
        private sealed class WrappedLine
        {
            public PositionedGlyph[] glyphs;
            public float width, step;
            public int substitutions;
            public bool shaping;
        }
        private readonly Dictionary<(ParsedMessage, ParagraphAnalysis, int, int, int), WrappedLine> wrappedLines =
            new Dictionary<(ParsedMessage, ParagraphAnalysis, int, int, int), WrappedLine>();
        private int wrappedLineGlyphCount;
        private bool WrappedLineKey(int start, int end, out (ParsedMessage, ParagraphAnalysis, int, int, int) key)
        {
            key = default;
            if (end <= start || end - start > 128 || activePreparedMessage == null || activeParagraphAnalysis == null) return false;
            // Reference identities come from collision-checked parsing/Unicode caches. A
            // digit-free range therefore has exactly the same tokens, styles and context.
            // No per-glyph hash, object access or style comparison is needed on a hit.
            if (activePreparedMessage.digitPrefix[end] != activePreparedMessage.digitPrefix[start]) return false;
            int flags = (enableShaping && !shapingUnavailable ? 1 : 0) | (enableKerning ? 2 : 0) | (enableLigatures ? 4 : 0);
            key = (activePreparedMessage, activeParagraphAnalysis, start, end, flags);
            return true;
        }
        private bool TryWrappedLine((ParsedMessage, ParagraphAnalysis, int, int, int) key, ref float y, out float width)
        {
            width = 0;
            if (!wrappedLines.TryGetValue(key, out var entry)) return false;
            FixedLineCacheHits++;
            foreach (var original in entry.glyphs)
            {
                var glyph = original; glyph.rect.y += y; layout.Add(glyph);
            }
            y += entry.step; width = entry.width;
            LastLayoutUsedShaping |= entry.shaping; LastGlyphSubstitutionCount += entry.substitutions;
            return true;
        }
        private void StoreWrappedLine((ParsedMessage, ParagraphAnalysis, int, int, int) key, int layoutStart, float y, float step,
            float width, int substitutions, bool shaping)
        {
            int count = layout.Count - layoutStart;
            if (count > 256) return;
            if (wrappedLines.Count >= 256 || wrappedLineGlyphCount + count > 8192)
            { wrappedLines.Clear(); wrappedLineGlyphCount = 0; }
            if (wrappedLines.TryGetValue(key, out var old)) wrappedLineGlyphCount -= old.glyphs.Length;
            var entry = new WrappedLine { glyphs = new PositionedGlyph[count], step = step, width = width,
                substitutions = substitutions, shaping = shaping };
            for (int i = 0; i < count; i++) { var glyph = layout[layoutStart + i]; glyph.rect.y -= y; entry.glyphs[i] = glyph; }
            wrappedLines[key] = entry; wrappedLineGlyphCount += count;
        }

        private struct NativeShapeGlyph { public uint glyph, flags; public int cluster, advance, x, y; }
        private sealed class ParagraphAnalysis
        {
            public uint[] text, scripts;
            public sbyte[] levels;
            public bool[] breaks;
            public bool wrap, shaping;
        }
        private readonly Dictionary<ulong, ParagraphAnalysis> paragraphAnalyses = new Dictionary<ulong, ParagraphAnalysis>();
        private ParagraphAnalysis activeParagraphAnalysis;
        private static uint AnalysisPoint(uint value) => value >= '0' && value <= '9' ? (uint)'0' : value;
        private void AnalyzeParagraphCached(int start, int end)
        {
            activeParagraphAnalysis = null;
            int length = end - start;
            bool wrap = wrapWidth > 0, shaping = enableShaping && !shapingUnavailable;
            ulong hash = (ulong)((wrap ? 1 : 0) | (shaping ? 2 : 0));
            for (int i = start; i < end; i++) hash = unchecked((hash ^ AnalysisPoint(codePoints[i])) * 1099511628211UL);
            if (length <= 512 && paragraphAnalyses.TryGetValue(hash, out var cached) && cached.text.Length == length && cached.wrap == wrap && cached.shaping == shaping)
            {
                bool equal = true;
                for (int i = 0; i < length; i++) if (cached.text[i] != AnalysisPoint(codePoints[start + i])) { equal = false; break; }
                if (equal)
                {
                    Array.Copy(cached.levels, 0, levels, start, length);
                    Array.Copy(cached.scripts, 0, scripts, start, length);
                    if (wrap) Array.Copy(cached.breaks, 0, breakAfter, start, length + 1);
                    activeParagraphAnalysis = cached;
                    UnicodeCacheHits++;
                    return;
                }
            }
            bidiData.Init(new Slice<int>(bidiPoints, start, length), 2); bidi.Process(bidiData);
            UnicodeCacheMisses++;
            for (int i = 0; i < length; i++) levels[start + i] = bidi.ResolvedLevels[i];
            ResolveScripts(start, end);
            if (wrap)
            {
                Array.Clear(breakAfter, start, length + 1);
                lineBreaker.Reset(new Slice<int>(bidiPoints, start, length));
                while (lineBreaker.NextBreak(out var lb))
                    if (lb.PositionWrap > 0 && lb.PositionWrap <= length) breakAfter[start + lb.PositionWrap] = true;
            }
            if (length > 512) return;
            if (paragraphAnalyses.Count >= 128)
            { InvalidatePreparationTemplates(); paragraphAnalyses.Clear(); wrappedLines.Clear(); wrappedLineGlyphCount = 0; measuredPlans.Clear(); measuredPlanGlyphCount = 0; measuredRunCache.Clear(); measuredRunGlyphCount = 0; }
            var result = new ParagraphAnalysis { text = new uint[length], scripts = new uint[length], levels = new sbyte[length],
                breaks = wrap ? new bool[length + 1] : null, wrap = wrap, shaping = shaping };
            for (int i = 0; i < length; i++) result.text[i] = AnalysisPoint(codePoints[start + i]);
            Array.Copy(levels, start, result.levels, 0, length); Array.Copy(scripts, start, result.scripts, 0, length);
            if (wrap) Array.Copy(breakAfter, start, result.breaks, 0, length + 1);
            paragraphAnalyses[hash] = activeParagraphAnalysis = result;
        }
        private sealed class NativeShape
        {
            public uint[] text;
            public NativeShapeGlyph[] glyphs;
            public IntPtr font;
            public uint script;
            public bool rtl, kerning, ligatures;
        }
        private readonly Dictionary<ulong, NativeShape> nativeShapes = new Dictionary<ulong, NativeShape>();
        private int nativeShapeGlyphCount;
        private NativeShapeGlyph[] nativeScratch = new NativeShapeGlyph[128];
        private unsafe NativeShapeGlyph[] ShapeNativeCached(ShapingFace face, int start, int end, int first, int last, out int count)
        {
            // Word boundaries isolate Unicode joining context. Runs touching other characters
            // always use the full contextual shaping path (e.g. Arabic across style boundaries).
            bool cacheable = (first == start || codePoints[first - 1] == ' ') &&
                (last == end || codePoints[last] == ' ') && last - first <= 512;
            // Changing damage values must not churn the word cache.
            for (int i = first; cacheable && i < last; i++)
                if (codePoints[i] >= '0' && codePoints[i] <= '9') cacheable = false;
            bool rtl = (levels[first] & 1) != 0;
            uint script = scripts[first];
            ulong hash = unchecked((ulong)face.NativeFont.ToInt64());
            hash = unchecked((hash ^ script) * 1099511628211UL);
            hash = unchecked((hash ^ (uint)((rtl ? 1 : 0) | (enableKerning ? 2 : 0) | (enableLigatures ? 4 : 0))) * 1099511628211UL);
            if (cacheable)
            {
                for (int i = first; i < last; i++) hash = unchecked((hash ^ codePoints[i]) * 1099511628211UL);
                if (nativeShapes.TryGetValue(hash, out var cached) && cached.font == face.NativeFont && cached.script == script &&
                    cached.rtl == rtl && cached.kerning == enableKerning && cached.ligatures == enableLigatures && cached.text.Length == last - first)
                {
                    bool equal = true;
                    for (int i = 0; i < cached.text.Length; i++) if (cached.text[i] != codePoints[first + i]) { equal = false; break; }
                    if (equal) { NativeCacheHits++; count = cached.glyphs.Length; return cached.glyphs; }
                }
            }
            ShapeNative(face, start, end, first, last, ref nativeScratch, out count);
            if (!cacheable) return nativeScratch;
            if (nativeShapes.Count >= 1024 || nativeShapeGlyphCount + count > 32768) { nativeShapes.Clear(); nativeShapeGlyphCount = 0; }
            if (nativeShapes.TryGetValue(hash, out var replaced)) nativeShapeGlyphCount -= replaced.glyphs.Length;
            var result = new NativeShapeGlyph[count]; Array.Copy(nativeScratch, result, count);
            var text = new uint[last - first]; Array.Copy(codePoints, first, text, 0, text.Length);
            nativeShapes[hash] = new NativeShape { text = text, glyphs = result, font = face.NativeFont, script = script,
                rtl = rtl, kerning = enableKerning, ligatures = enableLigatures };
            nativeShapeGlyphCount += count;
            return result;
        }
        private unsafe void ShapeNative(ShapingFace face, int start, int end, int first, int last,
            ref NativeShapeGlyph[] destination, out int count)
        {
            NativeShapeCalls++;
            using (LayoutTiming(6)) {
            if (shapingBuffer == IntPtr.Zero) shapingBuffer = HarfBuzzNative.hb_buffer_create();
            HarfBuzzNative.hb_buffer_clear_contents(shapingBuffer);
            fixed (uint* points = codePoints)
                HarfBuzzNative.hb_buffer_add_utf32(shapingBuffer, points + start, end - start, (uint)(first - start), last - first);
            HarfBuzzNative.hb_buffer_set_direction(shapingBuffer, (levels[first] & 1) != 0 ? 5 : 4);
            HarfBuzzNative.hb_buffer_set_script(shapingBuffer, scripts[first]);
            HarfBuzzNative.hb_buffer_guess_segment_properties(shapingBuffer);
            var features = stackalloc HarfBuzzNative.Feature[3];
            features[0] = new HarfBuzzNative.Feature { tag = HarfBuzzNative.Tag('k', 'e', 'r', 'n'), value = enableKerning ? 1u : 0u, end = uint.MaxValue };
            features[1] = new HarfBuzzNative.Feature { tag = HarfBuzzNative.Tag('l', 'i', 'g', 'a'), value = enableLigatures ? 1u : 0u, end = uint.MaxValue };
            features[2] = new HarfBuzzNative.Feature { tag = HarfBuzzNative.Tag('c', 'l', 'i', 'g'), value = enableLigatures ? 1u : 0u, end = uint.MaxValue };
            HarfBuzzNative.hb_shape(face.NativeFont, shapingBuffer, features, 3);
            var infos = HarfBuzzNative.hb_buffer_get_glyph_infos(shapingBuffer, out uint size);
            var positions = HarfBuzzNative.hb_buffer_get_glyph_positions(shapingBuffer, out _);
            count = (int)size;
            if (destination.Length < count) Array.Resize(ref destination, Mathf.NextPowerOfTwo(count));
            for (int i = 0; i < count; i++) destination[i] = new NativeShapeGlyph { glyph = infos[i].glyph, flags = infos[i].mask & 7u,
                cluster = start + (int)infos[i].cluster - first, advance = positions[i].xAdvance, x = positions[i].xOffset, y = positions[i].yOffset };
            }
        }
    }
}
