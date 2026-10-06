using System;
using System.Collections.Generic;
using AOT;
using BurstWord.Typography;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BurstWord.Adapters.HarfBuzz
{
    [CreateAssetMenu(menuName = "BurstWord/HarfBuzz Shaper")]
    [BurstCompile]
    public sealed unsafe class HarfBuzzTextShaper : TextShaperAsset
    {
        public override string Name => "HarfBuzz";
        public override bool RequiresFontData => true;
        public override uint GetScript(uint codePoint) => HarfBuzzNative.hb_unicode_script(HarfBuzzNative.hb_unicode_funcs_get_default(), codePoint);
        public override ITextShapingFont CreateFont(TMP_FontAsset font, byte[] openTypeData) => new FontSession(font, openTypeData);

        private static TextShapingFunctions functions;
        private static TextShapingFunctions Functions
        {
            get
            {
                if (!functions.IsCreated) functions = new TextShapingFunctions
                {
                    Shape = BurstCompiler.CompileFunctionPointer<ShapeTextRun>(ShapeRun),
                    Release = BurstCompiler.CompileFunctionPointer<ReleaseTextWorker>(ReleaseWorker)
                };
                return functions;
            }
        }

        private sealed class FontSession : IJobTextShapingFont
        {
            private IntPtr handle, worker;
            public IntPtr JobHandle => handle;
            public TextShapingFunctions JobFunctions => Functions;
            public FontSession(TMP_FontAsset font, byte[] data)
            {
                if (data == null || data.Length == 0) throw new ArgumentException("HarfBuzz needs original font bytes.");
                IntPtr blob;
                fixed (byte* pointer = data) blob = HarfBuzzNative.hb_blob_create(pointer, (uint)data.Length, 0, IntPtr.Zero, IntPtr.Zero);
                var face = HarfBuzzNative.hb_face_create(blob, 0);
                try
                {
                    handle = HarfBuzzNative.hb_font_create(face);
                    HarfBuzzNative.hb_ot_font_set_funcs(handle);
                    HarfBuzzNative.hb_font_set_scale(handle, (int)font.faceInfo.pointSize * 64, (int)font.faceInfo.pointSize * 64);
                    HarfBuzzNative.hb_font_make_immutable(handle);
                }
                finally { HarfBuzzNative.hb_face_destroy(face); HarfBuzzNative.hb_blob_destroy(blob); }
            }
            public bool TryGetGlyphIndex(uint codePoint, out uint glyphId) => HarfBuzzNative.hb_font_get_nominal_glyph(handle, codePoint, out glyphId) != 0;
            public void Shape(in TextShapingRequest request, List<TextShapingGlyph> output)
            {
                TextShapingJobResult result = default;
                fixed (uint* points = request.CodePoints)
                    ShapeRun(handle, ref worker, points + request.ContextStart, request.ContextLength,
                        request.RunStart - request.ContextStart, request.RunLength, request.Script,
                        (request.RightToLeft ? 1 : 0) | (request.Kerning ? 2 : 0) | (request.Ligatures ? 4 : 0), ref result);
                for (int i = 0; i < result.Count; i++)
                { var glyph = result.Glyphs[i]; glyph.Cluster += request.ContextStart; output.Add(glyph); }
            }
            public void Dispose()
            {
                if (worker != IntPtr.Zero) { ReleaseWorker(worker); worker = IntPtr.Zero; }
                if (handle != IntPtr.Zero) { HarfBuzzNative.hb_font_destroy(handle); handle = IntPtr.Zero; }
            }
        }

        private struct Worker { public IntPtr Buffer; public TextShapingGlyph* Glyphs; public int Capacity; }

        [BurstCompile(DisableDirectCall = true)]
        [MonoPInvokeCallback(typeof(ShapeTextRun))]
        private static void ShapeRun(IntPtr font, ref IntPtr worker, uint* text, int textLength,
            int runStart, int runLength, uint script, int flags, ref TextShapingJobResult result)
        {
            if (worker == IntPtr.Zero)
            {
                var created = (Worker*)UnsafeUtility.Malloc(sizeof(Worker), 16, Allocator.Persistent);
                *created = default; created->Buffer = HarfBuzzNative.hb_buffer_create(); worker = (IntPtr)created;
            }
            var state = (Worker*)worker;
            HarfBuzzNative.hb_buffer_clear_contents(state->Buffer);
            HarfBuzzNative.hb_buffer_add_utf32(state->Buffer, text, textLength, (uint)runStart, runLength);
            HarfBuzzNative.hb_buffer_set_direction(state->Buffer, (flags & 1) != 0 ? 5 : 4);
            HarfBuzzNative.hb_buffer_set_script(state->Buffer, script);
            HarfBuzzNative.hb_buffer_guess_segment_properties(state->Buffer);
            var features = stackalloc HarfBuzzNative.Feature[3];
            features[0] = new HarfBuzzNative.Feature { tag = 0x6b65726e, value = (uint)((flags & 2) != 0 ? 1 : 0), end = uint.MaxValue };
            features[1] = new HarfBuzzNative.Feature { tag = 0x6c696761, value = (uint)((flags & 4) != 0 ? 1 : 0), end = uint.MaxValue };
            features[2] = new HarfBuzzNative.Feature { tag = 0x636c6967, value = features[1].value, end = uint.MaxValue };
            HarfBuzzNative.hb_shape(font, state->Buffer, features, 3);
            var infos = HarfBuzzNative.hb_buffer_get_glyph_infos(state->Buffer, out uint size);
            var positions = HarfBuzzNative.hb_buffer_get_glyph_positions(state->Buffer, out _);
            int count = (int)size;
            if (state->Capacity < count)
            {
                int capacity = 128; while (capacity < count) capacity *= 2;
                if (state->Glyphs != null) UnsafeUtility.Free(state->Glyphs, Allocator.Persistent);
                state->Glyphs = (TextShapingGlyph*)UnsafeUtility.Malloc((long)capacity * sizeof(TextShapingGlyph), 16, Allocator.Persistent);
                state->Capacity = capacity;
            }
            for (int i = 0; i < count; i++) state->Glyphs[i] = new TextShapingGlyph
            {
                GlyphId = infos[i].glyph, Cluster = (int)infos[i].cluster, Flags = infos[i].mask & 7u,
                Advance = positions[i].xAdvance / 64f, OffsetX = positions[i].xOffset / 64f, OffsetY = positions[i].yOffset / 64f
            };
            result.Glyphs = state->Glyphs; result.Count = count;
        }

        [BurstCompile(DisableDirectCall = true)]
        [MonoPInvokeCallback(typeof(ReleaseTextWorker))]
        private static void ReleaseWorker(IntPtr worker)
        {
            var state = (Worker*)worker;
            if (state == null) return;
            HarfBuzzNative.hb_buffer_destroy(state->Buffer);
            if (state->Glyphs != null) UnsafeUtility.Free(state->Glyphs, Allocator.Persistent);
            UnsafeUtility.Free(state, Allocator.Persistent);
        }
    }
}
