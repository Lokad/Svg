# Rasterization design

Lokad.Svg prepares static SVG into an owned scene, rasterizes it with managed
kernels and encodes PNG. The [support matrix](SUPPORT_MATRIX.md) defines accepted
syntax and restrictions; [test guidance](../tests/README.md) owns qualification.
The renderer reproduces pinned software-renderer behavior without a runtime
browser, native rasterizer or machine-font service.

## Pipeline and ownership

| Stage | Responsibility | Implementation |
| --- | --- | --- |
| Invoke | Validate calls and retain operation, scene and output lifetimes. | [Invocation/output](../src/Lokad.Svg/SvgConverter.Invocation.cs) |
| Prepare | Read bounded XML/CSS, resolve sizing/inheritance, index definitions and select fonts. | [Preparation](../src/Lokad.Svg/Parsing/SvgDocumentPreparation.cs) |
| Retain | Own geometry, source curves, ordered commands and resource dependencies. | [Scene](../src/Lokad.Svg/Parsing/SvgRoot.cs) |
| Rasterize | Prepare coverage, execute effects, sample paints and composite pixels. | [Converter](../src/Lokad.Svg/SvgConverter.cs), [polygon traversal](../src/Lokad.Svg/SvgConverter.PolygonRaster.cs) |
| Encode | Produce owned PNG storage, caller-memory output or stream output. | [Encoder](../src/Lokad.Svg/Encoding/PngEncoder.cs) |

Raw, catalog-prepared and HTTP-prepared conversion share scene lowering and
admission. Preparation retains selected dependencies through acquisition and
lowering. Forward references replace recorded placeholders after definitions
resolve; instance inheritance and deferred opacity bind at replay.

Geometry uses inline primitive values and a typed reference payload. Contour
repainting transfers its existing charge after construction succeeds; independent
copies reserve receiving storage first. Authored paths keep their own owner.
Resources borrow immutable child scenes retained by the parent. Pattern graph
validation checks cycles/depth before rasterization; aliases share content.

Filter capture borrows immutable command ranges and geometry indices from the
retained scene. Rewritten command envelopes remain separately owned. Matching
capture/playback grids can borrow pixels with a transparent guard; expanded or
shifted grids retain admitted copies. Capture owners outlive restoration.
Graph queries use bounded scratch; stored pixels retain their independent owners.
Reference validation is independent of pixel demand. Flood consumes no image,
merge reads its nodes and color-matrix reads its resolved source; unused primary
references do not materialize alpha or transparent planes.
Only dependencies of the final result evaluate pixels. All primitive regions and
declarations still prepare, so an unread node can supply region defaults without
owning pixels. A bounded stack bitmap routes demand through primary, secondary
and merge operands; existing numerical kernels and coarse result owners remain.

A prepared document retains one scene/font operation and permits one conversion.
Font programs remain immutable for their borrowed lifetime. Raster scratch ends
with execution; independently owned PNG results retain sibling storage until
disposal. [HTTP font guidance](HTTP_FONTS.md) owns resolver/cache lifetimes.

## Geometry and native raster routes

Retain directed lines, quadratics, cubics and rational conics until placement and
[stroke construction](../src/Lokad.Svg/Rendering/SvgNativePathStroke.cs) are known.
Early flattening changes joins, dashes, bounds and fringe coverage. Dashed sources
retain verbs, patterns and `pathLength` calibration until their bitmap construction
context is known. A shallow authored curve can contain ink even when its fallback
has only two endpoints.

[Analytic coverage](../src/Lokad.Svg/Rendering/SvgAnalyticCoverage.cs) uses native
fixed-point advancement. Rectangle/frame, hairline, convex/compound and fallback
routes retain distinct eligibility and arithmetic. Whole-path bounds select
direct/run or mask painting; clipped windows bound storage. Border contributions
and direct fringe events survive storage trimming. Triangle/trapezoid alpha
narrows before additive saturation.

Polygon painting owns mapped/authored storage until traversal finishes.
Point placement chooses its authored or fallback map once; bounds include curve
controls before storage clamping. Source-coordinate admission remains separate.
`PolygonRaster` and fallback pixel coverage borrow its descriptor by readonly
reference. Traversal shares coverage preparation and scratch across paint kinds;
linear/radial/pattern polygons share ordered shader painting. Material setup,
hairlines, solid/font blending and rectangle sampling keep their own arithmetic.

## Coordinates and bounds

