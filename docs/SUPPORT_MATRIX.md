# SVG support

Lokad.Svg renders a subset of static SVG to PNG on .NET 10, on Windows and Linux,
without runtime package dependencies. This document describes the current
implementation. It does not claim full SVG or browser conformance.

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
before this resolution, including later embedded rules; unsupported sizing units
fail rather than silently defaulting.

### Geometry and strokes

Geometry retains authored curves through affine placement and stroke construction.
Fill and stroke paints may differ. Butt/round/square caps, miter/round/bevel joins,
dash phase and non-scaling widths apply through admitted local instances.
Rectangles support dashed solid/gradient/pattern strokes; zero-size rectangles
paint nothing. Line endpoints accept finite fractional bare-number/px values;
percentage endpoints and other endpoint units are unsupported.

Root and nested viewports preserve their own percentage bases, aspect maps and
physical clips. Instance presentation follows authored/inherited precedence;
referencing an alias excludes the source's ancestors. Missing/cyclic references
have categorized failures. Perspective transforms are unsupported.

### Gradients and patterns

Linear and radial gradients support objectBoundingBox/userSpaceOnUse units,
affine gradient transforms, local templates, pad/repeat/reflect spread and sRGB
color interpolation. Radial paints include focal-circle parameters. Patterns
support both tile/content unit systems, viewBox/aspect maps, fractional origins
and sizes, affine placement and bounded repetition. Tile content may include
ordered vectors, groups, local uses, opacity, gradients, embedded PNGs, admitted
filters and supplied-font text.

Current restrictions:

- Percentage coordinates in userSpaceOnUse gradients are unsupported.
- Gradient stops accept literal color/opacity attributes and stop inline styles.
  Stylesheet `stop-color`/`stop-opacity`, gradient resource/stop `currentColor`
  and gradient-container `color` are unsupported.
- Gradient interpolation in linearRGB is unsupported; filter operating color
  spaces have their own rules below.
- Nested pattern paints and recursive paint/resource dependencies are unsupported.

### Clips, masks and composition

Clip paths support the geometry above, ordered child unions, compound fills,
user-space/object-box coordinates and affine maps. Clip bindings resolve through
local instances and forward definitions. Alpha/luminance masks support ordered
vector/text content, clipping, local uses, both region/content unit systems and
transformed finite regions.

For one element, composition follows **filter → mask → clip → opacity**. A child
clip remains inside an ancestor's effect. Group opacity isolates the composed
children rather than multiplying each child's opacity independently.

Resource attribute/declaration locations have distinct boundaries:

- `clip-path` presentation attributes work on admitted rendered/resource children,
  including path/rectangle children of a clip resource. `clip-path` on the
  `clipPath` container itself is unsupported.
- `clip-path` in inline styles or stylesheets is unsupported. CSS `clip-rule`
  support does not enable that declaration.
- Embedded image targets can be clipped/masked and patterns can contain images,
  but an `image` child inside a mask is unsupported.

### Embedded PNGs

PNG decoding supports non-interlaced 8-bit RGB, RGBA, grayscale, grayscale-alpha
and indexed images, including tRNS and sBIT when every channel retains eight
significant bits. Chunk ordering, CRCs and bounded decompression are validated.
Image placement supports finite geometry/viewBox percentages, meet/slice
alignments, `preserveAspectRatio="none"` and general affine maps.

Unsupported: interlaced/16-bit PNG, reduced-significance sBIT, color-management or
orientation conversion, JPEG/GIF/WebP and external image fetching.

### Static CSS

Selectors include SVG element names, IDs, classes, attribute selectors, selector
lists, element+ID/class, descendants and children. Rules apply through admitted
root, group, symbol, nested-SVG and resource trees. Specificity, source order,
inline priority, inheritance and `!important` are implemented.

Declarations cover color/fill/stroke and their opacity, stroke width/caps/joins/
miter/dashes, display/visibility/opacity, fill-rule/clip-rule, paint-order, filter,
marker properties, overflow, parsed width/height and the text/font properties
listed below. Each property retains its target/value restrictions; this is not a
general CSS declaration parser. `paint-order` accepts normal or distinct
fill/stroke/markers lists, appending omitted operations.

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

Unsupported: backdrop inputs, external filter images, displacement, turbulence,
lighting, morphology, convolution, arithmetic composite, other blends,
backdrop-filter and CSS filter-function lists. Recursive effects and
forward/missing result names have categorized failures.

### Text and fonts

Fonts are explicit host-supplied resources through the existing manifest,
catalog and provider interfaces. Regular, bold, real italic and intentional
fallback use supplied programs; no machine-font discovery or synthetic style is
introduced. Required missing programs/glyphs cannot silently disappear or fall
back to machine fonts. The host owns acquisition, credentials, retries and caches.
See [FontProbe](../tools/Lokad.Svg.FontProbe/README.md) for local/HTTP host usage.

