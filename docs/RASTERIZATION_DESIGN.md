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

Filter source replay borrows a read-only command range from its retained scene.
Geometry and resource indices still address that scene; nested ranges use local
command indices. Captures finish before parent disposal. Ordinary filter capture
and terminal offset/merge replay share this rule; paths that rewrite commands
retain their admitted arrays. [Ownership evidence](../tests/Research/release-baselines/command-view-qualification.json)
protects fixed raster storage as the retained source grows.

Clip contribution and ID selection are independent. A group inside a clip resource
uses dormant ordered replay without adding its descendants to the clip silhouette.
Its IDs remain ordinary instance targets. Font indexing treats the clip as a
definition scope: unselected text stays inactive, while selected descendants use
the consuming instance's inheritance. The existing group and reference owners
serve both roles.

Pattern paints borrow sealed child scenes, including through template aliases,
glyph shaders and masks. One budgeted iterative walk validates content cycles and
recursive depth before rasterization; template aliases do not add scene depth.
Existing document owners retain shared content and release it once. The
[nested-pattern evidence](../tests/Research/svgweb/nested-patterns.json) separates
graph, ownership and canonical rendering responsibilities.

Picture sampling repeats each floating bilinear tap before clamping and gathering
its pixel. Rounded reciprocal multiplication can cross a period boundary before
the corresponding integer address does; the next tap cannot be inferred by adding
one to the first address. Text paints retain the enclosing reference, physical
font scale and fragment transform separately before native canvas composition.
Linear/radial/pattern fills and strokes share these stages; decorations retain
the logical map, and fitted glyph ink keeps its independent placement.
[Fragment evidence](../tests/Research/release-baselines/fragment-drawing-qualification.json)
checks the rounding boundary without changing final sampling or blending.
Source text boxes form physical float rectangles before mapping their corners.
Pattern tile origins join the local transform before float narrowing; picture-image
scale follows it. [Source evidence](../tests/Research/release-baselines/source-paint-qualification.json)
protects these stages across output densities. [Repeat evidence](../tests/Research/svgweb/pattern-repeat-addressing.json)
retains direct arithmetic vectors and the original glyph interactions.

Pattern registration, font discovery and resource selection share declaration
locations. Selection tracks rendered-group ancestry separately from general drawing
eligibility, so admitting a declaration also admits its required content. Known
foreign descriptions share one bounded, allocation-free subtree scan; stylesheet
collection and font discovery do not activate their contents. The first recorded
unsupported source survives later text rejection. [Traversal evidence](../tests/Research/svgweb/shared-traversal.json)
maps the mechanisms, prepared-font contract and independent rendering controls.
Attribute validators share an [inert event rule](../tests/Research/svgweb/inert-events.json)
across geometry, definitions, stylesheets, filters and span/path text. Values do
not enter paint, font or resource parsing; XML and structural accounting remain.
Non-executing script bodies share one bounded subtree consumer across preparation,
resource/font discovery and lowering. Retained text stores an empty marker instead
of script values, preserving lexical counts and surrounding text nodes. Text reads
use the same structural-limit object and absolute depth checks as ordinary traversal.
CSS selector syntax identifies absent interaction state before interpreting a rule's
declarations. Inactive rows keep only bounded rule bookkeeping, without selector/
value strings or enabling ancestor tracking. Mixed selector lists retain ordinary cascade
and declaration validation. Inline style readers share one diagnostic boundary;
stylesheet collection captures its opening element before consuming text.

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
Capture placement follows the parent bitmap's rounded drawing matrix even when
the draw has no transform chain. The [radial capture control](../tests/Research/radial-filter-capture/README.md)
protects that boundary with translucent paint and a nonzero blur.

Gradient opacity belongs to all prepared stops and the native factory, independently
of a sampled alpha. Opaque linear/radial programs reduce source-over to source before
coverage interpolation; genuinely conical and translucent programs retain source-over.
Full direct spans store source lanes, while A8 masks retain interpolation. Geometry
and glyph samplers share this material rule; hairline opacity can disable reduction.

