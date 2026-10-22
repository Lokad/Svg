# SVG support

Lokad.Svg renders a subset of static SVG to PNG on .NET 10, on Windows and Linux,
without runtime package dependencies. This document describes the current
implementation. It does not claim full SVG or browser conformance.

This matrix covers the 0.3.0-preview.1 development tree. Published 0.2.0 has a
narrower font boundary; see [release notes](../CHANGELOG.md).

Scripts/events, animation, a mutable DOM, foreignObject HTML and arbitrary external
documents are unsupported. Rendering does not discover filesystem/network resources.

## Supported SVG subset

| Area | Supported behavior |
| --- | --- |
| Documents and instances | Root/nested `svg`, `g`, `a`, `defs`, `symbol` and local `use`; forward definitions, aliases, inherited presentation, viewBox/aspect maps and viewport clipping. |
| Geometry | Rectangles and rounded rectangles, circles/ellipses, lines, polygons/polylines, and line/quadratic/cubic/arc paths; nonzero/even-odd compound fills. |
| Transforms | Composed translation, rotation, skew, reflection and nonuniform affine scaling through groups, viewports and local instances. |
| Strokes | Solid, linear/radial-gradient and pattern strokes; fractional widths, ordinary/non-scaling strokes, caps/joins, miter limits, finite dashes/offsets and pathLength normalization. |
| Paints | Solid colors, linear/radial gradients and bounded repeating patterns; independent fill/stroke opacity and paint order. |
| Composition | Nested element/group opacity, clip paths and alpha/luminance masks; local/forward references, resource transforms and object-box/user-space units. |
| Embedded images | Base64 data-URI PNGs, affine placement, aspect alignment, clipping, masking and opacity. |
| Static CSS | Embedded stylesheets, presentation attributes and inline styles; the selector/property subset below, cascade, inheritance and `!important`. |
| Markers | Local start/mid/end markers on lines, point lists, paths and instances, with vector/group/use content and inherited presentation. |
| Filters | Blur, offset, flood, over/in composite, merge, normal/multiply/screen blend and drop shadow, with bounded intermediate surfaces. |
| Text | Supplied-font labels, nested spans and text paths; positioning, anchors, spacing, fitted glyphs, fill/stroke paints and solid decorations. |

The restrictions below are part of this support boundary. Supported elements do
not imply that every SVG attribute, CSS property or resource combination is
implemented. Unsupported visible behavior reports `UnsupportedFeature`;
malformed input, unresolved resources and exceeded limits have separate
[error categories](../src/Lokad.Svg/SvgErrorCode.cs).

### Diagnostics

`SvgException.SourceDiagnostic` identifies the element/ID, offending attribute or
property/value and one-based XML line/column where captured. Retained text keeps
its original location; direct local text instances also identify the referencing
element ID. Unavailable fields are empty/null, and diagnostic strings are bounded
to 256 UTF-16 code units. Errors retain their existing categories and report one
failure; this is not a whole-document validation API.

`FontDiagnostic` includes requested family/weight/italic, selected face and
fallback status when known. Missing glyphs identify the Unicode scalar and its
zero-based scalar index in the normalized layout run before ligature substitution.
That index is independent of UTF-16 indices and original XML character positions.
Explicit snapshots still require prepared requests; catalog selection and
caller-controlled fallback retain their existing behavior.

### Document sizing and inspection

`SvgConverter.InspectDimensions` reports the CSS-resolved width/height, viewBox
and aspect mapping, logical viewport and dimension/fallback provenance. It reads
bounded XML/CSS without preparing fonts, geometry, images or raster surfaces.
Inspection does not assert that every drawing feature is supported. Output scale
and raster caps do not restrict logical metadata; parsing limits still apply.

