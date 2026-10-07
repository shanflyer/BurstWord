# Custom shader effects

BurstWord uses its own shaders to render TMP resources. TMP material properties supply
the supported face, outline, underlay and glow settings. A custom TMP shader is not
executed automatically. Use **Shader Effects** for your own GPU effects.

## Create and select an effect

1. Open your `BrgDamageTextRenderer` Inspector → **Shader Effects**.
2. Click **Create Custom Effect Shader** and save it inside your project's `Assets`.
   The generated shader is added to the list. You can also use
   **Assets → Create → BurstWord → Shader Effect** and assign it yourself.
3. Click **Edit** beside the shader and edit the two effect functions. An untouched
   template renders like built-in shading, including the selected TMP font's effects.

The list contains ordinary `Shader` assets. There is no extra effect Asset or material
list to maintain. Index **0** selects the first/default entry. An empty list or empty
slot uses built-in shading. **-1** explicitly selects built-in shading even when the
first slot contains a custom shader. Other invalid indices throw an exception.
Configure the list before Play. Reordering changes the indices used by subsequent calls.

```csharp
renderer.EmitText(hitPosition, "Critical 1234", Color.white,
    duration: 1.2f,
    fontIndex: 1,
    animationIndex: 2,
    effectIndex: 0,
    effectParameters: new Vector4(1, 0.5f, 0, 3));

renderer.Emit(hitPosition, 200, Color.green, effectIndex: -1);
```

The same `effectIndex` and `effectParameters` arguments are available on the Transform
and `TextPose` overloads. Batch requests expose `TextEmission.EffectIndex` and
`TextEmission.EffectParameters`, or constructor arguments of the same names in camel
case. Each request independently selects its font, animation and shader.
Batch shader/index/parameter validation completes before any request is emitted.

`effectParameters` supplies **four finite numbers per label**, defaulting to zero.
You define their meanings. They are shared by every glyph in that message and remain
independent when messages use the same shader. Shader `Properties` alone do not make
arbitrary parameters transfer from a TMP material; use this parameter channel.

For a handle returned by a spatial overload, update parameters without regenerating
the text:

```csharp
var handle = renderer.EmitText(target, "1234", Color.white,
    effectIndex: 0, effectParameters: new Vector4(0, 1, 0, 0));

renderer.TryUpdateEffectParameters(handle, new Vector4(1, 0, 0, 0));
```

Updating returns false for an expired handle or a label using the built-in shader.
NaN/infinite parameters are rejected. Existing labels retain their selected shader.

## Ordinary custom effects

The template includes:

```hlsl
#include "Packages/com.shanflyer.burstword/Runtime/BRG/BurstWordShaderEffects.hlsl"
```

The package HLSL owns instance decoding, the generated quad, glyph placement, screen
scaling/world transforms, GPU animation, TMP/Sprite shading and opaque-depth rejection.
Modify the template's two functions:

```hlsl
void BurstWordModifyVertex(inout BurstWordEffectVertex vertex)
{
    // Apply an offset to the completed clip-space position.
    vertex.positionCS.x += vertex.parameters.x * 2 / _BurstScreen.x
        * vertex.positionCS.w;
}

void BurstWordModifyFragment(inout half4 color, BurstWordEffectFragment fragment)
{
    // Change the finished text color while preserving its glyph alpha/fade.
    color.rgb *= lerp(half3(1, 0.3, 0.1), half3(1, 1, 0.4), fragment.glyphUV.y);
}
```

Vertex inputs contain the completed `positionCS`, `atlasUV`, local `glyphUV`, `color`
(glyph tint), `parameters`, `normalizedAge` and `elapsedSeconds`. The writable position,
UVs and tint are forwarded to the pixel stage. Fragment inputs contain the interpolated
position/UVs and the same parameters and lifetime information. `glyphUV` is 0..1 within
one glyph, not across the whole message. `normalizedAge` is 0..1 across that message's
duration; `elapsedSeconds` is time since emission. Fragment color uses **straight alpha**.

An effect that moves vertices does not change CPU layout bounds, wrap widths or the
whole-label sorting center. Keep motion that must participate in ordering in the
animation/pose system. Pixel effects cannot paint outside the glyph quad. Enlarging
the quad and managing additional padding is an advanced shader/layout responsibility.

The template compiles BRG and ordinary instancing variants of the same shader:

```hlsl
#pragma target 3.5
#pragma target 4.5 DOTS_INSTANCING_ON
#pragma instancing_options forcemaxcount:64
#pragma multi_compile_instancing
#pragma multi_compile _ DOTS_INSTANCING_ON
#pragma vertex BurstWordEffectVert
#pragma fragment BurstWordEffectFrag
```

Keep these pragmas and the ShaderLab properties/tags/passes. The blank template is
ready for both backends; no second effect implementation is needed. Final vertex/pixel
hooks are isolated from the package HLSL so package updates do not overwrite your file.

