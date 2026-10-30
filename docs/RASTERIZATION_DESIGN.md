# Rasterization design

Lokad.Svg lowers static SVG into an owned scene, rasterizes it with managed
kernels and encodes PNG. The [support contract](SUPPORT_MATRIX.md) owns public
syntax/restrictions; [test guidance](../tests/README.md) owns validation. Numerical
kernels reproduce pinned software-renderer behavior without a runtime browser,
native rasterizer or machine-font service.

## Pipeline and ownership

| Stage | Responsibility | Implementation |
| --- | --- | --- |
| Prepare | Read bounded XML/CSS, resolve sizing and inherited declarations, index definitions and select required fonts. | [Document preparation](../src/Lokad.Svg/Parsing/SvgDocumentPreparation.cs), [root parser](../src/Lokad.Svg/Parsing/SvgRootParser.cs) |
| Retain | Own geometry, source curves, ordered commands, resource bindings and replay dependencies. | [Scene](../src/Lokad.Svg/Parsing/SvgRoot.cs), [authored paths](../src/Lokad.Svg/Parsing/SvgAuthoredPath.cs) |
| Rasterize | Prepare mappings/coverage, render effects and layers, sample paints and composite pixels. | [Converter](../src/Lokad.Svg/SvgConverter.cs) |
| Encode | Produce independently owned PNG memory, caller-memory output or stream output. | [Encoder](../src/Lokad.Svg/Encoding/PngEncoder.cs) |

Definitions register before forward references resolve. Deferred paints, text,
tiles and uses replace recorded placeholders in batches, retaining their source
ranges and scope boundaries. Declarations are prepared once; instance inheritance
and deferred opacity bind at replay. Resource references borrow their scene;
sealed child scenes retain storage after parser staging ends. Partial construction
and failure release owners deterministically.

Solid, linear, radial and pattern polygon bindings use one
[contour payload](../src/Lokad.Svg/Parsing/SvgPolygonContours.cs) for points, contour
ends, winding signs and analytic spans. Repainting and mask stamping transfer its
existing scene charge after construction succeeds; they do not reserve the same
arrays again. Radial paint has a separate charge, and rejected paint admission or
vector placement leaves the source owner intact. Empty pattern paint disables
filling while retaining geometry. Mapping, clips, paint state and authored-path
ownership remain separate; mask snapshots still copy genuinely independent data.
Use and symbol mapping reserve the receiving contour payload before allocating
points or curves, then transfer that same charge to the completed binding. Failed
mapping releases the reservation. Borrowed contour ends/signs remain accounted for
in the result because definition storage is released before the scene's lifetime ends.
Temporary inherited-fill bindings borrow definition contours only during mapping.
Their batch owns its reference array; the definition retains its contour charge.

Prepared-document font discovery borrows the same collected styles, reader
workspace and active budget used by lowering. Discovery control storage is
temporary; selected font bytes stay alive through the prepared operation.
The admitted resource dependency graph and paint selection remain owned by that
preparation through acquisition, then serve lowering without a second collection.
The [HTTP resolver](HTTP_FONTS.md) owns shared downloads/cache under a longer-lived
host scope; document and retained PNG owners are siblings under the same root.

Geometry uses inline primitive values plus one typed reference payload. That
payload owns its reservations; authored paths retain separate ownership. Prepared
documents retain one scene/font operation for one conversion. Font programs remain
immutable for their borrowed lifetime; independent operations own their scratch.

## Geometry and native raster routes

Retain directed lines, quadratics, cubics and rational conics until transforms
and [stroke construction](../src/Lokad.Svg/Rendering/SvgNativePathStroke.cs) are known.
Early flattening changes offset curves, joins, dashes, bounds and fringe coverage.

[Analytic coverage](../src/Lokad.Svg/Rendering/SvgAnalyticCoverage.cs) walks winding
intervals using native fixed-point advancement. Rectangle/frame, hairline,
convex/compound and fallback routes have distinct eligibility and arithmetic.
Whole-path bounds choose direct/run versus mask painting; clipped bounds limit
storage. Direct events and pending additive runs preserve their order.

