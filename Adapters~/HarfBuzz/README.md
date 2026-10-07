# BurstWord HarfBuzz Adapter

Optional OpenType shaping for BurstWord, Unity 2022.3 and newer. Install the BurstWord core first, then add:

```text
https://github.com/shanflyer/BurstWord.git?path=/Adapters~/HarfBuzz#v0.3.0
```

Explicitly pass `Runtime/HarfBuzz.asset` from this package to `renderer.SetTextShaperProvider(harfBuzz)` in your initialization code. Installation does not select it automatically. The core manager has no shaping Asset field or enable switch. The adapter creates no object per text and preserves the shaping caches and Burst job-preparation path. `renderer.SetTextShaper(null)` removes it and returns to standard TMP glyph-data layout. This factory API is an advanced option for native Job integration; ordinary third-party integrations can use the core's simpler `TextShapingCallback` delegate without an Asset.

The editor prepares original font data/catalogs before Play and before build, using fonts referenced by TMP assets in your project's Assets. For fonts supplied by another package, ensure the consuming project has a TMP asset referencing that font or supply a `BrgFontSources` catalog. Font sources must exist, be readable and contain the required glyphs. Data is written under `Assets/BurstWord/Resources`; imported benchmark sources are already supplied. Use **Tools → BurstWord → HarfBuzz → Prepare Font Sources** for an explicit refresh.

HarfBuzz 8.3.1 desktop/Android libraries are filtered by platform and CPU; Apple/WebGL compile the bundled upstream C++ source with Unity's target toolchain. Windows x86/x64/ARM64, macOS universal, Linux x64, Android ARMv7/ARM64/x86/x64, iOS/tvOS/visionOS and WebGL integrations are included. Your Unity version/build modules must support the target. Closed consoles need dedicated native integration. These integrations are not all device-certified; see the core platform document for validation limits.

This package requires the core; it does not install legacy TMP into Unity 6. Other shaping plugins may implement `BurstWord.Typography.ITextShaper` without depending on this package.

BurstWord adapter code is MIT. HarfBuzz has its own retained licenses: `Runtime/Plugins/HarfBuzz-LICENSE.txt`, `HarfBuzz-THIRD-PARTY-NOTICES.txt` and `HarfBuzzSource~/COPYING`. Binary/source provenance and hashes are in `Runtime/Plugins/PROVENANCE.txt` and `NATIVE_MANIFEST.json`. Desktop/Android binaries originate from HarfBuzzSharp.NativeAssets 8.3.1.5; bundled source is upstream HarfBuzz 8.3.1.

For local adapter development, use a checkout outside a parent folder ending in `~`. Unity 6 can compile such nested local sources while failing to associate their MonoScript assets. Normal Git/UPM installation extracts the optional package into its own package-cache root and needs no workaround.
