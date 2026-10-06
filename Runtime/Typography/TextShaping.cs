using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TMPro;
using Unity.Burst;
using UnityEngine;

namespace BurstWord.Typography
{
    /// <summary>Optional shaping factory. No global registration or automatic provider selection.</summary>
    public interface ITextShaper
    {
        string Name { get; }
        bool RequiresFontData { get; }
        uint GetScript(uint codePoint);
        ITextShapingFont CreateFont(TMP_FontAsset font, byte[] openTypeData);
    }

    /// <summary>Inspectable provider asset. Third-party adapters may implement ITextShaper directly instead.</summary>
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
        public readonly uint[] CodePoints;
        public readonly int ContextStart, ContextLength, RunStart, RunLength;
        public readonly uint Script;
        public readonly bool RightToLeft, Kerning, Ligatures;
        public TextShapingRequest(uint[] points, int contextStart, int contextLength, int runStart, int runLength,
            uint script, bool rightToLeft, bool kerning, bool ligatures)
        {
            CodePoints=points; ContextStart=contextStart; ContextLength=contextLength; RunStart=runStart; RunLength=runLength;
            Script=script; RightToLeft=rightToLeft; Kerning=kerning; Ligatures=ligatures;
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
