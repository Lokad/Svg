# Support matrix

## Current state

The renderer is a narrow prototype. It supports only the explicit subset listed
below, and the accepted-input contract gaps recorded in [PLAN.md](../PLAN.md) remain
open. Do not infer that every unlisted visible SVG construct is rejected correctly.
At the legacy conic subdivision checkpoint, the 1,231 manifest entries contain
1,145 exact decoded-pixel references, 62 known-difference renders and 24
categorized rejections. These are fixture entries, including variants, and do not
establish corporate-document coverage.

Painted integer paths with diagonal edges retain directed authored contours,
including touching/coincident subpaths and even-odd crossings. Linear/radial
gradient and pattern paths retain whole fill contours and scene paint boxes.
Solid orthogonal paths keep checked integer bands. Authored straight replay uses
command-bounded scratch; rectangle mask and additive-run boundary bytes differ.
Original independent controls remain exact; broader fidelity is still open.

Authored fills retain source contours once for bitmap-local mapping. Float
translation moves into the bitmap before source point/control mapping; clipped
edge construction and vertical combination stay local, then kept fixed-point
walk/curve positions translate together. Raw-control raster bounds retain
extrema missed by flattened samples. Solid, linear, radial and pattern fills
share this preparation. Quadratic recurrence starts use snapped Y; clipped
rational curves enter bitmap coordinates before subdivision. The original native
legacy expression and finite-output fallback match observed browser circle
coverage; its browser build define remains unconfirmed. Broader shape,
transform, stroke and paint fidelity remains open.

Declaration-only DOCTYPE admission applies to bare, SYSTEM and PUBLIC declarations
before the root. External identifiers are inert; internal subsets (even empty),
duplicates and malformed syntax remain `MalformedInput`. No DTD resources,
declared entities or defaults are loaded. Original byte encodings and structural
limits are preserved across root parsing, font discovery and scale probing.

Inline/stylesheet declarations and presentation attributes admit `marker`,
`marker-start`, `marker-mid` and `marker-end` only with `none`. Shared validation
also admits default `enable-background:accumulate`; the existing root attribute
syntax retains its validated `new` hint. Active markers and backdrop filtering
remain unsupported. `overflow:visible` is accepted on non-viewport targets;
root/nested SVG viewports retain hidden clipping and reject visible overflow,
including matched stylesheet declarations. These admissions add no painting.

The complete frozen corporate matrix has 258 rendered comparisons: 234 exact
and 24 known differences, with zero renderer rejections. Rede Manchete, Tokyo
Tsukurou and Pepsi become exact in every variant: **23/27 documents**, marks
**11/13**, wordmarks **12/14**. All 225 earlier exact PNGs remain. Original inputs,
options and browser references stay frozen. S17's numerical minimum is met.
Representative Windows performance, local Windows/Linux delivery and the full
fresh Linux browser matrix are validated. Linux has the same exact/known counts
and document numerators; remote CI remains open.

Concentric general radial paints retain separate shader-local and referencing
matrices. The drawing CTM moves into bitmap coordinates before local-gradient
concatenation, inversion and unit-circle mapping. Retained parser/scene paint
storage is reserved before allocation and transferred through clipping/masks.
The existing focal/conical fallback remains. Native numeric vectors and six
independent browser controls verify this order; broader paint fidelity remains
open.

Eligible authored fills preserve directed winding and retained curves without
scene contact surgery. Root/group placement composes before float rounding;
scene bounds and paint coordinates remain. SVG endpoint arcs retain native
float rational conics; wider path arcs construct native quadratic offset outlines.
Eligible solid/dashed path hairlines retain conics. General event, coordinate
and clip/paint fidelity remain open.

Solid path strokes retain centerlines, closure and width maps. Dashed paths use
bounded native curve measurement and cuts, with retained conic weights, zero ON
cap dots and closed-seam continuation. Twelve new thin dash controls are exact;
six wider controls keep their known differences. Transformed/pathLength dash
browser coverage remains open.
Paths up to one device pixel replay lines, quadratics, cubics and endpoint-arc
conics through native fixed-point hairline coverage, ordered paints and
butt/square/round cap extension. Twelve new thin curves match their stable native
references; six wider controls retain their known differences. At that checkpoint,
every previous fixture/research image and all 258 corporate outcomes/PNGs were preserved.
Generic wider paths and integer/fractional polylines share native curve offsets,
joins/caps, closure and directed retained outlines, including dashes and painted
instances. Bare movetos paint nothing; non-scaling dashes keep host-coordinate
lengths. Full stroke/paint/clip fidelity and other hairline contexts remain open.

Circle/ellipse strokes fit normal offsets of original conics with retained
quadratic verbs, including an inverted inner offset for very wide strokes.
The existing positive uniform-scale and ellipse-rotation admission limits remain.
Native-stroke quadratic coverage consumes retained controls even when fallback
flattening has one chord. Linear/radial/pattern `use` copies preserve and reserve
curve spans. All twelve fresh oval controls and all nine SG Rotation variants
improve but remain inexact; all prior exact PNGs and dispositions are preserved.
Remaining generic stroke scan/paint residuals and sampled shape routes remain open.

Straight solid open strokes retain native float normals, endpoint miter replacement
and pivot inner joins through directed nonzero winding. Intersecting rings cannot
select convex fill. Bevel joins are admitted on polylines through the same kernel
as paths, including direct/inline/inherited styles and existing dash construction.
Circle/ellipse fills and native straight solid open-stroke round caps, dots and
joins retain rational verbs through final float mapping. Device-space subdivision
reuses quad clipping; convex walking advances live state with native smoothing.
Native nonzero stroke contacts retain signed walking, including reversals and
self-touches; intersecting rings cannot select convex fill. Four original probes
and three fresh reversed-join configurations become exact. All 837 prior exact
fixture PNGs and 70 exact corporate PNGs are preserved. Every Rede Manchete
variant improves at that checkpoint. The width-one cap probe then worsened
15 → 16 differing pixels; the subsequent hairline repair makes it exact against
the same original reference. Broader fractional events, curved/dashed overlap and clip/composition fidelity
remain open. Ordinary fill/contact gates retain their scope.
Bitmap-local edge clipping and float root mapping preserve all nine exact Artsy
variants.

Straight dash endpoints now retain native float interpolation. Nonzero orthogonal
contour sets without retained spans use signed-winding analytic walking through
crossings and coincident edges, preserving the one-byte ROC fringe previously
lost in geometric fallback. Classification is cached and charged once. All nine
ROC variants and twelve fresh controls are exact; every prior exact PNG is
preserved. Curves, self-touching contours and coincident even-odd sets retain their
separate gates. General stroke fidelity remains open.

