using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace BurstWord.BRG
{
    // Bind atlas APIs once. Legacy versions use delegates; newer internal face-handle
    // APIs use reflection only when adding a previously uncached glyph, never in jobs.
    internal static class FontEngineAtlasBridge
    {
        internal delegate bool AddGlyph(uint index, int padding, GlyphPackingMode packing, List<GlyphRect> free, List<GlyphRect> used, GlyphRenderMode mode, Texture2D texture, out Glyph glyph);
        private static readonly AddGlyph legacyAdd;
        internal static readonly Action<Texture2D> Reset;
        private static readonly MethodInfo loadBytes, loadFont, getGlyph, addGlyph, unload;
        static FontEngineAtlasBridge()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var engine = typeof(FontEngine);
            var parameters = new[] { typeof(uint), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>), typeof(List<GlyphRect>), typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph).MakeByRefType() };
            var add = engine.GetMethod("TryAddGlyphToTexture", flags, null, parameters, null);
            var reset = engine.GetMethod("ResetAtlasTexture", flags, null, new[] { typeof(Texture2D) }, null);
            if (reset == null) throw new NotSupportedException("This Unity version has incompatible FontEngine atlas reset APIs.");
            Reset = (Action<Texture2D>)reset.CreateDelegate(typeof(Action<Texture2D>));
            if (add != null) { legacyAdd = (AddGlyph)add.CreateDelegate(typeof(AddGlyph)); return; }
            var handle = engine.Assembly.GetType("UnityEngine.TextCore.LowLevel.FontFaceHandle");
            if (handle != null)
            {
                loadBytes = engine.GetMethod("LoadFontFace", flags, null, new[] { typeof(byte[]), typeof(float), typeof(int), handle.MakeByRefType() }, null);
                loadFont = engine.GetMethod("LoadFontFace", flags, null, new[] { typeof(Font), typeof(float), typeof(int), handle.MakeByRefType() }, null);
                getGlyph = engine.GetMethod("TryGetGlyphWithIndexValue", flags, null, new[] { handle, typeof(uint), typeof(GlyphLoadFlags), typeof(Glyph).MakeByRefType() }, null);
                var withHandle = new Type[parameters.Length + 1]; withHandle[0] = handle; Array.Copy(parameters, 0, withHandle, 1, parameters.Length);
                addGlyph = engine.GetMethod("TryAddGlyphToTexture", flags, null, withHandle, null);
                unload = engine.GetMethod("UnloadFontFace", flags, null, new[] { handle }, null);
            }
            if (loadBytes == null || loadFont == null || getGlyph == null || addGlyph == null || unload == null)
                throw new NotSupportedException("This Unity version has incompatible FontEngine face-handle APIs.");
        }

        internal sealed class Face : IDisposable
        {
            private readonly TMP_FontAsset font;
            private readonly byte[] data;
            private object handle;
            private object[] glyphArguments, atlasArguments;
            public Face(TMP_FontAsset font, byte[] data) { this.font = font; this.data = data; }
            public bool TryGetGlyph(uint index, out Glyph glyph)
            {
                glyph = null;
                if (legacyAdd != null)
                {
                    if (data != null ? FontEngine.LoadFontFace(data, (int)font.faceInfo.pointSize) != FontEngineError.Success :
                        font.sourceFontFile == null || FontEngine.LoadFontFace(font.sourceFontFile, (int)font.faceInfo.pointSize) != FontEngineError.Success) return false;
                    return FontEngine.TryGetGlyphWithIndexValue(index, GlyphLoadFlags.LOAD_NO_BITMAP, out glyph);
                }
                if (handle == null)
                {
                    if (data == null && font.sourceFontFile == null) return false;
                    var arguments = new object[] { data != null ? (object)data : font.sourceFontFile, font.faceInfo.pointSize, 0, null };
                    if ((FontEngineError)(data != null ? loadBytes : loadFont).Invoke(null, arguments) != FontEngineError.Success) return false;
                    handle = arguments[3];
                    glyphArguments = new object[] { handle, null, GlyphLoadFlags.LOAD_NO_BITMAP, null };
                    atlasArguments = new object[9]; atlasArguments[0] = handle;
                }
                glyphArguments[1] = index; glyphArguments[3] = null;
                bool success = (bool)getGlyph.Invoke(null, glyphArguments); glyph = glyphArguments[3] as Glyph; return success;
            }
            public bool Add(uint index, int padding, GlyphPackingMode packing, List<GlyphRect> free, List<GlyphRect> used, GlyphRenderMode mode, Texture2D texture, out Glyph glyph)
            {
                if (legacyAdd != null) return legacyAdd(index, padding, packing, free, used, mode, texture, out glyph);
                atlasArguments[1] = index; atlasArguments[2] = padding; atlasArguments[3] = packing;
                atlasArguments[4] = free; atlasArguments[5] = used; atlasArguments[6] = mode; atlasArguments[7] = texture; atlasArguments[8] = null;
                bool success = (bool)addGlyph.Invoke(null, atlasArguments); glyph = atlasArguments[8] as Glyph; return success;
            }
            public void Dispose()
            {
                if (handle != null) { unload.Invoke(null, new[] { handle }); handle = null; }
                glyphArguments = atlasArguments = null;
            }
        }
    }
}
