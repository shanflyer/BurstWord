using System;
using System.Runtime.InteropServices;

namespace BurstWord.Adapters.HarfBuzz
{
    // HarfBuzz 8.3.1 C ABI. Handles belong to a renderer and are disposed on disable.
    internal static unsafe class HarfBuzzNative
    {
#if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
        private const string Library = "__Internal";
#else
        private const string Library = "libHarfBuzzSharp";
#endif
        [StructLayout(LayoutKind.Sequential)] internal struct Info { public uint glyph, mask, cluster, var1, var2; }
        [StructLayout(LayoutKind.Sequential)] internal struct Position { public int xAdvance, yAdvance, xOffset, yOffset; public uint var; }
        [StructLayout(LayoutKind.Sequential)] internal struct Feature { public uint tag, value, start, end; }
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr hb_blob_create(byte* data, uint length, int mode, IntPtr user, IntPtr destroy);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_blob_destroy(IntPtr blob);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr hb_face_create(IntPtr blob, uint index);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_face_destroy(IntPtr face);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint hb_face_get_upem(IntPtr face);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr hb_font_create(IntPtr face);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_font_destroy(IntPtr font);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_ot_font_set_funcs(IntPtr font);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_font_set_scale(IntPtr font, int x, int y);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_font_make_immutable(IntPtr font);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int hb_font_get_nominal_glyph(IntPtr font, uint unicode, out uint glyph);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr hb_buffer_create();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_buffer_destroy(IntPtr buffer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_buffer_clear_contents(IntPtr buffer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_buffer_add_utf32(IntPtr buffer, uint* text, int length, uint offset, int count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_buffer_set_direction(IntPtr buffer, int direction);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_buffer_set_script(IntPtr buffer, uint script);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_buffer_guess_segment_properties(IntPtr buffer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void hb_shape(IntPtr font, IntPtr buffer, Feature* features, uint count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern Info* hb_buffer_get_glyph_infos(IntPtr buffer, out uint count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern Position* hb_buffer_get_glyph_positions(IntPtr buffer, out uint count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr hb_unicode_funcs_get_default();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint hb_unicode_script(IntPtr funcs, uint unicode);
        internal static uint Tag(char a, char b, char c, char d) => (uint)(a << 24 | b << 16 | c << 8 | d);
    }
}
