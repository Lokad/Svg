# Lokad.Svg — finite renderer delivery plan

Updated 2026-10-15 at the qualified marker-unit/CSS checkpoint; the effects sheet remains incomplete.
This plan owns remaining work and completion decisions;
[SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md)
owns admission, [INVENTORY](docs/INVENTORY.md) owns document evidence,
[RASTERIZATION_DESIGN](docs/RASTERIZATION_DESIGN.md) owns mechanisms and diagnostic
history, and [BENCHMARKS](docs/BENCHMARKS.md) owns performance evidence.

The checklist below is the single execution queue; the finite support boundaries
define its required exits. Preserve the qualified `inv-146` correction and every
original SVG/font/reference byte, option and default resource limit. Current
unsupported outcomes within the promised subset are remaining work, not scope
decisions. Implementation history stays in the linked records and Git.

The required release scope is the enumerated G2–G9 subset and the S17 contracts
below. A new case must demonstrate a listed missing mechanism or a regression;
it does not add a new feature promise or an exhaustive combination requirement.
Additional features and speculative improvements are optional unless explicitly
accepted. A proposed deferral of an existing promise stays required until decided.

## Qualified work to preserve

| Commitment | Qualified state and evidence |
| --- | --- |
| Frozen corporate cohort | **27/27 documents / 258/258 variants exact on Windows and Linux**, including 13/13 marks and 14/14 outlined wordmarks. Membership, bytes, options, backgrounds and references are frozen in the [cohort manifest](tests/References/inventory/corporate-cohort-v1.json) and [current measurement](tests/References/inventory/corporate-cohort-current.json). |
| Selected development documents | **50/50 complete**, including unchanged whole `inv-146`. Preserve every preceding declared output in the [gap ledger](tests/References/inventory/svg-gap-cases.json) and [supplied-font document banks](tests/Research/offset-merge-document/README.md). Failure-selected inputs establish no representative success rate; controls have separate denominators. |
| Original acceptance and supplied-font work | Original marker/drop-shadow fixtures **2/2**, original supplied-font label/path fixtures **2/2**, SVG Translate, `inv-127` and `inv-145` are complete. Preserve intended-font proof, original programs/aliases and metrics/pixels in the [document evidence](docs/INVENTORY.md), [supplied-font documents](tests/Research/supplied-font-documents/README.md) and [curved labels](tests/Research/curved-font-documents/README.md). |
| `inv-146` and shared rendering corrections | Whole `inv-146`, eight oval/text controls and four [merge controls](tests/Research/filter-merges/README.md) are exact. Preserve the [merge receipt](artifacts/maintenance-review-20261012/merge-qualified-results.json), [design record](docs/RASTERIZATION_DESIGN.md) and [support record](docs/SUPPORT_MATRIX.md). Paint recording is **148 exact / one required pattern-opacity gate**; I Ching is **24 exact / one out-of-scope animation boundary**. Later qualified evidence supersedes historical gates. |
| Additional authored documents | **Nine complete CC0 documents**, each **1/1 exact** on Windows/Linux, separate from both frozen cohorts. Preserve their separately counted controls: [symbol card](tests/Research/symbol-fractional-frames/README.md) **4/4**, [affine orbit](tests/Research/affine-oval-strokes/README.md) **6/6**, [scoped card](tests/Research/scoped-clips-masks/README.md) **5/5**, [colored signal](tests/Research/colored-effects-markers/README.md) original **7/7** plus repaired **1/1** and bounds **4/4**, [CSS/image card](tests/Research/static-css-images/README.md) original **3/3** plus phase/clip **2/2**, [layout guide](tests/Research/font-layout-diagram/README.md) **4/4**, [cascade sheet](tests/Research/root-resource-cascade/README.md) **4/4**, [label/path sheet](tests/Research/remaining-label-path-sheet/README.md) **8/8**, and [affine image sheet](tests/Research/affine-image-maps/README.md) **3/3**. Banks link immutable inputs, native witnesses and finished-correction receipts; wider groups stay open. |
| Qualified G2–G5 mechanisms | Fractional ordinary-use stroke widths, fractional/nonuniform symbol frames, nonuniform root curve fills and affine oval strokes, the non-scaling conic defect, composed cubic clips, translated custom mask regions and mask-container opacity have qualified witnesses. Preserve [use widths](artifacts/maintenance-review-20261012/fractional-use-qualified-results.json), [symbol frames](artifacts/maintenance-review-20261012/symbol-frames-final-results.json), [root curves](artifacts/maintenance-review-20261012/nonuniform-curves-qualified-results.json), [non-scaling strokes](artifacts/maintenance-review-20261012/non-scaling-qualified-results.json), [ovals](tests/Research/affine-oval-strokes/manifest.json) and [scopes](tests/Research/scoped-clips-masks/manifest.json). These completed mechanisms are dependencies already available, not work to repeat for wider maps or locations. |
| Filter bounds, rescaling and restoration | The [colored-effects bank](tests/Research/colored-effects-markers/README.md) preserves original controls **7/7**, repaired control **1/1**, bounds **4/4** and whole document **1/1**. The [filter-region bank](tests/Research/filter-regions/manifest.json) is **26/26 exact**, including sRGB rescaling/opacity and the unchanged displaced linearRGB blur. Preserve the [bounds](artifacts/maintenance-review-20261012/offset-image-bounds-qualified-results.json), [rescaling](artifacts/maintenance-review-20261012/rescaled-srgb-qualified-results.json) and [restoration receipts](artifacts/maintenance-review-20261012/linear-restoration-qualified-results.json). Wider G8 stays open. |
| Qualified marker mechanisms | The [effects/marker bank](tests/Research/effects-marker-sheet/README.md) has **2/3 exact controls**: absolute marker lengths/angles and overhanging CSS overflow with ordinary priority. The original [CSS overflow case](tests/Research/path-marker-content/marker-css-overflow-boundary.svg) is also exact against unchanged references. Existing marker and pattern outputs remain protected; the whole effects sheet and affine-filter control remain required gaps. No new document or G8 closure is counted. |
| Font-pack host and acquisition | The opt-in [FontProbe host](tools/Lokad.Svg.FontProbe/README.md), its six [transport/input regressions](tests/Lokad.Svg.FontProbe.Tests/HttpFontProgramSourceProviderTests.cs), catalog conformance and prepared ownership are implemented. Private deployment validation below remains required; general shaping remains outside scope. |
| Resource/API/package protections | Preserve bounded preparation/raster/encoding, allocation/work denial, cancellation/retry, partial-owner cleanup, immutable borrowed bytes, single-use prepared ownership and output integrity. Existing [API contract](docs/API_CONTRACT.md), installed-consumer and package checks apply; final release evidence must cover the finished revision. |