Finite source coordinates and emitted fixed-point edges have different admission
rules. Clipped construction validates endpoints/controls in bitmap-local and
translated canvas space before allocating edges; uncut construction retains its
range gate. Convexity uses authored controls after float placement. Flattened
samples cannot veto that classification. [Polygon traversal](../src/Lokad.Svg/SvgConverter.PolygonRaster.cs)
shares preparation, row work and scratch across paint kinds without merging their
sampling/blend semantics.

## Coordinates and bounds

| Domain | Invariant |
| --- | --- |
| Logical viewport/viewBox | Own percentage bases and layout; output density does not resize them. |
| Source/instance map | Preserve authored placement and resource inheritance; local aliases exclude source ancestors. |
| Object box | Measures pre-clip geometry/layout for normalized paints/resources. |
| Visual/drawing enclosure | Includes painted stroke/effect extents and source-space enclosure before placement. |
| Construction/dispatch bounds | Decide edge admission and raster route independently of clipped storage. |
| Captured/sampled image | Retain requested subsets, content clips, image hints, color space and origin even when storage is cropped. |

Nested viewports retain the parent map outside their local viewBox map. Ordinary
drawing bounds use native integer saturation/culling; resource pictures keep local
visual bounds. Culled drawings retain inert anchors when ancestor effects can
generate pixels from transparent input. Markers share a scene-owned canvas stage
across contours and paints, independently of viewport clipping.
Leaf polygon clips bind native clip coverage while retaining source contours and
shader coordinates; they do not geometrically intersect flattened paint vertices.

Mask paint clips enclose the union of the resource region and target visual bounds
in source space. Instance replay transfers local visual bounds and materialized
clip dependencies into the final scene owner without changing the source paint
arena. Alpha masks consume that clip during replay; luminance pictures preserve
their color context and clip at restore.

Glyph mask bounds include horizontal filter guards outside the image sampled at
four times resolution. Its origin, allocation, limits and filter indices share
the interior domain. Strike transforms retain recorded font size separately from
the relaxed device matrix. Vertical baselines snap device X to an integer; fills
and strokes share that rule and axial-outline classification.

## Paint and compositing

Resolve each fill/stroke independently, then apply paint order and element opacity
once. Empty servers suppress only their own operation. Text/curve strokes bind
paints to their source geometry; undashed curve bounds, object boxes and visual
stroke bounds are distinct. Point lists retain source float coordinates and
authored verbs regardless of numeric spelling.

[Shader coordinates](../src/Lokad.Svg/Rendering/SvgShaderCoordinates.cs) retain local,
reference and bitmap mappings. [Analytic paint](../src/Lokad.Svg/Rendering/SvgAnalyticPaint.cs)
keeps solid, constant-shader, general-shader, mask, direct/run and fringe-pair
consumers distinct. Premultiplication, coverage scaling, color transfer,
source-over and byte storage round at their actual stages. Continuous geometric
equivalence does not establish identical pixels.

Gradient preparation preserves float stop order, hard/flat boundaries, duplicate
groups, implicit endpoints and periodic normalization. Factory means use authored
stops; conical visibility is applied before spreading. Alpha precision depends on
the authored color syntax. Dithering belongs to the logical source bitmap, not
compositor tiles. Pattern scenes retain their own source maps, recording scopes
and bounded repetition; optional tile reuse requires matching sampling and paint.

## Embedded images

Decoded storage, fractional source windows and destination clips stay separate.
Integer sampling views enclose the source window while retaining decoded stride.
Recorded draws use ceil-sized decoded levels and Q15 source replacement; picture
draws use floor-sized mips and float interpolation. Strict subsets clamp sampling.
Reduction reserves scratch, charges samples and releases intermediate levels on
failure. Image alpha/edges retain their own rules even for uniform colors.

## Resources and effects