`SvgRasterBounds`, supplied through the six-argument `SvgRenderOptions`
constructor, opts into maximum raster width, height and/or pixel count. At least
one positive bound is required. `GetRasterSize(outputScale, bounds, limits)` plans
the same output without fonts or raster allocation. A uniform scale no greater
than `OutputScale` fits those bounds and the hard raster limits; logical sizing,
percentages, viewBox and clipping remain unchanged. Bounds never request upscaling.
Integer dimensions use nearest rounding away from zero, at least one pixel per
axis, with conservative scale reduction when the rounded product exceeds a cap.
An empty physical viewport clip still paints no content in that minimum canvas.
Without bounds, existing excessive-output failures remain. Memory/work and
intermediate/output limits remain failures rather than triggering a smaller retry.

Root dimensions accept bare numbers, px/in/cm/mm/pt/pc and percentages. Both
missing dimensions use 300×150, including with a viewBox. A single dimension uses
the viewBox ratio when present, or the other axis's default when absent. Percentages
use an explicit viewport or the corresponding 300/150 fallback base. The existing
caller viewport overrides absent/absolute root dimensions. Supported CSS applies
before this resolution, including later embedded rules. Winning root CSS `auto`
and `inherit` dimensions resolve as 100%, replacing XML dimensions. Unsupported
sizing units fail rather than silently defaulting.

Child percentage lengths use the resolved logical viewport when there is no
viewBox, including when a caller viewport overrides root dimensions. A viewBox
supplies its own user-space percentage bases; raster scaling does not change them.

### Geometry and strokes

Geometry retains authored curves through affine placement and stroke construction.
Fill and stroke paints may differ. Butt/round/square caps, miter/round/bevel joins,
dash phase and non-scaling widths apply through admitted local instances.
All-zero dash lists render as solid strokes; zero-length dashes with positive
gaps retain their ordinary cap-dependent behavior.
Dash placement follows authored endpoint order on horizontal and vertical lines.
Rectangular clips and viewports clip the full painted width of lines, including
strokes whose centerline lies just beyond an edge.
Rectangles support dashed solid/gradient/pattern strokes; zero-size rectangles
paint nothing. Line endpoints accept finite fractional bare-number/px values;
percentage endpoints and other endpoint units are unsupported.
Omitted circle radii and path data paint nothing. An ellipse with one omitted
radius copies the other resolved radius; omitting both paints nothing.
Omitted/empty point lists paint nothing. Repeated and closing vertices, and
one/two-point lists, follow the same fill/stroke rules for all numeric spellings;
markers retain the authored vertices.
Polygon/polyline point coordinates are bare SVG numbers; CSS length units such
as px are unsupported in these numeric lists.
Path numbers retain explicit signs and equivalent decimal/exponent spellings
through integer and general geometry routes.

Root and nested viewports preserve their own percentage bases, aspect maps and
physical clips. Instance presentation follows authored/inherited precedence;
referencing an alias excludes the source's ancestors. Missing/cyclic references
have categorized failures. Perspective transforms are unsupported.
An omitted or empty href makes a use element inert at admitted use locations;
an empty href takes precedence over xlink:href. Placement lengths and actual
child content still undergo the ordinary validation and limits.
Percentage rectangle positions, dimensions and corner radii resolve in each
instance's viewport, including nested viewBox and symbol instances.
Transform lists accept whitespace or comma separators between functions.
Length numbers and units form one contiguous token; SVG transform arguments are
bare numbers and do not accept CSS length units.
SVG transform function names and SVG-specific enumeration values, including
spreadMethod and preserveAspectRatio, retain their literal case.
ViewBox, transform, text-position and dash lists retain required values around
commas; leading, repeated and trailing commas reject during tokenization.

For rendered groups referenced by `use`, source-group opacity isolation, clips
or masks on the referenced group, and nested viewport content are unsupported.

### Gradients and patterns

