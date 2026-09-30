# 3D-to-2D Shape Projection MVP

## 当前默认：真正的二维多边形重绘（2026-09-29）

`Projected Shapes` 已启用。下面旧版 v1/v2 说明仅供回退参考；新路径不使用旧版 Toon、烘焙顶点颜色或屏幕后处理轮廓。

流程：逐 Renderer / 子材质采样原始 `_BaseMap` / `_MainTex` 与 `_BaseColor` / `_Color` → 深度可见性投影 → 按源材质和颜色分区 → 合并小区域 → 提取共享边界 → Douglas–Peucker 简化 → 带孔多边形梯形剖分 → 全屏二维网格重绘。最终色块使用区域内原始颜色的均值，不使用统一白色覆盖。白色源材质仍然会输出白色。

开关：`Tools > Cartoon Projection > Projection 2D > Enable / Disable Effect`。
参数位置：`Settings/CartoonUniversalRenderer.asset` 中的 `Cartoon Projection Renderer`。

- `Projection Width`：当前 640；降低会减少小细节和处理量。
- `Projection Color Step`：当前 40；增大减少颜色层次。
- `Projection Minimum Area`：当前 4 个采样像素；增大合并细碎色块。
- `Projection Contour Tolerance`：当前 1.5 个采样像素；建议 0.8–2.0，增大轮廓更概括。过大可能导致局部轮廓交叉，不是无损简化。
- `Projection Show Contours`：显示实际二维多边形的共享边界，用于确认不是普通 Toon。
- 关闭整个 `Enabled` 恢复原始渲染；仅关闭 `Projected Shapes` 则回退旧管线。

验证菜单：`Validate Geometry` 检查色块数量、孔洞、全屏填充和边数减少；`Report Statistics` 输出实际区域数、轮廓边数与 CPU 耗时；`Capture Comparison` 用临时相机生成原始、重绘和轮廓图到 `Library/CartoonProjectionValidation`。若测试人物存在，会自动对它取景；不会移动场景相机或保存场景。

当前边界：

- 实验性 CPU 重建，GPU 异步读回，默认最高 8 次/秒；不是每帧 60 FPS 的实时矢量化。镜头/物体快速移动有延迟，首个结果完成前保留原图。
- 支持不透明 MeshRenderer / SkinnedMeshRenderer 及常见 alpha cutout；不支持 Terrain 的特殊材质采样、程序材质、多层混合或任意自定义 Shader 的最终颜色。当前不读取原材质的 MaterialPropertyBlock 覆盖。
- 原光照、高光与法线贴图刻意不参与颜色输入；当前先验证图像化核心，不是完整 Paint Layer 光影复刻。
- 透明物体和 2D Sprite 保留原管线，在重绘后绘制；深度仍来自原 3D 模型，因此简化轮廓边缘与 Sprite 的遮挡可能略有差异。
- 没有跨帧区域跟踪，移动时细碎区域可能跳变。XR、相机堆叠、独立 Player 构建尚未验证；构建需确保两个 Projected Shader 不被裁剪。
- 只修改测试项目；原项目 Colorido 未修改。

---

