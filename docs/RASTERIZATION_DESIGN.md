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

Pattern paints borrow sealed child scenes, including through template aliases,
glyph shaders and masks. One budgeted iterative walk validates content cycles and
recursive depth before rasterization; template aliases do not add scene depth.
Existing document owners retain shared content and release it once. The
[nested-pattern evidence](../tests/Research/svgweb/nested-patterns.json) separates
graph, ownership and canonical rendering responsibilities.

Picture sampling repeats each floating bilinear tap before clamping and gathering
its pixel. Rounded reciprocal multiplication can cross a period boundary before
the corresponding integer address does; the next tap cannot be inferred by adding
one to the first address. Glyph shaders share the existing fragment drawing
transform with decorations, keeping its paint origin separate from fitted glyph
ink. [Repeat evidence](../tests/Research/svgweb/pattern-repeat-addressing.json)
retains direct arithmetic vectors and the original glyph interactions.

Pattern registration, font discovery and resource selection share declaration
locations. Selection tracks rendered-group ancestry separately from general drawing
eligibility, so admitting a declaration also admits its required content. Known
foreign descriptions share one bounded, allocation-free subtree scan; stylesheet
collection and font discovery do not activate their contents. The first recorded
unsupported source survives later text rejection. [Traversal evidence](../tests/Research/svgweb/shared-traversal.json)
maps the mechanisms, prepared-font contract and independent rendering controls.

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

Linear user-space paint keeps declared factory endpoints, its gradient-local matrix
and reference placement separate. Fill/stroke lowerers and use/symbol instances
share the [scene-owned paint map](../src/Lokad.Svg/Parsing/SvgLinearPaintMap.cs);
placement never folds endpoints into device coordinates. Map storage is admitted
before allocation, and identity placement reuses an existing binding. Object-box
normalization still uses source geometry before reference placement.

Linear and radial samplers replay the existing recorded transform chain in the
owning bitmap's coordinates. Subtract its origin before concatenating child stages;
flattening first loses intermediate float rounding. The first stage includes the
recorded projection and source placement. Draw-local caches publish only completed
mapping; deep-chain scratch is budgeted and released before sampling continues.
Source captures also retain the complete drawing reference: compose the gradient's
source placement with recorded layout before native destination narrowing. A device
matrix narrowed before that composition can change the stored blur input.

Gradient opacity belongs to all prepared stops and the native factory, independently
of a sampled alpha. Opaque linear/radial programs reduce source-over to source before
coverage interpolation; genuinely conical and translucent programs retain source-over.
Full direct spans store source lanes, while A8 masks retain interpolation. Geometry
and glyph samplers share this material rule; hairline opacity can disable reduction.

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

Roots, markers and stroke metrics share fixed CSS unit factors. Their callers
retain distinct sign, range, percentage/font bases and float-narrowing stages.
Stroke width selection uses the same conversion as construction, so `0pt` does
not activate an otherwise unused font or paint resource. The
[length evidence](../tests/Research/svgweb/fixed-stroke-lengths.json) separates
unit/boundary mechanisms from native rendering and host-selection contracts.

Retain directed lines, quadratics, cubics and rational conics until transforms
and [stroke construction](../src/Lokad.Svg/Rendering/SvgNativePathStroke.cs) are known.
Early flattening changes offset curves, joins, dashes, bounds and fringe coverage.

A shallow curve can enclose ink against its closing chord even when its fallback
contains only two endpoints. Fill execution and clip staging retain that authored
source; preparation adds a closing sample without changing verbs or native point
counts. Short straight contours and explicitly suppressed empty command streams
remain nonpainting. The [thin-contour evidence](../tests/Research/svgweb/thin-authored-contours.json)
separates these mechanisms from paint consumers and ownership contracts.

[Analytic coverage](../src/Lokad.Svg/Rendering/SvgAnalyticCoverage.cs) walks winding
intervals using native fixed-point advancement. Rectangle/frame, hairline,
convex/compound and fallback routes have distinct eligibility and arithmetic.
Whole-path bounds choose direct/run versus mask painting; clipped bounds limit
storage. Direct events and pending additive runs preserve their order.
Completed convex strips can emit a direct fringe beyond path-bounded mask rows;
the paint clip governs those events. Clip definitions instead construct within
the rounded path bounds intersected with the incoming clip. Existing direct-link
growth accounts for the extra event without widening pending alpha storage.
The [edge evidence](../tests/Research/svgweb/completed-strip-coverage.json) links
native source rules, curve-builder references and refusal/retry coverage.
Triangle and trapezoid alpha narrows to a byte before additive saturation, including
wrapped approximation results. Temporary alpha rows use byte storage; fixed-point
geometry and accumulation sums retain their wider arithmetic.

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
| Root image layout/projection | Keep CSS subpixel layout, recorded projection and the outer image destination separate. Uniform output density joins the recorded projection; its image boundary is independent of raster allocation. |
| Parameter/device mapping | The [drawing map](../src/Lokad.Svg/Rendering/SvgDrawingMap.cs) keeps unrounded filter/bounds coordinates separate from native device coefficients. Recorded source transforms narrow before destination concatenation; fitted device paths consume that placement once. |
| Source/instance map | Preserve authored placement and resource inheritance; local aliases exclude source ancestors. Clip children materialize into resource coordinates before reference placement and device mapping. |
| Canvas boundary | Retain parent rounding before child concatenation. A clip's resource normalization does not transform its consumer; mask construction uses the incoming canvas. |
| Object box | Measures pre-clip geometry/layout for normalized paints/resources. |
| Visual/drawing enclosure | Includes stroke/effect extents before placement. A clip child's stroke can enlarge the resource enclosure while contributing no silhouette coverage. |
| Raster window | Bounds admitted geometry, clip-resource construction and stored numerical coverage. Curve rasterization can paint beyond tight continuous extrema. |
| Clip coverage | Use the actual bitmap coverage tape to classify an opaque rectangle; request/layout bounds can hide an antialiased fringe. |
| Captured/sampled image | Retain requested subsets, content clips, image hints, color space and origin independently of the raster window. |

