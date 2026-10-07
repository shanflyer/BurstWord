using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TMPro;
using Unity.Burst;
using UnityEngine;

namespace BurstWord.Typography
{
    /// <summary>Optional application-owned shaping callback. Append glyphs to the reusable output list.
    /// Called on the main thread when layout needs shaping; cached results may be reused.</summary>
    public delegate void TextShapingCallback(in TextShapingRequest request, List<TextShapingGlyph> output);

    /// <summary>Advanced optional shaping factory for owned font sessions/Jobs. Ordinary callers use TextShapingCallback.</summary>
    public interface ITextShaper
    {
        string Name { get; }
        bool RequiresFontData { get; }
        uint GetScript(uint codePoint);
        ITextShapingFont CreateFont(TMP_FontAsset font, byte[] openTypeData);
    }

    /// <summary>Optional factory base for external adapters. The core manager selects providers through code only.</summary>
    public abstract class TextShaperAsset : ScriptableObject, ITextShaper
    {
        public abstract string Name { get; }
        public abstract bool RequiresFontData { get; }
        public abstract uint GetScript(uint codePoint);
        public abstract ITextShapingFont CreateFont(TMP_FontAsset font, byte[] openTypeData);
    }

    /// <summary>All indexes are UTF-32 indexes into CodePoints. Read only during Shape.
    /// RunStart/RunLength lie inside ContextStart/ContextLength; preserve surrounding joining context.</summary>
    public readonly struct TextShapingRequest
    {
        /// <summary>The actual TMP font after font selection and fallback resolution.</summary>
        public readonly TMP_FontAsset Font;
        public readonly uint[] CodePoints;
        public readonly int ContextStart, ContextLength, RunStart, RunLength;
        public readonly uint Script;
        public readonly bool RightToLeft, Kerning, Ligatures;
        public TextShapingRequest(uint[] points, int contextStart, int contextLength, int runStart, int runLength,
            uint script, bool rightToLeft, bool kerning, bool ligatures)
            : this(null, points, contextStart, contextLength, runStart, runLength, script, rightToLeft, kerning, ligatures) { }

        public TextShapingRequest(TMP_FontAsset font, uint[] points, int contextStart, int contextLength, int runStart, int runLength,
            uint script, bool rightToLeft, bool kerning, bool ligatures)
        {
            Font=font;
            CodePoints=points; ContextStart=contextStart; ContextLength=contextLength; RunStart=runStart; RunLength=runLength;
            Script=script; RightToLeft=rightToLeft; Kerning=kerning; Ligatures=ligatures;
        }
    }

    // Reuse the core's font sessions and layout caches without requiring an application to implement a factory.
    internal sealed class CallbackTextShaper : ITextShaper
    {
        private readonly TextShapingCallback callback;
        public CallbackTextShaper(TextShapingCallback callback) { this.callback = callback; }
        public string Name => "Custom callback";
        public bool RequiresFontData => false;
        // A callback owns script detection/segmentation within the supplied font/style/direction run.
        public uint GetScript(uint codePoint) => 0x5a797979; // ISO 15924 Common
        public ITextShapingFont CreateFont(TMP_FontAsset font, byte[] openTypeData) => new FontSession(font, callback);

        private sealed class FontSession : ITextShapingFont
        {
            private readonly TMP_FontAsset font;
            private readonly TextShapingCallback callback;
            public FontSession(TMP_FontAsset font, TextShapingCallback callback) { this.font = font; this.callback = callback; }
            public bool TryGetGlyphIndex(uint codePoint, out uint glyphId)
            {
                if (font.characterLookupTable.TryGetValue(codePoint, out var character))
                { glyphId = character.glyph.index; return true; }
                glyphId = 0; return false;
            }
            public void Shape(in TextShapingRequest request, List<TextShapingGlyph> output) => callback(in request, output);
            public void Dispose() { }
        }
    }

    /// <summary>Glyph IDs belong to the supplied TMP font's original face.
    /// Cluster is an absolute UTF-32 index in the request. Positions use TMP faceInfo.pointSize units, Y up.
    /// Output is in visual order within the run. UnsafeToBreak prevents cached reuse across that boundary.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TextShapingGlyph
    {
        public uint GlyphId;
        public int Cluster;
        public float Advance, OffsetX, OffsetY;
        public uint Flags;
        public const uint UnsafeToBreak = 1;
    }

    public interface ITextShapingFont : IDisposable
    {
        bool TryGetGlyphIndex(uint codePoint, out uint glyphId);
        // Append to the caller-owned reusable output list; do not retain the request array/list.
        void Shape(in TextShapingRequest request, List<TextShapingGlyph> output);
    }

    /// <summary>Optional acceleration. Ordinary C# adapters need not implement this interface.
    /// The immutable font handle stays alive until all jobs and worker cleanup have completed.</summary>
    public interface IJobTextShapingFont : ITextShapingFont
    {
        IntPtr JobHandle { get; }
        TextShapingFunctions JobFunctions { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TextShapingJobResult { public TextShapingGlyph* Glyphs; public int Count; }

    // flags: bit 0 RTL, bit 1 kerning, bit 2 ligatures. Clusters are relative to text[0].
    // Result memory belongs to worker and remains valid until the next Shape on that worker.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void ShapeTextRun(IntPtr font, ref IntPtr worker, uint* text, int textLength,
        int runStart, int runLength, uint script, int flags, ref TextShapingJobResult result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ReleaseTextWorker(IntPtr worker);

    public struct TextShapingFunctions
    {
        public FunctionPointer<ShapeTextRun> Shape;
        public FunctionPointer<ReleaseTextWorker> Release;
        public bool IsCreated => Shape.IsCreated && Release.IsCreated;
    }
}
