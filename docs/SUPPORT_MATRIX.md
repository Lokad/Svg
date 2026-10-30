# SVG support

Lokad.Svg converts a bounded subset of static SVG to PNG on .NET 10 and .NET 11,
on Windows and Linux, without runtime package dependencies. The .NET 11 target
currently uses the RC SDK/runtime pinned in [global.json](../global.json). This is the current support
contract; it does not imply full SVG, CSS or browser conformance.

## Supported SVG subset

| Area | Accepted features |
| --- | --- |
| Structure | Root/nested `svg`, `g`, `a`, `defs`, `symbol`, local `use`, forward references and conditional `switch`. |
| Geometry | Rectangles/rounded rectangles, circles/ellipses, lines, polygons/polylines, line/quadratic/cubic/arc paths and compound fills. |
| Placement | Translation, rotation, skew, reflection, nonuniform affine maps, viewBox/aspect maps and viewport clips. |
| Paint | Solid colors, linear/radial gradients, bounded repeating patterns, fill/stroke opacity and paint order. |
| Composition | Group opacity, clip paths, alpha/luminance masks, markers and the filter primitives below. |
| Content | Embedded base64 PNGs, static CSS and supplied-font labels/spans/text paths. |

Scripts/events, animation, a mutable DOM, `foreignObject` HTML, perspective and
arbitrary external documents are unsupported. Ordinary conversion does not fetch resources
or discover machine fonts. Unsupported visible behavior reports
`UnsupportedFeature`; malformed input, unresolved references and exceeded limits
have distinct [error categories](../src/Lokad.Svg/SvgErrorCode.cs).

### Diagnostics

`SvgException.SourceDiagnostic` carries available element/ID, attribute or property,
value and one-based XML location. Retained text keeps its source location; direct
text instances also identify their referencing element. Unavailable fields are
empty/null; strings are bounded to 256 UTF-16 code units. One failure is reported.

`FontDiagnostic` identifies the requested family/weight/style, selected face and
fallback when known. Missing-glyph indices count Unicode scalars in normalized
layout text, independently of UTF-16 and original XML character indices.

### Document sizing and inspection

Root lengths accept numbers, px/in/cm/mm/pt/pc and percentages. With neither
dimension, the default is 300×150, including with a viewBox. One dimension uses
the viewBox ratio, or the other axis's default without a viewBox. Percentages use
the caller viewport or the corresponding 300/150 base. A caller viewport replaces
absent/absolute root dimensions. Supported CSS, including later rules, participates
in sizing; winning root `auto`/`inherit` dimensions resolve as 100%.

Child percentages use the logical viewport, or viewBox user-space dimensions.
Nested viewports keep their own percentage bases, aspect mapping and physical
clips. Raster scaling does not change logical layout or those bases.

`InspectDimensions` reads bounded XML/CSS and returns resolved dimensions,
viewBox/aspect mapping and sizing provenance without preparing fonts, geometry or
images. It does not validate every drawing feature. Its `GetRasterSize` method
plans output using the same sizing rules.

`SvgRasterBounds` optionally caps raster width, height and/or pixels. At least one
bound must be positive. Bounds can reduce `OutputScale` uniformly; they do not
change layout or retry memory refusals at a smaller size. Pixel dimensions
round to nearest, away from zero, with a one-pixel minimum and conservative cap
enforcement. An empty physical clip still paints nothing. Excessive output without
bounds, and excessive intermediate surfaces, fail explicitly.

### Geometry and strokes

Fills support nonzero/even-odd winding. Strokes support butt/round/square caps,
miter/round/bevel joins, miter limits, finite dash arrays/offsets, `pathLength`,
independent paints and ordinary/non-scaling widths. Dashed non-scaling line,
circle and curved-path strokes reject rotation/shear; dashed gradient line
strokes require uniform scaling. CSS/inherited vector effects are unsupported.
Nonuniform symbol mapping of gradient/radial/pattern polygon strokes rejects.

All-zero dash lists use a solid stroke; zero-length dashes with positive gaps
retain cap behavior. Dash placement follows authored endpoint order. Zero-size
rectangles paint nothing. Missing circle radii/path data and empty point lists
paint nothing; an ellipse copies its one supplied radius to the missing axis.
Repeated/closing vertices and one/two-point lists retain ordinary stroke/marker
rules. Whitespace-only paths remain subject to input and structural limits.