Recorded primitive drawing bounds enclose local stroke extents before placement;
filter parameter hints retain their fractional visuals. Hairlines and polygons
bind to the same compositor grid and active raster scope. A source content clip
can narrow construction without moving the bitmap origin; owned interiors still
limit writes. The [opacity-source evidence](../tests/Research/svgweb/opacity-source-scope.json)
maps direct mechanisms, distinct leaf/group regressions and original consumers.

A fractional root image clipped by its integer viewport remaps the visible source
rectangle before residual image scaling. Keep both float stages even when the
snapped rectangles coincide; the recorded scene and logical layout stay separate.
The [source-rectangle evidence](../tests/Research/svgweb/root-visible-source.json)
links native matrices, rounding boundaries and a canonical transformed ellipse.

Nested viewports retain the parent map outside their local viewBox map. Ordinary
drawing bounds use native integer saturation/culling; resource pictures keep local
visual bounds. Culled drawings retain inert anchors when ancestor effects can
generate pixels from transparent input. Markers share a scene-owned canvas stage
across contours and paints, independently of viewport clipping.
Viewports, markers and path-clip consumers share the immutable
[canvas map](../src/Lokad.Svg/Parsing/SvgCanvasDrawMap.cs). Path clips round the
parent after output placement, then concatenate the relative child map. Their
resource map remains separate, including object-box normalization.
Leaf polygon clips bind native clip coverage while retaining source contours and
shader coordinates; they do not geometrically intersect flattened paint vertices.
Whole-resource path intersections reuse the curve boundary solver before AA.
Clipped children within a painted union retain independent child maps and the
resource clip in both canvas and child effect contexts. Their alpha applications
cannot be collapsed into one silhouette clip. The target enclosure clips target
ink; mask construction uses the allocated layer before that enclosure trims it.
An unpainted fill retained for a clipped stroke does not require curve source
commands for viewport admission; the painted stroke keeps its own source commands.
Replay scopes save and restore clip coverage, allocation enclosures and drawing maps
together; their frames own replacement storage while the active state borrows it.
Canvas coverage borrows either a tape or one analytic rectangle. Rectangles share
the existing clip rounding and binary-region behavior without a full-surface tape.
Direct draws consume image-boundary coverage separately; isolated groups consume
it at restoration. Source captures clear coverage while retaining their enclosure.
Parameter-space filter captures enclose source visual bounds before graph expansion,
then enclose the capture hint. Object boxes, layer/device enclosures and sampled-image
bounds retain their own rounding. The [native stage observations](../tests/Research/svgweb/root-image-projection.json)
and [capture bounds](../tests/Research/svgweb/filter-transform-review.json) distinguish
these roles independently of equal final images.

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

User-space radial definitions retain factory coordinates and their local matrix
before primitive or instance placement. References compose retained maps, including
axis-aligned placement; normalized paints bind their object box when known. Primitive
and path consumers share these stages. The [binding evidence](../tests/Research/svgweb/radial-primitive-binding.json)
maps direct factory tests, exact instance/viewport contracts and the canonical fixture.

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
Referenced clip children retain authored verbs and placement through registration.

