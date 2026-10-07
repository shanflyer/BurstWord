# Optional text shaping callback

BurstWord exposes an optional code callback. It does not choose a language plugin, require a shaping Asset, or load a shaping library for you. Without a callback, layout uses TMP font glyph data, native fallbacks, pair adjustments, supported rich text, sprites, bidi ordering and Unicode wrapping. A font's glyph coverage and contextual shaping are separate capabilities.

## Register or remove

```csharp
using System.Collections.Generic;
using BurstWord.Typography;

// Your method has this signature:
// void ShapeText(in TextShapingRequest input, List<TextShapingGlyph> output)
renderer.SetTextShaper(ShapeText);

// Remove the callback and skip shaping:
renderer.SetTextShaper(null);
```

Implement `ShapeText` yourself or translate your chosen plugin's result into the output format below. No factory, session class or ScriptableObject is needed. This is a glyph-layout contract; a string-only replacement plugin needs a small bridge to glyph IDs and original clusters.

Register on the main thread, before emitting text. Replacing/removing a callback completes pending jobs, releases font sessions and generated resources, and clears live labels and layout caches. Re-register after changing settings captured by the callback so stale results cannot survive. A disabled renderer can also be configured before enabling it. BurstWord does not own/dispose resources captured by an application delegate.

## Input

`TextShapingRequest` is a read-only struct:

- `Font`: the actual `TMP_FontAsset`, after font selection, alternate typeface and fallback resolution. Output glyph IDs must belong to this font's original face.
- `CodePoints`: a reusable UTF-32 array. Only the supplied context range is valid; do not modify or retain it.
- `ContextStart`, `ContextLength`: surrounding text available for contextual shaping.
- `RunStart`, `RunLength`: the part of that context to shape. All indexes are absolute UTF-32 indexes into `CodePoints`, including after explicit newlines; they are not UTF-16 string offsets.
- `RightToLeft`: resolved direction of this run. Fonts, styles, sprites/tabs and bidi levels are already separated by the renderer.
- `Kerning`, `Ligatures`: requested feature flags. A callback owns how its implementation handles those features.
- `Script`: ISO 15924 four-character tag packed into a uint. The simple callback path supplies Common (`0x5a797979`); detect/segment scripts in your own implementation if needed. Advanced providers may supply precise script tags.

Inputs are text-layout data. Per-label position, lifetime and animation remain in BurstWord's rendering path and do not belong in shaping. Keep the callback's result deterministic for the request and its registered settings so caching remains valid.

## Output

Append `TextShapingGlyph` values to the supplied reusable `List<TextShapingGlyph>`. BurstWord clears it before each call. Do not retain it, replace it, or allocate an intermediate list on every call when your plugin supports writing directly.

- `GlyphId`: a glyph index in `input.Font`'s original font face, not a Unicode code point or character-table index.
- `Cluster`: absolute UTF-32 input index within `[RunStart, RunStart + RunLength)`. Several glyphs can share one cluster; substitutions/ligatures must preserve their source cluster.
- `Advance`, `OffsetX`, `OffsetY`: finite metrics in `Font.faceInfo.pointSize` units, with Y pointing up. The renderer applies font size and font scale once.
- `Flags`: set `TextShapingGlyph.UnsafeToBreak` where splitting the shaped text would invalidate its contextual result. The renderer uses this when reusing measured glyphs across wrapping. If your bridge cannot certify a boundary, mark it unsafe.

Return glyphs in visual order within the run. Include every glyph needed to display that run; an empty result produces no glyphs. The core validates clusters and metrics. It retains responsibility for line layout, wrapping, alignment, decorations, glyph atlas preparation, sorting and BRG/instancing rendering. Missing shaped glyphs can be added through Unity FontEngine when a compatible original font source is available; a static atlas alone cannot supply glyphs it does not contain.

## Execution and performance

A registered callback is used for text needing shaping, including plain numbers. No callback restores the original numeric fast paths. Sprite glyphs and tabs are handled directly by the core.

The managed callback executes synchronously on the main thread. It is not invoked inside Burst jobs. Exact repeated requests can reuse cached output; registration does not mean one callback invocation per emission. The callback cache checks the full context, font, run range, direction and features. It does not assume that words are independent or that changing a damage digit preserves the returned glyphs. Layout/resource caches and instance-write jobs remain available. Changing callback behavior without re-registering violates the cache contract.

## Advanced native Job integration

`SetTextShaperProvider(ITextShaper provider)` is a separate, explicit integration point for adapters that need owned font sessions or native Job acceleration. It is not required to use a delegate. There is no automatic provider selection or Inspector provider field. `SetTextShaper(null)` also removes an advanced provider.

The existing interfaces in `Runtime/Typography/TextShaping.cs` remain available:

- `ITextShaper`: factory name, optional font-data requirement, script detection and font-session creation.
- `ITextShapingFont`: nominal glyph lookup, shaping and disposal.
- `IJobTextShapingFont`: optional immutable worker handle and Burst-compatible function pointers.
- `TextShaperAsset`: an optional factory base for external adapters; the core manager does not require or expose such an Asset.

An advanced provider that requests original font bytes can use `BrgFontSources`. Ordinary delegates do not scan/load those catalogs. Provider sessions remain stable while their output is cached and are disposed after jobs complete.

`TextShapingFunctions` contains `FunctionPointer<ShapeTextRun>` and `FunctionPointer<ReleaseTextWorker>`. Both must be valid. The job receives an immutable font handle, a per-thread worker pointer passed by reference, UTF-32 text/context, a sub-run and feature flags (RTL=1, kerning=2, ligatures=4). Job clusters are relative to its input pointer. Return an unmanaged glyph buffer owned by the worker, valid until its next call; reuse it and free it in Release. Sessions from one factory must use the same function pair, and worker state must safely handle changing fonts. Preserve callbacks for IL2CPP/AOT. The core completes jobs before worker/session cleanup.

## Optional HarfBuzz adapter

The separate `Adapters~/HarfBuzz` package is one optional implementation. Installing it does not activate it. Explicitly call `renderer.SetTextShaperProvider(harfBuzz)` with that adapter's factory if you choose its native Job integration. Font-data preparation and target-specific libraries belong to the adapter package. Details are in the [adapter README](../Adapters~/HarfBuzz/README.md). The core does not need it to use a delegate or render ordinary TMP glyphs.
