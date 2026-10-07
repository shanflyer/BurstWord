# Changelog

## Unreleased

- Expose built-in Rise Height under Animations; explain that empty lists/slots use it and speed is height divided by the per-emission duration. Preserve existing serialized height values and GPU motion.

- Add Text Layout → Open Layout Preview: editable text/color/font, shared live layout controls, fixed-area dimensions and resizing with Undo, actual glyph bounds, wrapping guides, auto fit and zoom/pan. Use an isolated editor-only preview scene and the real BurstWord layout/render path, including optional registered shaping, with no saved preview objects or Player overhead.

- Replace durationScale / TextEmission.DurationScale multipliers with per-emission duration / TextEmission.Duration in seconds on all numeric, text, spatial and batch APIs. Remove global Lifetime from both renderers and Setup; omitted duration defaults to 1.5 seconds. Reject non-finite/non-positive durations before batch commits, preserve normalized GPU animation, and pass seconds explicitly in animation preview and matching benchmarks. Existing callers must convert their old multiplier times manager lifetime to seconds.

- Organize the manager Inspector into Setup, Fonts, Text Layout, Space & Occlusion, UI Scaling, Animations and Runtime Status with session-persistent foldouts and conditional fields. Group animation authoring properties separately from preview settings, simplify help text, and keep pipeline installation visible when configuration is missing.
- Replace separate default/enable/preload animation settings with one indexed Animations list and per-row Edit / Preview. Add animationIndex to all text/numeric spatial emissions and batch requests, validate whole batches, and migrate old scene defaults/presets into the list.
- Remove the manager Inspector's Advanced rendering group. Use automatic rendering defaults and keep backend selection in the pressure-test panel; retain code overrides and existing serialized data for compatibility.
- Make optional shaping a code-registered TextShapingCallback with a font-aware standard request and reusable glyph output list. Null skips shaping; remove the manager's shaping Asset and enable switch. Keep explicit advanced font-session/Job integration under SetTextShaperProvider.
- Honor registered shaping for numeric text as well, restore numeric fast paths when removed, and validate callback caches against the complete context instead of assuming independent words or digit substitution.
- Group text fonts and native TMP Sprite fonts together under Fonts in the manager Inspector; hide Sprite settings when Use Sprite Fonts is disabled.
- Remove the independent Text To Sprite Mappings table, substring replacement and its parsing-cache bypass. Use TMP Sprite Asset Unicode/index/name and native fallback lookup directly.
- Resolve named tags against the TMP Settings default Sprite Asset when no manager Sprite Asset is assigned, and update the ready-made emoji benchmark to use native Sprite references.
- Use TMP's own sprite-name query across sprite fallback chains, avoiding assumptions about name hashes that changed in newer integrated TMP versions.

## 0.4.0

- Simplify effects to the chosen TMP Font Asset's own material. Remove independent manager material presets/overrides, public material emission parameters and `<material>` tags. Existing calls using those parameters must select a font asset owning the desired material instead.
- Add `fontIndex` to numeric/text, Transform/pose and batch emissions: 0 selects Default Font, 1..N selects the numbered font list. A direct font asset takes priority. Validate batch indices before any emission is committed.
- Replace Additional Fonts with font choices (serialized assets migrate automatically). The list selects fonts and resolves `<font>` names; missing glyphs follow native TMP font and global fallback chains.
- Supply ready-made default/effect font assets in the benchmark, selecting effects by index and sharing donor atlases. Preserve default numeric Burst preparation and account for owned material effects in numeric glyph padding.

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