Line endpoints accept fractional numbers/px, not percentages or other units.
Polygon/polyline coordinates and transform arguments are bare SVG numbers.
Point-list native-float overflow discards the whole list, its markers and its own
filter; ancestor filters can still process transparent input. Syntax and resource
limits remain checked. Path numeric recovery retains completed segments;
unsupported clip commands reject. Missing coordinates and invalid commas do not
become valid through whitespace normalization.

### Documents and instances

Local references exclude the source's ancestors and preserve instance inheritance.

`switch` uses an empty locale set and selects its first child without
`systemLanguage`; selection is local to that switch. `requiredFeatures` and
`requiredExtensions` reject.

Missing/cyclic references fail explicitly. An absent/empty `href` supplies no use
geometry; empty `href` takes precedence over `xlink:href`, while its own filter can
still generate output. Zero use dimensions preserve ordinary geometry targets;
zero symbol/nested-viewport dimensions remove source pixels and child font needs.

Root/direct nested viewports accept omitted/hidden overflow. Nested SVG rejects
inline styles; stylesheets may supply supported declarations. Symbols reject the
XML `overflow` attribute but accept CSS `overflow:hidden`. Nested SVG inside
retained symbol/pattern content is unsupported. Rendered group `use` sources do
not support source-group opacity isolation, clip/mask bindings or nested viewports.
Zero scale factors and singular pattern transforms reject.

### Gradients and patterns

Gradients accept object-box/user-space units, affine maps, local templates,
pad/repeat/reflect spread and sRGB interpolation. Stops retain hard/flat ranges
and syntax-dependent alpha precision. Degenerate paints use terminal pad colors
or periodic mean colors. Empty paint servers suppress only their own channel;
independent strokes, markers and effects remain active.

Fill and stroke accept local `url(#id)` with an explicit color, `currentColor`
or `none` fallback. Available servers retain their paint; missing/wrong-kind
targets and unavailable pattern tiles use the fallback. A valid empty gradient
or a pattern with empty drawing content paints transparently instead. Invalid
syntax, unsupported servers and missing targets without fallback still reject.
Quoted URLs and adjacent color functions are accepted. Effect/marker URLs do
not accept paint fallbacks.

Coordinates accept numbers and percentages. Each gradient definition or alias
resolves percentages in its own viewport; radial radii use the normalized diagonal.
Templates preserve units through inheritance and unit overrides. Cross-kind
templates share stops/common attributes and apply the child's coordinate defaults.
A child's transform replaces its parent's.

Radial focal circles must be contained; zero-radius foci may lie on the outer
circle. Outside foci and tangential nonzero focal circles reject. Templates retain
each center, radius and focal override independently; absent foci follow the
effective center.

Stops accept bounded `id` metadata. Color/opacity attributes and inline styles
support property priority/order. Other coordinate length units, stylesheet stop
declarations, resource/stop `currentColor`, gradient-container `color` and linearRGB
gradient interpolation reject.

Patterns support tile/content unit systems, viewBox/aspect maps, affine placement
and bounded repetition of vectors, groups, uses, opacity, gradients, PNGs, admitted
filters and supplied text. Tile x/y/width/height accept numbers/px, not percentages.
The pattern container accepts only opacity 1; children retain opacity. XML overflow
accepts hidden/visible; CSS accepts hidden only. Recursive/nested pattern paints reject.

Aliases with local element children use those children, including empty,
descriptive or foreign elements. Childless/comment/text-only aliases inherit
content. An alias can replace zero/omitted template dimensions; effective-zero
tiles remain inert. A hidden template supplies no dimensions/content, while a
visible alias may supply its own. An alias's own hidden display suppresses borrowing.

### Clips, masks and composition

Clip paths accept rectangles/rounded rectangles, paths, circles, ellipses,
restricted uses, ordered child unions, compound winding, both resource units and
affine maps. Container groups, direct line/point-list/text children and XML
transform/fill/opacity on `clipPath` reject. A clip-resource `use` must reference
one contour; plain rectangles and empty/compound sources through that use reject.
Direct rectangles and compound paths remain supported.

Alpha/luminance masks accept ordered vector content, grouped/instanced text,
clipping, local uses and transformed finite regions. Direct text/image mask
children reject. Object-box regions require bare fractions; user-space regions
also accept viewport percentages. Spatial content for an axis-aligned shape must
cover an explicit object-box region; smaller content boxes reject.