Linear and radial gradients support objectBoundingBox/userSpaceOnUse units,
affine gradient transforms, local templates, pad/repeat/reflect spread and sRGB
color interpolation. Radial paints include focal-circle parameters. Gradient
templates retain inherited units, transforms and spread methods when the child
supplies a complete replacement vector. Patterns support both tile/content unit
systems, viewBox/aspect maps, fractional origins and sizes, affine placement and
bounded repetition. Tile content may include
ordered vectors, groups, local uses, opacity, gradients, embedded PNGs, admitted
filters and supplied-font text.
Definition ancestors with `display:none` do not disable patterns. A pattern's own
`display:none` suppresses its paint and aliases, including aliases with their own
children. An alias's own hidden display suppresses borrowed visible content too.
Visible aliases with their own children replace the template content. Inherited paint and visibility
still apply to tile content; `display:none` on rendered children suppresses them.
Font preparation uses the same resource display rules for supplied-font tile text.
Template content, including images, effects and supplied-font text, survives zero
or omitted base tile dimensions when an alias supplies nonzero effective dimensions.
Patterns whose effective tile size stays zero remain inert.

Current restrictions:

- Percentage coordinates in userSpaceOnUse gradients are unsupported, including
  when the units are inherited from a template. This includes omitted linear x2
  (initially 100%) and radial cx/cy/r (initially 50%) unless a same-kind template
  supplies numeric values. Object-box defaults and complete numeric vectors work.
- Gradient stops accept literal color/opacity attributes and stop inline styles,
  with per-property `!important` priority and source order.
  Stylesheet `stop-color`/`stop-opacity`, gradient resource/stop `currentColor`
  and gradient-container `color` are unsupported.
- Gradient interpolation in linearRGB is unsupported; filter operating color
  spaces have their own rules below.
- Nested pattern paints and recursive paint/resource dependencies are unsupported.

### Clips, masks and composition

Clip paths support rectangles (including rounded rectangles), paths, circles,
ellipses and restricted local uses, ordered child unions, compound fills,
user-space/object-box coordinates and affine maps. Clip bindings resolve through
local instances and forward definitions. Rectangular leaf clips follow the
target's own and inherited transforms in both definition orders.
Direct line, polygon, polyline and text children of clip paths are unsupported;
use equivalent admitted path geometry where applicable.
Alpha/luminance masks support ordered vector/text content, clipping, local uses,
both region/content unit systems and transformed finite regions.

Hidden clip children do not contribute to the silhouette; visibility inherits
from the resource's own ancestors and can be overridden by a child. Display on
definition ancestors does not disable the resource. A clipPath's own CSS
`display:none` makes its references contribute no clipping; an empty or hidden
child instead contributes an empty silhouette. Fill/stroke paint and
opacity do not suppress raw clip geometry.
Likewise, a mask's own CSS `display:none` removes its binding and region; a hidden
definition ancestor leaves it available. An empty or hidden mask child remains
an empty mask, suppressing its target rather than removing the binding.

IDs on clip rectangles, rounded rectangles, paths, circles and ellipses remain
available to ordinary local instances, with authored paint/stroke independent of
the clip.
Geometric leaves, local uses and gradient stops accept self-closing or balanced
XML forms, including inert comments/descriptions. Drawing children remain
unsupported on those leaves.

For one element, composition follows **filter → mask → clip → opacity**. A child
clip remains inside an ancestor's effect. Group opacity isolates the composed
children rather than multiplying each child's opacity independently.
Explicit `clip-path="none"` and `mask="none"` omit that element's binding; ancestor
clips and masks still apply.

Resource attribute/declaration locations have distinct boundaries:

- `clip-path` presentation attributes work on admitted rendered/resource children,
  including path/rectangle children of a clip resource. `clip-path` on the
  `clipPath` container itself is unsupported.
- `clip-path` in inline styles or stylesheets is unsupported. CSS `clip-rule`
  support does not enable that declaration.
- A `use` child inside `clipPath` supports one referenced contour. Plain rectangle
  instances and empty/compound sources are unsupported; direct rectangles and
  compound clip paths work.
- Embedded image targets can be clipped/masked and patterns can contain images,
  but an `image` child inside a mask is unsupported.

### Embedded PNGs

