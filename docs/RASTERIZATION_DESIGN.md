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
Open groups retain their deferred clip/mask indices and close those entries directly.
Gradient and pattern templates wake their dependents when ready. Deferred paint,
text and tile-use commands replace their placeholders in batches; existing scope
boundaries and scopes created during replay map separately into final draw order.
Deferred paint and text replay emit into their recorded scenes and map only
their retained source ranges. Tile paints, text and uses expand together after
definitions resolve and before pattern group binding;
clip-only markers compact once afterward.
Gradient definitions retain original vectors separately from local paint maps;
aliases resolve their attributes before mapping. Scene-owned radial metadata also
retains focal declaration presence/units. Vector-free aliases share it while
inheriting the local map.
Expanded tile paints, including glyph shaders, are validated before scene sealing.
[Document preparation](../src/Lokad.Svg/Parsing/SvgDocumentPreparation.cs) shares
reader workspace and collected styles across sizing, output mapping and scene
lowering. Metadata traversal consumes the same work allowance as conversion;
font acquisition retains its separate discovery lifetime.
Font-bearing pattern selection shares the existing bounded source index with
font discovery and scene lowering. Consumers determine which tile sources are
needed; masks and markers retain declaration context while local uses inherit
their instance context. Alias headers remain in the pattern index. Font-free
patterns keep the header-only path; source visits share work and cancellation.
Rendered leaves prepare their cascaded declarations once and reuse that value
through deferred paints, filters, markers, recording and geometry lowering.
Local uses share one inline parse and authored style across reference, resource
and retained-group preparation, preserving their existing admission rules.
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

[Non-scaling drawing bounds](../src/Lokad.Svg/Parsing/SvgNativeVisualBounds.cs)
retain native source-dimension clamping, integer recording bounds and physical
viewport projection. Resource pictures use local visual bounds without document
drawing-record culling. Culled drawings keep inert anchors for object-box resources
and filters that can generate pixels from transparent input.

Sizing inspection and conversion share root CSS/unit resolution.
Opt-in raster bounds choose one uniform output scale before lowering, leaving
the logical viewport and percentage bases intact. Logical picture font sizing
precedes the separate native raster strike transform. Integer canvas rounding
does not enlarge the physical viewport clip, including a minimum one-pixel canvas.

[Shader coordinates](../src/Lokad.Svg/Rendering/SvgShaderCoordinates.cs) preserve
separate local, reference and bitmap mappings. Gradient, image and glyph consumers
round at different native stages; combining their arithmetic can move samples or
change dithering even when the real-valued transform is equivalent.
Vector patterns rasterize one source image. Outer compositor tile boundaries
do not split that paint device or reset its shader/dither origins.

## Paint and compositing

Resolve inherited stroke paint before selecting a leaf's lowering route. Path
gradients bind to the undashed centerline's native curve bounds, including open
paths with no fill; flattened fill bounds and stroke extents are separate.
Resolved empty servers affect only their own element-local paint. Recording and
lowering share that availability, while unknown resources retain deferred lookup
and authored values retain validation. Empty text strokes emit no outline: an
invisible outline can enlarge a native layer and shift another paint's bitmap origin.
Point lists keep native source floats and authored fill verbs through solid and
shader lowering. Coordinate spelling does not select rectangle-band coverage.
Pattern fills and strokes share a definition-owned replay scene; rectangle content
must retain its target map before instance and viewport placement.
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
source bitmap's coordinates. Conical visibility masks precede ramp spreading and
survive single-color replay. Uniform ramps need no segment array; their color
also survives nonfinite tile coordinates at the cone boundary.
Linear and radial spreading share native float operation order, followed by
absolute stop padding and ramp normalization. Partial stop ranges preserve the
full repeat period; see the [periodic controls](../tests/Research/linear-periodic-stops/README.md).
Normal ramp preparation and glyph representative colors share a borrowed,
bounded canonical stop sequence. It inserts implicit endpoints, detects nearly
uniform float positions and canonicalizes duplicate groups. Uniform coefficients
use fixed gap counts; arbitrary coefficients preserve constant terminal padding.
Flat ranges retain their first/last boundary colors. The
[stop controls](../tests/Research/gradient-stop-preparation/README.md) include
native glyph and 256-stop references. Factory means retain original source stops.
Stop paints retain source alpha separately from byte colors. Native numeric
fast parsing and full functional-color parsing preserve different precision;
RGB/hex byte division and HSL fast-path normalization also remain distinct.
The [color-alpha controls](../tests/Research/gradient-color-alpha/README.md)
cover spellings, byte midpoints, glyphs and resource replay.
Nonconstant cone padding retains terminal arithmetic; normalized shader lanes
clamp before coverage and source-over, including unordered native float values.
One-stop paints retain their shader identity and dithering. Native constant
factories are classified from definition coordinates before placement, using
the inclusive float threshold `1/32768`. Pad takes the terminal stop;
repeat/reflect integrate straight RGBA over stop intervals, then premultiply.
Their constant-paint replay is shared by rectangles, paths and glyphs, including
glyph luminance. See the [factory controls](../tests/Research/gradient-factory-colors/README.md).
Images retain their own sampling matrix, clipping
and alpha rules. [Pattern tiles](../src/Lokad.Svg/Rendering/SvgPatternTileCache.cs)
reuse bounded scene rendering and repeat sampling. Optional tile retention needs
matching source, maps, sampling and paint; declining it preserves rendering.