[Filter playback](../src/Lokad.Svg/Rendering/SvgFilterPlayback.cs) retains result
dependencies, primitive regions, operating spaces and source placement. Requested
output, native image hints, captured content, cropped storage and padded scratch
are separate. Descendants inside nested filters retain their original paint bounds
in the parent picture's image hint and dither origin; parent capture separately
includes their filtered output. Rescaled kernels read the captured subset;
offsets/aliases can retain deferred sampled images until a
consumer requires pixels.
Source visuals are enclosed in parameter coordinates before graph expansion;
capture and output image hints are enclosed there again before device placement.
Path stroke layout visuals stay fractional through child placement and union;
recorded painting separately encloses each draw before placement.
[Fractional stroke controls](../tests/Research/svgweb/filter-fractional-stroke-layout.json)
protect both stages, including rotated aliases.
Affine replay reuses retained parameter visuals: inverting a scene enclosure
inflates strokes and changes the source bitmap's shader/dither origin.
[Gradient-stroke evidence](../tests/Research/svgweb/filter-stroke-source-bounds.json)
protects this distinction even when alpha coverage is unchanged.
Non-displayed instances contribute no object or visual bounds to enclosing
effects; hidden visibility and zero opacity retain layout bounds. Source
validation still runs. [Instance display controls](../tests/Research/svgweb/filter-instance-display-bounds.json)
protect the recorded metadata and resulting gradient placement.
An explicit rectangular mask clip can become fully opaque within a compositor
bitmap. Polygon and hairline consumers share that opacity query; a hairline may
escape the bitmap without retaining an AA clip wrapper. Partial intersections and
clip construction retain their own dispatch. [Independent controls](../tests/Research/svgweb/mask-opaque-bitmap.json)
protect source alpha and the later mask composition separately.
Ordinary, nested and image-containing sources all capture the propagated sampling
request; unrelated source ink must not enlarge that curve-construction window.
Axis and transformed effects capture input for each bordered destination bitmap;
its nonzero clip bounds govern the output request, and only its interior owns
restored pixels. A canvas clip enclosure can be wider than one bitmap's coverage;
using that enclosure changes source curve construction before filtering.
Source construction retains image-hint
padding independently of captured-content extents and nested filter requests;
cancellation/refusal restores the enclosing invocation clip.
Content clips limit painting without moving the source bitmap's shader coordinates
or dither origin, including when compact capture omits part of its transparent border.
Affine image edges are constructed and walked against the bitmap clip; requested
output bounds govern row storage and restoration separately.
The RGBA8 clamp fast path rounds each bilinear product before accumulation. Generic
wrapped and picture/mipmap sampling retain fused accumulation, sharing preparation
and storage rules.
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
Final restoration consumes that shader state; deferred source captures retain it
for image sampling. Nonopaque restoration materializes it before packed paint alpha.

Fixed lengths and translate arguments resolve within native CSS layout range
before placement. Object boxes compose into filter parameter space before float
enclosure. Regions map through the padded bitmap; owning source draws use the
recorded layer map. Clip geometry and fitted glyphs retain their own concatenation.

Uniform flood alpha stays at pipeline precision through graph evaluation.
Operating color space travels with stored/deferred images; composing compatible
transforms cannot erase a required transfer. Image drawing transfers color before
edge coverage and uses destination clipping. Shader consumers retain separate
sampling rules. Black RGB retains its color-conversion stage because shader
restoration and packed bitmap restoration round differently over a backdrop.
Enclosing opacity first materializes sampling/color/crop coverage,
then applies bitmap alpha; its painted extent bounds restoration.
Source construction keeps its recording bitmap; final sampling uses the opacity
bitmap's origin. [Independent controls](../tests/Research/svgweb/filter-opacity-device.json)
cover copying and nontrivial kernels, coloured strokes and gradient source paint.
Filter capture retains both directions of the native bitmap axes. Their rounded
product can retain skew, selecting pipeline alpha before clip coverage instead of
packed row blending. [Native controls](../tests/Research/svgweb/group-opacity-stages.json)
protect this selection without snapping coefficients or changing references.

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
The Windows fitted-outline loader preserves positioned quadratic points, including
half-unit implied points. Ink measurement and painting share one scan factory that
rounds to the profile grid and splits extrema. Its fitted temporary remains charged
until preparation finishes; scan storage has its own exclusive lifetime.
The [scan-boundary evidence](../tests/Research/svgweb/windows-scan-boundary.json)
maps the point, mask and ownership responsibilities.
Light fitting separates alignment-zone matches from completed edge placement.
Linked edges are fitted in contour order: the first visited edge anchors later
interpolation, and later zone matches can reposition a shared non-zone edge.
The [fitting evidence](../tests/Research/svgweb/latin-blue-zone-fitting.json)
maps these mechanisms to supplied-program observations.
Projection/freedom vectors, hint grids, implied points, scan direction, endpoint
ownership and dropout modes keep their numerical boundaries.
Windows function recognition preserves ordinary stack/loop effects while suppressing
movement for an exact conditional pixel-delta body; equivalent inline or changed
bodies execute normally. The [independent controls](../tests/Research/windows-conditional-shift/README.md)
pin that distinction without font-name rules.
Windows stack projection loads preserve supplied F2Dot14 words, including rounded
unit-vector components returned by a query. Line construction normalizes its
direction separately; the classic target retains its established stack normalization.
The [stack-vector controls](../tests/Research/windows-stack-vectors/README.md)
cover the read/load round trip and a following relative move.
Windows movement projects the compensation dot with the same component rounding.
An explicit projection-to-freedom copy selects unit compensation; independent
vector setters clear that state even when their rounded words are equal.
General horizontal compensation retains its native signed division, while vertical
and exact-ratio placement keep their own rounding. The
[movement controls](../tests/Research/windows-movement/README.md) pin these transitions.
Font-selected
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