PNG decoding supports non-interlaced 8-bit RGB, RGBA, grayscale, grayscale-alpha
and indexed images, including tRNS and sBIT when every channel retains eight
significant bits. Chunk names/reserved bits, ordering, palette/transparency
relationships, CRCs and bounded decompression are validated. IDAT chunks must be
consecutive; transparency must precede image data and follow any palette.
Image placement supports finite geometry/viewBox percentages, meet/slice
alignments, `preserveAspectRatio="none"` and general affine maps.
Images with absent or empty href values paint nothing.
Percentages resolve in each instance's viewport, including nested viewBox and
symbol instances. Omitted image dimensions use the PNG's intrinsic size; if one
side is specified, the omitted side follows its intrinsic aspect ratio.
Images ignore inherited and CSS fill/stroke paint; display, visibility and
element/group opacity still apply.
Referenced images retain source opacity, clips, masks and filters through viewport
clipping; filters can move source pixels into the viewport before output is clipped.
Affine filter replay for image sources remains unsupported; ordinary affine image
placement, clipping and masking are admitted.

Unsupported: interlaced/16-bit PNG, reduced-significance sBIT, color-management or
orientation conversion, JPEG/GIF/WebP and external image fetching.

### Static CSS

Selectors include bounded unescaped ASCII SVG element names, IDs and classes,
id/class presence or exact-equality attribute selectors, selector lists,
element+ID/class, descendants and children. CSS separators use ASCII whitespace;
other Unicode characters are retained in identifiers and rejected when outside
this selector subset. Rules apply through admitted
root, group, symbol, nested-SVG and resource trees. Specificity, source order,
inline priority, inheritance and `!important` are implemented.
Empty, whitespace-only and comment-only stylesheets are no-ops. XML namespace
declarations on `style` and `defs` elements are accepted.
CSS comments work in embedded, inline and gradient-stop styles, preserving token
boundaries and literal text inside quoted strings.

Declarations cover color/fill/stroke and their opacity, stroke width/caps/joins/
miter/dashes, display/visibility/opacity, fill-rule/clip-rule, paint-order, filter,
marker properties, overflow, root width/height and the text/font properties
listed below. Each property retains its target/value restrictions; this is not a
general CSS declaration parser. `paint-order` accepts normal or distinct
fill/stroke/markers lists, appending omitted operations.
`pathLength` is an XML number attribute, not a CSS property; declarations in
inline styles or stylesheets report `UnsupportedFeature`.
CSS width/height declarations on rectangles and embedded images report
`UnsupportedFeature`; use XML dimension attributes on these elements. Dimension
declarations on nonapplicable targets do not affect rendering.
Solid colors and paint keywords ignore surrounding ASCII whitespace in attributes and
CSS; literal color limits apply to the authored token before normalization.
Non-ASCII whitespace remains part of scalar tokens; unsupported values reject
instead of becoming different colors, numbers or keywords, including font
presentation attributes and gradient-stop declarations. Unquoted family identifiers,
quoted family names and local references retain their Unicode characters; family
names containing non-ASCII whitespace do not become CSS global keywords.
Local `url(...)` function names and the filter `none` keyword ignore ASCII case;
referenced identifiers keep their case.
Comma-separated RGB/RGBA channels must use either numbers or percentages
throughout; modern space-separated channels may mix them. Modern RGB/RGBA and
HSL/HSLA function aliases accept an optional slash-separated alpha value.
`color:currentColor` uses the inherited color. Fill/stroke `currentColor` resolves
against each element's own color, including after inheritance and local instancing.

Unsupported: imports, media/supports rules, pseudo-classes/elements, sibling
selectors, layout, variables, animation and web fonts. The resource declaration
exceptions above also apply. `vector-effect="non-scaling-stroke"` is an SVG
attribute; CSS/inherited vector effects are unsupported.

### Markers and filters

