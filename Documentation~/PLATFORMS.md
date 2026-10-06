# Rendering and native platform integration

The default backend is `RenderBackend.Auto`. BRG is preferred on D3D11, D3D12, Vulkan and Metal when the device supports the storage-buffer shader. Shader support and BRG initialization are checked. Other devices use ordinary GPU instancing. `RenderBackend.BRG` also falls back when unavailable; `RenderBackend.Instancing` forces the fallback for testing. Select the backend before enabling/initializing the manager. The benchmark buttons clear and reinitialize the manager when switching.

The fallback uses `CommandBuffer.DrawMeshInstanced` for the ordered overlay/opaque-occlusion modes, and `Graphics.DrawMeshInstanced` for whole-label scene-transparent draws. It uses the same generated quad, layout caches, glyph data, label poses, sorting and sampled animation texture as BRG. It does not allocate raw/structured graphics buffers or text GameObjects. Glyph properties are uploaded in reusable uniform arrays, with up to 64 glyphs per draw to fit WebGL 2's uniform-block limits. Font/material changes split draws while preserving text order. Scene transparency also splits at label boundaries so Unity can sort complete labels with scene renderers. This path costs more CPU submissions than BRG; it is a compatibility fallback, not a promise of identical throughput.

Both backends require URP and the BurstWord renderer feature. Package setup installs that feature and keeps BRG/DOTS and ordinary instancing variants. A device without GPU instancing cannot use either backend; initialization reports this instead of silently changing to per-label objects. Built-in RP and HDRP are not implemented.

## Included native targets

| Target | Typography integration |
| --- | --- |
| Windows Editor / standalone | x64 Editor; x86, x64 and ARM64 Player libraries, filtered by CPU |
| UWP | Windows native libraries filtered by x86, x64 or ARM64 |
| macOS Editor / Player | Universal library containing x64 (macOS 10.13+) and ARM64 (macOS 11+) |
| Linux Editor / Player | x64 library; system libc, libm and libpthread only, no external font engine or C++ runtime library |
| Android | ARMv7, ARM64, x86 and x64 libraries; 16 KB load alignment; system libc/libm/libdl only |
| iOS / tvOS / visionOS | Upstream C++ source added to the generated UnityFramework Xcode target, compiled for the selected device/simulator |
| WebGL 2 | C++ source plug-in compiled by Unity's Emscripten toolchain; instanced rendering without storage buffers |

These are native integration targets, not a claim that every architecture is available in every Unity version. For example, Windows ARM64 Player support depends on Unity's build modules/version. Install the normal Unity build module and platform SDK for your target.

The native C ABI is HarfBuzz 8.3.1. Desktop/Android binaries come from HarfBuzzSharp.NativeAssets 8.3.1.5; file names, package members and SHA-256 hashes are recorded in `Runtime/Plugins/NATIVE_MANIFEST.json`. Apple/WebGL use the included upstream 8.3.1 amalgamation sources, preserving all OpenType shapers. This avoids coupling WebGL to an incompatible precompiled Emscripten archive or Apple to an incorrect device/simulator framework. Original licenses and notices are retained.

Closed console platforms require their proprietary SDK/toolchain and platform-specific native plug-in integration. Their binaries are not included, and console compatibility is not certified by this package. The upstream shaping source is available for that integration.

## Validation

Windows x64 Unity 2022.3.62f3 / URP 14 Development Players were used to test both backends on D3D11 and automatic fallback on OpenGL Core. Checks cover all eight benchmark workloads, all nine space/sorting combinations, actual rendered pixels, back-to-front whole-label ordering, GPU curve animation, and opaque/transparent scene occlusion. The instancing test also checks that BRG, glyph buffers, label buffers and animation metadata buffers are absent. Existing emission, capacity, reset and memory/FPS controls are retained.

The 23 imported HarfBuzz functions were checked in the supplied native files. The source amalgamation was compiled and its shaping output compared with the Windows binary for Arabic, Devanagari, Hebrew, Thai, Latin ligatures and CJK. Android ELF load alignment and dependencies, and macOS universal architectures/deployment targets, were inspected.

macOS, Linux, Android, Apple Xcode builds and WebGL browser execution have not been device-tested on this Windows workstation. Cross-platform integration is provided; real build/device validation remains necessary before declaring a platform release certified. The installed Unity editor currently has only Windows Player build support; its official WebGL module download returned HTTP 404 from the local download endpoint during validation.