## Resources and effects

Viewport clips draw into the parent bitmap through owned clip frames, preserving
its shader origin. Inherited clipping around filter output keeps a separate
recorded region kind so it does not change the filter input context.

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
Filter sources preserve native rectangle coverage and layer-bound rounding.
Object-box output clips index balanced source ranges once before measuring targets.
sRGB and linear-RGB image sampling retain their distinct precision. Resource
clips enclose group isolation, applying coverage once when opacity restores.
Ancestor clips also surround child filter output; source-local clips stay inside
the filter. Balanced group ranges preserve that order in direct and pattern
scenes. Offset captures keep source ink separate from image hints, and each
compositor bitmap supplies its own clipped output request. The
[ownership controls](../tests/Research/filter-clip-ownership/README.md) retain
independent native evidence for those paths.
Solid paint and inherited currentColor retain validated source alpha through
shape, instance, mask and glyph construction. Text element opacity encloses the
ordered fill, stroke and decoration commands, including deferred paint slots.

Uniform filter paint retains source alpha and pipeline precision until sampled.
Deferred uniform images retain their finite coverage and known texel independently
of compact storage. Affine frames identify their stored color space so final
transfer follows sampling. Virtual SVG output bounds and sampled source-image
bounds round in their respective coordinate systems; output admission keeps
replaced source geometry from enlarging an opacity layer.

## Supplied fonts

Rendering consumes verified immutable font bytes supplied by the host. Shared
bounded layout preserves text/span order, metrics, positioning, admitted shaping
and paint. Composite replay retains component placement and metric origins.
Chunk anchors use the extrema of positioned advance edges, including backtracking.
Ordinary fragment origins and anchor shifts truncate separately; merging those
steps loses half-grid middle anchoring. Zero shifts avoid a redundant glyph pass.
Windows uses matched TrueType/LCD coverage and preblend; Linux uses matched Latin
light fitting and grayscale coverage. Exact references are platform-specific.

Text paint boxes use canonical fitted ink and source fragment rectangles before
paint-origin truncation. Windows measures nonzero raw LCD coverage at the contour
perimeter; Linux rounds light-path bounds outward. Local budget-owned metric reuse
avoids refitting repeated glyphs across parser runs, and all paints in a run share its measured box.
Fragment transforms and inverse font scaling preserve native float operations.
The extracted-run owner retains normalized text-node boundaries and element
identity for fragment metrics. Comments preserve shaping; element changes stop
it when native screen font scaling makes the compared fonts differ. Reader
chunks create no boundaries. Fitted glyphs compose truncated physical origins
and stretching at float precision, including zero-angle text paths.
The existing fragment owner also retains each item's UTF-16 source range,
including nodes with no glyphs and prefixes consumed by a preceding ligature.
Cursor layout applies relative offsets, fitting, absolute positions and anchors
in that order. Source boxes retain physical float coordinates. Paint truncates the
final anchored rectangle; baseline positions and inverse-scale displacements retain
their metric precision. Natural fitting bounds are computed only for fitting requests.
Ordinary text drawing rectangles enclose source bounds before placement/output
mapping, preserving isolated-layer shader origins. Equal run paints share one
scene record; generic font routes and filter capture bounds remain independent.
Path source maps retain original coordinates before paint truncation, glyph stretch
and output placement.
Mixed direct/path text shapes once. Scoped glyph views borrow the parent storage
and own only rebased fragment arrays; the parent outlives every view. Source ranges
select direct/path metric and anchor rules without dividing shaping clusters.
Object-box paints measure the whole element before emission, retaining invisible
source boxes while preserving local drawing enclosures. Deferred own-filter
wrappers move only their appended command slots and clip/mask ranges.
Path character widths include fixed-point spacing before fragment rounding;
kerning stops at spacing changes. Signed advances position characters while
rotated fragment rectangles clamp their painted width to zero.
Source boxes retain fractional character widths after `textLength` scaling.
Deferred text and instances retain their enclosing filter recording context;
glyphs and decorations record source bounds before filter construction.
Nonpainting effect anchors contribute object bounds without enlarging painted bounds.
Path glyphs compose general parent poses; the translation shortcut requires unchanged axes.
Zero-angle path glyphs retain the same fragment-origin truncation as rotated glyphs.
Glyphs and decorations share enclosed source drawing bounds and recorded paint context.
Straight paths retain scalar source queries separately from fitted glyph placement.
Rectangle fast paints intersect their iteration bounds with the current isolated layer.
Glyph caches retain the recorded font size and relax the remaining device matrix separately.

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