Markers support userSpaceOnUse/strokeWidth units, refX/refY, finite dimensions,
viewBox/aspect maps, fixed/auto/auto-start-reverse orientation and hidden/visible
overflow. Lengths include absolute units, known-axis percentages and em/ex from
supplied font metrics. Paint/visibility and stroke presentation inherit through
marker content. A marker's display property does not suppress its content;
a symbol's `display:none` does suppress its instances.

Supported filter primitives are `feGaussianBlur`, `feOffset`, `feFlood`,
`feComposite` over/in, `feMerge`, `feBlend` normal/multiply/screen and
`feDropShadow`. Filters support SourceGraphic, SourceAlpha, earlier local results,
filter/primitive regions, both unit systems, sRGB/linearRGB operating spaces and
local CSS filter references. Intermediate surfaces and primitive work are bounded.
An element suppressed by its own or an ancestor's `display:none` does not activate
filters. Visible elements with no fill or transparent paint can still produce
filter output, such as a flood.
Empty groups also run their filters over transparent input, including self-closing
groups; display suppression and zero object-box filter regions still paint nothing.
A referenced filter with no primitives produces empty output. This differs from
an absent/none filter, which preserves the source graphic.
Offscreen source geometry does not suppress visible output generated by a filter.
Filtered lines retain their full stroke bounds through target placement and local
instances.
`feDropShadow` supports deviations up to 135 device pixels on each axis after
mapping and float rounding; transforms and output scale affect this limit.
Larger shadow deviations fail with `UnsupportedFeature`. `feGaussianBlur` uses
rescaling for larger deviations and saturates at 532 device pixels per axis.

Unsupported: backdrop inputs, external filter images, displacement, turbulence,
lighting, morphology, convolution, arithmetic composite, other blends,
backdrop-filter and CSS filter-function lists. Recursive effects and
forward/missing result names have categorized failures.

### Text and fonts

Fonts are explicit host-supplied manifest/catalog/provider resources. The host
owns acquisition, credentials, retries and caches; rendering does not discover
machine fonts or synthesize styles. Required unavailable/corrupt programs and
missing glyphs fail explicitly. [FontProbe](../tools/Lokad.Svg.FontProbe/README.md)
shows local/HTTP usage.

