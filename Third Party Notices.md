# Third-party components

## RichTextKit Unicode algorithms and data

The Unicode bidi, line-breaking and grapheme algorithms in `Runtime/ThirdParty/RichTextKit` originate from [RichTextKit](https://github.com/toptensoftware/RichTextKit), commit `e28a3f583a0d9b2221baab25b730f3bc5a863d35`. Resource loading was adapted to Unity Resources. The original MIT license and provenance are retained in that directory. Trie data used by these algorithms is in `Runtime/Resources/BurstWordUnicode`.

## HarfBuzz native library

`Runtime/Plugins/x86_64/libHarfBuzzSharp.dll` comes from HarfBuzzSharp.NativeAssets.Win32 8.3.0. Its license, third-party notices, source URL and binary hash are retained in `Runtime/Plugins`. This binary is enabled only for Windows x64 Editor and standalone Player.

## Sample fonts

Noto font files, TMP-derived font assets and OpenType byte copies in `Samples~/Benchmarks` use the SIL Open Font License 1.1. Copyright notices, license files and provenance are retained in `Samples~/Benchmarks/Fonts`. OpenType copies of Liberation Sans from the TMP essential resources also retain the supplied Liberation Sans OFL license in this directory.

The samples reference Liberation Sans TMP assets, TMP shaders and EmojiOne sprite assets supplied by **TMP Essential Resources**. Those Unity-provided assets are not redistributed by this repository. Import the resources through TextMeshPro in your Unity project; their own licenses and attribution remain in that imported resource set.
