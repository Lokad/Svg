# Rasterization design

Lokad.Svg lowers static SVG into an owned scene, rasterizes it in managed code,
and encodes PNG output. It targets exact decoded pixels from the pinned Chrome
software renderer within the [supported subset](SUPPORT_MATRIX.md#supported-svg-subset).
Native numerical behavior is reproduced by managed kernels; no browser, native
rasterizer or machine-font service participates in conversion.

This document describes the current architecture. Detailed experiments and
superseded checkpoints remain in [Git history](https://github.com/Lokad/Svg/blob/48da1627/docs/RASTERIZATION_DESIGN.md).
[INVENTORY](INVENTORY.md) records regression coverage and [BENCHMARKS](BENCHMARKS.md)
records measurements.

## Pipeline and ownership

| Stage | Responsibility | Main implementation |
| --- | --- | --- |
| Parse and resolve | Validate XML/CSS, compute inherited presentation, register definitions and resolve local references. | [SvgRootParser](../src/Lokad.Svg/Parsing/SvgRootParser.cs), [SvgResourceUseReplay](../src/Lokad.Svg/Parsing/SvgResourceUseReplay.cs) |
| Retain scene | Own geometry, ordered draw commands and resource bindings; preserve authored curves and replayable source. | [SvgRoot](../src/Lokad.Svg/Parsing/SvgRoot.cs), [SvgAuthoredPath](../src/Lokad.Svg/Parsing/SvgAuthoredPath.cs) |
| Rasterize | Map geometry and paints, build bounded coverage, replay layers and composite pixels. | [SvgConverter](../src/Lokad.Svg/SvgConverter.cs) |
| Encode | Write PNG to owned memory, caller memory or a writable stream. | [PngEncoder](../src/Lokad.Svg/Encoding/PngEncoder.cs) |

Forward definitions resolve after registration. Resource references borrow their
owning scene; sealed child scenes retain the storage needed after parser staging
is released. Partial construction and conversion failure release owned storage.
[SvgPreparedDocument](../src/Lokad.Svg/SvgPreparedDocument.cs) retains one prepared
scene/font operation and permits one conversion. Caller-owned font bytes remain
immutable and valid for the documented snapshot lifetime.

## Geometry and native raster routes

Preserve directed line, quadratic, cubic and rational-conic commands until the
relevant transform and stroke construction are known. Curve offsets, joins, caps
and dashes share [native path stroke construction](../src/Lokad.Svg/Rendering/SvgNativePathStroke.cs).
Flattening too early changes curvature, bounds and fringe coverage.

[Analytic coverage](../src/Lokad.Svg/Rendering/SvgAnalyticCoverage.cs) walks edges
with winding intervals and native fixed-point advancement. Convex, compound and
clipped paths retain their own eligibility checks. Rectangle, frame, hairline and
fallback routes remain where their numerical behavior differs.

Whole-path bounds select mask versus direct/run painting; clipped bitmap bounds
limit storage. Direct paint events and pending additive runs remain distinct.
Do not replace these routes with blanket supersampling, independently painted
triangles or a mathematically equivalent union without proving pixel equivalence.

## Coordinates and bounds

Compose root, viewport, group, instance and leaf transforms while retaining each
stage's percentage base and physical viewport clip. Local aliases exclude source
ancestors. Forward replay preserves authored/inherited paint and source order.

Keep geometry/object bounds, visual stroke bounds, construction clips, dispatch
bounds and stored bitmap bounds distinct. Object-box paints/resources bind to
pre-clip geometry. Cropping storage must preserve the logical device origin.
Nested viewports keep the parent affine map outside their local viewBox map.

Sizing inspection and conversion share root CSS/unit resolution.
Opt-in raster bounds choose one uniform output scale before lowering, leaving
the logical viewport and percentage bases intact. Logical picture font sizing
precedes the separate native raster strike transform. Integer canvas rounding
does not enlarge the physical viewport clip, including a minimum one-pixel canvas.

[Shader coordinates](../src/Lokad.Svg/Rendering/SvgShaderCoordinates.cs) preserve
separate local, reference and bitmap mappings. Gradient, image and glyph consumers
round at different native stages; combining their arithmetic can move samples or
change dithering even when the real-valued transform is equivalent.

## Paint and compositing

Resolve inherited stroke paint before selecting a leaf's lowering route. Path
gradients bind to the undashed centerline's native curve bounds, including open
paths with no fill; flattened fill bounds and stroke extents are separate.
Paint-filled paths and point lists compose ordinary fill and stroke lowering;
recording applies opacity and paint order once. Affine linear/radial strokes and
fractional pattern fills retain source commands, local bounds and composed paint
maps. Rectangle strokes reuse native construction for dashes, non-scaling widths,
joins and fractional shader edges. Sharp-rectangle dashes begin at the top-left
corner; zero-size rectangles paint nothing.

[Analytic paint](../src/Lokad.Svg/Rendering/SvgAnalyticPaint.cs) separates solid,
shader, mask and direct/run consumers. Preserve their byte premultiplication,
coverage scaling, source-over order and rounding boundaries. Alpha-only, binary,
antialiased and luminance coverage are distinct inputs.

Gradients retain float stop/ramp evaluation and ordered dithering in the logical
source bitmap's coordinates. Images retain their own sampling matrix, clipping
and alpha rules. [Pattern tiles](../src/Lokad.Svg/Rendering/SvgPatternTileCache.cs)
reuse bounded scene rendering and repeat sampling. Optional tile retention needs
matching source, maps, sampling and paint; declining it preserves rendering.

## Resources and effects

Clips retain child winding, mapped source curves and resource units. Curve unions
use bounded intersections and boundary linking; larger clip content can replay
through an owned mask scene. Mask children composite in source order before
alpha/luminance conversion. Retained clips/masks bind in the final scene owner,
including nested instances and pattern content.

For one element, composition follows **filter → mask → clip → opacity**. A child
clip remains inside an ancestor's effect. Group opacity isolates the composed
children instead of multiplying each child independently.

[Filter playback](../src/Lokad.Svg/Rendering/SvgFilterPlayback.cs) retains primitive
regions, result dependencies, operating color spaces and source-bitmap placement.
Padded source capture, cropped storage, intermediate image subsets and restoration
into a parent bitmap remain separate. Preserve source origins when translating
or bounding blur work; source capture and final painting must use the same maps.
Filter sources preserve native rectangle coverage and layer-bound rounding;
sRGB and linear-RGB image sampling retain their distinct precision. Resource
clips enclose group isolation, applying coverage once when opacity restores.

## Supplied fonts

The host supplies manifest/catalog/provider inputs and owns acquisition, retry,
caching, credentials and transport. Rendering consumes verified immutable font
bytes; it does not invoke resource acquisition implicitly.

Shared bounded layout retains text/tspan order, positioning, metrics, admitted
kerning/ligatures, text paths and paint. Outline replay preserves composite
component placement and metric selection. Windows uses its matched TrueType/LCD
coverage and preblend rules; Linux uses its matched light fitting and grayscale
rules. The bounded TrueType interpreter keeps projection and freedom separate;
the Windows target uses ClearType's hint grid for projections with a horizontal
component while preparation still rounds on the physical grid. Simple and
composite fitting share metric initialization; subpixel-positioned hints retain
the linear advance. Windows font-unit conversion retains float scale precision
before coordinate rounding; other targets retain their fixed-point scales.
Exact references are platform-specific. See the [font support boundary](SUPPORT_MATRIX.md#text-and-fonts)
and [public qualification](../tests/Research/public-font-qualification/README.md).

## Limits and cancellation

[RenderBudget](../src/Lokad.Svg/Rendering/RenderBudget.cs) reserves dominant owned
storage before allocation; [render limits](../src/Lokad.Svg/SvgRenderLimits.cs)
bound input/output size, dimensions and shared work. Temporary and retained owners
release deterministically. Tile/effect work consumes the enclosing conversion's
remaining allowance. Check cancellation inside bounded parsing, geometry, raster
and encoding loops, including growth/copy paths.

Memory estimates follow the [approximate accounting policy](SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).
Caller streams remain open; hosts own transactional output. Cancellation or
failure must preserve cleanup, borrowed-storage validity and output contracts.

## Maintaining fidelity

Use unchanged SVG/options/fonts and frozen platform references to diagnose a
mismatch. Reduce it to an independent control, correct the shared mechanism and
preserve established exact results. [Oracle capture](ORACLE.md) and
[pixel comparison](VISUAL_COMPARISON.md) define the evidence: decoded RGBA8 must
match exactly, ignoring RGB only where both pixels have zero alpha. Tolerance or
candidate-driven reference replacement does not close an admitted failure.

Run targeted checks during correction and full qualification at meaningful
checkpoints. Keep visual cases, representative API checks and resource regressions
independent. Update this design only for lasting architectural changes; keep
chronological experiment logs in local study artifacts and change descriptions.
Adapted algorithms retain their source notices and [third-party notices](../THIRD_PARTY_NOTICES.txt).
