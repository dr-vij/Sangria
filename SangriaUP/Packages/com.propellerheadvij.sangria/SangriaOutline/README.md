**Overview**
`SangriaOutline` is a URP Render Graph outline effect. It renders a mask of selected layers, expands it into an outline, blurs it, and composites the result back onto the camera color target.

**Dependencies**
- Universal Render Pipeline with Render Graph (`UnityEngine.Rendering.Universal`, `UnityEngine.Rendering.RenderGraphModule`).
- Unity Mathematics (`Unity.Mathematics`) for Gaussian sampling.
- Shaders in `Shaders/MaskShader.shader` and `Shaders/OutlineShader.shader`.

**Folder Layout**
- `SangriaOutline.asmdef` — module assembly definition.
- `Scripts/` — render feature and helpers.
- `Shaders/` — mask and outline shaders.
- `OutlineExample.unity` — sample scene.

**Quick Start**
1. Ensure the scripting define `OUTLINE_URP` is enabled for your build target.
2. Add `OutlineRenderGraphFeature` to your URP Renderer Data asset.
3. Assign `Shaders/MaskShader.shader` to `Mask Shader` and `Shaders/OutlineShader.shader` to `Outline Shader`.
4. Set `Layer Mask` to the objects that should receive outlines.
5. Adjust `Outline Width`, `Blur Width`, and `Color`.

**Render Flow**
1. `RenderMask` builds a renderer list for `RenderQueueRange.opaque` using the layer mask and draws it with the mask material.
2. `RenderOutline` runs outline pass 0 to expand the mask into `TempTextureA`.
3. `RenderHorizontalBlur` runs blur pass 1 into `TempTextureB`.
4. `RenderVerticalBlurAndMerge` runs blur pass 2, samples the original color, applies the outline color, and writes back into `TempTextureA`.
5. `RenderCopyBack` blits `TempTextureA` to the active color target.

**Settings**
- `LayerMask` — objects to include in the mask; filtering uses `RenderQueueRange.opaque`.
- `OutlineWidth` — width of the outline (1..32).
- `BlurWidth` — width of the Gaussian blur (1..32).
- `Color` — outline color; alpha controls blend strength.
- `GetGaussSamples()` — returns cached Gaussian samples per blur width.

**Key Classes**
- `OutlineRenderGraphFeature` — `ScriptableRendererFeature` that creates the render pass and enqueues it `AfterRenderingPostProcessing`.
- `OutlineSettingsData` — serialized settings, material creation/disposal, Gaussian samples.
- `OutlineHelpers` — Gaussian sampling and caching (max width 32).

**Shaders**
- `MaskShader` — renders selected objects as solid white into the mask.
- `OutlineShader` — three passes:
- `Outline` (pass 0) expands the mask.
- `Horizontal blur` (pass 1) applies Gaussian blur horizontally.
- `Vertical blur` (pass 2) applies Gaussian blur vertically and composites with the original color.

**Notes and Quirks**
- Only opaque renderers are outlined due to `RenderQueueRange.opaque`.
- If shaders are not assigned, `OutlineSettingsData` will not initialize materials and rendering will fail.
- The Gaussian sample buffer is fixed at 32; if you raise max widths, update both `OutlineHelpers.MaxWidth` and `_GaussSamples[32]` in `OutlineShader`.

**Where to Extend**
- Add transparent support by changing `RenderQueueRange` and shader tags.
- Customize outline style by editing the `OutlineShader` passes.
- Add per-object colors by encoding IDs in the mask pass and sampling a color lookup.
