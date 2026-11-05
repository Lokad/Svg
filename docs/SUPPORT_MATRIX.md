# SVG support

Lokad.Svg converts a bounded subset of static SVG to PNG on .NET 10 and .NET 11,
on Windows and Linux, without runtime package dependencies. The .NET 11 target
currently uses the RC SDK/runtime pinned in [global.json](../global.json). This is the current support
contract; it does not imply full SVG, CSS or browser conformance.

One DOCTYPE is accepted without loading an external subset. Internal general
entities may supply markup, attributes and text, including nested references.
The shared BCL reader expands them under the existing XML character ceiling
(`MaximumInputBytes`); exceeding that ceiling is malformed XML. Reader workspace
is admitted before parsing and held through preparation. Parameter/external entity
declarations and other DTD declaration kinds reject as `MalformedInput`. The
[XML declaration tests](../tests/Lokad.Svg.Tests/SvgDoctypeTests.cs) cover this
boundary consistently through parsing and font discovery; the
[entity tests](../tests/Lokad.Svg.Tests/SvgInternalEntityTests.cs) protect expansion,
resource selection, cancellation and storage ownership.

## Supported SVG subset

| Area | Accepted features |
| --- | --- |
| Structure | Root/nested `svg`, `g`, `a`, `defs`, `symbol`, local `use`, forward references and conditional `switch`. |
| Geometry | Rectangles/rounded rectangles, circles/ellipses, lines, polygons/polylines, line/quadratic/cubic/arc paths and compound fills. |
| Placement | Translation, rotation, skew, reflection, nonuniform affine maps, viewBox/aspect maps and viewport clips. |
| Paint | Solid colors, linear/radial gradients, bounded repeating patterns, fill/stroke opacity and paint order. |
| Composition | Group opacity, clip paths, alpha/luminance masks, markers and the filter primitives below. |
| Content | Embedded base64 PNGs, static CSS and supplied-font labels/spans/text paths. |

Script execution, animation, a mutable DOM, `foreignObject` HTML, perspective and
arbitrary external documents are unsupported. Ordinary conversion does not fetch resources
or discover machine fonts. Unsupported visible behavior reports
`UnsupportedFeature`; malformed input, unresolved references and exceeded limits
have distinct [error categories](../src/Lokad.Svg/SvgErrorCode.cs).

Foreign editor `namedview` and W3C SVG test-description `SVGTestCase` containers
are inert when their descendants are also foreign. Their nodes still count toward
structural limits. SVG descendants and unknown nonempty foreign wrappers reject.
Descriptive `xlink:title` metadata is inert on admitted elements and `defs`.
Namespace aliases use the XLink URI; XML/input and structural limits still apply.
Unknown unqualified attributes and names inapplicable to their element are inert:
for example, `rect r`, `g x` and lowercase `viewbox`. Attribute names are literal;
they are not corrected to a different SVG name. Applicable SVG 1.1/SVG 2 features
retain their value validation and unsupported outcomes. This rule does not admit
unknown CSS declarations or change reserved XML/XLink handling.
Unqualified lowercase `on*` event attributes are inert in static conversion,
including definitions, stylesheets, filters and text spans/paths. Their values
never affect paint, font selection or resource resolution. SVG `script` elements
with character-data content are inert too: their URLs are not resolved, and their
values consume no rendered-text allowance. Empty retained markers preserve lexical
node accounting and text-node boundaries. Element children inside scripts and
animation elements remain unsupported. Scripts retain their authored position in
switch selection and pattern content inheritance.

### Diagnostics

`SvgException.SourceDiagnostic` carries available element/ID, attribute or property,
value and one-based XML location. Retained text keeps its source location; direct
text instances also identify their referencing element. Unavailable fields are
empty/null; strings are bounded to 256 UTF-16 code units. One failure is reported.
An earlier unsupported source keeps its diagnostic when a later text node rejects. Duplicate-ID
rejections identify the second declaration's `id` attribute, including resources
registered after their children are parsed.

`FontDiagnostic` identifies the requested family/weight/style, selected face and
fallback when known. Missing-glyph indices count Unicode scalars in normalized
layout text, independently of UTF-16 and original XML character indices.