Element composition follows [filter → mask → clip → opacity](SUPPORT_MATRIX.md#clips-masks-and-composition).
Ancestor clips surround child filter output; source-local clips remain inside
the filter. Group isolation/restore applies each clip and opacity once. Identity
groups avoid unnecessary offscreen images; real opacity isolates.

Clip unions retain ordered original operands, winding and cyclic starts. Convex
operands share native normalization; other unions use bounded boundary operations.
More than 42 children use painted replay, preserving each child's authored paint
kind and a separate enclosing clip. Ordinary masks composite children in source
order before alpha/luminance conversion.

[Filter playback](../src/Lokad.Svg/Rendering/SvgFilterPlayback.cs) retains result
dependencies, primitive regions, operating spaces and source placement. Requested
output, native image hints, captured content, cropped storage and padded scratch
are separate. Parent capture includes filtered child output. Rescaled kernels read
the captured subset; offsets/aliases can retain deferred sampled images until a
consumer requires pixels.
The bounds query distinguishes deferred paint, padded images and cropped images.
Cropping invalidates transparent-padding knowledge. Affine restoration uses the
active clip for edge construction and replays coverage with the sampled paint.
Sampling subtracts the destination bitmap origin before concatenating the stored
image origin. Geometry keeps its canvas placement. Materialized and deferred
images share that map order; enclosing opacity stores covered, converted pixels
before applying packed row alpha, as ordinary layers do.
Packed and floating shader samplers retain their independent matrix arithmetic.
An inverse tile enclosure can reach a source whose restored image misses the tile;
empty destination areas return before capture or edge preparation.
Zero-effect aliases retain their sampling color filter across ordinary and fast paths.

Fixed lengths and translate arguments resolve within native CSS layout range
before placement. Object boxes compose into filter parameter space before float
enclosure. Regions map through the padded bitmap; owning source draws use the
recorded layer map. Clip geometry and fitted glyphs retain their own concatenation.

Uniform flood alpha stays at pipeline precision through graph evaluation.
Operating color space travels with stored/deferred images; composing compatible
transforms cannot erase a required transfer. Image drawing transfers color before
edge coverage and uses destination clipping. Shader consumers retain separate
sampling rules. Enclosing opacity first materializes sampling/color/crop coverage,
then applies bitmap alpha; its painted extent bounds restoration.

## Supplied fonts

Shared bounded layout preserves source text/span ranges, font/paint selection,
positioning and admitted shaping. Reader chunks add no boundaries. Comments can
preserve shaping; element boundaries may select distinct scaled fonts. Ligature
prefixes retain their source items even without their own glyphs.

Cursor layout applies relative offsets, fitting, absolute positions and anchors.
Chunk anchors measure extrema of positioned advance edges, including backtracking.
Fragment origins and anchor shifts truncate separately. Text boxes join fitted
ink and physical source rectangles; drawing enclosures remain local. Mixed
direct/path text shares parent shaping storage, with scoped views owning only
rebased fragment arrays. Unpainted characters may still contribute layout bounds.

Windows uses matched TrueType/LCD fitting, scanning and preblend; Linux uses light
fitting/grayscale coverage, with axial and unhinted routes chosen separately.
Projection/freedom vectors, hint grids, implied points, scan direction, endpoint
ownership and dropout modes keep their numerical boundaries. Font-selected
unfitted ranges retain scanner effects without fitted point movements. Metric
reuse is operation-local and budget-owned; it does not create shared mutable font
state. [Font qualification](../tests/README.md#qualification) owns the evidence.

## Limits and cancellation

[RenderBudget](../src/Lokad.Svg/Rendering/RenderBudget.cs) reserves dominant storage
before allocation. Parser, scene, glyph, tile, filter and encoder storage share the
borrowed host allowance when supplied; no private cap or available-balance policy
remains. Inspection and all output APIs share execution cleanup. Font discovery
retains requirements through awaited acquisition and transfers them to the snapshot;
metadata and scene charges follow their owners. Governed PNG output has a sibling
scope retained by SvgPngResult until disposal.
Growth/copy loops also observe cancellation. Failure releases private owners and
leaves borrowed inputs unchanged. Caller streams remain open and can contain
partial output; hosts own transactional handling. Memory estimates follow the
[accounting policy](SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).