Unity 6.3 / URP 17.3 Render Graph prototype inspired by
[`NikuKikai/3Dto2Dshape`](https://github.com/NikuKikai/3Dto2Dshape). The key visual rule is that
the final image keeps the source material surface by default while rebuilding its shape boundaries
from depth-sorted 2D parts.

## Material Color Blocks (v2)

The v2 milestone adds editor-baked color regions so the renderer can output flat,
stable color blocks instead of either raw PBR materials or single part colors.

### Surface modes (CartoonRenderSettings.surfaceMode)

| Mode | Behaviour |
|---|---|
| `OriginalMaterial` | Legacy v1: camera color, optional discrete lighting (`preserveSourceMaterials` stays serialized for old assets). |
| `MaterialColorBlocks` | Target mode: baked per-region vertex colors, two-band faceted lighting, no PBR. |
| `ManualPartColor` | Fallback: flat `CartoonShapePart.fillColor` with the same discrete lighting. |

The legacy `preserveSourceMaterials` checkbox is hidden; `OriginalMaterial` reads it so old
renderer assets keep rendering exactly as before. User-tuned values (render scale 0.44, outline
strengths, thresholds) are preserved.

### Region baking workflow

1. Open **Tools → Cartoon Projection → Material Color Region Baker**.
2. Assign the character root (e.g. an instance of the Lucy Princess FBX) and press **Scan Renderers**.
3. Review the per-material policies (name-based suggestions, always user-overridable):
   `HeroDetail` (eyes, mouth, ribbons — up to 16 regions), `Standard` (hair, skin, body — 8),
   `Aggressive` (patterns, lace, boot noise — 4). Merge strength, region caps and per-material
   color overrides are editable.
4. **Bake Preview** shows region counts, vertex growth and color swatches without writing assets.
5. **Bake Assets** writes under `Assets/CartoonRenderer/Generated/<name>/`:
   `<name>_ColorRegions.asset`, `Meshes/*.asset`, and `<name>_Cartoon.prefab`
   (a prefab variant — source FBX, materials and prefabs are never modified).

Baking details: textures are blitted into a temporary linear RenderTexture (the source
`TextureImporter` is never made readable); triangles are sampled at the barycentre (hero
materials add edge midpoints); adjacency uses position-quantized edge keys so UV seams stay
connected; clustering is a union-find over adjacent triangles in OKLab with area-weighted
representative colors; vivid identity colors are protected from forced merges; region ids are
deterministic for identical inputs and stamped into the dependency hash so stale bakes are
detectable.

### Runtime path

- The generated mesh carries region color in `COLOR` and `(regionId/255, policyFlag)` in
  `TEXCOORD3`. The capture shader prefers baked colors, then `CartoonShapePart.fillColor`, then a
  neutral fallback.
- The part-ID MRT is now `R16G16B16A16_UNorm`: R = object id, G = color-region id,
  B = paint layer, A = policy flag.
- `facetedLighting` derives per-triangle normals from world-position derivatives (flip-corrected
  against the smooth normal), giving the low-polygon facet shading of the reference image.

### Paint layers (ANGJustinl fork model)

Shadow/base/highlight are **not** a fullscreen lighting pass. The capture shader classifies each
triangle from its deformed geometric face normal against a fixed world light direction
(`fixedLightDirection`, default `(0.35, 0.8, 0.45)`; disable `useFixedLightDirection` to follow the
scene main light), then paints the region color directly in OKLab lightness:

- `paintLayerMode`: `BaseOnly` (pure blocks), `ShadowBase` (default, target look),
  `ShadowBaseHighlight` (only after confirming no PBR feel returns).
- Initial values from the fork, retune on the real character: shadow strength 0.38, highlight
  strength 0.24, shadow thresholds enter −0.30 / exit −0.15, highlight enter 0.62 / exit 0.55.
- **Hysteresis**: `paintLayerHysteresis` keeps a previous-frame NdotL buffer (ping-pong RHalf,
  Render Graph-imported). A triangle that was shadow last frame stays shadow until NdotL passes
  the *exit* threshold, which stops per-frame flicker near thresholds. No temporal color blur.
- Paint-layer boundaries are shape boundaries only — **no ink outlines between layers**; ink stays
  on silhouettes and object occlusion. The region-area filter (`shapeSimplification`) matches the
  full identity (object + region + layer), so small shadow islands merge into their base region.
- Fixed-base materials (painted eye shadow etc., flag 0.9 in the ID alpha channel) and optionally
  `heroDetailStaticLayers` skip dynamic classification entirely, so turning the head or blinking
  never re-shades them.
- `ddx/ddy` of world position are constant per primitive, so classification is inherently
  per-triangle stable within a frame. Known limit: the hysteresis history is screen-space, so a
  fast camera cut can momentarily carry stale layers; robust tracking arrives with the per-layer
  contour stage.

Debug views: NdotL (16), PaintLayerId (17), ShadowMask (18), BaseMask (19), HighlightMask (20).
Layer depth reuses the Depth view, layer contours reuse Outline, final paint composition reuses
Final.

- Outline masks classify silhouette / object occlusion / region boundary / normal detail.
  Region boundaries are **off by default** — color blocks separate by color, not lines.
- `backgroundMode` switches between the camera clear and a flat color for the target look.
- `shapeSimplification` (first-version screen-space approximation) filters the projected buffers
  inside the internal resolution: regions smaller than `minimumScreenRegionArea` pixels merge into
  their neighbour, removing islands and steadying block edges. No color blur, no readback.

### Debug views

Final, BaseColor, Depth, Normal, Lighting, Silhouette, Occlusion, Detail, Outline (v1) plus
SourceMaterial, SampledAlbedo, RegionColor, RegionId, ObjectId, FacetedNormal, LightingBands (v2).

### Tests

`Tools → Window → General → Test Runner` (EditMode) covers clustering merge rules, UV-seam
adjacency, region-id stability, mesh builder boundary splitting, and bone-weight/bindpose/
blendshape preservation.

### Current v2 limits

- Transparent materials are out of scope; alpha cutout is supported when the baker assigns the
  cutout texture to `CartoonShapePart`.
- One renderer = one mesh binding; the mesh variant swaps meshes but keeps source materials for
  `OriginalMaterial` rendering.
- `TEXCOORD3` of generated meshes is repurposed for region data.
- Preview bakes build meshes in memory; they are discarded on domain reload until you run
  **Bake Assets**.
- The screen-space simplification is an approximation; the plan's marching-squares + RDP stage
  (section 15.2) remains future work.

---

## 2D Shape Prototype (offline, visual validation only)

`Tools → Cartoon Projection → Shape Prototype (Offline)` runs the minimal complete
"3D projection → 2D shapes → redraw" sample on a programmatic tower (light body, red-brown
roof, dark base, a real see-through arch). It is a **single-frame offline visual prototype**,
explicitly not the runtime path:

1. Software-rasterizes visibility buffers (object id, region id, paint layer, depth, region
   color) plus a smooth-shaded "source" image.
2. Flood-fills connected components over the full identity.
3. Traces pixel-grid contours per component: outer loops and holes, with shared-boundary
   chains between neighbouring shapes.
4. Simplifies each chain with Ramer-Douglas-Peucker (epsilon in screen pixels). Shared chains
   simplify once and are reused by both sides, so neighbouring shapes never crack.
5. Redraws the image from the simplified 2D polygons with an even-odd scanline filler.

Four stages are written to `Generated/ShapePrototype/`: `Stage1_Original.png`,
`Stage2_RegionMask.png`, `Stage3_SimplifiedContours.png` (outer edges black, shared boundaries
red, holes blue), `Stage4_Redrawn.png`, plus a 2×2 `Overview.png`. Verified reference output
from the offline harness ships in `Generated/ShapePrototype/Reference/` (720×540: roof 1348→9
corners, body 1718→17 corners with the arch kept, hole preserved, no cracks).

`Tools → Cartoon Projection → Report Region Data Status` prints which pipeline/renderer/feature
is active, every renderer's region-data state (baked / fill-color fallback / no data), and
warns when `MaterialColorBlocks` is selected without any baked renderers — no silent fallback.

## v1 pipeline (unchanged architecture)

```text
3D renderers + CartoonShapePart metadata
   |
   v
Source Material Color Copy
   |
   +-------------------------------+
   |                               |
   v                               v
2D Shape Part Projection (geometry is re-rasterized)
   |-- Flat part fill color
   |-- Device depth
   |-- World normal (optional detail debug)
   `-- Stable render-only part ID
   |
   v
Depth + Part-ID Boundary Classification
   |-- Silhouette: shape/background boundary
   |-- Occlusion: different part IDs / depth discontinuity
   `-- Detail: optional normal discontinuity (off by default)
   |
   v
Source Material (or Flat Fill) + Dark Contour Composition
   |
   v
Camera output
```

`CartoonShapePart` supplies each renderer's flat fallback color and stable, non-gameplay ID through a
`MaterialPropertyBlock`. The projection pass draws opaque geometry again into its own internal depth
buffer and four MRTs for contour classification. The final composite uses the camera's rendered color
by default, so source material colors and textures remain visible.

## Defaults

- Lighting quantization: off
- Preserve Source Materials: on
- Pixelation: off, so projected contours use final-output pixels
- Normal detail lines: off
- Shape silhouettes and inter-part occlusion contours: on
- Contour width: 1.25 pixels

Select `Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset` to tune the feature.
Disable **Preserve Source Materials** there when a pure flat per-part fill is desired.
The test scene is `Assets/Scenes/CartoonRendererTest.unity`.

## Relationship to the reference project

This milestone implements its most important visual layer: per-part projection, flat shape fill,
stable part IDs, depth ordering, and part contours. It remains GPU-only and does not yet reproduce the
reference repository's CPU/WASM contour-loop extraction, Douglas-Peucker-style simplification,
shared-chain constraints, motion-field drag, or polygon re-rasterization. Those are the next stage if
the projected-part visual direction is approved.

## Current limits

- Opaque `Renderer` components are supported; transparent parts require a separate ordering policy.
- Renderers without `CartoonShapePart` use the capture shader's neutral fallback color and ID.
- One renderer currently corresponds to one projected part. Sub-mesh/material segmentation is not automatic.
- Camera stacking and XR have not been validated.