Prepared-document font discovery borrows the same collected styles, reader
workspace and active budget used by lowering. Discovery control storage is
temporary; selected font bytes stay alive through the prepared operation.
The [XML admission policy](../src/Lokad.Svg/Parsing/SvgXmlReaderPolicy.cs) grows
that same owner before the BCL parses internal general entities. A streaming
classification pass counts declarations, literal sizes and possible references;
it stores neither names nor replacement text. An acyclic path uses at most the
declared entity count, giving a conservative expansion bound capped at the existing
XML character ceiling. Saturation admits storage rather than rejecting a document.
Declaration tables and overlapping UTF-16 value buffers retain separate coarse
allowances. BCL parsing validates names, references and cycles. Entity-bearing
inputs bypass raw-byte style/font/resource shortcuts; there is no expanded copy
or second resolver. [Entity evidence](../tests/Research/xml-entities/README.md)
separates mechanisms, ownership and browser rendering.
Source font/anchor intervals remain distinct from resolved physical font intervals.
Adjacent intervals selecting the identical face object and size share shaping;
paint, node and positioning boundaries retain their independent rules. The
[ligature control](../tests/Research/text-shaping-boundaries/README.md) protects
this separation and the original scalar-boundary validation.
Substituted glyphs keep explicit emitted UTF-16 source ranges through spacing,
filtered pair lookup and position resets. Ordinary one-scalar glyphs borrow the
text without allocating that scratch array; substituted runs charge and release
it during layout. The [qualified source ranges](../tests/Research/svgweb/text-glyph-source-ranges.json)
preserve the ordinary ligature fast path. Marked runs use a transient shared-cluster
map: source paint owners stay distinct from overlapping cluster ranges. Pinned
canonical decomposition/order, font-covered composition and selected `ccmp`
substitutions precede pair/context positioning and GDEF-backed attachments.
Default-ignorables remain context barriers until positioning completes. Late
zeroing preserves contextual execution without adding mark advances. Only the last
glyph consumes a shared source cluster during fitting. Attachment design offsets
share the existing font-size/fitting scale; no runtime shaping dependency is added.
The [canonical shaping evidence](../tests/Research/svgweb/text-canonical-shaping.json)
records mechanism, ownership and independent platform rendering coverage.

Native scalar `atan2f` can differ across Linux libm versions. Reference selection
uses an independent BCL scalar probe, with separately captured exact native
companions for path angles, source boxes and affected pattern images. The
[math-profile evidence](../tests/Research/native-math-profiles/README.md) preserves
the original references; renderer arithmetic and coverage remain unchanged.

Before relaxing a glyph-mask strike, transformed font axes select canonical
unhinted paths above the native 256-pixel bound. These paths use the original
unsnapped placement and existing analytic fill/clip/shader renderer. Smaller
glyphs retain platform fitting, mask preblend and subpixel rounding. The
[path control](../tests/Research/text-canonical-path/README.md) separates translucent
solid, gradient and clipped-gradient paint; it creates no large glyph bitmap.
Case projection resolves original SVG positioning lists before expanding text.
It lifts source intervals into emitted UTF-16 offsets once; shaping retains its
ordinary GSUB consumption and fitting model. Unchanged text borrows its inputs;
material conversion reserves temporary text/maps/arrays until layout has copied
retained data. Streaming and retained-instance font discovery share the pinned
scalar mapper. [Case evidence](../tests/Research/text-case-mapping/README.md)
keeps original addressing distinct from transformed browser observations.
Case context follows original scalars independently of whitespace chunk restarts.
Projection inspects following source characters; streaming font discovery keeps
one pending scalar/font binding for sigma, Turkic dot lookahead or Lithuanian
above-accent lookahead. Both routes use the same original-text predicates.
Retained-instance discovery uses the same text lifecycle. Unicode cased/ignorable
properties share a pinned lookup, including their overlap and possessive matching.
Capitalization tracks the last two significant word classes in that same context.
Pinned titlecase mappings affect only lowercase word initials; source positions
and unchanged-text borrowing use the existing projection. Word classes require
no pending text, new owner or general segmentation framework.
Deletion removes empty spans before font selection and layout. Source indices
still resolve positioning lists; deleted characters retain no absolute position
or rotation, while relative offsets reach the next surviving character. Text-node
owner identities and directly painted recording scopes survive projection. Each
placement range borrows its measured path, replacing a parallel measurement list.
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

