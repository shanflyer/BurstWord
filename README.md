# BurstWord

GPU-animated damage text for Unity 2022.3 and newer, including Unity 6, with URP, rendered with BatchRendererGroup using TextMeshPro font resources, with an automatic GPU instancing fallback. Each message is data; no GameObject, TMP component or mesh is created per message. Glyphs share one generated quad.

## Install

In **Window → Package Manager → + → Add package from git URL**, enter:

```text
https://github.com/shanflyer/BurstWord.git
```

Use a URP project. Unity resolves the URP, Burst and uGUI dependencies for your editor version. On Unity 2022.3 / 2023.1, BurstWord installs the separate TMP package automatically if it is missing. Newer Unity uses TMP from uGUI, without installing a conflicting legacy TMP package. Import **Window → TextMeshPro → Import TMP Essential Resources** if your project does not have them yet. BurstWord configures the active URP renderer and preserves both rendering shaders and their instancing variants automatically; **Tools → BurstWord → Install BRG Rendering** reapplies that configuration when you change renderer assets.

## Use

No sample import is required. Run **Tools → BurstWord → Install BRG Rendering**, then check the Renderer Data actually used by your camera has an enabled **BurstWord ordered text** Renderer Feature. Check Graphics/Quality URP overrides and the camera's Renderer selection when using multiple renderers. Add `BrgDamageTextRenderer` to one manager object and assign a camera and TMP font asset. The Inspector checks this feature in Setup and shows installation and Renderer Data selection buttons when configuration is missing. Emit from your combat code:

```csharp
using BurstWord.BRG;
using UnityEngine;

// renderer is a reference to your BrgDamageTextRenderer manager.
renderer.Emit(hitPosition, 1234, Color.white);
renderer.EmitText(hitPosition, "<b>Critical 1234</b>", Color.yellow);
// Direct font assets use their own material; registration is optional.
renderer.Emit(hitPosition, 1234, Color.yellow,
    font: criticalFont, animation: criticalAnimation, fontSize: 40);
// 0 = Default Font; 1..N = the manager's font list, in Inspector order.
renderer.Emit(hitPosition, 1234, Color.yellow, fontIndex: 1);
renderer.EmitText(hitPosition, "Healing +200", Color.green, fontIndex: 2);
```

The manager provides whole-message sorting, three occlusion modes, fixed-size camera-facing text, transform following, world-space perspective and sampled GPU animation. The default typography uses TMP font/glyph resources, font fallbacks, pair adjustments, material effects, sprites, supported rich-text tags, Unicode bidirectional ordering and wrapping. It creates no TMP text component. Assign fonts containing your required glyphs. OpenType complex-script shaping is optional: the core package contains no external native font library and does not select a third-party provider for you.

Start with the [direct-integration tutorial](Documentation~/QUICKSTART.md). The Inspector groups optional settings and hides child settings when their feature is disabled; collapsing a group only hides its UI. `font` / `fontIndex`, `fontSize` and `animation` / `animationIndex` choices are also available on both `EmitText` spatial overloads and each `TextEmission` in `EmitBatch`; the manager defaults and existing labels stay unchanged. A direct `font:` takes priority over `fontIndex`. Animations use one indexed Inspector list: index 0 is the first/default item, and an empty list/slot uses linear motion. A direct `animation:` takes priority over `animationIndex`. Each animation row opens Edit / Preview. Fonts use their own TMP material for outline, underlay and glow; there is no separate material override list or parameter. The font list is for selection and `<font>` name lookup; glyph fallback follows TMP font-asset/global fallback lists.

The **Fonts** group also holds native TMP Sprite Assets for image fonts and emoji. Enable **Use Sprite Fonts**, assign a **Default Sprite Font** (empty uses TMP Settings), and use the resource's Unicode entries or `<sprite>` index/name tags. Its native sprite fallback chain is used automatically; optional additional sprite fonts are available for named asset tags. There is no separate text-to-sprite mapping table.

Screen-space text supports the same scaling modes and formulas as Unity's **Canvas Scaler**: Constant Pixel Size, Scale With Screen Size (Match Width Or Height / Expand / Shrink), and Constant Physical Size. Configure **UI Scaling** on the manager, or assign an existing screen-space Canvas to use its root Canvas's actual scale factor. The default remains 1920×1080 with Match 0.5. Scaling changes apply to live screen text without rebuilding labels; world-space text retains world units and perspective.

**Text alignment** supplies left/center/right and top/middle/bottom controls. Align against the emission point, or enable **Use Fixed Text Area** to align inside a numeric width/height area centered on that point. Both modes create no UI objects. Each emission and batch request can override `alignment: TextAnchor.UpperLeft` and `textAreaSize: new Vector2(300, 100)`; alignment is captured at emission and shared with cached and Job-prepared layouts.

Rendering automatically uses BRG on compatible D3D11/D3D12, Vulkan and Metal devices, and falls back to ordinary instanced draws when that BRG path is unavailable. WebGL 2 and OpenGL/OpenGL ES use the fallback. Backend selection, preparation jobs, tight glyph bounds and shader selection need no manager Inspector configuration. Both backends keep the same typography, whole-message ordering, space modes and GPU animation; the fallback never creates text objects. The benchmark panel shows the active backend and lets you switch to **Instancing** for comparison.

## Optional shaping

Shaping is an optional code callback: `renderer.SetTextShaper(MyShapeMethod)`. No callback means standard TMP glyph-data layout; `renderer.SetTextShaper(null)` removes it. Your method receives the resolved TMP font and a UTF-32 text run and appends glyph IDs, clusters and positions to the supplied list. Use your own implementation or any external plugin; BurstWord does not select one or require a shaping Asset. See the [callback contract](Documentation~/SHAPING.md).

To use the supplied optional HarfBuzz adapter, install the core first, then add this second Git URL:

```text
https://github.com/shanflyer/BurstWord.git?path=/Adapters~/HarfBuzz#v0.3.0
```

The optional HarfBuzz adapter is explicitly selected in code through the advanced `SetTextShaperProvider` API. Installation alone does not activate it. Its font-data preparation and native files belong to that separate package. Ordinary callback users do not need that package, a provider Asset or a factory/session implementation.

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