### Document sizing and inspection

Root lengths accept numbers, px/in/cm/mm/pt/pc/q and percentages. With neither
dimension, the default is 300×150, including with a viewBox. One dimension uses
the viewBox ratio, or the other axis's default without a viewBox. Percentages use
the caller viewport or the corresponding 300/150 base. A caller viewport replaces
absent/absolute root dimensions. Supported CSS, including later rules, participates
in sizing; winning root `auto`/`inherit` dimensions resolve as 100%.

Child percentages use the logical viewport, or viewBox user-space dimensions.
Nested viewports keep their own percentage bases, aspect mapping and physical
clips. Raster scaling does not change logical layout or those bases.

The outer root's CSS `background-color` paints the SVG image canvas behind element
content, independently of root opacity and visibility. It supports ordinary color
syntax and `currentColor`; `initial`, `unset` and root `inherit` use transparent.
Backgrounds on other SVG elements have no effect where their styles are admitted.
Outer-root `padding` and its four side properties accept nonnegative fixed CSS
lengths and percentages, with ordinary declaration priority. Nonzero bare numbers
are not CSS padding lengths. Percentages use the natural image container width
on every edge. Padding moves and clips content without enlarging the logical
viewport or PNG; SVG percentage bases and text layout remain independent.
Padding on other SVG elements has no effect where styles are admitted.
Root borders support fixed widths, width keywords and `solid`, `none` or `hidden`
styles, including side declarations, color alpha, `currentColor` and declaration
priority. Border insets participate in the same fixed image box as padding;
`box-sizing` accepts `content-box` and `border-box` in that image context.
Borders on other SVG elements have no effect where styles are admitted. Rounded
or other border styles, root filters and root clip paths remain unsupported.

The natural image viewport rounds before uniform output scaling; extra pixels in
the final raster are padding. Tiny or very large logical sizes retain their admitted
sizing behavior when native image layout cannot represent them.

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
Local uses retain rectangle source strokes independently of fill/stroke material.
Affine uses also retain rectangle fills and hidden path/point-list source commands;
visibility continues to control painting independently of geometry retention.
Finite singular element maps suppress painting while retaining object bounds for
enclosing effects. Their source-local effects do not paint. Root `transform`
attributes remain unsupported; paint-server transforms keep their own admission rules.
Nonuniform symbol viewBox maps preserve retained source fills and stroke outlines,
including solid, gradient and pattern paints. Lowered polygons without retained
source commands remain unsupported under nonuniform symbol scaling.

Stroke widths, dash entries and offsets accept bare numbers and `px`, `in`, `cm`,
`mm`, `pt`, `pc`, `q` lengths. Conversion precedes numeric range admission; zero
widths avoid resources needed only by that stroke. Percentage and font-relative
stroke metrics remain unsupported.

All-zero dash lists use a solid stroke; zero-length dashes with positive gaps
retain cap behavior. Dash placement follows authored endpoint order. Zero-size
rectangles paint nothing. Missing circle radii/path data and empty point lists
paint nothing; an ellipse copies its one supplied radius to the missing axis.
Repeated/closing vertices and one/two-point lists retain ordinary stroke/marker
rules. Whitespace-only paths remain subject to input and structural limits.

Rectangle and oval coordinates/radii, line endpoints, local-use placement,
nested/image dimensions, user-space mask regions and pattern grids accept bare
numbers and `px`, `in`, `cm`, `mm`, `pt`, `pc`, `q`. Unit conversion precedes each
consumer's numeric/sign checks; geometry retains its bounded coordinate range.
Existing viewport percentages retain their axis/diagonal bases. Line endpoints
do not accept percentages; font-relative geometry lengths remain unsupported.
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
retained symbol/pattern content is unsupported. Rendered group references retain
source-group opacity isolation and filters. Source-group clip/mask bindings and
nested viewports remain unsupported. Local uses may reference named uses; authored
placement and presentation remain separate from source ancestors.
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
An absent `gradientUnits` inherits; an invalid explicit value selects the initial
`objectBoundingBox` units, overriding the template. Values are case-sensitive and
are not trimmed.

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
Definitions may appear at the root, as direct `defs` children, or within rendered
`g` groups. Group styles are inherited; external consumers retain their own placement.
The pattern container accepts only opacity 1; children retain opacity. XML overflow
accepts hidden/visible; CSS accepts hidden only. Acyclic nested pattern paints share
retained tile content. Their content graph supports up to 32 vector-scene levels,
including masks; collapsed template alias chains add no levels. Content cycles
reject as unsupported; excessive content depth returns `LimitExceeded`.

