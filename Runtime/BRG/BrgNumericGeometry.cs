using TMPro;
using Unity.Collections;
using UnityEngine;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        internal bool useNumericGeometryCache = true;
        private struct NumericGlyph
        {
            public PositionedGlyph geometry;
            public float scale, advance, ascent, descent, baseline;
            public bool valid, visible;
        }
        private NumericGlyph[] numericGlyphs;
        private Vector3[] numericFirstPairs, numericSecondPairs;
        private TMP_FontAsset numericFont;
        private int numericSize;
        private bool numericKerning;
        private int numericGeometryVersion, nativeNumericVersion = -1;
        private struct NumericPreparedGlyph
        {
            public PreparedGlyph geometry;
            public float scale, advance, ascent, descent, baseline;
            public int visible;
        }
        private NativeArray<NumericPreparedGlyph> numericJobGlyphs;
        private NativeArray<Vector3> numericJobFirstPairs, numericJobSecondPairs;
        private static int NumericIndex(char c) => c == '+' ? 10 : c == '-' ? 11 : c - '0';
        private void PrepareNumericGeometry()
        {
            if (numericGlyphs != null && ReferenceEquals(numericFont, font) && numericSize == fontSize && numericKerning == enableKerning) return;
            numericFont = font; numericSize = fontSize; numericKerning = enableKerning;
            numericGeometryVersion++;
            numericGlyphs = new NumericGlyph[12]; numericFirstPairs = new Vector3[144]; numericSecondPairs = new Vector3[144];
            for (int i = 0; i < 12; i++)
            {
                uint unicode = (uint)(i < 10 ? '0' + i : i == 10 ? '+' : '-');
                if (!Resolve(unicode, out var resolved)) continue;
                var glyph = resolved.character.glyph; var face = resolved.font.faceInfo; var metrics = glyph.metrics;
                float scale = (float)fontSize / face.pointSize * face.scale * resolved.character.scale * glyph.scale;
                float baseline = face.baseline * scale, padding = Mathf.Min(1, resolved.font.atlasPadding);
                var atlas = resolved.font.atlasTextures[glyph.atlasIndex]; var rect = glyph.glyphRect;
                numericGlyphs[i] = new NumericGlyph { valid = true, visible = metrics.width > 0 && metrics.height > 0,
                    scale = scale, advance = metrics.horizontalAdvance, baseline = baseline, ascent = baseline + face.ascentLine * scale,
                    descent = baseline + face.descentLine * scale,
                    geometry = new PositionedGlyph { glyph = resolved,
                        rect = new Vector4(metrics.horizontalBearingX - padding, metrics.horizontalBearingY - metrics.height - padding,
                            (metrics.width + padding * 2) * scale, (metrics.height + padding * 2) * scale),
                        uv = new Vector4((rect.x - padding) / atlas.width, (rect.y - padding) / atlas.height,
                            (rect.width + padding * 2) / atlas.width, (rect.height + padding * 2) / atlas.height) } };
            }
            if (!enableKerning) return;
            for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++)
            {
                var a = numericGlyphs[i]; var b = numericGlyphs[j];
                if (!a.valid || !b.valid || !ReferenceEquals(a.geometry.glyph.font, b.geometry.glyph.font) ||
                    !Pair(a.geometry.glyph.font, a.geometry.glyph.character.glyph.index, b.geometry.glyph.character.glyph.index, out var pair)) continue;
                var first = pair.firstAdjustmentRecord.glyphValueRecord; var second = pair.secondAdjustmentRecord.glyphValueRecord;
                numericFirstPairs[i * 12 + j] = new Vector3(first.xPlacement, first.yPlacement, first.xAdvance);
                numericSecondPairs[i * 12 + j] = new Vector3(second.xPlacement, second.yPlacement, second.xAdvance);
            }
        }
        private bool PrepareNumericJob(string text, out int count)
        {
            count = 0;
            if (!useNumericGeometryCache || !CanUseNumericLayout || wrapWidth > 0 || !IsBasicNumber(text)) return false;
            PrepareNumericGeometry();
            for (int i = 0; i < text.Length; i++)
            {
                var glyph = numericGlyphs[NumericIndex(text[i])]; if (!glyph.valid) return false;
                if (glyph.visible) count++;
            }
            EnsureNumericJobStorage();
            if (nativeNumericVersion == numericGeometryVersion) return true;
            for (int i = 0; i < numericGlyphs.Length; i++)
            {
                var glyph = numericGlyphs[i];
                numericJobGlyphs[i] = new NumericPreparedGlyph { geometry = PreparedGlyph.From(glyph.geometry), scale = glyph.scale,
                    advance = glyph.advance, ascent = glyph.ascent, descent = glyph.descent, baseline = glyph.baseline, visible = glyph.visible ? 1 : 0 };
            }
            for (int i = 0; i < 144; i++) { numericJobFirstPairs[i] = numericFirstPairs[i]; numericJobSecondPairs[i] = numericSecondPairs[i]; }
            nativeNumericVersion = numericGeometryVersion; return true;
        }
        private void EnsureNumericJobStorage()
        {
            EnsurePreparationCapacity(ref numericJobGlyphs,12);
            EnsurePreparationCapacity(ref numericJobFirstPairs,144); EnsurePreparationCapacity(ref numericJobSecondPairs,144);
        }
        private bool BuildCachedNumbers(string text, Color color)
        {
            PrepareNumericGeometry();
            layout.Clear(); lineWidths.Clear(); float x = 0, ascent = float.MinValue, descent = float.MaxValue;
            for (int i = 0; i < text.Length; i++)
            {
                int index = NumericIndex(text[i]); var glyph = numericGlyphs[index]; if (!glyph.valid) return false;
                Vector3 adjustment = default;
                if (enableKerning)
                {
                    if (i > 0) adjustment += numericSecondPairs[NumericIndex(text[i - 1]) * 12 + index];
                    if (i + 1 < text.Length) adjustment += numericFirstPairs[index * 12 + NumericIndex(text[i + 1])];
                }
                ascent = Mathf.Max(ascent, glyph.ascent); descent = Mathf.Min(descent, glyph.descent);
                if (glyph.visible)
                {
                    var placed = glyph.geometry; placed.tint = color;
                    placed.rect.x = x + (placed.rect.x + adjustment.x) * glyph.scale;
                    placed.rect.y = glyph.baseline + (placed.rect.y + adjustment.y) * glyph.scale;
                    layout.Add(placed);
                }
                x += (glyph.advance + adjustment.z) * glyph.scale;
            }
            lineWidths.Add(x);
            float y = (ascent + descent) * .5f;
            for (int i = 0; i < layout.Count; i++) { var placed = layout[i]; placed.rect.x -= x * .5f; placed.rect.y -= y; layout[i] = placed; }
            return true;
        }
    }
}