Completed work remains closed. Final-release qualification covers the finished
revision without reopening unchanged mechanisms for a new accounting or coverage
policy.

The marker correction is locally qualified: **7,724 tests/platform**
(5,325 core / 2,393 visual / six FontProbe), with no preceding test rows removed.
The effects bank remains **0/1 complete document / 2/3 exact controls**; the
original CSS marker-overflow control is also exact against unchanged references.
The [receipt](artifacts/maintenance-review-20261012/marker-units-overflow-qualified-results.json)
owns admission changes, preservation, bounded-failure checks and timings.
[Hosted Windows/Linux CI is terminal green](https://github.com/Lokad/Svg/actions/runs/38681043665)
for exact implementation revision `5e46a158`, including package/consumer, memory,
browser/setup, font and validator checks. Build plus complete tests took
**130s Windows / 111s Linux**, separately from local timings. Reuse this completed
evidence for unchanged documentation; do not poll this or preceding green runs.

<a id="delivery-order"></a>
<a id="3-ordered-support-queue"></a>

## Ordered delivery checklist

All six support/release items remain **required and open**; the image sheet is
complete, while G7's remaining location/alignment and dependent-content evidence
still needs reconciliation. The **next three deliverables**, in order, are the
**effects/marker sheet, composition sheet and pattern tile sheet** in rows 2–4.
Bring any remaining G7 or G9 slice forward when a document needs it. This order
introduces no feature-scope deferral and no exhaustive group prerequisite.

Select shared fixes by the retained documents they advance. Extend existing
geometry, source replay, paint maps and bounded layers, preserving native pixel
distinctions. Bring dependencies forward when they unblock a document; neither
exhaustive G2 completion nor unrelated group closure is a prerequisite. No document
IDs, source coordinates or observed failure pixels may become renderer predicates.

| Order / required item | Missing behavior and dependencies | Acceptance case and completion condition |
| --- | --- | --- |
| 1. Remaining G7 acceptance | The whole image-map correction is complete. Reconcile independent alignment axes and the enumerated resource/style locations against existing witnesses; dependent image content remains required with G6/G8. Dependencies: qualified decoder, crop/sampling, cascade, replay and G5 layers. | Preserve the [complete affine image sheet and three controls](tests/Research/affine-image-maps/README.md). Reuse [selector](tests/Lokad.Svg.Tests/SvgCssSelectorTests.cs), [PNG](tests/Lokad.Svg.Tests/PngImageDecoderTests.cs), static-image and cascade witnesses; add a case only for an uncovered independent alignment or listed location. Close G7 when every finite mechanism has acceptance and affected decode/resource/error checks pass. No new alignment document or Cartesian census is required. |
| 2. Effects and markers (G8), active | Image/filter source admission, graph/default/result and mixed-color behavior, region/shadow/affine maps and remaining interactions; font-relative marker lengths, container presentation and degenerate/closure evidence. Reuse qualified units/CSS overflow, bounded capture/composition and G5 scopes; bring in only needed G7/G9 slices. | Finish the unchanged [effects/marker sheet and remaining affine control](tests/Research/effects-marker-sheet/README.md). Two marker controls and the original CSS-overflow fixture are already exact. Extend existing filter source capture to images first, then advance this document through graph/map gates. Close G8 only when all finite mechanisms have witnesses, measured zero-length outcomes and affected graph/resource checks. |
| 3. Shared maps/composition (G2–G5) | Remaining percentage/instance/paint/stroke maps, dashed shader caps/translucency, clip child unions, mask units/scopes and nested leaf/group/use boundaries. Land shared dependencies with items 1–2 and the dependent tile/text cases. | Graduate [percentage oval use](tests/Research/oval-percentage-lengths/percentage-use-boundary.svg), partial-mask use in [group-mask tests](tests/Lokad.Svg.Tests/SvgGroupMaskCropTests.cs), [opposite-winding children](tests/Research/compound-clip-content/overlapping-opposite-children.svg) and [clip child union](tests/Research/clip-intersections/overlapping-path-child-union.svg). Bind other missing independent semantics to one composition sheet or focused stroke/degenerate-map controls. Complete when finite G2–G5 mechanisms have qualified witnesses; reuse completed single-child overlap, curve, symbol and scope evidence. |
| 4. Patterns (G6) | Remaining unit/map/combined and grouped opacity; image/text/filter tile content. Reuse bounded replay and only the G7–G9 slices each tile needs. | Graduate [combined tile opacity](tests/Research/paint-recording/pattern-tile-tile-fill-stroke-element.svg) and [group opacity](tests/Research/pattern-vector-content/group-opacity-promotion.svg), then use a tile sheet for missing unit/viewBox/phase/reflection and dependent content. Complete when finite G6 mechanisms are qualified with recursive/size/work cleanup outcomes; preserve the eight complete pattern documents. |
| 5. Remaining labels and paths (G9) | Stroked decorations, decorated paths, affine glyph/paint-map qualification, the three named composite flags and private validation. Dependencies: qualified layout/fragment painting, supplied-font preparation and G3–G5 maps. | Freeze a stroked/decorated direct-and-curved-path witness and bounded synthetic overlap/scaled-offset composites; reuse existing witnesses for rotation/skew/reflection/nonuniform glyph maps. Record measured degenerate-path outcomes and the sanitized private request set below. Close G9 when every finite mechanism has linked acceptance and affected bounded-failure checks. The completed sheet, eight controls and intended-program proofs stay closed. |
| 6. Release (S17) | Finished-revision contracts and delivery evidence after items 1–5. | Meet every release condition below: no unresolved required mechanism, all established exact results preserved, complete platform/API/resource/package/consumer/CI checks green and representative measurements reported. Document counts alone cannot establish completion. |

The image sheet's uniform, affine and image-use rejections are resolved. Its
[original declaration and baseline](artifacts/affine-image-sheet-20261015/declaration.json)
remain diagnostic history; current acceptance belongs to the public bank and
qualified receipt above. The same unchanged document now matches through source
cropping, shared coverage, recorded opacity and clip/mask replay. It adds one
completed document, not a G7 closure or a replacement for preceding controls.

The [effects declaration](artifacts/effects-marker-sheet-20261015/declaration.json)
now binds the next document to frozen inputs/options and healthy platform triples.
Its first unit and CSS gates are resolved. Current whole-sheet rejection is image
filter admission; the separate affine control rejects rotated/skewed filtering.
Keep mixed-color/nested graph and other declared interactions required as this
same sheet advances. Original baselines remain evidence, not work to repeat.
The [reduced image-filter source control](artifacts/effects-marker-sheet-20261015/image-filter-source-declaration.json)
is separately frozen at `5e46a158` with healthy stable platform triples. It isolates
capture/opacity before the whole sheet's outer clip interaction; its current
image-filter rejection is required work, with no additional acceptance claimed.

The image/effects/composition/tile sheets are bounded acceptance recipes, not new
feature promises or acquisition quotas. Before implementation, bind each missing
mechanism to an existing witness or freeze a rejected/different in-scope output:
licensed bytes, declared options, supplied programs and healthy platform triples.
Prefer a suitable redistributable export; an authored CC0 sheet is permitted.
Acquire cases only for identified gaps, boundaries or defects. Preserve original
inputs when replacing obsolete rejection assertions with positive regressions.

At a finished correction, record implementation, exact acceptance and checkpoint
qualification together in the existing bank; link that witness from the owning
row. Reuse it across groups and APIs. Distinguish rejection, known difference and
admitted behavior lacking evidence. Add neither duplicate evidence for a checklist
label nor an exhaustive shape × paint × placement × API prerequisite. At release,
every required row and its finite boundary must agree with the finished renderer.

## Required support boundaries and finite exits

These lists preserve the promised useful subset, including combinations that
currently reject. A current rejection is not an approved scope deferral. Each
group closes when every listed mechanism has a representative exact positive
case, the named retained defects are resolved, its negative boundaries are
categorized and affected resource/output regressions pass. Cover independent
semantics and discovered interactions, not every shape × paint × placement × API.
Keep an unverified behavior open; completed document counts do not prove it.
**G2–G9 and S17 remain open.** The six delivery items own execution; the finite
lists below define their exits, rather than creating a second backlog. Bind each
still-unverified mechanism to an existing acceptance case or to the next declared
document before implementing it. Reuse qualified witnesses; no exhaustive
combination census or separate tracking framework is required.

### G2–G4: instances, geometry, maps and strokes

- G2: local backward/forward uses and child aliases; ordinary leaves,
  group/symbol/use targets; root/defs/rendered-group/symbol/nested-SVG definition
  and presentation locations; authored/inherited precedence and source-ancestor
  exclusion. Compose static instance maps through viewport, opacity, clip/mask
  boundaries. Missing/cyclic references have categorized outcomes.
- G3: rect/rounded rect, circle/ellipse, line, polygon/polyline and retained
  line/quadratic/cubic/arc paths; nonzero/even-odd compound fills; translation,
  rotation, skew, reflection and nonuniform affine scaling at root, group, leaf
  and admitted instances. Preserve original directed curves/source geometry.
  Degenerate finite maps have measured nonpainting or categorized outcomes;
  perspective maps remain outside scope.
- G4: the G3 geometry with solid/linear/radial/pattern paints; finite fractional
  and effective instance widths, ordinary/non-scaling stroke construction,
  butt/round/square caps, miter/bevel/round joins/miter limits, finite dash arrays
  and offsets, independent fill/stroke opacity. Include dashed curved paints and
  nonuniform placement. Shared stroke-to-paint routing must preserve native
  shape distinctions; paint-specific geometry kernels are not the delivery plan.
- Still required: percentage instance bases; shared rotation/skew/reflection/
  nonuniform stroke-to-paint mapping where no qualified witness exists; dashed
  curved shader round/square caps and translucency; reflected/magnified pattern
  interactions. Existing exact examples satisfy their mechanisms without another
  shape-by-paint census.
  The [percentage oval use](tests/Research/oval-percentage-lengths/percentage-use-boundary.svg)
  remains an instance-coordinate acceptance gap. Reuse qualified witnesses in
  [use paints](tests/Research/use-paint-properties/manifest.json),
  [native replay](tests/Research/native-path-replay/manifest.json),
  [paint recording](tests/Research/paint-recording/manifest.json) and
  [oval source](tests/Research/oval-source-replay/manifest.json); completed cases
  are listed above and in the support/design records, not blockers to repeat.

### G5: viewports, clips, masks and composition

Nested SVG/symbol viewBox/aspect maps and percentage bases; local paint definitions;
G3 clipPath shapes in userSpace/objectBoundingBox coordinates, ordered overlapping
children and compound/winding behavior; alpha/luminance masks with both region and
content unit systems, transformed finite regions and implicit root viewport bases;
nested leaf/group/use opacity, clips and masks. Include mask-container non-unit
opacity and remaining nested-definition/style locations. Distinguish definition
scope, object bounds, visual stroke bounds and percentage bases. Forward binding
and source order must agree; bounded surfaces preserve native AA/BW consumers.
The object-box clip bank and retained composed cubic/custom translated-region
cases are complete; preserve their unit/overlap/affine witnesses. Mask-container
non-unit opacity is admitted with native child-painting semantics and explicit
child inheritance. Wider region/content-unit, scope and composition promises
remain required; the scoped-card document does not close them.
Evidence: [object-box clips](tests/Research/object-bounding-clips/manifest.json),
[local mask references](tests/Research/mask-local-references/manifest.json),
[group masks](tests/Research/mask-group-documents/README.md) and the design record.

### G6: patterns

Ordered G3 vector leaves, groups and local uses with solid/gradient paint and
opacity; both pattern/content unit systems, viewBox/aspect mapping, fractional
finite tile origins/sizes and composed affine transforms. Embedded images and
filtered/text content are required G7–G9 dependencies. Preserve tile-border,
phase, alpha, transformed fill/stroke and inheritance behavior without unbounded
repeat-count work. Recursive pattern/paint dependencies reject explicitly.
Native first linear/radial/rounded payloads, fractional phase and border clipping
are qualified; combined tile/fill/stroke/element opacity and grouped tile opacity
remain required gates, with retained cases in item 4. The [vector content](tests/Research/pattern-vector-content/manifest.json),
[viewBox](tests/Research/pattern-viewbox/manifest.json) and
[instance](tests/Research/pattern-instance-content/manifest.json) banks protect
completed work; their exact cases do not close dependent tile content.

### G7: embedded PNGs and static CSS

- PNG: non-interlaced 8-bit RGB/RGBA, grayscale/grayscale-alpha and indexed
  palettes, including tRNS transparency, from base64 data URIs. Keep chunk/CRC/
  decompression validation. Finite image dimensions/positions, viewBox percentage
  bases, all meet/slice alignments, `none`, fractional/general affine placement,
  clipping/masking/opacity use shared bounded decode/sampling/maps.
  Shared decoding now retains varied premultiplied grayscale/grayscale-alpha/
  palette/RGB/RGBA pixels with bounded ownership. The complete CSS/image card and
  phase/clip controls qualify fractional axis-aligned meet/slice/none and opacity.
  The complete [affine image bank](tests/Research/affine-image-maps/manifest.json)
  now qualifies uniform fractional paint opacity, rotation/skew/reflection,
  source-cropped slice, local image-use and clip/partial-mask replay. These gates
  are closed. Reconcile other independent alignment axes and listed tree/resource
  locations against existing evidence; image tile/filter dependencies remain
  required in their owning rows. Preserve low-precision image matrix rounding
  separately from the existing gradient/glyph arithmetic.
- CSS: existing ID/class/attribute/selector lists plus SVG element,
  element+ID/class, descendant and child selectors across supported root/nested
  SVG/definition/instance trees. Existing compound/combinator matching is
  implemented; qualify remaining tree/resource locations. Static rules apply in
  source order with specificity, `!important`, inline precedence and inheritance for the
  presentation/font properties enumerated in G2–G6/G9; retain parsed geometry
  width/height declarations. `paint-order` supports normal and distinct
  fill/stroke/markers lists, appending omitted operations in default order;
  marker participation depends on G8. Duplicate/unknown tokens reject explicitly.
- Preserve completed inert geometry typography validation and late stylesheet
  prepass. Their value boundaries are in [SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md)
  and [inert typography](tests/Research/inert-typography/manifest.json).
  Nested SVG identity attributes and late nested-rule collection are qualified
  by the [CSS/image bank](tests/Research/static-css-images/manifest.json);
  root/nested viewport/defs/symbol presentation and viewport text now have the
  [cascade-sheet witnesses](tests/Research/root-resource-cascade/manifest.json).
  Keep only other unverified resource/style locations open; reuse these witnesses
  for G2/G5/G7 instead of requiring another cascade document.
- Outside scope with explicit outcomes: interlaced/16-bit/color-managed PNG,
  JPEG/GIF/WebP/external image fetching; CSS imports/media/supports, pseudo
  classes/elements, sibling combinators, layout, variables, animation and web fonts.

### G8: markers and finite static effects

- Markers: local start/mid/end on G3 line/polyline/polygon/path and instances;
  userSpaceOnUse/strokeWidth, refX/refY, finite sizes, viewBox/aspect mapping,
  angle/length units, fixed/auto/auto-start-reverse orientation; ordered
  vector/group/use content with ordinary paint, source-order paint participation,
  overflow attributes/CSS and container presentation. Preserve the qualified
  [path](tests/Research/path-marker-content/manifest.json) and
  [instance](tests/Research/marker-instance-content/manifest.json) witnesses.
  Absolute number/px/in/cm/mm/Q/pt/pc lengths, known-viewport percentages and
  unitless/deg/grad/rad/turn orientation have retained marker witnesses and
  numeric/range controls. Literal hidden/visible CSS overflow now follows source
  order, specificity and important/inline priority. Preserve the existing pattern
  attribute route. Font-relative marker lengths, other container presentation and
  unverified auto closure/tangent semantics remain required; the two exact controls
  do not close G8 or create an exhaustive combination prerequisite.
  Measure zero-length/closed-path outcomes;
  missing/recursive marker references have explicit errors.
- Effects: `feGaussianBlur`, `feOffset`, `feFlood`, `feComposite` over/in,
  `feMerge`, `feBlend` normal/multiply/screen and `feDropShadow`; SourceGraphic,
  SourceAlpha and earlier local results; filter/primitive regions, both unit
  systems, sRGB/linearRGB and local CSS `filter:url(#id)`. Preserve qualified
  [graphs](tests/Research/filter-documents/manifest.json),
  [merges](tests/Research/filter-merges/manifest.json),
  [kernels](tests/Research/filter-blur-kernel/manifest.json) and
  [colored/bounds witnesses](tests/Research/colored-effects-markers/manifest.json).
  Preserve the qualified sRGB rescaling/opacity and linearRGB restoration controls.
  Finish unverified color-space/shadow behavior,
  graph/default/result handling,
  clipped halos, rotated/skewed filter maps, other containers/instances, nested
  filters and clip/mask/marker/text/image interactions. These are required
  mechanisms, with representative interactions rather than exhaustive products.
- Allocate bounded intermediate regions and charge real blur/composition work;
  preserve ordered premultiplied arithmetic, cancellation and partial cleanup.
  The [filter-region bank](tests/Research/filter-regions/manifest.json) is complete
  at 26/26 exact; the six-pixel defect is closed. Completed fractional bounds and
  `inv-146` source/parent-device rounding and padded capture work remain closed; their evidence
  does not qualify every fractional chain.
- Outside scope with explicit outcomes: backdrop inputs, external filter images,
  displacement, turbulence, lighting, morphology, convolution, arithmetic
  composite, other blend modes, backdrop-filter and CSS filter-function lists.
  Recursive effects and forward/missing result names have categorized outcomes.

### G9: supplied-font labels and text paths

- Preserve supplied standalone static TrueType face 0, catalog/style/coverage
  fallback, existing bounded pair positioning and default Basic Latin ligatures.
  The admitted `liga` subset is Latin/DFLT default language, one unfiltered type-4
  lookup, format-1 subtables, coverage 1/2, two to eight components in font order.
  Nonzero spacing and positioned-span boundaries prevent merging. General
  shaping/other lookup flags/types/formats remain explicit boundaries.
- Labels: horizontal LTR ASCII/tested precomposed Latin and covered supplementary
  scalars; text/CDATA/whitespace and nested tspan order; inherited/per-span
  family/size/weight/style/paint/visibility; scalar/list x/y/dx/dy/rotate;
  start/middle/end anchors, letter/word spacing, explicit lines, textLength with
  spacing/spacingAndGlyphs, alphabetic/middle/hanging baselines; affine glyph maps,
  ordinary fill/stroke paints and solid underline/overline/line-through.
  Filled-label decoration is qualified; stroked decorations and decorated text
  paths remain required. For glyph/paint maps, reuse qualified translation/rotation
  and nonuniform examples; bind any unverified skew/reflection, stroke-paint or
  instance-map mechanism to item 5's witness. A current rejection supplies neither
  acceptance nor a feature deferral.
- Paths: one local retained open/closed G3 path per run, supplied advances/kerning,
  length/percentage startOffset, anchors/tangents, method align, spacing exact;
  curved/transformed source paths, measured out-of-range/zero-length outcomes.
  Keep admitted curved labels and scalar positioned spans closed. Dashed,
  non-scaling, stroke-before-fill and text-path strokes now have exact focused
  witnesses in the completed sheet. Preserve their shared replay and preceding
  uniformly mapped solid-stroke qualification; map qualification uses the finite
  list above rather than an exhaustive font/size/transform grid.
- Finish queued valid composite flags: OVERLAP_COMPOUND,
  SCALED_COMPONENT_OFFSET and UNSCALED_COMPONENT_OFFSET, using bounded public
  synthetic component fixtures for overlap and transformed offsets. Parent-hint
  metrics are complete; preserve
  [composite metrics tests](tests/Lokad.Svg.VisualTests/SvgCompositeMetricsTests.cs)
  and their [frozen native witnesses](tests/Research/curved-font-documents/diagnostics/composite-metrics-contours.json),
  plus [composite ownership regressions](tests/Lokad.Svg.Tests/OpenTypeFontCompositeRasterTests.cs).
  Admitted USE_MY_METRICS, nested accents,
  Windows LCD/dropout/contour and Linux light fitting remain protected. Implement
  the finite admitted font semantics needed by these labels; executed unsupported
  opcodes/formats reject explicitly, without making a general VM grid an exit.
- Outside scope: wrapping, bidi/joining/complex scripts, general GSUB/GPOS/mark
  positioning, vertical text, path stretch/auto spacing, variable/color/bitmap
  fonts, CFF/CFF2, WOFF/WOFF2 and machine-font discovery. Missing required
  programs/glyphs cannot vanish or substitute a machine font. Private success
  alone cannot establish public admission. The font/host contracts below apply.

## Release completion (S17)

Release is complete only when all of these conditions hold for the promised
useful subset. No release is established by a document/test count or a status
paragraph alone.

1. Items 1–5 are closed against the finite G2–G9 boundaries: each remaining
   mechanism links to an exact positive witness (or a measured nonpainting outcome
   for a valid degenerate case) and its applicable negative
   outcomes in existing manifests/tests. Existing evidence may satisfy an exit;
   unverified behavior stays open. No unapproved scope deferral or
   known defect in this subset is concealed by a current gate. Any residual
   outside the subset is categorized with reproducer/evidence and a support
   contract consistent with the implementation.
2. All established exact results remain exact, including frozen **27/27 and
   258/258**, all completed selected documents and useful visual regressions.
   Every declared variant of a document must be measured and exact to count.
   Rejection, known difference, oracle failure and unmeasured remain separate.
   The original S17 80% overall/per-stratum criterion in
   [S17_DECISIONS](docs/S17_DECISIONS.md) is already exceeded; it permits no
   regression from achieved fidelity. No tolerance or quirk budget is added.
3. Finished-revision Windows/Linux Release builds, complete offline test suites,
   package/notices and isolated installed-consumer checks pass. Managed-only,
   no bundled fonts/runtime dependencies and public API/lifetime contracts hold.
   Hosted Windows/Linux CI is green for the finished substantive release
   revision. Inspect its terminal result and reuse it for unchanged documentation;
   do not trigger builds or poll repeatedly to refresh plan status.
   Historical "remote CI pending" statements in linked checkpoint records do not
   reopen their green deliveries; final-release CI applies to the finished code.
4. Existing normal/adversarial resource scenarios verify meaningful size/work
   limits, cancellation/retry, cleanup and output integrity. Approximate memory
   accounting below holds directionally; completed ownership work stays closed.
5. Existing benchmark scenarios report latency and allocated bytes for a small
   representative set covering outlined geometry, instances/paint, clips/layers,
   filters/images and supplied-font labels. State hardware/options/setup and
   selection limits; no speculative cache/optimization or new framework is a gate.
   Investigate material regressions against retained measurements; exact pixels
   cannot be traded for throughput. Update README/API_CONTRACT/SUPPORT_MATRIX
   to the actually qualified subset and keep per-platform oracle provenance.

The failure-selected document queue and 27-document corporate cohort support
only their declared results, not a claim of general SVG or “most logos” success.
Broader corpus acquisition follows identified remaining gaps; no open-ended
representativeness quota delays this finite release.

## Orthogonal validation and execution

- During development, build and run the affected semantic/visual/resource checks.
  At a finished shared correction, run complete Windows/Linux qualification once;
  run full package/consumer/platform checks when their contracts change and at
  release. Reuse unchanged valid evidence. PLAN-only edits require consistency
  and diff review, without renderer requalification.
- Preserve every distinct SVG, viewport, scale, background and platform reference.
  Compare ordinary visuals through a canonical output API; exercise six raw and
  six independently prepared sinks in the existing representative contract matrix
  (supplied font, patterned mask on white, nested instanced mask on transparency).
  Keep useful API/feature interaction regressions. Existing per-case twelve-API
  qualified banks need no new qualification merely because this policy changes.
- Preserve capacity, overlap, non-array memory, named errors, independent results,
  single-use prepared disposal, async scheduling/backpressure, token cancellation,
  stream failure/publication and cleanup in
  [PublicContractTests](tests/Lokad.Svg.Tests/PublicContractTests.cs) and
  [PngEncoderTests](tests/Lokad.Svg.Tests/PngEncoderTests.cs). Do not multiply every
  visual by every API or require all combinations before advancing a document.
- Keep **routine build/test qualification below ten minutes per platform**.
  The [latest receipt](artifacts/maintenance-review-20261012/marker-units-overflow-qualified-results.json)
  records **7,724 tests/platform** in **84.3s Windows / 261.8s Linux**, including
  normal build/startup and complete tests of the finished correction. Both meet
  the target. Development attempts remain in that receipt; earlier timings
  stay in the [image receipt](artifacts/maintenance-review-20261012/affine-image-qualified-results.json),
  with distinct workload/environment definitions; these are no speedup claims.
  Use normal incremental builds without forced compiler shutdown. No rerun is
  needed merely to refresh timing. Record one before/after timing when
  materially changing routine cost. If it exceeds the target, remove redundant
  API permutation work while preserving distinct visuals/semantics. Fresh browser
  captures, optional benchmarks and final release/package work are separate
  checkpoints, not appended to every routine run.
- New cases must demonstrate an identified missing behavior, boundary or defect.
  Freeze original inputs/options and licensed healthy references before correction;
  require three stable fresh-process captures. Compare decoded RGBA8 exactly,
  ignoring RGB only when both pixels are fully transparent. Preserve rejected
  models in existing design evidence; no silent reference replacement.
- Re-render affected inventories for narrow fixes and all declared variants for
  broad geometry/transform/composition changes. Explain complete before/after
  differences for known mismatches; maxima/counts alone do not identify an output.
  Missing/oracle-failed/unmeasured variants cannot leave the denominator.
- Git identifies implementation. Do not hash our source, scripts, builds,
  packages, adapters or reports in tests/manifests. Keep SVG/font/reference/tool
  asset identities, upstream revision/license provenance and dependency integrity.
  Ordinary code edits need no metadata refresh. Detailed run/pixel/timing records
  remain ignored artifacts; extend existing tests/benchmarks, not infrastructure.
- Make small, buildable commits with affected tests/docs. Serialize local build,
  test and measurement jobs; avoid competing load during timing. Push substantive
  qualified batches and inspect their terminal CI result without busy polling.

## Maintenance decisions and evidence

Integrated review: [REVIEW.md](artifacts/maintenance-review-20261012/REVIEW.md).
These S01–S14 review IDs are separate from stages S1–S17. Rendering correctness
has priority; maintenance does not add prerequisites to support-group exits.
Completed maintenance remains closed; the table owns its status and the linked
records own implementation and qualification history.

| Maintenance status | Decision / retained evidence |
| --- | --- |
| Complete | S01 first tranche, S02 and S04–S12. Keep direct contour storage, prepared gradient/glyph data, retained-string/CSS reductions, bounded tile reuse, cheap unarmed probes, shared stroke admission and helper cleanup. Existing exact/resource regressions remain; mechanisms and measurements are recorded in the [review](artifacts/maintenance-review-20261012/REVIEW.md), [design record](docs/RASTERIZATION_DESIGN.md) and [maintenance artifacts](artifacts/maintenance-review-20261012/). |
| Optional / deferred | S03 preblend-cache prototype was removed after public-label comparisons **5.49 → 6.43 ms** and **6.59 → 6.86 ms**, allocation **142.15 → 142.51 KiB**, without established benefit. S13 wider glyph cache, S14 immutable table/profile or VM preparation, vertical glyph-scan reuse and wider polygon/cross-boundary tile reuse require representative profiling before reconsideration; none is a release gate. |

Preserve the measured improvements, with their original workload limits:

| Completed measurement | Retained result |
| --- | --- |
| S01 orthogonal test coverage | Suite-only Windows **5m51s → 1m58s**, Linux **5m34s → 1m43s** at that checkpoint; every distinct prior case retained plus two representative cases. [Existing receipt](artifacts/maintenance-review-20261012/s01-results.json). Latest suite timing is separately reported above. |
| S02 direct contour storage | Synthetic contour conversion allocation **688 → 480 bytes**; styled lowering about **202,613 → 197,700 bytes**. Short timing ranges overlap; this is an allocation result, not a universal speedup. |
| S04 prepared gradient ramps | Retained `inv-140` owned conversion **100.5 → 89.93 ms**, about **2.7 KiB** more allocation around 5.32 MiB. Preserve arithmetic/dithering and bounded preparation. |
| S05 retained parser strings | Attribute-parser allocation **820.67 → 599.79 KiB**; definition/use **751.03 → 653.16 KiB**; complete attribute conversion about **853.5 → 632.6 KiB**. Timing is mixed; reduced allocation is established. |
| S06 CSS identity | Small timing change with overlapping uncertainty and unchanged allocation; no speedup claim. |
| S07/S08 glyph preparation | Selected public-label local time **30–34% lower** for about 16 KiB more allocation; verified fallback **29–35% lower** for about 0.54 KiB. Optional preparation yields under resource pressure. These are distinct workload claims. |
| S09 tile reuse | Selected stroke/repeated-fill allocation **13%/37% lower**. Wider tile reuse and timing outside these workloads remain conditional. |
| S10–S12 | Unarmed probe microbenchmarks improve without allocation; shared stroke parsing and unused-helper removal preserve behavior. No general rendering-throughput claim. |

Detailed measurements/receipts are retained under the existing ignored
[maintenance artifacts](artifacts/maintenance-review-20261012/). No new
measurement framework or completed-memory requalification is required.

Speculative optimization, pooling/global caches and new test/evidence
infrastructure are deferred from release. Existing source/PNG/font lease and prepared-owner
protections are not redesign targets merely because a copy is visible.

**Scope-deferral decisions:** the maintenance deferrals above are explicit and
narrow no G2–G9 promise. The former 200-case and 10–20-diagram guidance is
optional coverage guidance, not an exhaustive acquisition or release quota.
There are **no newly approved feature-scope deferrals**
in this revision. Any future proposal must name the promised behavior, missing
acceptance, reason and release consequence, and remain open pending an explicit
decision. Current unsupported gates inside the subset cannot count as completion.

## Product and approximate memory contract

Keep one pure-managed C# net10.0 library for Windows/Linux. Retain the project's
MIT license and the adapted clipping code's BSD license/notices in
[THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt). Bounded managed source
adaptations follow the optional reference-source policy. Preserve zero third-party runtime packages, native
assets/PInvoke, System.Drawing, installed-font engines, browser rendering services
or other Lokad runtime dependencies. Rendering consumes
explicit options/resources; it does not discover filesystem/network resources.
Scripts/events, animation, mutable DOM, foreignObject HTML and arbitrary external
documents stay out of scope. Live fonts and effects must not delay outlined logos.

Preserve the six output APIs, owned/caller memory, overlap rejection and non-array
memory; named null arguments, categorized errors and token-preserving cancellation.
Async memory schedules CPU work once; async streams await backpressure, propagate
failures and stay open. Hosts own transactional output. Prepared documents are
single-use owners; snapshots/borrowed bytes retain their documented lifetimes.
Read [the C# guidelines](docs/GUIDELINES_CSHARP.md) before C# changes.

| Default bound | Value / interpretation |
| --- | --- |
| Renderer-accounted live storage | 256 MiB budget under approximate accounting; shared runtime/host overhead and borrowed storage are identified separately. Audit material R38/R39 ownership/allocation changes. |
| SVG input / encoded PNG | 16 MiB / 128 MiB. |
| Output | 16,384 per axis; 16,777,216 pixels. |
| Nodes / cumulative path commands | 100,000 / 1,000,000. |
| Shared conversion work | 100,000,000 units through preparation, scene, raster and encoding. |
| XML depth / attributes per element | 128 / 256. |
| Direct text | 32,768 cumulative UTF-16 characters. |
| Snapshot caps | 256 faces / 32 MiB distinct borrowed programs, separate accounting. |

Root lowering and discovery share the conservative XML allowance
`max(16 KiB, 4 × input bytes, 512 × configured attribute limit)`. Distinguish that
allowance, ledger peaks, cumulative managed allocations, borrowed storage and
process working set. Charge curve/stroke/dash/reference expansion, pair/vertex
scans, coverage/paint, images and intermediate surfaces before denied work;
check cancellation within bounded chunks and release owners on every exit.

Memory accounting is approximate: directionally no more than **20% of
conversion-attributable live memory** should be unaccounted for across
representative workloads. This is an engineering target, not an exact guarantee
at every instant. Identify shared runtime/host overhead and borrowed storage
separately. Prefer conservative estimates and aggregate allowances for dominant
allocations: pixel buffers, decoded images, intermediate surfaces, geometry,
glyph data and large collections. Avoid excessive overcharging.

Completion does not require byte-exact small-object accounting, individual
metadata receipts, GC-round retirement proofs or precise accounting of the
bookkeeping machinery. Interpret ownership audits according to material memory
impact. Preserve meaningful size/work limits, cancellation, cleanup and output
integrity; validate through a small representative normal/adversarial workload
set using existing scenarios, without creating another measurement framework.

These requirements supersede conflicting exact-accounting completion wording.
Rendering correctness remains the priority. Preserve completed work and useful
protections; do not reopen them for requalification under this clarification.

## Font, text and host contracts

### Shared public font-pack contract

Preserve the public Lokad.OoxPdf 0.1.4 manifest contract; the 2026-09-20 source review
of the local OoxPdf checkout at `8e4b0333` (declared version 0.1.5, not a verified
published release) confirms the same fields and resolution order. The reviewed
public files are `Fonts/OoxPdfFontPackResolver.cs`, `Fonts/FontProgramLoader.cs`,
`FontTests.cs`, `README.md` and `docs/PrivateValidation.md`. Both renderers must use
unchanged manifest/font files from one location, without a converted/pruned SVG
manifest, duplicated deployment or OoxPdf assembly/package/project dependency.
Focused independent font-management adaptation is allowed; PDF embedding and SVG
glyph rasterization retain distinct responsibilities.

| JSON location | Compatibility |
| --- | --- |
| Root | `packId`, `files`, `families`, optional `fallbacks`; requested ID validation. Convention `<source>/<packId>/manifest.json`, font paths relative to root. |
| Files | `relativePath`, `byteSize`, `sha256`; safe paths, verified selected declarations. |
| Families | `requestedFamily`, `resolvedFamily`, `relativeFontFile`, `weight`, `italic`, `faceIndex`, `hasMathTable`; aliases/multiple faces per file. |
| Omitted fields | Weight 400, italic false, face index 0, math flag false; math metadata implies no SVG math layout. |
| Fallbacks | Ordered `{ "family": "..." }`; omission means empty list; case-insensitive family/alias matching and deterministic style/selection tie-breaks. |

Preserve web-style case-insensitive properties and unknown-property tolerance;
require no new schema/license/source fields. Provenance may be a sidecar. Bound
metadata without rejecting unused unsupported-format entries; reject explicitly
when selected. Pin self-contained conformance fixtures to the public contract:
defaults/aliases/fallback order, unknown metadata, paths, unused TTC and selective
loading. No other renderer checkout is required.

The focused comparison establishes these alignment targets; source inspection of
upstream tests is not a claim that those tests were executed here:

| OoxPdf behavior inspected | Lokad.Svg alignment and remaining work |
| --- | --- |
| Manifest first; font bytes fetched lazily for selected files and checked by length/SHA-256. | Keep `SvgFontPackCatalog` metadata-only and acquire through caller-owned sources during async preparation. Verify the unchanged private manifest with Svg itself before claiming end-to-end compatibility. |
| Case-insensitive requested/resolved family matching; italic match, closest 400/700 weight, then deterministic family/path/face tie-breaks; ordered configured fallback. | Existing Svg selection follows that order and accepts numeric SVG weights. Independent conformance vectors cover aliases, four real styles, fallback (`b0d4d5d`), plus unused collection entries that parse and select without poisoning the catalog while snapshots reject selected collection faces before acquisition. OoxPdf's bold request maps to 700; preserve SVG's numeric weight semantics. |
| Shared per-file sources coalesce concurrent downloads, cache successful bytes and use at most three attempts for transient transport failures. Manifest reads cap at 1 MiB; font reads cap at the smaller of the declared size and 64 MiB. | Implemented in the opt-in [FontProbe host](tools/Lokad.Svg.FontProbe/README.md), with synthetic transport regressions. Preserve Svg's separate 32 MiB aggregate borrowed-program and 256-face caps; the source's per-file cap does not replace them. |
| Font loading can select collection faces; the inspected loader synchronously waits for bytes, copies them and can return no font for certain failures. | Retain Svg's async acquisition, immutable borrowed bytes and categorized required-font failures. Current face-0 standalone TrueType admission remains explicit; packs may contain unused TTC or other unsupported entries without rejecting the whole catalog. |
| Private validation keeps manifests, inputs and reports ignored and commits anonymized findings. Download exceptions can contain source URIs. | Apply the private workflow below and redact transport errors before they reach repository reports or shared logs. Do not propagate private source identities through diagnostic `StableId` values. |

### Authorized private font testing

User authorization covers testing with the existing deployment's blob storage and
manifest. It does not authorize committing or publishing any platform content.
Never commit the storage address, account/container identifiers, credentials or
SAS values, deployment pack identifier, actual manifest, font binaries, platform
configuration/code, or derived artifacts that disclose them. This includes
embedded-font SVG/data URLs, request/response traces, exception text and reports.
Any retained private inputs, font caches and captures belong outside this repo or
under its already-ignored `artifacts/private-fonts/`; check that they are untracked
before use. Follow OoxPdf's private-validation approach with Svg's existing ignore
layout. Ordinary builds/tests and public CI must remain offline and independent of
the private source and sibling checkouts; do not upload private test artifacts.

The earlier one-program format inspection establishes no whole-pack or rendering
qualification; its historical detail stays in Git. The host and catalog are
implemented. Preserve them rather than rebuilding them as another S7 queue.

**Required private validation, attached to item 5 (G9):** use the unchanged deployed
manifest to prepare only representative requested regular/bold/italic and fallback
programs; inventory their relevant formats, coverage, composites and positioning
tables, and compare selection with the public OoxPdf contract. For those requests,
compare curves/counters/composites, AV/space and fractional labels using identical
verified bytes in Svg and an SVG-image-local browser font embedding. Existing
intended-font controls, three stable captures and independent metrics/pixels apply.
Completion is a sanitized outcome for every selected request, with private gaps
categorized and reproducible in-scope defects converted to public synthetic cases
and resolved under G9. Unsupported formats retain explicit outcomes; no whole-pack
combination quota or new host is required. Private success cannot replace public
acceptance. The security/retention rules above remain mandatory.

Hosts own locations, credentials, I/O/retry/concurrency and bounded shared caches;
the library owns SVG analysis, ordered family/generic mapping, face selection,
coverage fallback, layout and painting. Prefer real styles; no implicit synthetic
styles or machine fallback. Record substitutions and deliberate policy differences.
Scalar fallback is not complex shaping.

Bounded async preparation acquires only required resources and retains admitted
parse/style state plus byte leases in a disposable prepared document, initially
with explicit single-conversion ownership and shared limits through output.
Raw snapshots may have a separately specified concurrent-read lifetime. Never
synchronously block on acquisition. Avoid unconditional font copies/unbounded
global caches. Identity is hash plus face index; caller bytes stay valid/unchanged.
Shape-only input works offline; required missing faces/glyphs cannot disappear.

Core bundled/required font bytes remain zero. Count distinct programs rather than
aliases/faces and measure acquired bytes separately from parsed storage. Keep
deployment inventories and private program identities out of this plan. Select
redistributable public test fonts separately, retaining their asset licenses.

### Retained text foundation

The finite G9 boundary above is the single text completion checklist. Preserve
SFNT/range and `head/maxp/hhea/hmtx/cmap/loca/glyf` validation, optional name/OS/2
metadata, simple curves/implied points, bounded composites, supplied coverage
fallback, pair positioning and admitted ligatures. Their evidence is in the
[font tests](tests/Lokad.Svg.Tests/OpenTypeFontTests.cs),
[catalog tests](tests/Lokad.Svg.Tests/SvgFontPackCatalogTests.cs) and qualified
supplied-font document banks. Existing fallback/preparation is implemented;
general shaping is outside scope. Qualified positioning, spacing, baselines,
length policies and the complete path sheet stay closed. Only item 5 and its
finite G9 boundary own remaining text work; historical rejection wording in
linked checkpoint records does not reopen completed mechanisms.

### Host/oracle/corpus acceptance

Preserve the implemented [FontProbe host](tools/Lokad.Svg.FontProbe/README.md),
including bounded nonseekable input with preserved XML/BOM bytes and growth-time
limits. A new public stream-input overload is not a release requirement. Host
buffers/caches are separate budgets; concurrency, cancellation isolation, retry,
backpressure and partial-output publication keep their existing contracts. Public
supplied-font documents provide real-label evidence alongside transport tests.

Retain SVG and font hashes, face index, materialized oracle-input hash, browser/
platform/version, recipe, scale, locale/background and font/raster policy. Verify
font use inside the SVG image with missing/corrupt/substitution controls; outer-page
font readiness is insufficient. Require three stable fresh-process captures.
Compare metrics/layout separately from pixels and keep candidates/references/diffs,
bounds and classified findings. Private evidence stays in the authorized local
workflow above and must not enter public reference manifests or CI artifacts.
A box glyph or single size is insufficient.

Keep exact decoded RGBA8 comparison, ignoring RGB only for transparent pixels.
No tolerance or silent reference regeneration. Separate support and fidelity
(`exact`, `known-difference`, `oracle-failure`) in explicit inventories.
Preserve rejected diagonal/overlap/opacity models as negative evidence; Windows
oracle results are not canonical Linux evidence. Corpus acquisition and optional historical coverage guidance follow the
validation and scope decisions above; no further acquisition quota applies.

## Optional reference sources

The local reference review in the earlier draft remains useful design guidance.
Keep `external/` optional, ignored and unreferenced by builds, tests, packages and
runtime: no submodule, CI download or hidden Rust/C++/Node dependency. Do not modify
another worker's reference checkout to test its absence. Ordinary
build/test/package/consumer workflows need no sibling checkout.

Permit bounded C# adaptations of numerical algorithms needed by a demonstrated
renderer dependency, as with the clipping kernel at `57c6a7e3`. Record the exact
source revision/files, retain copyright and license notices in source and packages,
and verify independent vectors plus the shared ownership/work/cancellation
contracts. Adaptations must remain self-contained managed code with portable
regressions. Whole-renderer vendoring or translation remains outside the plan.

| Inspected source / revision | Bounded use |
| --- | --- |
| resvg `75b6bbadd7999d0516dcd7153b4a321bfdf8670a` | `usvg` style/tree, use/clip/paint binding and renderer operation order; typed resolved state and distinct geometry/stroke/layer bounds. |
| tiny-skia `5d4754777746eef0828be166896eaf482c49f8f2` | Edge/span organization, compact paths, stroke/dash expansion and storage reuse. Its fourfold supersampling is not Chrome-exact analytic AA. |
| resvg-test-suite `d8e064337faf01bc5a9579187a56dbdbe3eacc72` | Semantic counterexample index. It overlaps heavily with resvg's bundled suite; neither is a separate real-logo cohort or our PNG oracle. |
| NanoSVG `239e102ec2c691f2902e20ace2ed36ee4a35cfe6` | Secondary small-parser/scanline comparison only; five-subsample output is not the exactness target. |
| Matched Skia `f8b66b7597c4cc859d3ed190e9c6872241e6721c` | Release-tag scan, paint routing, stroker and curve construction for demonstrated S11/S12/S14 dependencies. The bounded clipping/solver adaptation retains this pin and BSD notices; compiled-native vectors and experiment provenance are in the design record. |

The recorded Skia comparison `8b8c3872fbc03f025855db96ce683f34ec98a815`
is a main revision, not the browser release pin. Use explicit matched
source reads without changing it implicitly. resvg's registry tiny-skia dependency
also need not be byte-identical to the sibling checkout despite equal version text.