**Text Layout → Open Layout Preview** lets you select the shader and supply its four
parameters. This static editor view renders through Instancing and reports a clear error
for BRG-only shaders. Its layout guides show CPU glyph bounds before custom vertex
displacement. The game manager's selected backend is unchanged.

## Advanced: fully custom shaders

You may implement the complete shader without including the shared HLSL. The public
shader contract is version **1**. Use one URP SubShader with:

```shaderlab
Tags {
    "RenderPipeline"="UniversalPipeline"
    "BurstWordContract"="1"
    "BurstWordBRG"="True"
    "BurstWordInstancing"="True"
}
```

Set a backend tag to `False` when you do not implement that backend. Selection never
silently replaces an unsupported effect with a different shader. The Inspector reports
declared backend support, missing bindings, malformed passes and compilation errors
known to Unity. At emission, an invalid contract or an unsupported active backend/device
throws a descriptive exception. A BRG-only shader can run on BRG but will be rejected
on a device that selects Instancing; provide a dual-backend shader for portability.

Pass **0** is named `Scene`, with `LightMode=SRPDefaultUnlit`. Pass **1** is named
`OrderedOverlay`, with `LightMode=BurstWordOverlay`. Use transparent blending, `Cull Off`,
`ZWrite Off`, `ZTest [_BurstZTest]` and respect `_BurstSortingMode`: 0 overlays, 1 rejects
pixels behind the opaque depth texture, 2 participates in scene transparency/depth testing.
The renderer controls pass enablement and render queue.

The complete required ShaderLab property list is public in `BrgShaderContract.cs`.
Runtime binding names, instance metadata and decoding are defined in `BrgTMPGlyph.hlsl`:

Keep the `UnityPerMaterial` layout identical across BRG and Instancing variants. Instance
fields are HLSL bindings; do not add the glyph/pose/animation/effect instance fields as
ShaderLab material `Properties`, which can make Unity reject the combined shader for
BRG. The template already follows these rules. The editor compatibility check follows
[Unity's Shader Inspector](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Editor/Mono/Inspector/ShaderInspector.cs).

- BRG supplies six `float4` DOTS instance fields: `_WorldBirth`, `_GlyphRect`, `_AtlasRect`,
  `_LifeMotion`, `_Tint`, `_GlyphStyle`. `_WorldBirth.x` identifies the page's font resource;
  `.w` stores birth time. `_GlyphStyle.w - 1` identifies the label.
- `_BurstResources` is a `StructuredBuffer<float4>` with ten entries per font/atlas resource.
  It holds TMP colors, effect metrics, atlas index/mode and texel size. `_BurstAtlas0..15`
  bind the original atlas textures. Resource decoding must match the supplied HLSL.
- `_BurstLabels` is a raw buffer with a 64-byte header followed by capacity-sized `float4`
  blocks for world anchor, world right and world up. Use `_BurstLabelCapacity` for block offsets.
- `_BurstAnimationLabels` and `_BurstAnimationCurves` supply animation indices/amplitudes
  and shared sampled curves. `_BurstEffectLabels` supplies per-label `float4` parameters.
- Instancing supplies the six glyph fields plus `_BurstPoseAnchor`, `_BurstPoseRight`,
  `_BurstPoseUp`, `_BurstAnimationLabel` and `_BurstEffectParameters` as instanced fields.
  `_MainTex` and `_BurstResource0..9` replace BRG's atlas/resource lookup for each draw.
  Support 64 instances per draw and fit the uniform limits of your intended devices.
- `_BurstTime`, `_BurstScreen` and `_BurstAnimationInfo` supply time, viewport/scale and
  animation table dimensions. Preserve the semantics of world/screen transforms, tint,
  lifetime and the SDF/bitmap/RGBA modes if your shader claims those features.

Tags declare support; they cannot prove arbitrary HLSL behaves correctly. Advanced
authors must verify both instancing variants, ordering, TMP/Sprite resources and target
graphics APIs. The standard template handles that data contract for ordinary effects.

## Cost and verification

Effect selection happens after layout, so different shaders share parsing, shaping,
numeric, wrapping and Job preparation caches. Shared materials/pages are created per
resource/shader group, never per message. BRG parameters add 16 bytes per label and
are uploaded when changed; built-in-only use does not allocate that parameter buffer.
Instancing adds one `float4` instance field only for custom effects. Shader changes
split draws while preserving whole-label order; messages are never regrouped by effect
at the expense of transparency order. More interleaved shaders can mean more draw calls.

The API and generated template are intended for Unity 2022.3 and newer with URP.
Actual verification uses Unity 2022.3.62f3 and 6000.6.0f1 Windows D3D11 editor/player,
both BRG and Instancing, template parity, real vertex/pixel output, parameter changes,
mixed numeric/wrapped Job batches, single-page Job draws, three ordering modes and
opaque-object occlusion. Windows OpenGL Core players also exercise the Instancing path.
Other graphics APIs need
testing on their target devices; backend declaration is not a platform test result.