| Domain | Contract |
| --- | --- |
| Logical viewport/viewBox | Own percentage bases and layout; output density does not resize them. |
| Root image layout/projection | Keep CSS subpixel layout, recorded projection and image destination separate from allocation. |
| Object bounds | Measure pre-clip geometry/layout for normalized resources. |
| Visual/recording bounds | Include local stroke/font/effect extents before placement and enclosure. |
| Construction bounds | Preserve source operands and the owning bitmap's clip/origin. |
| Storage/paint windows | Bound retained rows and exclusive writes without changing complete-path dispatch. |
| Clip coverage | Retain actual coverage independently of its enclosing rectangle. |
| Captured/sampled image | Keep content, requests, padding, color space, origin and sampling views distinct. |

The [drawing map](../src/Lokad.Svg/Rendering/SvgDrawingMap.cs) separates unrounded
parameter coordinates from native device coefficients. Declaration, local,
reference, recorded and output transforms retain their composition order.
Subtract a bitmap origin at its native stage; flattening transforms first loses
intermediate float rounding. Global containment alone does not prove bitmap-local
containment: retained endpoints/controls must pass the local check.

The [canvas map](../src/Lokad.Svg/Parsing/SvgCanvasDrawMap.cs) retains parent,
boundary and source stages. Nested viewports, markers and path clips preserve
their concatenation boundaries. Resource normalization does not transform the
consumer. Root CSS box paint, content projection, image coverage and content clips
retain independent maps; direct paints and isolated restoration consume coverage
at their own stages.

Invisible or collapsed paint can retain source/layout bounds needed by enclosing
effects. Non-displayed instances contribute no bounds. Unpainted source objects
keep their local effects; synthetic empty-scope carriers retain ancestor effects
around generated output. Neither empty pixels nor equal bounds establish those
ownership roles. The [bounds/map contract](../tests/Research/release-baselines/bounds-map-contracts.md)
joins these domains to source owners and independent witnesses.

## Paint and compositing

Resolve fill/stroke independently, then apply paint order and element opacity
once. Empty paint servers suppress only their operation. User-space gradients keep
factory coordinates, local matrices and reference placement; normalized paints
bind the source object box before placement.

[Shader coordinates](../src/Lokad.Svg/Rendering/SvgShaderCoordinates.cs) and
[analytic paint](../src/Lokad.Svg/Rendering/SvgAnalyticPaint.cs) preserve distinct
solid, shader, mask, direct/run and fringe consumers. Premultiplication, coverage,
color transfer, source-over and byte storage round at their actual stages.
Equal continuous geometry does not establish identical pixels.

Gradient preparation retains authored stop precision/order, duplicate boundaries,
implicit endpoints and periodic normalization. Factory opacity can select a blend
program independently of sampled alpha. Dithering follows the logical source
bitmap. Pattern sampling repeats each floating tap before clamping/gathering;
tile reuse requires matching sampling, maps and paint state.

## Embedded images

Decoded stride, fractional source windows, integer sampling views and destination
clips remain separate. Recorded draws and picture draws retain their different
mipmap sizing, interpolation and alpha rules. Strict subsets clamp sampling.
Coordinate restart boundaries remain observable even with equal coverage bytes.
Reduction reserves scratch before allocation and releases intermediate levels on
failure. Uniform image color does not authorize solid-paint substitution.

## Resources and effects