Cullable dashed lines and rectangles retain owned source verbs and patterns until
their bitmap construction context is known. Source length calibrates `pathLength`
before phase-preserving culling and dash measurement. The inverse construction map
uses the narrowed matrix and bitmap origin; owned output interiors remain separate.
Thin strokes and wider outlines share this stage and keep their existing paint
paths. Glyph-strike filtering remains before canvas replay. The
[dash controls](../tests/Research/dash-image-width/README.md) retain independent
references and distinguish geometry, ownership and rendering responsibilities.

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
Mask walking intersects the complete path bounds with the bitmap clip, independently
of the row's owned pixels. Row storage and full-span temporary arrays are budgeted
separately; narrowing output storage must retain contributions from border pixels.
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

Global containment does not prove bitmap-local containment: subtracting the bitmap
origin before source mapping can move a touching operand just outside the clip.
Retained endpoints and controls establish that final check; flattened fallback
samples do not define the source drawing's bounds.

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

Ordinary isolated draws with retained canvas stages apply output placement before
the first recorded group. Unrecorded source placement and prepared text/marker
drawing retain their complete maps; filter-device stages keep their own contracts. The
[symbol canvas controls](../tests/Research/symbol-fractional-frames/README.md)
retain independent image references and native command operands for both cases.
Recording belongs to the isolated canvas independently of its solid or shader
material. Prepared text/fragment drawing is an explicit paint role; it keeps its
complete map while shader preparation retains its own ancestor stages.

Retained rectangle definitions and late source reconstruction keep native stroke
commands for solid and shader paints alike. Instance placement then applies to the
source stroke independently of fill material. The
[admission controls](../tests/Research/rectangle-shader-frames/admission-review.json)
retain original source/target identities and separate direct, group-inherited and
use-supplied stroke routes.

Complete instance placement decides whether source commands must survive lowering.
Rectangle fills and path/point-list fills and strokes retain requested commands
even when invisible; paint flags still suppress their output. Shader lowering
uses the same retention contract as solid geometry. The
[affine source controls](../tests/Research/affine-source-retention/README.md)
preserve the independent visibility, material and definition-order routes.

Collapsed element placement retains a nonpainting source with mapped object bounds
before inverse-dependent paint construction. Complete instance placement selects
source reconstruction; recoloring preserves the original recorded ownership role.
Enclosing effects can still use those bounds and generate output. Text retains its
shaped layout bounds and required caller fonts while skipping glyph paint, strokes
and decorations. Font discovery suppresses programs needed only by collapsed paint.
The [singular-map controls](../tests/Research/singular-source-maps/README.md) keep
these responsibilities separate from root-attribute and paint-server admission.

An unpainted source keeps its own opacity/mask/clip bindings inside its parent
effects. A synthetic empty-scope carrier keeps ancestor bindings around generated
output. These are explicit paint roles; bounds and pixel emptiness do not establish
effect ownership. [Empty-source controls](../tests/Research/filter-empty-sources/README.md)
preserve the distinct cases.

A fractional root image clipped by its integer viewport remaps the visible source
rectangle before residual image scaling. Keep both float stages even when the
snapped rectangles coincide; the recorded scene and logical layout stay separate.
The [source-rectangle evidence](../tests/Research/svgweb/root-visible-source.json)
links native matrices, rounding boundaries and a canonical transformed ellipse.