For each element the order is **filter → mask → clip → opacity**. Child clips
remain inside ancestor effects; group opacity isolates the composed children.
Object boxes are measured independently of visual/stroke enclosures and clipping.
Mask text styles come from the resource context, with explicit child overrides.
Inactive sources need no mask fonts unless layout or generated filter output
depends on them.

Masks on uses inside symbols preserve placement, but masks on retained symbol
group containers reject. Masked rectangle/path sources require axis-aligned
instance placement; masks on live rectangle/path uses permit affine placement.
Direct masked text, masked text uses and references to already-masked text work.

`clip-path`/`mask` bindings are presentation attributes, not inline/stylesheet
properties. Explicit `none` removes that element's binding, retaining ancestors.
`initial`/`unset` reject; mask `inherit` rejects, while clip `inherit` copies the
immediate container or its initial value. `clip-path` on `clipPath` itself rejects;
admitted resource children may have their own clips.

Resource display is distinct from hidden content:

| Resource with CSS `display:none`, including on defining `defs` | Effect |
| --- | --- |
| Gradient | Local stops are unavailable; visible aliases may supply their own. Coordinate inheritance remains. |
| Pattern | Template dimensions/content are unavailable. |
| Clip or mask | The binding is omitted; empty/hidden children instead produce empty coverage. |
| Filter | Filtered output, including SourceGraphic, is suppressed. |

Hidden outer groups do not disable named paint/clip/mask resources. Clip visibility
inherits through its resource ancestors and admits child overrides; paint/stroke
opacity does not suppress raw clip geometry. IDs on clip leaves remain usable as
ordinary painted instances. Geometry leaves, uses and stops allow balanced or
self-closing XML with inert descriptions/comments, not drawing children.

### Embedded PNGs

Images accept base64 data-URI, non-interlaced, 8-bit RGB/RGBA/grayscale/gray-alpha/
indexed PNGs, including valid tRNS and full-eight-bit sBIT. Chunk names/order,
palette/transparency relationships, CRCs and bounded decompression are validated.
IDAT chunks must be consecutive; transparency precedes image data and follows
any palette. Other formats, interlacing, 16-bit/reduced-significance data,
color-management/orientation conversion and external fetching reject.

Placement accepts finite viewport-relative percentages, affine maps, meet/slice
and `preserveAspectRatio=none`. Missing/empty href paints nothing; omitted
dimensions use intrinsic size/aspect. Fill/stroke do not paint images;
display/visibility/opacity, clips, masks and source filters still apply. Patterns
can contain images; masks cannot. Affine filter replay for image sources rejects,
independently of supported ordinary affine image placement.

### Static CSS

Selectors include bounded unescaped ASCII element/ID/class names, their presence or
exact-equality attribute selectors, lists, element+ID/class, descendants and
children. Specificity, source order, inline priority, inheritance and `!important`
apply. Empty/comment-only stylesheets are no-ops. Comments preserve token boundaries
and quoted text; namespace declarations on style/defs are accepted.

Properties cover paint/color/opacity, strokes/dashes, fill/clip rules, display/
visibility, paint order, filter, markers, overflow, root sizing and the typography
below. Admission depends on target/location; [location contracts](../tests/Lokad.Svg.Tests/OpenTypeFontTests.SupportLocations.cs)
define those exceptions. `pathLength` is XML-only. Rectangle/image CSS width/height
reject; use XML attributes. Container XML `color-interpolation-filters` rejects;
container CSS accepts only inert default linearRGB. Filter/primitive declarations
select operating spaces.

`paint-order` accepts normal or distinct fill/stroke/markers lists, appending
omitted operations. `display` accepts inline/block/none/inherit; omitted/initial/
unset use inline, ancestor suppression is cumulative, and root inherit is inline.
`currentColor` resolves against each element's effective color; `color:currentColor`
uses inherited color. Comma RGB/RGBA channels must all be numbers or percentages;
space syntax may mix them. RGB(A)/HSL(A) aliases accept slash alpha.

Token rules:

- SVG separators/padding use ASCII whitespace; non-ASCII characters retain their
  meaning and cannot normalize into another number, keyword or family.
- Length numbers and units form contiguous tokens. Lists retain required values
  around commas and reject leading/repeated/trailing commas. Transform functions
  may be comma/whitespace separated; SVG function/enumeration names retain case.
- Scalars and inline property values have a 256-character authored bound, checked
  before trimming. Gradient stop/color limits retain their authored tokens.
