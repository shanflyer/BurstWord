# Changelog

## 0.3.0

- Remove mandatory HarfBuzz/native integration from the core. Use standard TMP glyph data by default and expose explicit optional shaping factories/assets.
- Move the supplied HarfBuzz integration into its own opt-in UPM package, preserving original native targets, licenses, cached shaping and Burst worker acceleration.
- Preserve default TMP wrapping preparation in jobs through compiled glyph/pair data; managed adapters can use the main layout path without implementing native callbacks.
- Select separate legacy TMP only where required, avoid Unity 6 integrated TMP/uGUI conflicts, and isolate embedded RichTextKit namespaces.
- Add URP Render Graph overlay drawing and version-specific TextCore, render-pass and EntityId compatibility through installed Unity 6000.6.
- Document provider contracts, activation, version checks and platform/device validation limits.

## 0.2.0

- Prefer BRG automatically and fall back to ordinary GPU instanced draws on incompatible devices; the fallback creates no per-label objects or storage buffers.
- Preserve typography, whole-label sorting, all space/occlusion modes and GPU animation across both rendering backends; add backend controls/readout to the benchmark.
- Bundle HarfBuzz 8.3.1 for Windows, macOS, Linux and Android, including 16 KB-aligned Android libraries and platform/CPU import filtering.
- Compile the included upstream shaping source with Apple Xcode and Unity WebGL toolchains, using static native linkage.
- Preserve ordinary instancing shader variants for runtime-created materials and use half-float animation textures when float filtering is unavailable.
- Document platform integration and the distinction between supplied integration and actual device validation.

## 0.1.0

- Initial Unity Package Manager distribution for Unity 2022.3 / URP 14.
- BRG glyph rendering, multilingual shaping and wrapping, whole-message sorting and world-space following.
- GPU animation presets authored through a constrained standalone AnimationClip editor.
- Two ready-made benchmark scenes and four animation presets supplied through Samples, with English benchmark controls and a default emission rate of 200 per second.
