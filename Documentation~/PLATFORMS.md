# Unity versions, rendering and optional native integration

The default backend is `RenderBackend.Auto`. BRG is preferred on D3D11, D3D12, Vulkan and Metal when the device supports the storage-buffer shader. Shader support and BRG initialization are checked. Other devices use ordinary GPU instancing. `RenderBackend.BRG` also falls back when unavailable; `RenderBackend.Instancing` forces the fallback for testing. Select the backend before enabling/initializing the manager. The benchmark buttons clear and reinitialize the manager when switching.

The fallback uses `CommandBuffer.DrawMeshInstanced` for the ordered overlay/opaque-occlusion modes, and `Graphics.DrawMeshInstanced` for whole-label scene-transparent draws. It uses the same generated quad, layout caches, glyph data, label poses, sorting and sampled animation texture as BRG. It does not allocate raw/structured graphics buffers or text GameObjects. Glyph properties are uploaded in reusable uniform arrays, with up to 64 glyphs per draw to fit WebGL 2's uniform-block limits. Font/material changes split draws while preserving text order. Scene transparency also splits at label boundaries so Unity can sort complete labels with scene renderers. This path costs more CPU submissions than BRG; it is a compatibility fallback, not a promise of identical throughput.

Both backends require URP and the BurstWord renderer feature. Package setup installs that feature and keeps BRG/DOTS and ordinary instancing variants. A device without GPU instancing cannot use either backend; initialization reports this instead of silently changing to per-label objects. Built-in RP and HDRP are not implemented.

## Unity versions

The supported baseline is Unity 2022.3 with URP. Unity 6 uses its integrated TMP/uGUI and the Render Graph renderer path. Minimum UPM dependency versions are not pins to obsolete Unity 2022 packages: a fresh Unity 6000.6 installation resolved URP 17.6, uGUI 2.6 and Burst 2.0 without legacy TMP. On 2022.3 / 2023.1 a bootstrap assembly installs separate TMP only when missing. TMP types and feature-record changes are conditionally adapted. Unity 6000.6 uses full-width EntityId for cache and camera identities.

Compiled on installed editors 2022.3.62f3, 6000.2.1f1, 6000.4.7f1 and 6000.6.0f1. Windows Player checks and limits are described below. Unity 2023.x / 6000.0 are targeted by the corresponding API paths but are not separately run here. Future Unity releases cannot be certified before testing their API changes.

## Optional HarfBuzz native targets

The **core package has no HarfBuzz dependency**. Standard TMP glyph layout can be built without platform-specific shaping files. The following integrations belong exclusively to the optional `com.shanflyer.burstword.harfbuzz` adapter; users may choose another provider instead.


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

The native C ABI is HarfBuzz 8.3.1. Desktop/Android binaries come from HarfBuzzSharp.NativeAssets 8.3.1.5; file names, package members and SHA-256 hashes are recorded in `Adapters~/HarfBuzz/Runtime/Plugins/NATIVE_MANIFEST.json`. Apple/WebGL use the included upstream 8.3.1 amalgamation sources, preserving all OpenType shapers. This avoids coupling WebGL to an incompatible precompiled Emscripten archive or Apple to an incorrect device/simulator framework. Original licenses and notices are retained.

The optional HarfBuzz adapter needs proprietary SDK/native integration for closed console platforms; its console binaries are not included. The core has no such shaping-library requirement. Rendering on consoles still depends on Unity/SRP/device capabilities and is not certified without access to their SDKs. The included upstream shaping source is available for adapter integration.

## Validation

Windows x64 Development Players were built on Unity 2022.3.62f3 / URP 14, 6000.2.1f1 / URP 17.2, 6000.4.7f1 / URP 17.4 and 6000.6.0f1 / URP 17.6. Both backends were checked on D3D11; Unity 2022.3 also checks automatic fallback on OpenGL Core. Unity 6 checks use Render Graph. Checks cover all eight benchmark workloads, all nine space/sorting combinations, actual rendered pixels, back-to-front whole-label ordering, GPU curve animation, and opaque/transparent scene occlusion. The instancing test also checks that BRG, glyph buffers, label buffers and animation metadata buffers are absent. Existing emission, capacity, reset and memory/FPS controls are retained.

The default TMP wrapped Job path was compared against full main-thread typography, including deliberately varying pair metrics that force fallback. Managed-only provider substitution, provider switching/disposal and optional HarfBuzz Job acceleration were also tested in a Windows Player. Fresh 2022.3 core installation automatically installed TMP; fresh 6000.6 used integrated uGUI/TMP without legacy TMP.

The 23 imported HarfBuzz functions were checked in the supplied native files. The source amalgamation was compiled and its shaping output compared with the Windows binary for Arabic, Devanagari, Hebrew, Thai, Latin ligatures and CJK. Android ELF load alignment and dependencies, and macOS universal architectures/deployment targets, were inspected.

macOS, Linux, Android, Apple Xcode builds and WebGL browser execution have not been device-tested on this Windows workstation. Cross-platform integration is provided; real build/device validation remains necessary before declaring a platform release certified. No mobile, browser, macOS or console device certification is claimed by these Windows tests.
