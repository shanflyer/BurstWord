# BurstWord

GPU-animated damage text for Unity 2022.3 and URP 14, rendered with BatchRendererGroup using TextMeshPro font resources. Each message is data; no GameObject, TMP component or mesh is created per message. Glyphs share one generated quad.

## Install

In **Window → Package Manager → + → Add package from git URL**, enter:

```text
https://github.com/shanflyer/BurstWord.git
```

Use a URP project. Unity installs the declared TMP, Burst and URP dependencies. Import **Window → TextMeshPro → Import TMP Essential Resources** if your project does not have them yet. BurstWord configures the active URP renderer and preserves its BRG shader automatically; **Tools → BurstWord → Install BRG Rendering** reapplies that configuration when you change renderer assets.

## Use

Add `BrgDamageTextRenderer` to one manager object and assign a camera and TMP font asset. Emit from your combat code:

```csharp
using BurstWord.BRG;
using UnityEngine;

// renderer is a reference to your BrgDamageTextRenderer manager.
renderer.Emit(hitPosition, 1234, Color.white);
renderer.EmitText(hitPosition, "<b>Critical 1234</b>", Color.yellow);
```

The manager provides whole-message sorting, three occlusion modes, fixed-size camera-facing text, transform following, world-space perspective and sampled GPU animation. It supports TMP font fallbacks, material effects, sprites, rich text, Unicode bidirectional text, shaping and wrapping. Assign fonts that contain the required glyphs. When adding your own fonts, run **Tools → BurstWord → Prepare Font Sources**; the generated shaping data belongs to your project under `Assets/BurstWord/Resources`, not to the installed package.

The supplied HarfBuzz native binary supports **Windows x64 Editor and Player**. Other platforms require a compatible native library exporting the same HarfBuzz ABI; they are not covered by this release. This release uses BRG and URP; an instancing fallback is not implemented.

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