Actual rectangles retain original fill edges and separate paint clips through
viewport, affine, symbol and instance mappings. Solid materials use round/255
premultiplication before rounded native 24.8 shape coverage. Gradient/radial/
pattern bands preserve the same clip, including pattern content and solid stroke
bands. The 960 native pixel vectors and 36 new browser configurations establish
thirty exact cases and six known shader quantization differences. The original
material grid and translucent border band become exact; all 669 previous exact
PNG identities are preserved. All nine IBM variants improve but remain inexact.
Successive clip-alpha intersections, cache history and general paint fidelity
remain open.

Rectangle and polygon gradient/pattern samples retain premultiplied float lanes
through coverage and source-over, with one final byte conversion. Gradient stops
retain source stop-opacity through clamping, style and href resolution while CSS
color alpha keeps its byte encoding. The browser distinguishes `stop-opacity=".5"`
from `rgba(...,.5)`; all six fresh alpha comparisons are exact. Eighteen existing
fixtures and one research probe graduate with their original images. Twenty-four
fresh configurations add twenty-one exact and three known notched-pattern cases.
All 735 prior exact fixture PNGs and 66 exact corporate PNGs are preserved;
27 SimpleLogin/SDL/Liputan 6 variants improve but remain inexact. The corporate
rate stays 3/27. Gradient coordinates/interpolation, notched edges, successive
clip/cache behavior and general shader fidelity remain open.

Gradient dither cells now use the owning software bitmap's local indices while
paint samples stay in canvas space. Original material-pair controls become exact;
large linear/radial probes improve to one/four differences. Twelve fresh tiled
configurations add nine exact controls and three radial cases with four RGB-only
differences each. All 775 prior exact PNGs remain unchanged. Six intended/large
SDL variants improve; corporate exact documents remain 3/27. Coordinate
construction, remaining geometry/clip events and full cache behavior remain open.

Linear and centered radial coordinates now use native float unit matrices,
bitmap-local seeds and affine inversion. Gradient-transform numbers share the
bounded float reader, work counter and cancellation owner; legacy RGB and
gradient paint opacity normalize before fused interpolation. Seven original
configurations graduate, with the research ellipse transferred using its original
assets. Twelve fresh configurations add nine exact controls and three geometry
differences. All 787 previous exact PNGs and all 258 corporate outcomes are
preserved. All nine SDL variants improve; corporate exact documents stay 3/27.
Conical/multistop sampling, wider mapping, fractional geometry/clip events and
full cache history retain separate gates.

Convex rectangular strips round coverage separately from the raw-rectangle
calibration. Native event evidence closes YouTube's opaque triangle witness;
thirteen fresh configurations become exact and one shader control retains known
paint differences. Rectangle clip bytes now close the remaining YouTube boundary
differences: all nine variants are exact against their original references.
Twelve new nonrectangular path configurations are exact across horizontal,
vertical, corner, opacity and nearly integral clips. Integer orthogonal `path`,
`polygon` and filled `polyline` now retain their contour and separate AA paint
clip instead of clipping rectangle bands during lowering. All twelve original
path-band controls and eighteen new scaled box/notch configurations are exact.
All 628 prior exact fixture PNGs were unchanged at that checkpoint. The newer
rectangle repair above retains paint clips across actual `rect` and paint-server
bands; general clip stacks and shader quantization remain partial.

Single clean retained contours now distinguish raw control-polygon convexity
from sampled-ring convexity. Float sign/turn/reversal classification is charged
once and cached across raster contexts, preserving genuine convex routes and
declined-hint fallback. The 75 native vectors match; twelve independent controls
and one existing fractional shoulder become exact. All nine Ford variants retain
exact PNG identity. Ford and YouTube completed both selected checkpoints;
at that checkpoint the corporate matrix had 240 fidelity comparisons and four
verified exact documents. All eighteen then-rejected variants had independent references.
General mapped curve/paint events remain open.

Rectangle paint-clip classification is independent of construction clipping and
path containment. Native float nearly-integral/edge-snapping rules select binary
or antialiased paint contexts; the latter retain additive run storage and ordinary
fringe-pair forwarding across solid, linear/radial-gradient and pattern paints.
Per-boundary provenance preserves geometric origins through intersections,
reflections and reference placement; mask allocation regions contribute no AA
paint edge. The 188 native predicate/setup vectors and 41 independent browser
configurations retain twenty-one exact, eleven known-difference and nine existing
UnsupportedFeature outcomes. This bounded proof does not cover complete native
AA masks, successive clip-alpha intersections or all cache histories. General
clip-byte composition and curve/layer pixels remain partial.

Analytic coverage reserves complete live-edge objects, list growth and retained
curve expansion before allocation. Cubic forward-difference positions evolve
independently of walked X, with a separate displacement preserving the current
quarter-grid pixels; fractional endpoint fidelity remains implementation work.

Eligible native full-canvas solid rectangles preserve source fill-opacity for
the pinned browser's bounded float paint-analysis route. Paths, generated bands,
paint servers, opacity layers and scenes beyond its five-draw limit keep their
existing raster routes; opaque recipe-3 page backgrounds consume two draws.
The 80 new routing configurations include 79 exact controls and one retained
group-opacity-white difference. That bounded rectangle repair establishes no
general alpha/layer fidelity; Ford's later paint progress is recorded below.

Eligible analytic fills now preserve ordered direct single/pair/span paints
separately from pending additive runs, with the native bounds-selected mask/run
route and operation-specific uniform/shader arithmetic. All 12 original dispatch
controls are exact. A further 24 material configurations bank 15 exact and nine
known differences. Float capture conversion and compact full-span storage retain
bounded ownership/work and Default admission. At the paint checkpoint, Ford
improved in all nine variants but remained inexact; circle/stroke residuals include
mixed alpha-error changes.

Run walking now uses a scan window distinct from path storage/dispatch when the
complete raw path and retained controls fit inside the clip. The mask route keeps
its path window. Eighteen independent phase/scale/clip/material configurations are
exact; five research controls graduate with unchanged references. At that checkpoint all
nine Ford PNGs were unchanged. The later clip-built construction below preserves
that boundary; general clipping remains open.

Retained cubics now enter the existing live/pre-stepped paths even when their
flattening has only one chord; quadratic gates stay separate. Every cubic setup
attempt, including zero-height pieces, is charged within existing complete-span
reservations. Twelve independent phase/scale/material configurations retain eight
exact and four known differences. All prior fixture/research PNGs are unchanged;
six small/intended Ford variants improve and the three large PNGs are unchanged.
At that checkpoint intended-transparent retained 433 differing pixels; general cubic event fidelity
and the all-nine wordmark exit remain open. Default inventory renderability is
preserved; ten changed non-Ford outputs lack matched oracle evidence.

Shared clipped-edge preparation now uses the independently verified line/quad/cubic
kernel when complete path/control bounds escape the canvas or explicit clip.
It clips every retained eligible verb, preserving X/Y monotonic splitting,
projected winding and empty pieces. Two charged stack-scratch passes size and
construct complete edge storage under existing limits. All four paints share the
boundary; fractional clip coverage remains per operation. Mask walking intersects
construction bounds with path storage, while run walking uses construction bounds.
Existing contact/retention gates remain; this does not admit new syntax.