- Local href/URL fragments decode UTF-8 percent escapes once; malformed escapes,
  invalid UTF-8 and encoded ASCII whitespace reject. URL function names and filter
  `none` ignore ASCII case; identifiers do not. Surrounding/trailing URI padding is
  accepted, but leading whitespace inside quoted URIs and fragment whitespace reject.

Imports, media/supports rules, pseudo selectors, sibling selectors, layout,
variables, animation and CSS web-font acquisition are unsupported.

### Markers and filters

Markers accept user-space/stroke-width units, refX/refY, finite dimensions,
viewBox/aspect maps, fixed/auto/auto-start-reverse orientation and hidden/visible
overflow. Lengths include absolute units, known-axis percentages and em/ex from
supplied metrics. Content inherits paint/stroke/visibility and may use local
gradient/pattern paints. Unstroked lines still paint markers. A marker's own
display does not suppress its content; display-none defining defs prevents
activation. Symbol display-none suppresses instances. Extreme transforms may
collapse visual bounds without removing object bounds/filter eligibility.

Filters accept `feGaussianBlur`, `feOffset`, `feFlood`, `feComposite` over/in,
`feMerge`, `feBlend` normal/multiply/screen and `feDropShadow`. Inputs are
SourceGraphic, SourceAlpha or earlier exact result names. Both unit systems,
primitive/filter regions, local CSS references and sRGB/linearRGB spaces work.
Result/input names retain case and whitespace; missing/forward/cyclic references
fail explicitly. Intermediate surfaces obey dimension, pixel and memory limits.

Flood color alpha combines with flood opacity. Cropping, image sampling, color
transfer and enclosing opacity retain separate numerical stages. Offscreen source
geometry can generate visible output. A filter without primitives paints nothing;
an absent/none filter retains its source. Display suppression disables effects.
Zero-size basic shapes and images without usable sources suppress their own
filters; enclosing groups/uses can still filter transparent input. Empty groups
run their filters. Paintless paths/lines/nonempty point lists retain source bounds
for ancestor effects; empty basic shapes and absent images do not enlarge them.
Clipped strokes retain their original source boxes. Crops of materialized images
discard transparent-padding assumptions; rotated restoration retains the active
clip bounds and the sampling colour transfer of zero-effect aliases.
Empty tile restoration paints nothing. Some affine filter opacity/kernel combinations
still have [rounding differences](../tests/Research/svgweb/filter-image-residuals/README.md).

Drop shadows admit mapped/rounded deviations through 135 device pixels per axis;
larger deviations reject. Gaussian blur rescales larger deviations and saturates
at 532 device pixels per axis. Backdrop/external-image inputs, displacement,
turbulence, lighting, morphology, convolution, arithmetic composite, other blend
modes and CSS filter-function lists are unsupported.

### Text and fonts

Fonts come from explicit host manifests/catalogs/providers. Hosts own acquisition,
credentials, retries and caches. Required unavailable/corrupt programs and missing
glyphs fail; conversion neither discovers fonts nor synthesizes styles.
[FontProbe](../tools/Lokad.Svg.FontProbe/README.md) demonstrates host acquisition.
The opt-in [HTTP resolver](HTTP_FONTS.md) supplies a verified shared cache from a
host-selected pack root; it adds no SVG-directed fetching or ambient font discovery.

| Area | Accepted boundary |
| --- | --- |
| Programs | Standalone static TrueType, face index 0, composites and bounded hinting; unsupported executed instructions fail. |
| Characters | Horizontal LTR ASCII, tested precomposed Latin and covered supplementary scalars; no bidi/joining/complex-script/mark shaping. |
| Layout | Ordered text/CDATA/whitespace, nested spans, inherited/per-span styles, scalar/list x/y/dx/dy/rotate, anchors, spacing, explicit lines, textLength spacing/spacingAndGlyphs and alphabetic/middle/hanging baselines. |
| Painting | Affine solid/gradient/pattern fills and strokes, dashes/non-scaling widths/paint order and solid underline/overline/line-through. |
| Text paths | One local open/closed geometry path per run, transformed/curved paths, advances/kerning, length/percentage startOffset, anchors/tangents, align/exact spacing and mixed direct/path text. |
| Ligatures | Default Latin/DFLT Basic Latin, one unfiltered type-4 lookup, format-1 subtables, coverage 1/2 and 2–8 components in font order. |