Family lists preserve order, quoted names, CSS escapes and inheritance. Missing
families are skipped before catalog fallback. Quoted priority punctuation, braces
and comment markers remain part of a family name through inline/stylesheet
parsing; an actual priority suffix retains its cascade behavior. SvgFontFaceRequest.FromCss
exposes decoded named/generic candidates. SvgFontSelectionPolicy supplies host
generic mappings and ordered fallback restrictions to snapshot/prepared-document
creation; quoted "serif" remains a literal name. Existing single-family
catalog resolution retains its fallback behavior. Selection uses one program
per covered interval: SVG-based snapshot/prepared creation lazily acquires
alternatives for missing admitted scalars, trying later authored families before
host fallback. Kerning/ligatures stay within a selected face; positioning, spans
and text paths retain their layout. Combining/joining fallback does not expand
the shaping subset below. Missing-everywhere requests keep scalar/index/source
diagnostics; required corrupt or unavailable bytes are not silently skipped.
Raw conversion uses only the supplied snapshot. Request-only snapshot creation
has no text coverage to inspect and retains its first-face selection contract;
callers can explicitly supply prepared alternatives for the same request.
Ordinary shaped text keeps fractional glyph offsets when scaled. Paint-origin
rounding follows absolute span resets; text paths and fitted-spacing text retain
their separate character-metric rules.
The [ordered-family document](../tests/Research/font-family-stacks/README.md)
and [mixed-coverage document](../tests/Research/glyph-fallback/README.md) retain
supplied-program metrics and frozen Windows/Linux pixels.

Supported format: standalone static TrueType, face index 0. Text supports
horizontal LTR ASCII/tested precomposed Latin and covered supplementary scalars;
text/CDATA/whitespace order; nested `tspan`; inherited/per-span family, size,
weight, style, paint and visibility; scalar/list x/y/dx/dy/rotate; start/middle/end
anchors; letter/word spacing; explicit lines; textLength spacing/spacingAndGlyphs;
and alphabetic/middle/hanging baselines. Affine glyphs admit solid/gradient/pattern
fills and strokes, dashes/non-scaling strokes and paint order. Underline, overline
and line-through use the supported solid-decoration subset.

Text paths use one local open/closed geometry path per run, supplied advances and
kerning, length/percentage startOffset, anchors/tangents, `method="align"` and
`spacing="exact"`, including transformed/curved paths and mixed direct/path text.

Shaping is limited to bounded pair positioning and default Basic Latin ligatures:
Latin/DFLT default language, one unfiltered type-4 lookup, format-1 subtables,
coverage 1/2 and two to eight components in font order. Nonzero spacing or
positioned-span boundaries prevent ligature merging.

Pair positioning selects the default Latin language system before
DFLT, sharing selection with ligatures. Repeated feature tags in other language
systems do not invalidate the selected feature. Language-specific shaping remains
outside this subset.
Selected GPOS kerning takes precedence over legacy kern, including zero
adjustments. Superseded legacy contents are not parsed; all SFNT table ranges
remain validated. Required legacy kerning retains its single horizontal format-0
subtable boundary. The [precedence document](../tests/Research/kerning-precedence/README.md)
retains independent metrics and frozen Windows/Linux pixels.

Type-2 pair lookups admit RIGHT_TO_LEFT (irrelevant to that lookup type),
IgnoreBaseGlyphs and IgnoreLigatures with bounded GDEF classes. Each lookup
filters starting glyphs and finds its own next eligible partner within a
font/size/absolute-position chunk. IgnoreMarks, mark attachment classes and
format-1 mark-filtering sets admit supported non-mark label runs with validated
GDEF metadata; actual mark positioning remains unsupported. Reserved flags,
invalid classes, set indices and table ranges fail explicitly. The
[lookup-filter document](../tests/Research/kerning-flags/README.md) retains
independent glyph/advance evidence and exact Windows/Linux outputs.

The [static TrueType compatibility controls](../tests/Research/static-truetype-fonts/README.md)
also cover Liberation Sans Regular 2.1.5 and DejaVu Sans 2.37, including composites,
kerning/ligatures and Windows hinting. Font acquisition and fallback remain explicit;
these controls retain the same font-format and shaping boundaries.

