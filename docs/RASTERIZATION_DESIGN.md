# Rasterization design

Lokad.Svg lowers static SVG into an owned scene, rasterizes it in managed code,
and encodes PNG output. It targets exact decoded pixels from the pinned Chrome
software renderer within the [supported subset](SUPPORT_MATRIX.md#supported-svg-subset).
Native numerical behavior is reproduced by managed kernels; no browser, native
rasterizer or machine-font service participates in conversion.

The [support matrix](SUPPORT_MATRIX.md) owns public boundaries;
[tests](../tests/README.md) and [benchmarks](BENCHMARKS.md) own validation and measurements.

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
Anonymous pattern teardown drops its deferred entries before disposing staging.
Gradient and pattern templates wake their dependents when ready. Deferred paint,
text and tile-use commands replace their placeholders in batches; existing scope
boundaries and scopes created during replay map separately into final draw order.
Text replay emits into its recorded scene and maps only its retained source
ranges. Tile text and uses expand together before pattern group binding;
clip-only markers compact once afterward.
[Document preparation](../src/Lokad.Svg/Parsing/SvgDocumentPreparation.cs) shares
reader workspace and collected styles across sizing, output mapping and scene
lowering. Metadata traversal consumes the same work allowance as conversion;
font acquisition retains its separate discovery lifetime.
Rendered leaves prepare their cascaded declarations once and reuse that value
through deferred paints, filters, markers, recording and geometry lowering.
Instance inheritance and deferred opacity apply afterward, so retained sources
can bind independently for each replay.
[SvgPreparedDocument](../src/Lokad.Svg/SvgPreparedDocument.cs) retains one prepared
scene/font operation and permits one conversion. Caller-owned font bytes remain
immutable and valid for the documented snapshot lifetime.

Scene geometry keeps inline primitive values and one typed reference payload,
rather than a reference slot for every paint/geometry variant. The payload owns
its resource reservations; authored-path ownership remains separate.

## Geometry and native raster routes

Preserve directed line, quadratic, cubic and rational-conic commands until the
relevant transform and stroke construction are known. Curve offsets, joins, caps
and dashes share [native path stroke construction](../src/Lokad.Svg/Rendering/SvgNativePathStroke.cs).
Flattening too early changes curvature, bounds and fringe coverage.

[Analytic coverage](../src/Lokad.Svg/Rendering/SvgAnalyticCoverage.cs) walks edges
with winding intervals and native fixed-point advancement. Convex, compound and
clipped paths retain their own eligibility checks. Rectangle, frame, hairline and
fallback routes remain where their numerical behavior differs.

[Polygon traversal](../src/Lokad.Svg/SvgConverter.PolygonRaster.cs) shares analytic/
fallback preparation, row work, fringe bounds and scratch ownership across solid,
linear, radial and pattern paints. Sampling and paint arithmetic stay specialized.

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

Effects obey the [composition contract](SUPPORT_MATRIX.md#clips-masks-and-composition).
Isolation and restoration must apply each clip/opacity once.

[Filter playback](../src/Lokad.Svg/Rendering/SvgFilterPlayback.cs) retains primitive
regions, result dependencies, operating color spaces and source-bitmap placement.
Padded source capture, cropped storage, intermediate image subsets and restoration
into a parent bitmap remain separate. Preserve source origins when translating
or bounding blur work; source capture and final painting must use the same maps.
Filter sources preserve native rectangle coverage and layer-bound rounding;
sRGB and linear-RGB image sampling retain their distinct precision. Resource
clips enclose group isolation, applying coverage once when opacity restores.

## Supplied fonts

Rendering consumes verified immutable font bytes supplied by the host. Shared
bounded layout preserves text/span order, metrics, positioning, admitted shaping
and paint. Composite replay retains component placement and metric origins.
Windows uses matched TrueType/LCD coverage and preblend; Linux uses matched Latin
light fitting and grayscale coverage. Exact references are platform-specific.

The bounded interpreter keeps projection/freedom vectors and physical/hint grids
separate. Windows font-unit conversion retains floating-point scale precision
before signed rounding; other targets retain their fixed-point scales. Font-selected
unfitted ranges retain scanner effects without painting fitted point movements.
Linux light fitting retains standard-stem edge separation, non-base classification,
pending-edge collapse bounds and relative serif offsets.

Windows natural/symmetric LCD modes follow bounded gasp data at actual raster
size. Ordinary fill, smart dropout profiles, span ownership and stub continuation
keep distinct crossing/rounding rules. Preserve implied half-unit points, six-bit
profiles, scan direction, turning/flat-tangent subdivisions and full/half-scan
endpoint ownership. Rounding bounds must preserve thin strokes. Do not combine
these numerical stages merely because their continuous geometry is equivalent.
The `SvgGlyphLcdCoverageTests.*` controls in [core tests](../tests/Lokad.Svg.Tests/)
protect these distinctions; feature-specific font manifests retain native evidence.

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

Use unchanged SVG/options/fonts and frozen platform references. Reduce a mismatch
to an independent control, correct the shared mechanism and preserve established
exact results. [Research and comparison procedures](../tools/README.md#research-probes)
define the workflow. Keep experiment logs out of this architectural guide.
Adapted algorithms retain source notices and [third-party notices](../THIRD_PARTY_NOTICES.txt).
