# BurstWord

GPU-animated damage text for Unity 2022.3 and newer, including Unity 6, with URP, rendered with BatchRendererGroup using TextMeshPro font resources, with an automatic GPU instancing fallback. Each message is data; no GameObject, TMP component or mesh is created per message. Glyphs share one generated quad.

## Install

In **Window → Package Manager → + → Add package from git URL**, enter:

```text
https://github.com/shanflyer/BurstWord.git
```

Use a URP project. Unity resolves the URP, Burst and uGUI dependencies for your editor version. On Unity 2022.3 / 2023.1, BurstWord installs the separate TMP package automatically if it is missing. Newer Unity uses TMP from uGUI, without installing a conflicting legacy TMP package. Import **Window → TextMeshPro → Import TMP Essential Resources** if your project does not have them yet. BurstWord configures the active URP renderer and preserves both rendering shaders and their instancing variants automatically; **Tools → BurstWord → Install BRG Rendering** reapplies that configuration when you change renderer assets.

## Use

Add `BrgDamageTextRenderer` to one manager object and assign a camera and TMP font asset. Emit from your combat code:

```csharp
using BurstWord.BRG;
using UnityEngine;

// renderer is a reference to your BrgDamageTextRenderer manager.
renderer.Emit(hitPosition, 1234, Color.white);
renderer.EmitText(hitPosition, "<b>Critical 1234</b>", Color.yellow);
```

The manager provides whole-message sorting, three occlusion modes, fixed-size camera-facing text, transform following, world-space perspective and sampled GPU animation. The default typography uses TMP font/glyph resources, font fallbacks, pair adjustments, material effects, sprites, supported rich-text tags, Unicode bidirectional ordering and wrapping. It creates no TMP text component. Assign fonts containing your required glyphs. OpenType complex-script shaping is optional: the core package contains no external native font library and does not select a third-party provider for you.

`Render Backend` defaults to **Auto**: it uses BRG on compatible D3D11/D3D12, Vulkan and Metal devices, and falls back to ordinary instanced draws when that BRG path is unavailable. WebGL 2 and OpenGL/OpenGL ES use the fallback. Choose **Instancing** to test it directly. Both backends keep the same typography, whole-message ordering, space modes and GPU animation; the fallback never creates text objects. The benchmark panel shows the active backend and lets you switch it.

## Optional shaping

Leave **Typography → Text Shaper** empty for standard TMP glyph-data layout. Complex scripts that need joining, contextual substitutions or mark positioning require a shaping provider; a font containing those glyphs alone does not perform shaping.

To use the supplied optional HarfBuzz adapter, install the core first, then add this second Git URL:

```text
https://github.com/shanflyer/BurstWord.git?path=/Adapters~/HarfBuzz#v0.3.0
```

Assign the ready-made `HarfBuzz` asset from that package to **Text Shaper** on the manager. Installation alone does not activate it. The adapter prepares original font data before Play/build and supplies target-filtered native libraries or source integration. The core package needs none of those native files. Other plugins can implement the public `ITextShaper` interface, with optional Job acceleration; see [shaping adapters](Documentation~/SHAPING.md).

Use URP's normal Render Graph mode on Unity 6. Unity 2022.3 uses the classic render-pass implementation. See [version/platform support and verification limits](Documentation~/PLATFORMS.md).

## Examples and benchmarks

Select BurstWord in Package Manager and import **Benchmarks and Animation Examples** from its Samples section. All scenes and animations are supplied as assets; no generation menu is needed.

- `TextFeatures.unity`: BRG stress scene with eight workloads, sorting and space controls, GPU animation and an English panel.
- `TextBaseline.unity`: traditional pooled TMP UGUI comparison scene with an English panel.
- `Animations`: Float, Critical, Heal and Wobble presets with editable `.anim` clips. Double-click a preset or open **Tools → BurstWord → Animation Editor** to edit and preview it.

Both scenes default to **200 messages/second** and a **200-message burst**. The panel controls emission rate and displays FPS, frame time, memory and GC data. For a comparable numerical benchmark, select **Numbers** in the BRG scene and use matching lifetimes, capacities and display settings. The TMP scene prewarms its configured pool; reduce its capacity in the Inspector before Play when testing on limited hardware.

Benchmark scripts and the large multilingual font assets are contained in `Samples~`. They are imported only when you choose the sample. The package repository contains no Unity project, Library cache, build output or diagnostic logs.

Further documentation: [rendering](Documentation~/BRG.md), [typography](Documentation~/TEXT_FEATURES.md), [space and sorting](Documentation~/SPATIAL_RENDERING.md), [GPU animation authoring](Documentation~/GPUAnimation.md).

## License

BurstWord code is licensed under [MIT](LICENSE.md). Included third-party code, native libraries and sample fonts retain their own licenses; see [Third Party Notices](Third%20Party%20Notices.md).