`SvgFontFaceRequest.FromCss` preserves family order, quoting, escapes and named/
generic identity. `SvgFontSelectionPolicy` supplies host generic mappings and
fallback restrictions. CSS requests use directional weight fallback, including
400–500; literal constructor requests retain nearest-weight selection. SVG-based
preparation acquires later families lazily for uncovered admitted scalars, one
program per interval. Request-only creation has no coverage to inspect and selects
the first face; raw conversion uses only supplied prepared snapshot requests.
Kerning/ligatures stay within selected faces.

CSS requests admit 256 families, 1,024 UTF-16 units per decoded name and 4,096 units
for both authored/canonical lists. SVG weights accept integers 1–1000, normal/bold;
public request objects retain their positive-weight contract. Font sizes must be
positive. Family inherit/initial/unset work; those keywords reject for weight,
style, size, spacing and white-space. Typography on geometry remains inert.

Nonzero spacing, face/paint changes and absolute positions prevent ligature
merging. Relative positions/rotation retain clusters and advance source cursors.
Comments/descriptions/CDATA retain fragment metrics; shaping may cross nodes,
while scaled element boundaries follow native font selection. Inert title/desc/
metadata text does not consume rendered-text allowance; XML/input and structural limits apply.
Anchors include relative advances/backtracking. Ordinary/path ranges share shaping
and object paint boxes, including unpainted characters. Text after a nonempty path
resumes at its authored endpoint; empty paths do not move the cursor. Zero
textLength retains natural layout; spacing with one inline item retains natural
metrics, while positive spacingAndGlyphs scales it.

Pair positioning chooses default Latin before DFLT. Selected GPOS kerning,
including zero adjustments, supersedes legacy kern; required legacy kern permits
one horizontal format-0 subtable. Type-2 pairs admit RIGHT_TO_LEFT, IgnoreBaseGlyphs
and IgnoreLigatures with validated GDEF. IgnoreMarks, mark attachment and format-1
filter sets admit non-mark runs; actual mark positioning rejects. Other reserved
flags/types/formats and invalid metadata reject.

Font discovery follows live pattern/mask/marker uses in their resource context.
Dormant/overridden/inactive paint does not require programs unless layout advances
or effect bounds need them. Glyph masks retain platform-specific fitting, filter
guards and baseline rounding. The embedded family `Courier New` selects its
Windows one-cell span policy; catalog aliases cannot enable another program's
policy. [Native font references](../tests/README.md#qualification) record exactness
and platform distinctions.

Wrapping, vertical text, general/language-specific GSUB/GPOS, path stretch/auto
spacing, variable/color/bitmap fonts, CFF/CFF2 and WOFF/WOFF2 are unsupported.

## Resource limits and memory accounting

`SvgRenderLimits` independently bounds input/output, dimensions/pixels, XML
structure and path commands. `SvgRenderOptions.WithMemoryBudget` borrows a host
allowance for inspection, preparation, rendering and encoding; absent a budget,
convenience calls are explicitly ungoverned. Cancellation and
deterministic cleanup span discovery/preparation, parsing, rasterization and
encoding. Intermediate surfaces also consume limits. Caller streams stay open;
the host owns transactional output. PNG encoding checks encoded size and memory
admission, with cancellation during scanlines, CRC and writes. Parsing, rendering
and encoding have no synthetic work quotas; cooperative cancellation remains
available throughout. Supplied font/source bytes remain immutable
and valid for their documented borrowed lifetimes. A prepared document permits
one conversion; independently owned PNG outputs outlive the operation.

Memory accounting estimates dominant retained capacity and temporary peaks.
Roughly 20% unaccounted small objects/metadata is an intentional allowance, not a
per-workload coverage proof or instantaneous heap cap. Borrowed storage and
runtime/host overhead are separate. Library-owned snapshot metadata remains
charged through snapshot disposal; caller-owned font bytes are borrowed.
`ConvertToOwnedPng`/`ConvertToOwnedPngAsync` return disposable PNG storage whose
sibling scope survives conversion cleanup. Governed bare-memory output rejects;
caller-memory/stream destinations account only library-owned scratch.
[HTTP-prepared documents](HTTP_FONTS.md) share the allowance across cache, execution
and output; an options budget must share that resolver root. Refunds follow real
ownership lifetimes. Admission refusal ends the operation rather than
selecting an alternate rendering/storage route. Representative refusal,
cancellation, cleanup and retry checks protect these contracts.

## Qualification

[Tests and provenance](../tests/README.md) owns platform references, known
differences, exclusions and qualification commands. Accepted syntax alone does
not establish browser equivalence for every combination. [Benchmarks](BENCHMARKS.md)
defines performance measurement separately from correctness.