Aliases with local element children use those children, including empty,
descriptive or foreign elements. Childless/comment/text-only aliases inherit
content. An alias can replace zero/omitted template dimensions; effective-zero
tiles remain inert. A hidden template supplies no dimensions/content, while a
visible alias may supply its own. An alias's own hidden display suppresses borrowing.

### Clips, masks and composition

Clip paths accept rectangles/rounded rectangles, paths, polygons/polylines, circles, ellipses,
restricted uses, supplied text, ordered child unions, compound winding, both resource
units and affine maps. Text uses the ordinary shaping, positioning, fitting and
decoration pipeline with black fill and no stroke/filter. Ignored text paint servers
need no resolution. Text selects ordered mask replay, retaining child opacity and
visual bounds separately from geometric path coverage. See the
[native text-clip controls](../tests/Research/text-clip/README.md).
Group children contribute no clip silhouette; their IDs and descendant
IDs remain available to independent drawing instances. A clip-resource `use` that
targets a group also contributes no contour. The
[child-role controls](../tests/Research/clip-child-roles/README.md) protect these
distinct roles and selected text-font acquisition. Direct line children and
`transform` on `clipPath` reject. Clip-resource paint does not paint a silhouette;
typography, display, visibility and explicit opacity inheritance retain their roles.
A geometric clip-resource `use` must reference
one contour; plain rectangles and empty/compound sources through that use reject.
Multiple uses and mixtures with direct children form an ordered union; local
geometry may be defined later. Both resource units apply to referenced content;
object-box percentages use normalized geometry before the referencing canvas map.
Referenced content rejects its own clip binding. Direct rectangles and compound
paths remain supported.
Clip-child strokes affect the resource's visual enclosure but never paint its
silhouette. Stroke presentation can inherit from `clipPath`; referenced geometry
does not need to resolve an ignored stroke paint server.

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
immediate container or its initial value. `clip-path` on `clipPath` intersects the
resource; admitted children may have their own clips, including within a union.
Nested object-box clips on retained resource geometry reject. Point-list clips
close implicitly for filling; fewer than three points contribute no coverage.

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
Active unknown or unsupported property names reject; CSS error recovery remains
unsupported.

`:hover`, `:active`, `:focus`, `:focus-visible` and `:focus-within` are admitted
as inactive states in image conversion. Such components retain class specificity;
their rules cannot select paint, fonts or resources. Inactive-only rule blocks
still obey declaration syntax/size and rule-count limits. A selector list with an
active member validates the shared declarations normally. `float` is inert;
SVG image conversion has no surrounding box flow.

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
SVG image mode always generates the outer viewport even under root `display:none`;
its computed display remains available to children that explicitly inherit it.
`currentColor` resolves against each element's effective color; `color:currentColor`
uses inherited color. Comma RGB/RGBA channels must all be numbers or percentages;
space syntax may mix them. RGB(A)/HSL(A) aliases accept slash alpha.

Token rules:

- SVG separators/padding use ASCII whitespace; non-ASCII characters retain their
  meaning and cannot normalize into another number, keyword or family.
- Length numbers and units form contiguous tokens. Lists retain required values
  around commas and reject leading/repeated/trailing commas. Transform functions
  may be comma/whitespace separated; SVG function/enumeration names retain case.
- Scalars and inline property values have 256-character token bounds.
  Gradient stop/color limits retain their authored tokens.