At the clipped-preparation checkpoint, all six small/intended Ford variants became
exact; large variants improved but retained 4,900/4,956/4,404 changed pixels.
All nine original references and current candidate
identities are portable regressions. All 493 prior exact fixture PNGs remain exact.
One predeclared clip configuration and the subsequent fractional clipped-stroke
witness are exact; the star-stroke witness remains a known difference. Twenty-six
clip configurations retain failures, including the quadratic phase's increased
alpha error. The updated bank retains both directions and every rejection.

A traced follow-up records one CPU bitmap tile for small/intended Ford and eight
for large Ford. Bounded CPU raster contexts now carry bordered construction clips
and exclusively owned interiors through all four paints. Whole-path mask/run
dispatch stays separate from bitmap-bounded row storage, and the converter closes
the original row-6 alpha witness. The six exact Ford PNGs retain their identities;
at that checkpoint large transparent/white/black variants improved to
929/1,245/560 changed pixels,
with transparent total alpha error 3,573. The all-nine wordmark exit remains open.
Sixteen context controls retain four exact results, eleven known differences and
one UnsupportedFeature rejection. Twenty-nine changed Default inventory outputs
lack matching captures in that sweep; their fidelity is unmeasured. Actual native
authoring, tile-local mapping and full-image paint parity remain work in PLAN.

Horizontal clip wrappers now forward direct fringe pairs as ordered ordinary
runs. Complete path bounds include retained controls; vertical-only clipping and
contained paths preserve specialized pairs. All fifteen solid configurations of
the 18 predeclared controls are exact, while three shaders retain differences.
Seven fixture failures and one research probe graduate with unchanged references.
The six exact Ford PNGs are preserved; large differences fall to 174/174/159 and
transparent alpha error to 2,835. Two quadratic clip controls gain three alpha-error
units each; 26 changed Default inventory outputs lack matching captures in that
sweep. Those failures and evidence gaps remain explicit. General curve/event,
convex/quad/stroke, clip/mask and layer fidelity remains open.

Path numbers and transform arguments now share independently verified float
reading; relative/smooth path updates use float lanes, while general affine
computation keeps doubles and its existing gates. Thirty-one browser numeric
vectors, all 436 captured Ford points and the actual large CTM are reproduced.
Proper crossings in separated multi-contour even-odd fills keep their directed
contours and zero signed-area lobes. Exact terminal axis adjacency preserves raw
points and closes an `inv-040` admission regression; single-ring split/admission
and other contact gates remain. All 12 parity/retrace controls are exact; the full
30-configuration bank also retains 12 known differences and six rejections.
All 660 previous fixture PNGs and every existing render/reject disposition are
preserved. Ford keeps six exact variants and improves large variants to 10/10/7,
with transparent alpha error 16 at that checkpoint. The density repair below
now closes the all-nine Ford exit; general corporate fidelity remains open.

Intersection-density dispatch now uses the raw points represented by admitted
retained curve verbs, with ordered-range validation, charged cancellation-aware
traversal and a borrowed per-fill cache. Declined hints preserve flattened counts;
clipping does not change whole-path point count. All nine original Ford variants
are exact, preserving all six prior exact PNGs. The independent 24-configuration
bank retains 15 exact controls, three known differences and six categorized
rejections. No syntax gates or resource limits expand. General affine/instance,
fractional events, quad/convex/stroke and layer fidelity remain explicit limits.

R36 line dashes also admit presentation attributes, inline styles and stylesheets on the line, with group and root inheritance rendering identically to direct attributes.

Stroke widths flow through the same cascade: presentation attributes, inline styles and matching stylesheets on the shape plus group and root inheritance render identically to direct attributes, with negative widths still rejected at lowering (zero widths paint nothing for solid and paint-server strokes while positive fractional widths lower through the shared outline kernel; line/rect-band and paint-server band strokes keep integer discipline). A width with no stroke paint anywhere renders fill only on rectangles and circles. Stroke paint inherits through group and root containers to shapes without their own (explicit none blocks it); paint-server strokes keep their dedicated rejection except at zero width, where nothing paints and the fill renders alone; text with painted strokes stays unsupported before font acquisition. Miter limits flow through the same cascade with sub-one values still rejected at lowering. Stroke opacity multiplies stroke alpha on solid strokes while paint-server strokes stay opaque-only. Path lengths flow through the same cascade with the single lowering-time validation unchanged. Stroke linecaps and joins flow through the same cascade with per-shape lowering enforcement unchanged and invalid values still rejected at parse.

