using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace BurstWord.BRG
{
    // Unity 2022 exposes glyph metrics publicly, but grants atlas packing only to TMP.
    // Bind once to the same native-backed entry points TMP uses; no reflection per emission.
    internal static class FontEngineAtlasBridge
    {
        internal delegate bool AddGlyph(uint index, int padding, GlyphPackingMode packing, List<GlyphRect> free, List<GlyphRect> used, GlyphRenderMode mode, Texture2D texture, out Glyph glyph);
        internal static readonly AddGlyph Add;
        internal static readonly Action<Texture2D> Reset;
        static FontEngineAtlasBridge()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var add = typeof(FontEngine).GetMethod("TryAddGlyphToTexture", flags, null,
                new[] { typeof(uint), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>), typeof(List<GlyphRect>), typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph).MakeByRefType() }, null);
            var reset = typeof(FontEngine).GetMethod("ResetAtlasTexture", flags, null, new[] { typeof(Texture2D) }, null);
            if (add == null || reset == null) throw new NotSupportedException("This Unity version has incompatible FontEngine atlas entry points.");
            Add = (AddGlyph)add.CreateDelegate(typeof(AddGlyph));
            Reset = (Action<Texture2D>)reset.CreateDelegate(typeof(Action<Texture2D>));
        }
    }
}