- Local href/URL fragments decode UTF-8 percent escapes once; malformed escapes,
  invalid UTF-8 and encoded ASCII whitespace reject. URL function names and filter
  `none` ignore ASCII case; identifiers do not. Surrounding/trailing URI padding is
  accepted, but leading whitespace inside quoted URIs and fragment whitespace reject.

Imports, media/supports rules, other pseudo selectors, sibling selectors, layout,
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

Filters accept `feGaussianBlur`, `feOffset`, `feFlood`, `feColorMatrix`, `feComposite` over/in,
`feMerge`, `feBlend` normal/multiply/screen and `feDropShadow`. Inputs are
SourceGraphic, SourceAlpha or earlier exact result names. Both unit systems,
primitive/filter regions, local CSS references and sRGB/linearRGB spaces work.
Color matrices support matrix/saturate/hueRotate/luminanceToAlpha, with finite
single-precision coefficients and the declared type's exact value count. Omitted
values use identity; luminanceToAlpha ignores values. Matrix evaluation uses straight
channels, clamps, then premultiplies. Alpha-generating matrices can fill the primitive
region beyond source ink. [Native controls](../tests/Research/filter-color-matrix/README.md)
cover transfer, sampling, crops and existing filter consumers.
Result/input names retain case and whitespace; missing/forward/cyclic references
fail explicitly. Intermediate surfaces obey dimension, pixel and memory limits.
Pixel evaluation follows the final result's dependencies. Unread branches retain
reference, region and mapped-scalar validation without materializing pixel planes.

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
Final offsets retain earlier image pixels outside the output region when replay needs them.
Empty tile restoration paints nothing. Image placement retains the owning bitmap's
sampling phase; enclosing opacity stores restored pixels before paint-alpha modulation.
Its painted-output bounds determine the sampling origin; source capture retains
its recording context.

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

Legacy SVG `font`/`font-face` definitions inside `defs` are dormant: they do not
register faces, fetch programs or change the live text request. Text still needs
the host's supplied TrueType program. The removed SVG 1.1 font format is not an
implemented font source; [declaration controls](../tests/Research/legacy-font-declarations/README.md)
keep this boundary separate from supplied-program rendering.

| Area | Accepted boundary |
| --- | --- |
| Programs | Standalone static TrueType, face index 0, composites and bounded hinting; unsupported executed instructions fail. |
| Characters | Horizontal LTR ASCII, covered Latin combining sequences and supplementary scalars; no bidi, joining or general complex-script shaping. |
| Layout | Ordered text/CDATA/whitespace, nested spans, inherited/per-span styles, scalar/list x/y/dx/dy/rotate, anchors, spacing, explicit lines, textLength spacing/spacingAndGlyphs and alphabetic/middle/hanging baselines. |
| Painting | Affine solid/gradient/pattern fills and strokes, dashes/non-scaling widths/paint order and solid-color underline/overline/line-through. |
| Text paths | One local open/closed geometry path per run, transformed/curved paths, advances/kerning, length/percentage startOffset, anchors/tangents, align/exact spacing and mixed direct/path text. |
| Ligatures | Default Latin/DFLT Basic Latin, one unfiltered type-4 lookup, format-1 subtables, coverage 1/2 and 2–8 components in font order. |

Text x/y/dx/dy accept finite numbers, fixed CSS length units and `em` from the
declaring element's computed font size. Lists retain that basis across descendant
font changes and instance replay. Rotation is unitless; percentages and `ex` text
positions remain unsupported.

Validated CSS `text-align` declarations are inert on supported preformatted text
and containers. Explicit positions and `text-anchor` retain their meaning;
[native controls](../tests/Research/preformatted-text-align/README.md) cover styled
spans and inherited path text. Auto-wrapped text is outside this layout contract.

CSS `line-height` is validated and inert in the supported explicit-line layout;
it creates no line boxes or baseline offsets. CSS `white-space:pre` currently uses
the legacy `xml:space="preserve"` normalization: tabs and newlines become spaces.
Automatic SVG2 line layout is outside this contract. Independent
[line-layout controls](../tests/Research/text-line-layout/README.md) distinguish
these behaviors from explicitly positioned lines and preserve native platform pixels.

