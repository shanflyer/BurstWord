# Third-party components

## RichTextKit Unicode algorithms and data

The Unicode bidi, line-breaking and grapheme algorithms in `Runtime/ThirdParty/RichTextKit` originate from [RichTextKit](https://github.com/toptensoftware/RichTextKit), commit `e28a3f583a0d9b2221baab25b730f3bc5a863d35`. Resource loading was adapted to Unity Resources. The original MIT license and provenance are retained in that directory. Trie data used by these algorithms is in `Runtime/Resources/BurstWordUnicode`.

## HarfBuzz native library

The Windows, macOS, Linux and Android native libraries in `Runtime/Plugins` come from HarfBuzzSharp.NativeAssets 8.3.1.5 (native HarfBuzz 8.3.1). Original licenses, notices, source provenance and each binary's SHA-256 hash are retained in that directory. Plug-in metadata filters each library by platform and CPU.

Apple and WebGL source integration uses upstream [HarfBuzz 8.3.1](https://github.com/harfbuzz/harfbuzz/tree/2b3631a866b3077d9d675caa4ec9010b342b5a7c). The transitive amalgamation sources and original copyright notices are retained in `Runtime/Plugins/HarfBuzzSource~`, with the upstream `COPYING` license. `hb-version.h` is generated from the upstream template for version 8.3.1. The small target wrappers and Xcode integration belong to BurstWord.

## Sample fonts

Noto font files, TMP-derived font assets and OpenType byte copies in `Samples~/Benchmarks` use the SIL Open Font License 1.1. Copyright notices, license files and provenance are retained in `Samples~/Benchmarks/Fonts`. OpenType copies of Liberation Sans from the TMP essential resources also retain the supplied Liberation Sans OFL license in this directory.

The samples reference Liberation Sans TMP assets, TMP shaders and EmojiOne sprite assets supplied by **TMP Essential Resources**. Those Unity-provided assets are not redistributed by this repository. Import the resources through TextMeshPro in your Unity project; their own licenses and attribution remain in that imported resource set.
