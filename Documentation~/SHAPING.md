# Optional text shaping

BurstWord reads TMP font assets directly. Its default path supports glyph lookup/fallbacks, TMP pair-adjustment data, supported rich text, sprites, bidi ordering and Unicode wrapping without a native shaping library. It does not promise TMP's entire layout/tag API. Arabic joining, Indic rearrangement, OpenType ligatures and contextual mark positioning require a shaping provider. Bidi ordering and font coverage are separate capabilities.

## Selecting a provider

Assign a `TextShaperAsset` to the renderer's **Text Shaper** field, with **Enable Shaping** enabled. Leaving the field empty uses TMP glyph data. There is no automatic global registration or provider selection.

Code may supply a factory directly:

```csharp
using BurstWord.Typography;
// provider implements ITextShaper; renderer is BrgDamageTextRenderer.
renderer.SetTextShaper(provider);
```

Call on the main thread. Switching clears active text, all typography/layout/preparation caches, worker buffers and font sessions before reinitializing. `SetTextShaper(null)` removes the runtime override and uses the Inspector asset, if assigned. To use the default path, clear that asset too. Set configuration before initialization; for runtime changes to font/provider configuration, disable/reconfigure/re-enable the renderer, or use `SetTextShaper` for a factory change. Changing private provider configuration needs the same restart.

The benchmark displays the active typography name. If a selected adapter's native library/entry point is missing, BurstWord warns once and returns to TMP glyph data. This preserves rendering, not complex-script correctness. Missing per-font source/session support is counted as unavailable shaping.

## Adapter contract

The public interfaces are in `Runtime/Typography/TextShaping.cs` (`BurstWord.Typography`):

- `ITextShaper`: factory name, whether original OpenType bytes are required, script discovery and font-session creation.
- `TextShaperAsset`: optional ScriptableObject factory for Inspector selection.
- `ITextShapingFont`: nominal glyph lookup, shaping into a caller-owned reusable list, and disposal.
- `IJobTextShapingFont`: optional immutable worker handle plus Burst-compatible function pointers.

A normal C# plugin needs only the first interfaces. It can use its own font source or set `RequiresFontData` to true to request bytes from `BrgFontSources`. `CreateFont` receives the actual resolved TMP font, so returned glyph IDs must belong to that font's original face. A plugin that only transforms a string (for example into Arabic presentation-form characters) needs an adapter translating its result back into glyphs and original clusters; attaching that plugin does not automatically satisfy this contract.

`TextShapingRequest` contains a reusable UTF-32 array. All context/run indexes refer to that array. Preserve the supplied surrounding context when shaping a sub-run; do not retain or modify the input. The renderer has already divided fonts/styles, resolved bidi levels and selected direction. `Script` uses ISO 15924 four-character tags packed into a uint, for example Latin `0x4c61746e`, Common `0x5a797979` and Inherited `0x5a696e68`.

Append `TextShapingGlyph` results in the run's visual order. `Cluster` is an **absolute UTF-32 input index**, not a UTF-16 index or output glyph index. Multiple glyphs may belong to one cluster. Advances and offsets are in the supplied font's `faceInfo.pointSize` units, Y up; the core applies text size and font scale once. Return finite metrics and clusters inside the run. `UnsafeToBreak` marks boundaries that cannot reuse an independently shaped cached line. Preserve clusters/substitution semantics across wrapping. The core owns wrapping, decoration, generated atlas glyphs and rendering; the adapter supplies typography data only.

A minimal factory/session outline:

```csharp
public sealed class MyShaper : TextShaperAsset
{
    public override string Name => "My plugin";
    public override bool RequiresFontData => false;
    public override uint GetScript(uint point) => MyPluginScript(point);
    public override ITextShapingFont CreateFont(TMP_FontAsset font, byte[] data)
        => new MyFontSession(font);
    // MyFontSession implements nominal lookup, Shape and Dispose using your plugin.
}
```

The plugin owns its font session until `Dispose`. It must not retain Unity text components or create an object per label. FontEngine glyph-atlas updates remain on the main thread. Keep sessions stable/immutable while the renderer caches their output.

## Optional Job acceleration

Ordinary managed providers shape on the main thread and still use layout caches, numerical preparation and instance-write jobs. For repeated contextual text preparation in jobs, implement `IJobTextShapingFont`. The supplied HarfBuzz adapter does so. Default TMP wrapping also compiles glyph metrics and pair adjustments into immutable job data, without accessing TMP objects from workers.

`TextShapingFunctions` holds `FunctionPointer<ShapeTextRun>` and `FunctionPointer<ReleaseTextWorker>`. Both must be valid. The job receives an immutable font handle, a per-thread worker pointer passed by reference, UTF-32 text/context, a sub-run and feature flags (RTL=1, kerning=2, ligatures=4). Job clusters are relative to this input pointer; unlike managed requests there is no separate context offset. Return an unmanaged glyph buffer owned by the worker, valid until its next shaping call. Reuse it instead of allocating per call. Release must free the worker/buffer. All sessions from one factory must use the same function pair; worker state must safely handle changing fonts. Compile callbacks with Burst and preserve them for IL2CPP/AOT. The core completes jobs before invoking worker cleanup and then disposing font sessions.

Metric/cluster checks are performed on each dynamic request before a cached prepared layout is used. Incompatible values use the full layout path in that frame. This is not asynchronous Unity API access or delayed text display.

## HarfBuzz option

The separate `Adapters~/HarfBuzz` package includes the existing HarfBuzz 8.3.1 integration and a ready-made shaper asset. No native code is imported by the core package. Install/selection and platform details are in the [adapter README](../Adapters~/HarfBuzz/README.md). Original source data is prepared automatically before Play/build; the optional manual menu is **Tools → BurstWord → HarfBuzz → Prepare Font Sources**. Source assets/catalogs belong to the consumer project under `Assets/BurstWord/Resources`, not the installed package.