The bounded hint interpreter includes stack/dual projection vectors, physical
super-rounding, line intersections, projected coordinate and relative moves, signed MIN/MAX,
ODD/EVEN predicates and curve-tag range changes. Fitting preserves the native
Windows/Linux rounding distinctions and simple/composite outline tags. Other
executed instructions outside the supported subset fail explicitly.
Windows MIRP/MSIRP retain native horizontal cut-in, including coincident-point
placement; composite CVT writes retain their operand units. Windows also retains
native curve-tag changes and the documented VacuFormRound function-zero switch.
Natural LCD dropout selection retains nearest-row lower bounds, observes earlier
surviving spans and rounds monotonic conic endpoints before LCD expansion.
Excluded stubs cannot suppress later strokes; turning arcs retain their distinct
subdivision behavior. Restored candidates observe the same ordered neighbors,
and reduced conic equations stay within their terminal bounds.
Original native controls cover these cases.
At fractional horizontal glyph-grid endpoints, natural conic smart midpoints
retain implied half-unit ordinates through direction-normalized subdivision
before six-bit profile emission. Scan ordinates stay six-bit; on-grid curves
retain their span profile for midpoint selection. Fill, span ownership and stub
continuation retain separate crossings.
[Native ordinate controls](../tests/Lokad.Svg.Tests/SvgGlyphLcdCoverageTests.NaturalOrdinates.cs)
cover half-unit curves, adjacent phases, signed origins, both stub policies and
ordinary line-span ownership.
[Interpolation controls](../tests/Lokad.Svg.Tests/SvgGlyphLcdCoverageTests.NaturalInterpolation.cs)
cover monotonic bands, an on-grid guard and turning rings at both retained
strike phases.
Symmetric smart dropout control retains corners supported by both scan
directions; half-scan continuation owns exact gap-endpoint vertices. Original
[corner controls](../tests/Lokad.Svg.Tests/SvgGlyphLcdCoverageTests.SymmetricCorners.cs)
also protect nearby phases, excluded upright/bar stubs and disabled dropouts.
Straight-edge smart midpoint selection rounds symmetric crossings to 1/64
sampling cells before averaging. [Native midpoint controls](../tests/Lokad.Svg.Tests/SvgGlyphLcdCoverageTests.SmartMidpoints.cs)
cover both scan directions, signed positions and rounding counterexamples.
Conic dropout selection retains bounded six-bit profiles with normalized scan
direction separately from ordinary fill; [native conic controls](../tests/Lokad.Svg.Tests/SvgGlyphLcdCoverageTests.SymmetricConics.cs)
cover implied-point caps, curved stems/tips, nearby phases and signed positions.
Fitted initial points and control values retain Windows floating-point scale
precision and signed half-unit rounding, as do axial original distances and
font-unit control writes. [Original native controls](../tests/Research/windows-font-unit-scaling/README.md)
cover fractional strikes, signed halves and identity composites.
Latin light fitting keeps nearby edges distinct according to the measured
standard stem width, with a quarter-pixel maximum merging allowance.
Mapped Latin non-base glyphs retain scale adjustment and stem fitting but skip
alignment-zone matches. Classification follows glyph identity through cmap aliases
and composites; it does not enable mark shaping.
Pending edges constrain fitted stems unless the bound would collapse the stem
to a quarter pixel or less; unlinked serifs retain their original relative offsets.
Instruction-free static programs retain their metric origins and native masks,
including simple Windows tip dropout control distinct from smart dropout control.
Windows selects natural or symmetric LCD sampling from the supplied font's
bounded gasp recommendations and actual raster size. Natural sampling retains
native fixed-point curve/dropout decisions, including excluded stubs and exact
terminating vertices. Natural half-scan overshoot must reach the selected pixel
center; neighboring full scans retain exact vertex continuation.
Authored simple dropout modes remain distinct from smart modes and preserve
scan-control bounds and component state. The
[original span document](../tests/Research/windows-font-span-extension/README.md)
qualifies the Windows one-cell span extension for the exact embedded family
`Courier New`. A catalog alias to another program does not enable that policy.
Font-selected unfitted Windows ranges retain integer-size
flags, signed point rounding and inherited metric origins; component assembly
precedes final device rounding. Ranges requiring fitting still execute the bounded
interpreter. Windows/Linux numerical rules remain separate.

Unsupported: wrapping, bidi/joining/complex scripts, general GSUB/GPOS/mark
positioning, other lookup flags/types/formats, vertical text, path stretch/auto
spacing, variable/color/bitmap fonts, CFF/CFF2 and WOFF/WOFF2.

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

The renderer is qualified on Windows and Linux against frozen platform-specific
Chrome references, using the [26-document upstream collection](../tests/Research/upstream-static-documents/README.md)
and [focused geometry/paint regressions](../tests/Research/native-path-replay/manifest.json).
Comparison requires exact decoded RGBA8, ignoring RGB only where both pixels have
zero alpha. These cases establish their declared outputs, not general SVG coverage.
[INVENTORY](INVENTORY.md) indexes retained evidence.

Current [Windows/Linux CI](https://github.com/Lokad/Svg/actions/runs/39030907217)
passes for renderer revision `397c8ea2`, including 8,121 tests per platform,
package/notices, installed consumers and resource/font-host checks. See
[BENCHMARKS](BENCHMARKS.md) for current measurements and
[RASTERIZATION_DESIGN](RASTERIZATION_DESIGN.md) for implementation details.

[Public font qualification](../tests/Research/public-font-qualification/README.md)
covers regular, bold, real italic and intentional fallback end to end, including
acquisition, intended-face selection, independent metrics and exact Windows/Linux
rendering. Standalone qualification is complete. The four private deployed-manifest
requests remain pending before production adoption; public success makes no claim
of compatibility with that deployment. Builds and CI need no private configuration.