`SvgFontFaceRequest.FromCss` preserves ordered named/generic families, quoting,
escapes and inheritance; punctuation inside quotes remains literal, including
quoted "serif". `SvgFontSelectionPolicy` supplies host generic mappings and ordered
fallback restrictions for snapshots/prepared documents. Missing authored families
are skipped before host fallback. SVG-based preparation lazily acquires later
families for missing admitted scalars, selecting one program per covered interval.
Kerning/ligatures stay within a selected face; spans, positioning and text paths
retain their layout. Missing-everywhere requests keep scalar/index/source
diagnostics. Request-only snapshot creation has no coverage to inspect and keeps
first-face selection; callers may supply prepared alternatives explicitly. Raw
conversion uses only its supplied snapshot. After style matching, CSS requests
use [directional weight fallback](https://www.w3.org/TR/css-fonts-4/#font-style-matching),
including the 400–500 range. Literal `SvgFontFaceRequest` constructor requests
retain nearest-weight selection and existing fallback behavior.

CSS family requests admit at most 256 families and 1,024 UTF-16 code units per
decoded name. Both authored and canonical list text must fit 4,096 code units;
normalization that exceeds this bound fails immediately with `LimitExceeded`.
SVG scalar attributes and inline property values retain their 256-character bound.
SVG font-weight values accept integers from 1 through 1000, normal and bold;
other numeric/relative forms reject. Explicit public font request objects keep
their documented positive-weight catalog selection contract.

| Area | Supported boundary |
| --- | --- |
| Programs | Standalone static TrueType, face index 0, including composites and bounded hinting. Other executed instructions fail explicitly. |
| Characters | Horizontal LTR ASCII, tested precomposed Latin and covered supplementary scalars; no bidi/joining/complex-script or mark shaping. |
| Layout | Text/CDATA/whitespace order, nested `tspan`, inherited/per-span family/size/weight/style/paint/visibility, scalar/list x/y/dx/dy/rotate, start/middle/end anchors, letter/word spacing, explicit lines, textLength spacing/spacingAndGlyphs and alphabetic/middle/hanging baselines. |
| Paint | Affine glyph solid/gradient/pattern fills and strokes, dashes, non-scaling strokes and paint order; solid underline/overline/line-through decorations. |
| Text paths | One local open/closed geometry path per run, transformed/curved paths, supplied advances/kerning, length/percentage startOffset, anchors/tangents, `method="align"`, `spacing="exact"` and mixed direct/path text. |
| Ligatures | Default Latin/DFLT Basic Latin, one unfiltered type-4 lookup, format-1 subtables, coverage 1/2 and two to eight components in font order. Nonzero spacing or positioned-span boundaries prevent merging. |

Pair positioning selects the default Latin language system before DFLT, sharing
selection with ligatures. Feature tags repeated in other language systems do not
invalidate it. Selected GPOS kerning, including zero adjustments, supersedes legacy
kern without parsing unused legacy contents; all SFNT table ranges remain validated.
Required legacy kerning admits one horizontal format-0 subtable.

Type-2 pair lookups admit RIGHT_TO_LEFT (irrelevant for that type), IgnoreBaseGlyphs
and IgnoreLigatures with bounded GDEF classes. Each lookup selects its next eligible
partner within a font/size/absolute-position chunk. IgnoreMarks, mark attachment
classes and format-1 mark-filtering sets admit non-mark label runs with validated
GDEF metadata; actual mark positioning is unsupported. Reserved flags, invalid
classes/set indices and table ranges fail explicitly.

Ordinary shaped text preserves fractional offsets when scaled and rounds paint
origins after absolute span resets. Text paths and fitted-spacing text use their
own character-metric rules. Fitting/coverage matches platform-specific numerical
rules; the exact embedded family `Courier New` enables its Windows one-cell span
policy, which a catalog alias to another program cannot enable. See
[font rasterization](RASTERIZATION_DESIGN.md#supplied-fonts) for internal invariants
and [font qualification](../tests/README.md#qualification) for evidence.
Zero textLength preserves natural layout. Spacing adjustment also preserves
natural metrics and anchoring when shaping leaves only one glyph; positive
spacingAndGlyphs still scales that glyph.

Unsupported: wrapping, vertical text, language-specific/general GSUB/GPOS, other
lookup flags/types/formats, path stretch/auto spacing, variable/color/bitmap fonts,
CFF/CFF2 and WOFF/WOFF2.

## Resource limits and memory accounting

[SvgRenderLimits](../src/Lokad.Svg/SvgRenderLimits.cs) bounds input/output size,
dimensions, pixels, XML structure, path commands, accounted storage and shared
work. Cancellation and deterministic cleanup apply through preparation, parsing,
rasterization and encoding. Supplied font/source bytes remain immutable and valid
for their documented borrowed lifetime. Caller streams stay open; hosts own
transactional output handling.

Memory accounting is approximate: directionally no more than **20% of
conversion-attributable live memory** should be unaccounted for across
representative workloads. This is an engineering target, not an exact guarantee
at every instant. Shared runtime/host overhead and borrowed storage are identified
separately. Conservative estimates and aggregate allowances cover pixel buffers,
decoded images, intermediate surfaces, geometry, glyph data and large collections,
while avoiding excessive overcharging.

Ownership audits follow material memory impact. Byte-exact small-object accounting,
individual metadata receipts, GC-round retirement proofs and precise accounting of
bookkeeping machinery are not requirements. Existing representative normal and
adversarial checks cover limits, cancellation/retry, cleanup and output integrity.

## Qualification

[Tests and provenance](../tests/README.md#qualification) defines platform reference
coverage and the remaining private deployment gate. [Benchmarks](BENCHMARKS.md)
records dated performance measurements.