`text-anchor` accepts `start`, `middle`, `end`, `inherit`, `initial` and `unset`
through presentation attributes and admitted inline/stylesheet contexts. Root,
group, text and span scopes share inheritance and cascade rules. Each chunk uses
its first addressed character's anchor; absolute x/y starts another chunk, while
an anchor-only change preserves continuous shaping. Text paths share spans' font,
paint, whitespace and anchor cascade, including their own inline/stylesheet rules.
Placement uses the computed path anchor; inapplicable path x/y/dx/dy/rotate
attributes do not introduce positioning lists. Child opacity/filter, transforms
and textLength remain unsupported. The
[anchor controls](../tests/Research/text-anchor/README.md) protect these interactions.
The [path-style controls](../tests/Research/text-path-styles/README.md) protect
CSS precedence, inherited anchors, nested spans and mixed direct/path text.
The [shared-range control](../tests/Research/text-shared-anchor/README.md) retains
uniform child anchors when direct text and path text are joined.

CSS `text-transform` admits `uppercase`, `lowercase`, `capitalize`, `none`, `inherit`, `initial`
and `unset` through root, group, text and span inheritance/cascade. Conversion uses
pinned Unicode 17 full default mappings, including expansions and contextual final
sigma, with unspecified/English, Turkish (`tr`), Azerbaijani (`az`) or Lithuanian
(`lt`) content language, including language subtags. Original SVG scalar positions
precede conversion; shaping, fitting and font coverage use emitted text. Context
continues across source chunks, styles and path boundaries within one text element;
the [case controls](../tests/Research/text-case-mapping/README.md) retain independent
literal evidence where pinned browser CSS processing differs. Existing character/
glyph bounds and font-shaping limitations still apply. Capitalization titlecases
the lowercase first letter unit and preserves other characters. Cased word starts
follow Unicode 17 UAX29 rules, including medial punctuation, numeric prefixes and
connectors. Conditional language rules can add or remove combining dots; empty
spans do not select a font or become another element's text. Emitted characters
remain subject to the font-shaping subset below. Complete accent-heavy language
controls and their independent literal counterparts protect the emitted clusters;
[shaping evidence](../tests/Research/svgweb/text-canonical-shaping.json) records
their exact scope. Other content-language tailoring remains unsupported.

Canonical Unicode 17 decomposition, stable combining-class ordering and
font-covered recomposition precede selected Latin/DFLT `ccmp` substitution.
The admitted substitutions are unfiltered single, ligature and bounded
class-context substitutions calling single lookups. Shared source clusters retain
original paint/position ownership. Selected GPOS `mark`/`mkmk` supports base,
actual ligature-component and mark attachment with format-1 anchors and validated
GDEF classes/filter sets. Contextual kerning admits bounded class contexts calling
single X-advance adjustments. Marks and default-ignorables keep their context
through positioning before their advances are zeroed. Unsupported selected lookup
forms still reject; attached marks on text paths remain unsupported.

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
positive. Presentation attributes and SVG inline styles accept unitless sizes;
stylesheet rules require units for nonzero sizes. The
[source-context regression](../tests/Research/svgweb/font-contexts.json) preserves
this distinction through inheritance and preparation. Family inherit/initial/unset work; those keywords reject for weight,
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
one horizontal format-0 subtable. Format-1 type-9 extensions preserve the supported
inner lookup's behavior, outer flags and execution order; recursive/mixed-type
extensions reject. Type-2 pairs admit RIGHT_TO_LEFT, IgnoreBaseGlyphs
and IgnoreLigatures with validated GDEF. IgnoreMarks, mark-attachment classes and
format-1 filter sets also apply to admitted positioned-mark runs. Other reserved
flags/types/formats and invalid metadata reject.

Font discovery follows live pattern/mask/marker uses in their resource context.
Dormant/overridden/inactive paint does not require programs unless layout advances
or effect bounds need them. Glyph masks retain platform-specific fitting, filter
guards and baseline rounding. The embedded family `Courier New` selects its
Windows one-cell span policy; catalog aliases cannot enable another program's
policy. [Native font references](../tests/README.md#milestone-and-release-qualification) record exactness
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