Composition follows [filter, mask, clip, opacity](SUPPORT_MATRIX.md#clips-masks-and-composition).
Ancestor clips surround child filter output; source-local clips stay inside the
filter. Identity/single-paint scopes can replay directly; other partial-opacity
groups isolate. Zero opacity or zero retained group-mask alpha suppresses
descendant painting while preparation and source bounds remain intact.

Geometric clips ignore paint opacity and retain ordered operands, winding and
source curves. Large unions use painted replay. IDs inside clip resources remain
ordinary instance targets even when their group contributes no clip silhouette.
Live text clips borrow retained text/font dependencies. Successive rectangular
clips multiply their coverage separately, including coincident edges; an enclosure
intersection does not replace that arithmetic.

Masks composite children before alpha/luminance conversion. Mask target object
bounds precede placement; local visual bounds and source-enclosed paint regions
remain distinct. Alpha-mask replay and luminance-picture restoration retain their
different clip consumers. Mask text constructs fonts in its resource context,
independently of the consumer map. Replay scopes restore maps, clip coverage and
allocation bounds together; replacement buffers stay with their owning frames.

### Filter bounds and placement

[Filter playback](../src/Lokad.Svg/Rendering/SvgFilterPlayback.cs) retains graph
dependencies, primitive crops and operating spaces. Planning and replay share
alias resolution, while bounds propagate forward and sampling requests propagate
backward. Capture hints enclose retained source visuals in parameter space before
graph expansion/device placement; inversion of a scene enclosure cannot recover
that source information.

Destination requests bound capture construction and restoration independently of
image hints and logical bitmap origins. Stored content, initialized padding and
materialized sampling views remain distinct; cropping invalidates padding
knowledge. `SourceGraphic` picture padding differs from `SourceAlpha` and stored
graph-result views. A copying blur kernel can still require image materialization;
kernel identity alone does not justify aliasing.

An empty primitive region retains graph bounds without a pixel plane. A later
reader borrows one clear surface for the current evaluation; an empty final result
transfers that surface to the caller. Other outputs and temporary storage retain
their own admission and cleanup. Color-matrix bias can still generate visible
output from an empty input.

Drop shadows borrow the clear stage destination for alpha input to a separate
blur frame, then clear that scratch before restoration. High-precision final
sampling retains its independent owner and can still govern peak storage.

Sampling subtracts the destination bitmap origin before concatenating the image
origin. Packed, floating, picture and wrapped samplers retain their own rounding.
Geometry, clip construction and fitted glyphs keep their own drawing stages.
Non-scaling stroke visuals do not enlarge object boxes; narrowed bitmap axes can
change restoration routing without changing those layout bounds.

### Filter color and restoration

Operating color space travels with stored/deferred images. Required transfers
survive aliases, including black RGB. Flood paint retains pipeline precision;
color operations precede image-edge coverage and destination clipping. Enclosing
opacity materializes sampling/color/crop coverage before packed bitmap alpha.

Color matrices borrow immutable scene coefficients and retain individual
transfers/clamps, straight-channel evaluation and premultiplication. Compatible
chains avoid intermediate byte images only where execution permits. Terminal
offsets preserve deferred programs through sampling and later coverage; other
consumers retain required materialization. Alpha bias can extend output to the
primitive region. Final restoration consumes deferred color state once.

### Filter evidence

[Test guidance](../tests/README.md) and the
[release evidence index](../tests/Research/release-baselines/README.md) own numerical,
allocation and independent platform qualifications. Source/context/alias cases
remain separate from authored projections and original-document admission.

## Supplied fonts

Bounded shared layout retains source text/span ranges, emitted UTF-16 glyph/cluster
ranges, font/paint selection, positioning and supported shaping. Reader chunks add
no boundaries. Source positions resolve before case projection; deletion removes
empty spans before font selection. Identical resolved face/size intervals can
share shaping while node, paint and positioning boundaries retain their roles.

Relative offsets, fitting, absolute positions and chunk anchors remain ordered.
Anchors measure positioned advance extrema, including backtracking. Fragment
origins and anchor shifts truncate separately. Text paths share child-style
resolution with discovery, while their placement/reference syntax stays distinct.
Unpainted characters can still contribute layout bounds.

Windows TrueType/LCD and Linux light-fitting/grayscale routes retain independent
references. Canonical unhinted paths handle large transformed glyphs through the
existing analytic renderer. Fitted point placement, phantom origins, hint vectors,
scan boundaries, implied points, endpoint ownership and dropout keep their own
numerical stages. Glyph masks retain filtering guards separately from sampled
interiors; ink exclusions union independently rounded glyph clips.

Font discovery, resource selection and lowering share retained preparation.
Metric reuse is operation-local and budget-owned; font programs do not acquire
shared mutable layout state. The [support matrix](SUPPORT_MATRIX.md#text-and-fonts)
owns shaping restrictions; [font qualification](../tests/README.md#milestone-and-release-qualification)
owns source/native and meaningful platform evidence.

## Limits and cancellation

[RenderBudget](../src/Lokad.Svg/Rendering/RenderBudget.cs) admits dominant storage
before allocation. Parsing, scenes, glyphs, tiles, filters and encoding share the
borrowed host allowance when supplied. Capacity growth accounts for overlapping
storage; dependency/source metadata stays charged through its owner's lifetime.
Internal-entity admission grows the existing XML owner before BCL expansion;
entity-bearing inputs bypass raw-byte shortcuts without a second resolver.

Growth, traversal, raster and output loops observe cancellation. Failure releases
owned storage after borrowers finish and leaves borrowed inputs valid. Streams
stay open and can contain partial output; hosts own transactional handling.
Admission refusal ends the operation. Bare-memory convenience output is explicitly
ungoverned; owned PNG output retains its own sibling scope until disposal.

The [accounting contract](SUPPORT_MATRIX.md#resource-limits-and-memory-accounting)
owns estimates, structural limits and lifetime rules. [Benchmarks](BENCHMARKS.md)
owns performance methodology; functional validation times establish no renderer
throughput claim.