| Area | Status |
| --- | --- |
| Public options, limits and failure categories | Initial contract and categorized public failures implemented; current rectangle raster work is preflight-bounded; polygon fills charge mapped vertices up front with setup and coverage by taken traversal (analytic rows cost pixels, fan keeps pixels-times-points). Lowering and font-request discovery both reserve the greater of 16 KiB, four times input bytes, or 512 bytes per configured XML-attribute limit before `XmlReader` construction; it is an accounted renderer allowance, not a process-RSS cap or exact BCL allocation measure |
| Root SVG parsing | Bounded validation counts every element including the root against `MaximumNodes` (matching font-request discovery) and rejects XML nesting beyond the configured `MaximumXmlDepth` (128 by default) or more than `MaximumAttributesPerElement` attributes (256 by default) before this parser consumes them; inert `id`, title/description/metadata and unused definitions (container-wrapped definition content is explicitly unsupported rather than painted); empty foreign-namespace editor elements are ignored while non-empty foreign content stays explicitly unsupported; self-closing rectangle, supported axis-aligned-line, or polygon/polyline/path/circle/ellipse fill definitions in `id`-only-or-attribute-free direct or group-nested `defs` (link-nested `defs` stays explicitly unsupported), or local `symbol` definitions (with optional `viewBox`/`preserveAspectRatio`, containing exactly one such self-closing geometry) can be instantiated by direct or group/link-nested local `use`/`xlink:href`, including forward references, with definition-local geometry (definition ancestor transforms do not move instances; transformed definition attributes apply exactly once); group-targeted uses un-bake transformed source ancestors through the ancestor inverse (solid polygon/axis-aligned rectangle, line and band content; viewport-scoped, singular, stroked-rectangle, minified-curve, glyph and paint-server source content stays explicitly unsupported); `switch` renders its first child without a `systemLanguage` attribute, evaluating selection against an empty locale set, `x`/`y` placement, bare-number, percentage or omitted-`100%` `width`/`height` (omitted dimensions and percentages require known parent dimensions), bare-number or percentage `x`/`y` placement, integral `x`/`y` line offsets, the documented finite transform subset (`translate()`/`scale()`/`rotate()`/`skewX()`/`skewY()` plus axis-aligned `matrix()`, per-consumer axis gates below), and use `opacity`/`display`/`visibility` via presentation attributes, inline styles or generic stylesheet selectors (`id`/`class` are admitted for that purpose and capped at 1,024 characters). Root `display`/`visibility` propagates to each supported direct or nested local `use` instance as well as rendered leaves. A `fill` supplied on a `use` instance is exact only for a visible, otherwise unstyled single-geometry definition (rectangle, orthogonal band or polygon fill), including through symbol viewBox mapping; paint-server fills on such instances lower exactly as direct fills at the instance box (userSpace vectors shifted by instance placement and composed through symbol viewBox maps, bounding-box vectors mirrored by instance transforms, reflected pattern placements explicitly unsupported); explicit definition paint and all other use paint properties remain unsupported. Symbol instances map content through the symbol `viewBox` into the use viewport (all `meet`/`slice`/`none` alignments, defaulting `xMidYMid meet`), then apply instance placement/transforms and clip to the instance-transformed viewport box; lines map via stroke bands. Direct local `use` instantiates polygon/polyline/path/circle/ellipse fills (including multi-geometry orthogonal bands and fill-plus-stroke pairs, backward and forward) with the same placement/transform/opacity/clip composition; definition-embedded linear-gradient bands instance per band through the same vector and opacity mapping, including through symbol viewBox maps, and definition-embedded pattern bands instance per band through the shared tile and opacity mapping for direct references and through integer-mapped symbol viewBox maps while non-integer-mapped symbol pattern tiles stay explicitly unsupported; symbol-held polygon fills (including fill-plus-stroke pairs) and orthogonal bands instance through uniform viewBox maps with the same composition, while non-uniform symbol polygon maps stay explicitly unsupported; masked polygon fills compose through the same mask values and regions with shared contour storage; forward multi-geometry references expand in place preserving paint order. Direct scalar presentation/layout values and inline-style property/value tokens cap at 256 characters, and transform values at 4,096; inheritable `color`, `fill`/`fill-opacity`, `display` (`inline`/`none`) and `visibility` (`visible`/`hidden`) on root and `g`/`a`; isolated root or one group `opacity`; the same supported properties through narrowly parsed inline `style` declarations, with inline precedence; bare-number/px/in/cm/mm/pt/pc dimensions implemented at 96 CSS pixels/inch; root-targeting stylesheet selectors, references to other geometry, non-`id` definition-container attributes or child content, symbol viewports/styles, remaining geometry/style root and other group attributes and relative units explicit unsupported; non-rendering `role`/`tabindex`/`focusable`/`pointer-events`/`cursor`/`aria-*`/`data-*`, link `target`/`rel`/`hreflang`/`download`/`type`/`title`, legacy root `version`/`baseProfile` and foreign-namespace editor attributes are ignored; outermost root `x`/`y` (validated bare-number/px, without effect), root `xml:space` (validated `default`/`preserve`, whitespace-mode wiring pending) and root `enable-background` (validated `new`/`accumulate`, no backdrop compositing) are admitted without rendering effect; root `class` is admitted, inert unless a stylesheet class rule matches it (root-targeting rules stay explicitly unsupported, enforced in both the renderer and font discovery) |
| Viewport resolution | An explicit host `SvgViewport` overrides the root outer dimensions. Root `viewBox` mapping is implemented with all `meet`/`slice` alignments or `preserveAspectRatio="none"`, only when every supported rectangle edge maps to an integral or quarter-pixel output edge; other sizing and fractional-coverage cases explicit unsupported. Nested `svg` viewports with explicit bare-number or percentage `x`/`y`/`width`/`height` (percentages against known bases, including percentage-root bases in parent user units through the threaded explicit viewport with viewBox sizing and default-intrinsic fallback; omitted dimensions under dimensionless roots stay explicitly unsupported) shift admitted non-text content by the viewport origin and clip it to the viewport box (intersected through nesting, stroked rectangles admitted only fully inside); nested `viewBox` mappings scale content through the shared alignment helper with per-feature gates (strokes, pattern fills, use instances, clip paths and custom-region masks stay explicitly unsupported in scaled viewports) while viewport percentage dimensions, viewport styling and viewport text stay explicit unsupported (nested viewports isolate one presentation-attribute `opacity` scope; nested scopes stay explicitly unsupported); hidden `overflow` is accepted on root and nested viewports |
| Empty explicitly sized SVG | Integral transparent or caller-background PNG implemented |
| Caller-owned PNG destination | Synchronous/asynchronous `Memory<byte>` and writable nonseekable `Stream` destinations implemented for the current empty/rectangle subset; span-visible input/output overlap is rejected before parsing/writing, including custom memory providers; streams stay open and may be partial on failure |
| Integer rectangles | Document-order layers, bare-number/px integer coordinates, ordered lists of finite `translate()`, nonzero `scale()` (including reflections), `rotate()` with optional center, `skewX()`/`skewY()` and nonzero axis-aligned `matrix(a 0 0 d e f)` transforms on containers (rectangles, lines, true ellipses, arcs, gradient/pattern paints and clips keep the axis-aligned subset; general `matrix()` forms stay explicitly unsupported), and output-scaled geometry (only when scale preserves integral edges), SVG zero defaults for omitted coordinates/dimensions and zero `rx`/`ry`, the standard CSS named colors plus `none`/`transparent`/`currentColor`, comma or CSS Color 4 space/slash-form `rgb()`/`rgba()`/`hsl()`/`hsla()`/`#RGB`/`#RGBA`/`#RRGGBB`/`#RRGGBBAA` fill, with `fill-opacity` multiplying any color alpha, `nonzero`/`evenodd` fill rule, non-painting `stroke="none"`, and a separately bounded stroke case: untransformed integer rectangles with solid or translucent color and even integer `stroke-width` no larger than either dimension (`stroke-linecap` butt, `stroke-linejoin` miter, `stroke-dasharray` none and finite `stroke-dashoffset` accepted as no-ops), instanced through direct local `use` with placement, uniform-scale widths, multiplied opacity and viewport containment (shape definitions resolve from defs or as direct root children, which also render directly), and through `symbol` viewBox maps as fill plus four viewport-clipped stroke bands with uniform-scale even widths and integer-mapped geometry, while non-uniform scales, non-uniform symbol maps and fractionally-mapped symbol strokes stay explicitly unsupported; element `opacity` is exact only for fill-less strokes, and the corresponding supported inline properties, source-over; other stroke styles and attributes explicit unsupported |
| Fractional rectangle coverage | Analytic per-pixel area coverage for integer or quarter-pixel decimal/px rectangle edges (including only mappings that preserve quarter-pixel device edges), with a calibrated fractional source-over branch; exact pinned-Chrome overlap probe admitted. Other fractional edges, fractional paths, and fractional point-list coordinates remain unsupported |
| Shapes, paths and composition | Simple closed, absolute, axis-aligned integer `polygon` elements, filled `polyline` elements (the fill closes the contour), and `path` elements using `M`/`H`/`V`/`L` commands in either case with implicit repeated coordinates and relative movetos resolved from each open subpath end (closepath returns to the subpath start) (segment commands after closepath resume at the subpath start in both grammars; dangling segments stay malformed) with sign-abutted integer coordinates split into separate numbers lower to bounded non-overlapping rectangle bands (redundant collinear vertices merged exactly; `Z` may be lowercase), including transverse-intersecting multi-subpath contours with `nonzero`/`evenodd` fill rules (evenodd pairs crossings, nonzero sweeps winding per crossing pair; coincident vertical edges resolve through evenodd pair cancellation and nonzero winding accumulation (abutting contours, retracing, spikes and touching holes lower to exact disjoint bands; degenerate zero-width pairs and odd leftovers stay explicitly unsupported), every unclosed subpath seals implicitly for fill and lowers identically to the closed form, as does an explicit return to the contour start without closepath, while closed single-point figures stay explicitly unsupported; subpaths with fewer than three distinct points seal to zero area (empty fill) while solid strokes outline every subpath, lowering fully-degenerate open centerlines to cap-determined dots (flatness-bounded discs for round caps, axis-aligned squares for square caps, nothing for butt caps) with zero-length dash runs discarded; the same drop rule applies to flattened curve and fractional device-space all-degenerate fills, while mixed degenerate and non-degenerate contours stay explicitly unsupported there and under paint-server strokes, masks and clips; solid diagonal `polygon`/`polyline` fills, integer- or fractional-coordinate straight `path` fills, and fractional-coordinate `polygon`/`polyline` point lists (device-mapped exactly like fractional paths, including strokes) with separated simple contours lower to shared polygon outlines with analytic winding/evenodd coverage (opposite-wound nonzero holes and nested even-odd holes included; nonzero/evenodd fill rules inherit through root and group containers with leaf values winning and other values rejected as unsupported; linear-gradient fills evaluate on the same outlines through paint-box geometry) while touching, coincident and multi-contour self-intersecting diagonals stay explicitly rejected at the lowering gate pending general coverage (single self-crossings split into simple loops sharing the crossing point, with bowtie unions pinned; nonzero multi-crossing contours split iteratively under the pairwise loop guard with even-odd multi-crossing staying gated); integer-valued (including integral decimal/exponent spellings) quadratic/cubic/smooth `path` fills flatten through the same outlines at quarter-pixel flatness with arcs admitted only under uniform-positive transforms, while non-integral coordinates, degenerate curves and curve fills under non-uniform output scaling stay explicitly rejected; solid `circle`/`ellipse` elements and nonzero-`rx`/`ry` rectangles lower through sampled ellipse and rounded-rectangle outlines with the same coverage (integral-scale viewBox maps on solid rounded rectangles byte-identical to direct rendering), with linear- and radial-gradient fills on both repainted through shared paint-box evaluation (pattern-tile fills on rounded rectangles and circles); solid `circle`/`ellipse` strokes lower to opposite-wound sampled ring fills with measured oracle delta (positive integer or fractional widths, validated-only miterlimit, uniform scales; round and square caps with round joins and bevel render byte-identical to butt/miter on the smooth rings; butt-capped dashes lower through centerline sampling with the shared closed-loop kernel, with dash offsets and pathLength normalization flowing through, and definition-held and symbol-held dashed ellipses rendering byte-identical to direct play), as do linear-, radial- and pattern-tile paints on circle and ellipse rings through white ring lowering with shared repaint (opaque-only; translucent and dashed pairs staying explicitly rejected; paint-stroke definitions and stroked symbol instances byte-identical to direct rendering), while non-circle clip content stays explicitly rejected; solid positive-integer-width `polygon`/`polyline`/`path` strokes (open butt, round or square caps, miter, round or bevel joins on paths and polylines with per-subpath explicit-closure tracking, miter limits with bevel fallback, bare-number/px dash arrays with at most eight entries and odd-doubling plus finite dash offsets, bare-number path lengths scaling the pattern by computed over asserted length (zero pinning a solid stroke), fill-plus-stroke in paint order) lower to shared nonzero outline fills with fractional-capable output mapping (closed loops merge seam-touching runs preserving joins; all-zero patterns paint no stroke; zero-length dashes need butt caps; userSpaceOnUse linear-gradient strokes repaint straight polygon/polyline outlines with zero added paint error, as do bounding-box gradients on straight polygon/polyline outlines and straight path outlines through fill-bounds paint evaluation, as do bounding-box gradients on curved and fractional paths through fill-geometry-bounds evaluation, as do linear, radial and pattern paints on fractional point-list outlines through the same shared machinery, userSpace gradients on curved path outlines, and userSpace radial gradients on straight outlines and curved paths, and pattern tiles on straight outlines and curved paths through tile-grid evaluation with zero added paint error, and linear-, radial- and pattern-tile outlines compose through direct use instances with contour and paint placement mapping (uniform instance scales only; magnified instances of flattened curve outlines stay explicitly unsupported while straight outlines map exactly at any scale), and linear-, radial- and pattern-tile outlines through symbol viewBox maps with uniform-scale gating (uniform instance scales only; magnified symbol maps of flattened curve outlines stay explicitly unsupported while straight content maps exactly at any scale), while triangle outline probes render with measured oracle delta of 186 pixels at maximum channel error 15 and maximum alpha error 9 on any gradient paint and a half-tile pattern triangle renders 88 pixels inside that standing set at the same error bounds and a curved arch renders with measured delta of 254 pixels at maximum channel error 255 and maximum alpha error 107 from shared flattening), while dashed rectangle band strokes and fractional line/rect-band widths stay explicitly unsupported, as do dashed circles and ellipses outside butt caps and uniform scales; attribute, inline and stylesheet dash lists all evaluate through the admitted dash kernel on lines, polygons, paths and circles, as do non-uniformly scaled strokes except through `vector-effect="non-scaling-stroke"` on polygon/polyline/path/circle/ellipse, which keeps the width fixed through element transforms including non-uniform ones (dashes keep scaling normally; non-uniform dashed strokes, band strokes and stylesheet/inline/inherited vector effects stay explicitly unsupported); rectangle, gradient and symbol-held rectangle band strokes keep miter joins with butt-only caps on dashed strokes, while round and square caps on solid closed strokes lower as butt (spec no-ops proven byte-identical across direct, gradient-band and symbol paths), with solid rounded-rectangle strokes lowering to fill plus fractional outline bands under the same caps, as do linear-, radial- and pattern-tile paints on rounded-rectangle outlines through white-solid lowering with shared repaint (opaque-only with stop and tile alpha gates; translucent, dashed and bevel pairs staying explicitly unsupported, with round and bevel joins admitted on rounded outlines while sharp-rectangle round and bevel joins stay gated), with definition-held rounded rectangles lowering through the same shared path for direct use instances with paint-stroke definitions, symbol instances and white-masked strokes (all byte-identical to direct rendering)). Untransformed horizontal or vertical `line` elements with solid even-width butt or square strokes and multiplied element/stroke opacity (square caps extending tips by half the width across solid, gradient and pattern paints, round caps lowering opaque solid strokes to a band plus two sampled tip discs, as do linear-, radial- and pattern-tile paints on round-capped lines through white band-plus-discs lowering with shared repaint (opaque-only with stop and tile alpha gates), solid butt-capped lines also admit dash arrays with offsets as dash-run bands, as do linear-gradient, radial-gradient and pattern-tile paints on dash-run bands through the shared run computation with single-stop and degenerate solids, with pathLength normalization scaling dash patterns by computed over asserted length across solid and paint-server dashes (zero pinning a solid line, negative rejected as malformed), square caps extending each dash run by half the width through the same shared run computation (opaque nonzero dashes only; translucent and zero-length square dashes staying explicitly unsupported), and their integral local `use` instances through the same axis-aligned transform subset with integer line offsets and integer effective widths, retain axis-aligned line geometry before lowering to a stroke band (userSpace gradient strokes evaluate on the band, including through integral local `use` placement, while bounding-box gradient strokes stay unsupported). One `g`/`a` opacity scope is isolated in a bounded temporary RGBA surface before source-over composition; nested opacity scopes are explicit unsupported pending separate calibration. Coincident-edge configurations (shared subpath edges, backtracking spikes, collinear overlaps), non-integral curve coordinates, all other path grammar and shapes explicit unsupported-feature failure. Grayscale single-rectangle `mask` definitions with content covering the masked geometry, plus single-element solid-grayscale circle content evaluated spatially on masked groups through a temporary mask surface (single paint; multi-element, chromatic, multi-paint, non-polygon, leaf/use-targeted, text-bearing and transformed-viewport content stays explicitly unsupported) (id plus `luminance`/`alpha` `mask-type`) modulate admitted leaves and `use` instances through `mask="url(#id)"` (quoted forms accepted; forward mask definitions admitted on use and symbol instances, on direct leaves (deferred values with default, userSpaceOnUse and bounding-box regions resolving against retained bounds) and on direct groups (rewritten or inserted isolation scopes with retained coverage), viewports still requiring backward references, default regions, explicit userSpaceOnUse regions admitted on direct leaves and use/symbol instances outside nested viewports, stroked rectangles admitted only fully inside and masked groups composite on isolated surfaces with combined opacity-masked factors and custom (explicit or bounding-box-fraction) regions clipped at composition with clip-box edge discipline (region-only scopes composite without opacity interaction; nested group opacity inside opacity scopes stays unsupported; each masked-group child, including bands, polygon fills, stroked extents and deferred uses, requires covering mask content and text inside masked groups stays explicitly rejected; empty masked groups carry no scope) while masked text stays explicitly rejected; chromatic luminance content, mask styling and non-covering or transformed mask content stay explicitly unsupported while explicit userSpaceOnUse mask regions clip direct leaves and explicit bounding-box regions resolve per referencing box on direct leaves and use instances with specification defaults and content-coverage policy (other units and group targets stay unsupported). Single-value masks stamp linear-gradient, radial-gradient and pattern-tile outline strokes like polygon fills (white-mask pass-through proven byte-exact on gradient outlines); non-white masks on overlapping fill-plus-stroke paint evaluate per geometry rather than per element, translucent-mask dither order is uncalibrated, and stroke-fringe mask-region edges are uncalibrated. General masks remain unsupported. Single-rectangle and single-straight-path `clipPath` definitions resolving from defs or as direct root children (id-only with optional nonzero/evenodd `clip-rule`; `clip-rule` is also admitted but unwired on regular shapes, groups and roots, userSpaceOnUse or objectBoundingBox units; rectangular paths lower to boxes while single convex straight contours in userSpaceOnUse intersect leaf polygon fills and paint polygons at document end with element transforms composed at retention, and multi-contour convex unions concatenate per-pair intersections (exact for nonzero fills including translucent and gradient paints; even-odd needs pairwise-disjoint contours with vertex touching, overlapping contours explicitly unsupported), and concave, curved, degenerate, transformed-ancestor, transformed-viewport, group, group-use-instance and non-polygon-geometry targets stay explicitly unsupported) clip `g`/`a` containers, admitted leaves (including paths, lines and gradient bands) and local `use`/symbol instances through `clip-path="url(#id)"` (quoted forms accepted; forward clip-path definitions admitted on use and symbol instances and on direct shape leaves in userSpaceOnUse (rectangles, rounded rectangles, bands, straight contours and embedded images) or objectBoundingBox (rectangles, rounded rectangles, bands and straight contours), and on direct groups in userSpaceOnUse through deferred command ranges with passthrough viewport frames, other targets still require backward references); objectBoundingBox fractions resolve per referencing rectangle, band, straight-contour polygon fill, flattened curved contours, fractional point lists or direct use instance box (backward and forward use references), with symbol instances resolving through the viewBox-mapped content box (backward and forward use references), while non-integral curves, forward clip-path definitions outside use, symbol instances and direct leaves (including bounding-box forwards on lines, circles, images and groups, masked or scaled groups, and any forwards on viewports), and other targets stay explicitly rejected, transformed fraction content stays unsupported while transformed userSpace clip content composes through the admitted axis-aligned transform subset (rotated clip content stays explicitly unsupported); nested clips intersect, stroked rectangles admitted only fully inside, clipped text explicitly rejected; clip boxes map through the root viewBox and output transform with the clipped geometry, and fractional clip edges scale per-pixel coverage exactly like fractional rectangle edges (byte-exact against the pinned oracle on the solid-polygon probe) |
| CSS, gradients, images, filters and text | Attribute-free root-level `style` elements, each preceding every supported definition and rendered leaf, may contain ordered comma-separated `rect`, `g`, `#id`, `.class`, `rect.class`, `rect#id`, `g.class`, `g#id`, `[id]`, `[class]`, `[id=value]`, `[class=value]`, `g rect`, and `g > rect` selector rules using the narrowly documented declaration subset. `rect`/`g` type selectors and group combinators retain their stated scope; generic ID, class and id/class-attribute selectors apply to every currently supported shape/container carrying those attributes, including paths, point lists, lines and `use` instances; supported identifier tokens cap at 1,024 characters. Group paint/display/visibility then inherit to descendants. Per-property `!important` priority, source order and selector specificity (`#id` > class/attribute > type selectors) are implemented. Inline important declarations override stylesheet important ones; stylesheet important declarations override normal inline ones; normal inline declarations override normal stylesheet rules; stylesheet rules override presentation attributes. `inherit`/`initial`/`unset` keywords resolve for the paint subset (`inherit` where the property inherits, static initial values otherwise; unplumbed `inherit` stays explicitly unsupported). `font-variant: normal` (or `none`) and `color-interpolation="sRGB"` are accepted as no-ops. Integer-grid `userSpaceOnUse` single-rectangle `pattern` definitions tile admitted rectangle and orthogonal path/point-list band fills through `fill="url(#id)"` (quoted forms accepted; explicit grid/content integers, content inside the tile (shifted content and stroke extents included), solid content paint, no nested patterns or definition reuse; fill-paired strokes stay solid while pattern-url strokes on rectangles and lines evaluate the shared tile grid on stroke bands (even widths, butt caps, unscaled viewports, definition and line composition, pairing with solid and gradient fills); pattern fills pair with gradient-evaluated stroke bands through fill-plus-band geometries, as do pattern fills with pattern strokes through shared tile evaluation; integer translate-only pattern transforms shift content; tile content admits clip paths in either unit system, element opacity, positive integer content scales and in-bounds solid strokes while masks on content, tile-escaping shifts and tile-overflowing strokes stay explicitly rejected); other pattern units, fractional grids, overflowing content, reflected placements stay explicitly unsupported; pattern fills apply to rectangles, orthogonal path/point-list bands and diagonal polygon/path outlines repainted with the tile, with curved paths flattened through shared device outlines carrying a small curve-edge residual, while other targets stay explicitly unsupported; pattern fills under root viewBox or output scaling map their integer tile grid through the device transform while fractional tile maps and stroked tile content under scaling stay explicitly unsupported; tile content raster work is charged per blitted pixel with bounded cancellation checks; curved pattern fills pair with admitted solid stroke outlines through flattened geometry with the standing curve-edge delta; orthogonal and diagonal pattern fills pair with admitted solid stroke outlines in paint order; fill opacity composes onto pattern fills while solid strokes keep element opacity only. General selectors/cascade, stylesheet attributes, stylesheets outside the direct root position, scripts, animation and stylesheets after styleable content stay explicitly unsupported. Filters are unsupported. One-to-eight-stop linear gradients in objectBoundingBox or userSpaceOnUse units (stops carry alpha interpolated under the calibrated straight-ramp premultiplied rule, non-decreasing offsets for multi-stop gradients (falling offsets rise to the previous stop, forming hard edges with the later stop winning at the shared offset per the pinned oracle; zero clamped ranges stay unsupported), clamped into [0,1] and retained per gradient with one scene-owned reservation per distinct stop array (shared href and band references counted once), pad/repeat/reflect spread, no non-pad degenerate vectors (degenerate pad-spread fills render the end-stop color, as do single-stop gradients); userSpaceOnUse vectors map through the referencing transform and reject percentage coordinates, with transformed viewports composed through the viewport map and non-uniform referencing transforms evaluated through the recorded inverse paint map; gradientTransform composes finite translate, positive scale and positive axis-aligned matrix forms eagerly into the vector and rejects rotated, skewed, non-positive and non-finite forms; objectBoundingBox vectors mirror with reflected referencing geometry transforms; solid fills pair with gradient-evaluated stroke bands under the solid-stroke geometric policy, and gradient fills pair with gradient-evaluated stroke bands through fill-plus-band geometries, with pattern fills pairing with gradient-evaluated bands the same way; single-stop gradient strokes render their stop color on the bands, as do degenerate pad-spread strokes with the end-stop color; gradient-stroke rectangle and line definitions instance through the same placement and vector mapping, including through symbol viewBox maps; backward href inheritance merges inherited stops with whole-vector or same-space partial-vector overrides under explicit mixed-space gates; radial gradients inherit stops and circles through the same same-type href mechanism in both `href` and `xlink:href` spellings while linear-to-radial references stay unsupported) fill admitted rectangles (gradient definitions resolve from defs or as direct root children), diagonal linear- and radial-gradient polygon and path outlines and orthogonal path/point-list bands with sRGB-direct transposed-Bayer dithered interpolation, with linear-gradient fills on fractional-coordinate straight and curved paths evaluated on shared device outlines repainted through the same paint-box geometry evaluated in normalized bounding-box space; one-to-eight-stop radial gradients in objectBoundingBox or userSpaceOnUse units (focal point defaulting to the center and admitted strictly inside the circle through point evaluation, contained focal circles defaulting to zero radius through two-circle cone evaluation with negative radii rejected as malformed, pad/repeat/reflect spread on the circle parameter, finite translate, positive scale forms (uniform, or non-uniform for userSpace circles through recorded inverse-map evaluation with pre-transform focal validation) and positive axis-aligned matrix gradient transforms composed eagerly into the circle; userSpace circles map with their focal point and radius through the referencing transform, with non-uniform scales and transformed viewports evaluated through the recorded inverse paint map, reject percentage coordinates) fill admitted rectangles, diagonal polygon outlines, curved paths flattened through shared device outlines with the standing curve-edge delta, and orthogonal path/point-list bands through normalized-space evaluation with the same stop interpolation, single-stop solids and degenerate end-stop rendering, and compose through direct use instances (bounding-box and userSpace placement with paint-server fills), symbols (viewBox scaling with recorded inverse-map scales, uniform for contours) and single-value masks exactly like linear fills; solid fills pair with radial-evaluated stroke bands under the solid-stroke geometric policy on butt-capped rectangles (even widths, unscaled viewports) and userSpaceOnUse lines, and radial fills pair with radial-evaluated and pattern-evaluated stroke bands through fill-plus-band geometries, while non-pad degenerate radial strokes and bounding-box line strokes stay unsupported, while on-or-outside-circle foci, escaping focal circles, rotated, skewed, non-positive and non-finite gradient transforms, non-uniform transforms on bounding-box circles, viewport-nested maps and percentage userSpace coordinates stay explicitly unsupported; over-eight-stop and non-pad-degenerate linear gradients stay explicitly unsupported, as do rotated, skewed, non-positive, non-finite and empty linear gradient transforms. Gradient-filled curved paths with solid strokes compose through flattened outlines with the standing curve-edge delta; radial- and pattern-filled curved paths with strokes, and all paint-server fill-plus-stroke pairs, stay unsupported; orthogonal and diagonal linear and radial fills pair with admitted solid stroke outlines in paint order; fill opacity composes onto gradient fills while solid strokes keep element opacity only; group-inherited gradient fills stay unsupported. Solid-color base64 PNG data-URI `image` elements with explicit bare-number `x`/`y`/`width`/`height` (or omitted dimensions defaulting to 100%) lower to exact rectangles with CRC-checked chunk integrity (unknown critical chunks rejected as malformed, unknown ancillary chunks ignored, corrupt compressed pixel data categorized as malformed) (matching intrinsic/placement aspects; explicit `none` stretching; meet/slice alignments through the shared viewBox mapping with integral output edges, defaulting to centered meet with presentation attributes winning over inline declarations); clip paths, masks, opacity and the admitted axis-aligned transform subset compose through the same path (rotated image placements stay explicitly unsupported); non-data references, interlaced/color-managed/multi-color images and non-solid palette content stay explicitly unsupported; solid-color 8-bit palette images with PLTE (and optional tRNS) decode through the same uniformity gate; base64, IDAT, palette and transparency copies are budgeted before allocation with transparency lengths validated first, and chunk, decompression and scan work is charged proportionally with bounded cancellation checks. Experimental direct text requires a caller-supplied prepared snapshot; its single-font family, optional weight/style and size may be inherited from `svg`, `g` or `a`, supplied directly/on inline `style`, or resolved through the admitted root stylesheet selectors. Its snapshot validates optional `name` table record/storage bounds and OS/2 version-specific minimum lengths, but keeps the manifest as the source of face family/style selection. It supports scalar `x`/`y`/`dx`/`dy`, scalar `letter-spacing` added only between glyphs and included in anchor width, one validated version-zero horizontal format-zero legacy `kern` subtable or one validated DFLT-script `kern` feature using an unfiltered GPOS PairPos format-1 first-glyph X-advance lookup applied between adjacent glyphs, `text-anchor` (`start`, `middle`, `end`), a bounded diagnostic-only `id`, a bounded `class` token for generic stylesheet selectors, and admitted paint/visibility attributes. GPOS takes precedence over legacy `kern` when both are present. Letter spacing inherits through `svg`, `g` and `a` from presentation attributes, inline styles or admitted stylesheet declarations. On direct text, XML `default`/`preserve` whitespace modes retain their existing behavior. CSS `white-space: normal` and `white-space: pre` are admitted on direct text and inherit through `svg`, `g` and `a` from inline or admitted stylesheet declarations; a direct CSS declaration takes precedence over `xml:space`. Nested `tspan` elements concatenate into that run when attribute-free, carrying exactly `xml:space="default"` or `xml:space="preserve"`, carrying scalar `letter-spacing` and/or scalar presentation `dx`/`dy`, carrying a `style` containing only `white-space: normal` or `pre` and/or scalar `letter-spacing`, or carrying an `id`/`class` selected by an admitted generic stylesheet rule containing only those style properties; inline span declarations take precedence over matched rules. Each span inherits its parent mode and letter spacing otherwise. A scalar span `dx`/`dy` shifts the current position before its first retained glyph or that of a descendant; the nearest span that specifies each axis takes precedence, and the shift remains in force for later glyphs. Span spacing applies after its glyphs, including across its following parent boundary, and contributes to the enclosing text chunk's anchor calculation. Scalar span offsets apply after that anchoring adjustment and do not change the anchor width. Other CSS whitespace values, transforms, span paint/font styling, span `x`/`y`, positioning lists, clipping, stroke and other GPOS forms are explicit unsupported features. It is a regression-gated pipeline experiment, not admitted ordinary text or independent-browser-fidelity support |
| PNG encoding | Managed non-interlaced RGBA8 encoder; owned-memory, caller-memory, synchronous-stream and asynchronous-stream output; stream forms aggregate into at-most-64 KiB `IDAT` chunks under a budgeted 64 KiB cap with byte-identical output across all four forms, while memory forms accumulate budgeted segments without geometric copies; stream payloads are written in cancellation-checked chunks of at most 64 KiB |
| Independent RGBA comparison | Exact RGB8/RGBA8 comparison with alpha and mismatch diagnostics |
| Visual comparison CLI | Exact JSON gate for bounded RGB8/RGBA8 PNGs |
| Chrome oracle lifecycle/CDP connection | Hash-pinned Windows CFT archive, transparent capture recipe, bounded graceful CDP shutdown with profile-cleanup retries, three byte-identical fresh-process smoke captures, and retained pinned references for all 601 exact fixture entries; entry count is not a count of fresh capture triples |
| Fixture corpus | 823 original CC0 fixture entries with verified hashes across 457 base documents and 366 variants; all 628 exact entries compare exactly, and 195 other entries retain their observed behavior (168 known-difference renders with pinned-Chrome references and complete comparisons, 27 categorized rejections). The independent Ford matrix is separate: all nine exact against original references/source provenance. These entry counts are not corporate-document coverage. |

### Experimental supplied-font text

Direct text is an experimental, caller-snapshot-only path. Its admitted
single-font family, weight, style and size inherit from `svg`, `g` or `a`
presentation attributes, the admitted root stylesheet selector subset, and inline
declarations, with inline declarations taking precedence. It admits finite
bare-number/`px` scalar position and offset values, bounded text/CDATA extraction,
and `start`/`middle`/`end` anchors. Spans, lists, clipping, strokes and transforms
remain explicitly unsupported. These mechanics do not constitute ordinary text or
browser-fidelity support.

Stylesheet declaration/selector copies retained by rules, definition identifiers
and copied local-reference identifiers reserve their owned string and reservation-
tracker storage before the copy, then release it with parser document state.
The direct-root stylesheet reader incrementally copies text through charged
growing character storage and a charged temporary string, checking work and
cancellation before each read or copy; it releases that temporary string once
stylesheet parsing completes. Reader-provided transient attribute and inline-style
strings remain within the documented conservative reader allowance; this is not
exact BCL-allocation accounting. Font-request discovery reserves ancestor, whitespace, eligibility, group and distinct-request storage from its own preparation budget before allocation and releases it deterministically.
Supported element attributes are validated before stylesheet matching, while the
selector matcher independently enforces the identifier cap; class-selector scans
also observe the shared parser-work limit. Each attribute walked by that policy
also consumes one work unit and checks cancellation.
Every XML-reader event is charged to that same limit, including inert metadata and
the root closing event.
Opaque-background initialization and PNG's per-pixel transparent-RGB normalization
are also charged before those scans run. Opaque-background initialization checks
cancellation every at most 4,096 pixels after allocation.
Every compressed PNG byte produced by the owned/caller-memory buffer and both
streaming IDAT sinks is charged before its copy, checksum, or output write; stream-sink growth copies of already-buffered bytes carry their own work charge.
The isolated Windows probe directly traverses a 64 KiB text node, a full
short-attribute root and 255 × 4 KiB attribute values below that allowance;
those observations are platform/runtime-fingerprinted regression evidence, not a
BCL or process-RSS guarantee. Each retained XML-reader report breaks managed
allocation into setup, traversal and `Name`/`Value` property-access deltas,
and counts text/CDATA nodes without retrieving their values; property access is a subset of
traversal. This makes the probe's attribution inspectable without claiming
cross-runtime exactness. Each child report records its exact input byte count and
SHA-256, which the parent verifies against the launched input. One retained
schema-3 JSON summary then groups every child report under its shared platform/runtime
information, retains the fixed fixture hash and fails mixed runtime/platform data.
Git and CI workflow revisions identify the code; solution sources, scripts and
assemblies are not hashed into reports.
The definition table reserves a two-times requested-capacity allowance before the
BCL selects its prime backing capacity, and verifies that returned capacity is
within the allowance.
Async PNG streaming reserves one small per-conversion scratch buffer before
allocation and reuses it for header and chunk-length framing, including all `IDAT`
chunks.

The target scope and release criteria remain in [PLAN.md](../PLAN.md).