Root CSS box paint, SVG content and the image destination retain distinct maps.
Padding resolves on the CSS layout grid; content projects through the snapped
outer box after viewBox fitting, while its clip snaps separately in box coordinates.
Preparation and rendering share that map. Canvas background rectangle coverage
combines with image-boundary coverage before one solid-color store; constant-shader
rounding cannot replace it. The [layout and paint evidence](../tests/Research/svgweb/root-css-padding-layout.json)
links mechanisms, ownership, prepared text and independent native controls.
Root border paint sits inside source opacity and before content clipping. It retains
CSS used widths separately from SVG strokes; uniform frames, translucent side
unions and mixed-color opacity groups reuse the existing coverage and layer
compositors. The [border evidence](../tests/Research/svgweb/root-css-border-layout.json)
also distinguishes the integer image viewport from its fractional AA footprint.

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
Canvas coverage borrows a tape or analytic image/content rectangles. Successive
rectangles multiply their coverage separately even at coincident edges; geometric
intersection only narrows their enclosure. They share the existing clip rounding
and binary-region behavior without a full-surface tape.
Direct draws consume image-boundary coverage separately; isolated groups consume
it at restoration. Source captures clear coverage while retaining their enclosure.
Parameter-space filter captures enclose source visual bounds before graph expansion,
then enclose the capture hint. Object boxes, layer/device enclosures and sampled-image
bounds retain their own rounding. The [native stage observations](../tests/Research/svgweb/root-image-projection.json)
and [capture bounds](../tests/Research/svgweb/filter-transform-review.json) distinguish
these roles independently of equal final images.
Capture hints use font recording enclosures and retained local stroke extents.
Mapping an ink enclosure back from scene coordinates changes the source bitmap
origin and subsequent clipping and image sampling. The existing font and stroke
regressions protect this [shared bounds contract](../tests/Research/svgweb/filter-recording-bounds.json).

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
Axis-aligned bitmap sampling restarts coordinates at native rectangle-call and
coordinate-buffer boundaries; equal coverage bytes can still cross such a boundary.

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
Text clips borrow retained text and the existing font dependency index. A live
picture keeps ordered geometric/text sources, child intersections and measured
visual bounds until target replay. Source geometry opacity and use-instance
opacity remain separate; geometric path unions ignore paint opacity. A painted
picture may need an invisible text child's layout bounds, while an entirely
inactive picture needs no font. Deferred mask and clip admission share the scene
source-presence scan. A group's own opacity restores after its clip, independently
of descendant effect isolation.

Mask text keeps font construction in the resource context, independently of the
consumer's replay map. Its own child transform still selects the font scale;
geometry retains complete target construction. The parser scopes and restores
this borrowed map through existing text preparation. The
[mask text controls](../tests/Research/mask-text-recording/README.md) distinguish
those placements from clip-mask coverage and protect following text context.

[Filter playback](../src/Lokad.Svg/Rendering/SvgFilterPlayback.cs) retains result
dependencies, primitive regions, operating spaces and source placement. Requested
output, native image hints, captured content, cropped storage and padded scratch
are separate. Descendants inside nested filters retain their original paint bounds
in the parent picture's image hint and dither origin; parent capture separately
includes their filtered output. Rescaled kernels read the captured subset;
offsets/aliases can retain deferred sampled images until a
consumer requires pixels.
Deferred storage includes the input view needed before the offset as well as the
final output. Request planning limits that view by primitive crops and possible
image extents; planning and playback share alias resolution.
[Storage regressions](../tests/Research/svgweb/filter-deferred-input-storage.json)
protect those bounds without duplicating destination or ownership matrices.
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
Capture hints also retain recorded AA fringes before the propagated request crops
source ink. A tighter SVG layout hint cannot discard those recorded pixels.
Content clips limit painting without moving the source bitmap's shader coordinates
or dither origin, including when compact capture omits part of its transparent border.
Isolated children retain that logical bitmap origin independently of their storage
window and use the same scope for geometry and shader construction. The origin can
lie outside compact storage; scan windows and owned interiors remain bounded.
SourceGraphic's nearest shader retains picture padding separately from its result
extent. SourceAlpha and stored graph results sample their materialized image views.
Spatial consumers retain the source picture's complete clip-classification domain
when compact storage omits a viewport fringe. Source aliases and deferred color
filters retain their parent paint clip. Classification never indexes omitted pixels.
The [source-context controls](../tests/Research/filter-source-contexts/README.md)
protect thin rectangles and wide strokes in nested viewports through these stages.
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
Non-scaling strokes retain local visual bounds without enlarging object boxes;
hairline width reduction keeps the native canvas/inverse product and its rounding.
The [context controls](../tests/Research/shader-contexts/README.md) protect these
stages through instances, symbols, clips, masks, filters and isolated opacity.
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
An existing compositor bitmap still enters compact source storage before capture;
retaining its origin alone can displace or erase later-tile source drawings.
[Source-origin controls](../tests/Research/filter-source-origins/README.md) cover
compact storage, second-tile solid/gradient construction and tile crossings.
Drawable recording bounds enclose locally before placement; filter-output hints
enclose in parameter space. Their union seeds the source image and dither origin.
[Gradient controls](../tests/Research/filter-gradient-sampling/README.md) protect
both source aliases and a general color stage without rounding object bounds.
Fractional offsets use the same per-bitmap filter path as other graph stages.
Final transferred paint composes directly into its destination; an extra byte
bitmap would consume floating colour and clip/opacity stages too early.
[Destination controls](../tests/Research/filter-bitmap-restoration/README.md)
cover operating space, bitmap size, opacity and clipping across a tile boundary.
Terminal offsets retain compatible colour programs through image sampling and
later destination coverage, including trailing zero-offset, zero-blur and single-input
merge aliases. An immediately
preceding matrix skips unused byte evaluation when that terminal draw is its sole
consumer. Existing final sample storage preserves fractional lanes without a new
replay or resource owner.
[Coverage controls](../tests/Research/filter-color-program-coverage/README.md)
protect rounding order and both operating spaces.

