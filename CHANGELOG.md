# Changelog

## 0.3.4

- Group default font, font size and optional additional fonts together in the manager Inspector; clarify that TMP font-asset fallback lists already work without additional registration.

## 0.3.3

- Add horizontal left/center/right and vertical top/middle/bottom alignment, preserving centered emission-point alignment by default.
- Optionally align within a fixed numeric text area without creating a RectTransform; automatic wrapping respects the area's width.
- Allow alignment and text-area overrides on numeric, text, Transform/pose and batch emissions, preserving previous public signatures.
- Include alignment and area dimensions in positioned-layout cache keys, and retain numeric Burst preparation and instance-write jobs for mixed alignment batches.

## 0.3.2

- Match screen-text scaling to Unity Canvas Scaler: Constant Pixel Size, Scale With Screen Size with Match Width Or Height / Expand / Shrink, and Constant Physical Size with DPI fallback.
- Optionally follow an existing screen-space root Canvas's actual scale factor without creating UI objects; show only the applicable scaling controls in the Inspector.
- Preserve the default 1920×1080 / Match 0.5 configuration. Apply scaling changes to live text and GPU animation without rebuilding labels; keep world-space text unchanged.
- Share the same scaling calculation between BRG, instancing, material initialization and transparent-animation sorting. Use the full output for manual scaling and the camera viewport for pixel projection.

## 0.3.1

- Add a direct-integration tutorial that requires no Samples and explicitly checks the active camera's BurstWord ordered text Renderer Feature.
- Replace the flat manager Inspector with grouped optional settings, real feature switches, conditional controls and camera-specific pipeline detection/installation.
- Allow font, TMP material preset, font size and GPU animation selection per numeric/text emission, Transform/pose emission and batch request, without changing manager defaults or existing text.
- Include font/material/size in preparation cache identity and preserve shared numeric Job tables when mixing font overrides; verify mixed wrapping batches and rendered output on BRG and instancing in Unity 2022.3 and 6000.6 Windows Players.

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