Color-matrix parameters are immutable values in the existing filter graph. A
borrowed color program retains individual transfers and clamps until image sampling;
it borrows scene coefficients and adds no resource owner or replay pipeline. Native
unorm loads precede straight-channel evaluation and premultiplication. Matrix chains
do not introduce byte images between compatible stages. Uniform inputs retain their
original floating paint rather than reloading quantized flood pixels.

When another sampling operation consumes a pending draw, its placement operands,
image map and finite source view remain distinct. A resolved view is not expanded
as a capture-grid hint. Color operations precede image-edge coverage; alpha bias
can extend output to the primitive region. [The matrix controls](../tests/Research/filter-color-matrix/README.md)
protect these stages, including both orders of blur, composition, aliases and crops.

## Supplied fonts

Shared bounded layout preserves source text/span ranges, font/paint selection,
positioning and admitted shaping. Reader chunks add no boundaries. Comments can
preserve shaping; element boundaries may select distinct scaled fonts. Ligature
prefixes retain their source items even without their own glyphs.

Cursor layout applies relative offsets, fitting, absolute positions and anchors.
Chunk anchors measure extrema of positioned advance edges, including backtracking.
The shared inherited style retains anchor changes through font-interval compaction
and [default-interval elision](../tests/Research/text-shared-anchor/README.md).
Only absolute chunk boundaries receive a scoped anchor; changes within a chunk do
not split shaping. Borrowed position storage is copied only when its chunk metadata
needs an override. [Mechanism and rendering evidence](../tests/Research/text-anchor/README.md)
keeps these responsibilities separate.
Spans and text paths share one child-style resolver in extraction and font
discovery. Reference syntax remains separate from presentation: path placement
receives the computed anchor after CSS precedence, even when the resulting font
interval matches its parent and is elided. Inapplicable path coordinates do not
become span position lists. [Independent path controls](../tests/Research/text-path-styles/README.md)
retain cascade, nested-font and mixed-run interactions without changing layout math.
Fragment origins and anchor shifts truncate separately. Text boxes join fitted
ink and physical source rectangles; drawing enclosures remain local. Mixed
direct/path text shares parent shaping storage, with scoped views owning only
rebased fragment arrays. Unpainted characters may still contribute layout bounds.

Windows uses matched TrueType/LCD fitting, scanning and preblend; Linux uses light
fitting/grayscale coverage, with axial and unhinted routes chosen separately.
Bounded conic equations retain a strict sampling-center bias after axis
normalization; straight borders keep directed contour ownership. The
[conic-center controls](../tests/Research/glyph-conic-centers/README.md) separate
that numerical boundary from colored glyph paint and black clip-mask coverage.
Windows alignment translates current/original outline points with the left metric
origin. Current advance rounding stays separate from the original absolute right
coordinate. Composite parent programs use their own phantoms, while inherited
child metrics still select the completed paint origin. [Native readouts and masks](../tests/Research/svgweb/windows-phantom-origins.json)
protect both handoffs independently of text shaping and fitting.
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
