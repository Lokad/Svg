# Lokad.Svg — finite renderer delivery plan

Updated 2026-10-15 at the complete pattern tile-sheet checkpoint.
The unchanged whole sheet and all ten controls are exact on Windows/Linux.
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
There are **no proposed or newly approved feature deferrals** in this revision.

## Qualified work to preserve

| Commitment | Qualified state and evidence |
| --- | --- |
| Frozen corporate cohort | **27/27 documents / 258/258 variants exact on Windows and Linux**, including 13/13 marks and 14/14 outlined wordmarks. Membership, bytes, options, backgrounds and references are frozen in the [cohort manifest](tests/References/inventory/corporate-cohort-v1.json) and [current measurement](tests/References/inventory/corporate-cohort-current.json). |
| Selected development documents | **50/50 complete**, including unchanged whole `inv-146`. Preserve every preceding declared output in the [gap ledger](tests/References/inventory/svg-gap-cases.json) and [supplied-font document banks](tests/Research/offset-merge-document/README.md). Failure-selected inputs establish no representative success rate; controls have separate denominators. |
| Original acceptance and supplied-font work | Original marker/drop-shadow fixtures **2/2**, original supplied-font label/path fixtures **2/2**, SVG Translate, `inv-127` and `inv-145` are complete. Preserve intended-font proof, original programs/aliases and metrics/pixels in the [document evidence](docs/INVENTORY.md), [supplied-font documents](tests/Research/supplied-font-documents/README.md) and [curved labels](tests/Research/curved-font-documents/README.md). |
| `inv-146` and shared rendering corrections | Whole `inv-146`, eight oval/text controls and four [merge controls](tests/Research/filter-merges/README.md) are exact. Preserve the [merge receipt](artifacts/maintenance-review-20261012/merge-qualified-results.json), [design record](docs/RASTERIZATION_DESIGN.md) and [support record](docs/SUPPORT_MATRIX.md). Paint recording is **149/149 exact**; I Ching is **24 exact / one out-of-scope animation boundary**. Later qualified evidence supersedes historical gates. |
| Additional authored documents | **Twelve complete authored documents** (eleven CC0 and the MIT tile sheet), each **1/1 exact** on Windows/Linux, separate from both frozen cohorts. Preserve all separately counted controls in the [document evidence](docs/INVENTORY.md) and linked banks, including the [effects sheet](tests/Research/effects-marker-sheet/README.md) **11/11** and [composition sheet](tests/Research/composition-sheet/README.md) **4/4**. Bank manifests own immutable inputs, options, references and control denominators; document completion does not close a support group. |
| Qualified G2–G5 mechanisms | Fractional ordinary-use stroke widths, fractional/nonuniform symbol frames, nonuniform root curve fills and affine oval strokes, the non-scaling conic defect, composed cubic clips, translated custom mask regions and mask-container opacity have qualified witnesses. Preserve [use widths](artifacts/maintenance-review-20261012/fractional-use-qualified-results.json), [symbol frames](artifacts/maintenance-review-20261012/symbol-frames-final-results.json), [root curves](artifacts/maintenance-review-20261012/nonuniform-curves-qualified-results.json), [non-scaling strokes](artifacts/maintenance-review-20261012/non-scaling-qualified-results.json), [ovals](tests/Research/affine-oval-strokes/manifest.json) and [scopes](tests/Research/scoped-clips-masks/manifest.json). These completed mechanisms are dependencies already available, not work to repeat for wider maps or locations. |
| Filter bounds, rescaling and restoration | The [colored-effects bank](tests/Research/colored-effects-markers/README.md) preserves original controls **7/7**, repaired control **1/1**, bounds **4/4** and whole document **1/1**. The [filter-region bank](tests/Research/filter-regions/manifest.json) is **26/26 exact**, including sRGB rescaling/opacity and the unchanged displaced linearRGB blur. Preserve the [bounds](artifacts/maintenance-review-20261012/offset-image-bounds-qualified-results.json), [rescaling](artifacts/maintenance-review-20261012/rescaled-srgb-qualified-results.json) and [restoration receipts](artifacts/maintenance-review-20261012/linear-restoration-qualified-results.json). Wider G8 stays open. |
| Qualified marker and filter-source mechanisms | The [effects/marker bank](tests/Research/effects-marker-sheet/README.md) is **1/1 complete document / 11/11 exact controls** on Windows/Linux. Preserve marker units/overflow, image/shadow sources, leaf/group output clips, nested capture/halos, mixed spaces and affine solid-source replay. The unchanged whole sheet qualifies opaque-background integration; wider G8 remains open. [Affine receipt](artifacts/maintenance-review-20261012/affine-filters-qualified-results.json), [design record](docs/RASTERIZATION_DESIGN.md#complete-effects-sheet-through-bounded-affine-filtering-2026-10-15). |
| Font-pack host and acquisition | The opt-in [FontProbe host](tools/Lokad.Svg.FontProbe/README.md), its six [transport/input regressions](tests/Lokad.Svg.FontProbe.Tests/HttpFontProgramSourceProviderTests.cs), catalog conformance and prepared ownership are implemented. Private deployment validation below remains required; general shaping remains outside scope. |
| Resource/API/package protections | Preserve bounded preparation/raster/encoding, allocation/work denial, cancellation/retry, partial-owner cleanup, immutable borrowed bytes, single-use prepared ownership and output integrity. Existing [API contract](docs/API_CONTRACT.md), installed-consumer and package checks apply; final release evidence must cover the finished revision. |

Completed work remains closed. Final-release qualification covers the finished
revision without reopening unchanged mechanisms for a new accounting or coverage
policy.

The [tile-content receipt](artifacts/maintenance-review-20261012/pattern-content-qualified-results.json)
records finished platform checks and preservation. The unchanged tile sheet is
**1/1 complete document / 10/10 exact controls** against its original Windows/Linux
references, including supplied-font proof. Image/filter/font content now joins
qualified opacity, viewport/unit and reflected-map behavior. The
[bank](tests/Research/pattern-tile-sheet/README.md) owns assets and options; the
[design record](docs/RASTERIZATION_DESIGN.md#complete-pattern-tile-content-2026-10-15)
owns mechanisms and rejected models. Current-revision CI is pending; preceding
terminal checks remain valid historical evidence and must not be repolled.
Remaining G6 maps/resource presentation belong to row 3 below. **G2–G9 and S17
remain open**; completing the sheet does not close those finite exits.

<a id="delivery-order"></a>
<a id="3-ordered-support-queue"></a>

## Ordered delivery checklist

The **next three deliverables**, in order, are **the remaining label/path/composite
cases and finite private font validation**, **remaining composition acceptance
including pattern maps**, then **remaining effects/markers**. These are rows 2–4.
The pattern, effects and composition document milestones are delivered; independent
uncovered support obligations remain required. Finish rows 2–4 before release.
Bring a later dependency forward only when it unblocks an earlier deliverable;
no exhaustive G2 completion or unrelated group closure is a prerequisite.

Select shared fixes by the retained documents they advance. Reuse geometry,
source replay, paint maps and bounded layers, preserving native pixel distinctions.
No document IDs, source coordinates or observed failure pixels may become renderer
predicates. The finite exits below own each independent semantic once; reuse its
witness across groups and APIs rather than opening a second checklist.

| Required checklist | Missing behavior and dependencies | Concrete acceptance / completion condition |
| --- | --- | --- |
| [x] 1. Pattern tile sheet document (G6 milestone) | Delivered image/filter/supplied-font content through existing bounded leaf replay, decode/capture and native font/shader stages. | The unchanged whole sheet is **1/1 exact / 10/10 exact controls** on both platforms, with affected ownership/denial/cancellation checks. Preserve the eight preceding complete pattern documents. Remaining G6 maps/presentation stay required in row 3; this document milestone does not close G6. |
| [ ] 2. Remaining labels, paths and font validation (G9) | Stroked decorations/paths; uncovered glyph maps; three composite flag semantics; private deployment validation. Reuse qualified layout, fitting and paint/source maps. | Direct and curved-path stroked decorations, bounded overlapping/transformed-offset composites, and only uncovered affine glyph semantics. Complete when each is exact on both platforms, valid degenerates have measured outcomes, affected failure checks pass and the finite private request set has sanitized outcomes. Preserve completed label/path and intended-font evidence. |
| [ ] 3. Remaining composition acceptance (G2–G7 maps, placement/style) | Unwitnessed stroke/pattern/instance maps and applicable pattern presentation; heterogeneous/curved clip-child unions and larger fallback; remaining mask units/nested restoration; image offsets and resource presentation. Reuse the completed [composition sheet](tests/Research/composition-sheet/README.md), geometry and native source replay. | The finite cases below cover dashed curved shader caps/alpha, reflected pattern maps, remaining child-union and unit/restoration boundaries, independent image offsets and applicable resource style. Complete when each missing semantic is exact on both platforms, with measured degenerate outcomes and affected cleanup. Percentage-use, convex overlap and partial-use-mask controls are completed dependencies. |
| [ ] 4. Remaining finite effects/markers (G8) | Colored/translucent linearRGB shadows; identity/linear-image restoration; uncovered affine paint/source maps, object-box clips, source crossings and marker semantics. Reuse qualified bounded capture, transfer and restore. | Preserve the delivered [whole sheet and eleven controls](tests/Research/effects-marker-sheet/README.md). Complete each named finite G8 acceptance below, including the retained standalone linear-image defect, with affected graph/region/resource checks. Whole-sheet completion alone does not close this row. |
| [ ] 5. Release (S17) | Finished-revision contracts and evidence after rows 1–4. | Close all finite G2–G9 exits; preserve established exact results; pass complete platform/API/resource/package/consumer checks and exact-revision CI; report representative measurements. Every [release condition](#release-completion-s17) must hold. Counts alone cannot establish completion. |

**Next correction:** freeze the missing direct stroked-decoration and curved
decorated-path cases alongside the retained label/path bank, then take both to
exact Windows/Linux output through existing glyph, stroke and paint routing.
Preserve the completed tile sheet and intended-font evidence. Bind uncovered
label/composite semantics to existing witnesses or freeze only a missing independent
interaction. G6 target/stroke/instance maps remain on row 3; no feature promise
is deferred or dropped to advance the next document.

The effects/composition/tile sheets are bounded acceptance recipes, not new
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
**G2–G9 and S17 remain open.** The five delivery items own execution; the finite
lists below define their exits, rather than creating a second backlog. Bind each
still-unverified mechanism to an existing acceptance case or to the next declared
document before implementing it. Reuse qualified witnesses; no exhaustive
combination census or separate tracking framework is required.
The composition and pattern tile sheets are qualified; their wider finite
support exits remain open. Acceptance recipes without file links are **planned**, not captured or
qualified. Freeze a new case only for
an uncovered independent semantic or demonstrated interaction.

<a id="g2g4-instances-geometry-maps-and-strokes"></a>

### G2–G4: instances, geometry, maps and strokes — row 3

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

| Required remaining behavior | Dependency / concrete acceptance / completion |
| --- | --- |
| Unwitnessed stroke-to-paint maps, dashed curved shader caps/translucency, reflected/magnified patterns | Reuse [use paints](tests/Research/use-paint-properties/manifest.json), [native replay](tests/Research/native-path-replay/manifest.json), [paint recording](tests/Research/paint-recording/manifest.json) and [oval source](tests/Research/oval-source-replay/manifest.json). Add focused panels only for uncovered rotation/skew/reflection/nonuniform maps: a dashed curved shader with round/square caps and fractional alpha, and reflected/magnified pattern placement. Complete when each independent missing map/cap/opacity semantic is exact through shared routing, preserving native shape distinctions. |
| Finite degenerate maps | Reuse existing geometry checks; measure a singular map and zero-size geometry only where an outcome is absent. Complete when finite valid degenerates have measured nonpainting or categorized outcomes with affected cleanup checks. No new shape-by-map grid. |

<a id="g5-viewports-clips-masks-and-composition"></a>

### G5: viewports, clips, masks and composition — row 3

Nested SVG/symbol viewBox/aspect maps and percentage bases; local paint definitions;
G3 clipPath shapes in userSpace/objectBoundingBox coordinates, ordered overlapping
children and compound/winding behavior; alpha/luminance masks with both region and
content unit systems, transformed finite regions and implicit root viewport bases;
nested leaf/group/use opacity, clips and masks. Include mask-container non-unit
opacity and the root/group/defs/symbol/nested-SVG definition locations.
Distinguish definition scope, object bounds, visual stroke bounds and percentage bases. Forward binding
and source order must agree; bounded surfaces preserve native AA/BW consumers.
Preserve completed [object-box clips](tests/Research/object-bounding-clips/manifest.json),
composed cubic/custom translated-region cases and mask-container opacity; their
unit/affine/native child-painting witnesses are dependencies, not blockers.
The completed [composition bank](tests/Research/composition-sheet/README.md) adds
live percentage instance bases, bounded convex straight-child overlap under nested
viewport maps and partial uniform/spatial use masks. Preserve these results; they
do not establish heterogeneous/curved unions or all mask-unit/restoration boundaries.

| Required remaining behavior | Dependency / concrete acceptance / completion |
| --- | --- |
| Remaining heterogeneous/curved/compound clip-child unions and larger fallback | Reuse native source/clip replay and the exact convex-overlap controls. Freeze a mixed rectangle/path union, overlapping curved and compound/concave children with independent winding, and one 43-child fallback case within ordinary limits. Complete when these missing independent semantics are exact on both platforms with bounded work/cleanup. No shape-by-child-count grid. These promised G5 behaviors are not closed by the convex-only correction. |
| Remaining mask region/content-unit and nested restoration boundaries | Existing mask/source replay and qualified custom-region/container-opacity work. Preserve the exact partial-use mask; reuse [local references](tests/Research/mask-local-references/manifest.json) and [group masks](tests/Research/mask-group-documents/README.md), then freeze only uncovered region/content-unit or nested leaf/group/use boundaries. Complete when each independent unit/restoration boundary is exact, with scope termination and affected cleanup checks. The completed scoped card alone cannot close these exits. |

<a id="g6-patterns"></a>

### G6: patterns — remaining work on row 3

Retain ordered G3 vector leaves/groups/local uses with solid/gradient paint and
opacity; both tile/content unit systems, viewBox/aspect maps, fractional finite
tile origins/sizes and composed affine transforms. Embedded images and filtered/
supplied-font content now have exact retained witnesses. Preserve tile borders, phase, alpha,
transformed fill/stroke and inheritance without unbounded repeat-count work.
Recursive pattern/paint dependencies reject explicitly. Qualified native first
linear/radial/rounded payloads, fractional phase and border clipping stay closed.
Qualified dependencies are linked from the
[tile bank](tests/Research/pattern-tile-sheet/README.md) and
[coordinate receipt](artifacts/maintenance-review-20261012/pattern-units-qualified-results.json):
unchanged element/group opacity, scaled nested-viewport rectangle fills, both
object-box unit systems without viewBox, inherited default units and the retained
fractional reflected map. Use original local object bounds, separate from clip/
stroke extents, and retain native drawing/shader-local matrix stages. These
mechanisms are completed dependencies. The complete tile sheet additionally
qualifies image/filter/supplied-font content via the
[content receipt](artifacts/maintenance-review-20261012/pattern-content-qualified-results.json);
G6 remains open for the shared maps/presentation exits below.

| Required remaining behavior | Dependency / concrete acceptance / completion |
| --- | --- |
| Uncovered target/stroke/instance maps and viewBox/phase interactions | Reuse qualified [viewBox](tests/Research/pattern-viewbox/manifest.json), [vector](tests/Research/pattern-vector-content/manifest.json), [instances](tests/Research/pattern-instance-content/manifest.json) and new unit/map witnesses. Retained boundaries still include scaled-viewport patterned strokes/uses and skewed oval targets; they are required G4/G6 work, not approved deferrals. Bind each independent missing map to the row-3 curved shader/stroke case or one focused existing control before correction. Complete when each missing map is exact, finite degenerates have measured outcomes and tile work remains bounded; no exhaustive geometry/location census. |
| Applicable container presentation and local content clip/mask scopes (shared G5/G7 exit) | Keep the remaining row-3 resource presentation and scope promises visible. Reuse existing clipped vector-tile witnesses; freeze one uncovered bounded image/vector content clip/mask case and the inherited-container-paint/child-override panel before correction. Complete when each independent scope/cascade boundary is exact with bounded cleanup; no content × paint × instance product. Current extended-content clip/mask rejections are explicit, not approved feature deferrals. |

<a id="g7-embedded-pngs-and-static-css"></a>

### G7: embedded PNGs and static CSS — remaining work on rows 2–4

Retain non-interlaced 8-bit RGB/RGBA, grayscale/grayscale-alpha and indexed PNG,
including tRNS, from base64 data URIs with chunk/CRC/decompression validation.
Finite image coordinates/dimensions, viewBox percentages, all meet/slice alignments,
`none`, fractional/general affine placement, clips/masks/opacity remain in scope.
Varied pixels, fractional axis sampling, uniform paint opacity, affine sampling,
source-cropped slice and local image-use/partial-mask replay are qualified in the
[CSS/image](tests/Research/static-css-images/manifest.json) and
[affine-image](tests/Research/affine-image-maps/manifest.json) banks; these gates are
closed. Preserve bounded decode ownership and low-precision image matrix rounding
separately from gradient/glyph arithmetic.

Retain existing ID/class/attribute/selector lists, SVG element and element+ID/class,
descendant/child selectors through supported root/nested SVG/definition/instance
trees; specificity, source order, `!important`, inline precedence and inheritance
for the enumerated G2–G6/G9 presentation/font properties, plus parsed geometry
width/height. Keep qualified [inert typography](tests/Research/inert-typography/manifest.json),
late stylesheet collection and [root/resource cascade](tests/Research/root-resource-cascade/manifest.json)
closed. Root/rendered-group/defs/symbol/nested-SVG locations and source-ancestor
exclusion are no longer general cascade blockers; value boundaries remain in
[SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md). `paint-order` retains normal and distinct
fill/stroke/markers lists, appending omitted operations in default order;
duplicate/unknown tokens reject. Apply each resource's applicable properties.

| Required remaining behavior | Dependency / concrete acceptance / completion |
| --- | --- |
| Independent horizontal/vertical image alignment offsets | All nine spellings, `none` and omitted-meet equivalence are implemented in [aspect tests](tests/Lokad.Svg.Tests/SvgAspectRatioTests.cs). Reconcile native image-bank evidence for horizontal/vertical Min/Mid/Max offsets in meet/slice on portrait/landscape sources. Row 3 adds only uncovered offset semantics. Complete when independent offsets are qualified with existing PNG/resource validation; no nine-alignments × placements census. |
| Remaining resource presentation and dependent image content | Reuse cascade witnesses, [selector](tests/Lokad.Svg.Tests/SvgCssSelectorTests.cs) and [paint-order](tests/Lokad.Svg.Tests/SvgPaintOrderTests.cs) regressions. Qualify only missing applicable clipPath/mask presentation in row 3, uncovered pattern presentation in row 3, marker/filter in row 4, text/span paint in row 2. Concrete panels use inherited/container paint versus a child override and stylesheet/inline priority for the uncovered resource. Image filter/tile content has qualified witnesses; remaining interactions belong to their owning support rows. Complete when each independent boundary has exact acceptance in its owning document/control; add source-order coverage only for an uncovered interaction. |

Outside scope with explicit outcomes: interlaced/16-bit/color-managed PNG,
JPEG/GIF/WebP/external image fetching; CSS imports/media/supports, pseudo
classes/elements, sibling combinators, layout, variables, animation and web fonts.

<a id="g8-markers-and-finite-static-effects"></a>

### G8: markers and finite static effects — row 4

Retain local start/mid/end markers on G3 lines/polylines/polygons/paths and
instances; userSpaceOnUse/strokeWidth, refX/refY, finite lengths/sizes,
viewBox/aspect maps, fixed/auto/auto-start-reverse orientation, ordered
vector/group/use content, ordinary paint, source-order participation, overflow
and container presentation. Absolute number/px/in/cm/mm/Q/pt/pc lengths,
known-axis percentages, unitless/deg/grad/rad/turn angles and literal hidden/visible
CSS overflow are qualified. Preserve [path](tests/Research/path-marker-content/manifest.json)
and [instance](tests/Research/marker-instance-content/manifest.json) witnesses.

Retain feGaussianBlur, feOffset, feFlood, feComposite over/in, feMerge, feBlend
normal/multiply/screen and feDropShadow; SourceGraphic, SourceAlpha and earlier local
results; filter/primitive regions, both unit systems, sRGB/linearRGB and local CSS
filter references. Parsing/default/result routing, per-primitive spaces,
binary/merge transfers, distinct CPU-kernel/shadow consumers and nested capture
are qualified. Reuse [graph](tests/Research/filter-documents/manifest.json),
[merge](tests/Research/filter-merges/manifest.json),
[kernel](tests/Research/filter-blur-kernel/manifest.json),
[colored/bounds](tests/Research/colored-effects-markers/manifest.json) and
[mixed-space](tests/Research/effects-marker-sheet/manifest.json) witnesses with
[playback regressions](tests/Lokad.Svg.Tests/SvgFilterPlaybackTests.cs).
A flood override alone does not establish a color transition.

| Required remaining behavior | Dependency / concrete acceptance / completion |
| --- | --- |
| Uncovered affine paint/source maps | Solid-source affine filtering and whole-panel integration are qualified in the completed bank. Non-solid affine paints, source clips and nested resource boundaries retain explicit gates. Reuse the shared layer/source/paint maps and bind an uncovered route to its owning composition/tile/label panel or focused control. Complete when each remaining independent promised route is exact, preserving all eleven effects controls, nearest offsets and native image origins. |
| Colored/translucent linearRGB shadows; identity/zero-offset colored-source and standalone linear-image/outer-opacity restoration | Existing transfer/restore routes. Reuse retained colored-source diagnostics; freeze a colored linearRGB shadow over a translucent source and identity/zero-offset controls only where not already qualified. The retained [single-linear-image/outer-opacity diagnostic](artifacts/effects-marker-sheet-20261015/mixed-consumer-single-blur.svg) still differs; one healthy native observation is not acceptance. Complete when each named shadow/restore semantic is exact, preserving region/opacity and mixed-space results. This remains required after the completed whole-sheet milestone. |
| Object-box output clips and filtered sources crossing mask, nested viewport, marker, text and local-use boundaries | Reuse qualified image/vector/group output clips and nested source/halo replay. Freeze a filtered target with an object-box clip and one focused witness for each uncovered source boundary, bringing G2/G5/G9 dependencies forward as needed. Complete when each independent boundary is exact with source/output ordering, halos and scope termination protected. No content × graph × container census. |
| Font-relative marker lengths, container presentation/inheritance, closure/tangent/degenerate behavior | Existing marker length/style/vertex replay. Freeze font-relative sizing and inherited container paint versus a child override, plus auto/auto-start-reverse open/closed/zero-length-path witnesses. Reuse exact angle/overflow controls. Complete when named semantics are exact or valid-degenerate outcomes measured; missing/recursive references remain explicit errors. |

Preserve bounded intermediate regions, real blur/composition work, ordered
premultiplied arithmetic, cancellation and partial cleanup. The 26/26 filter-region
bank and `inv-146` source/parent-device rounding/padded capture remain closed.
Outside scope with explicit outcomes: backdrop inputs, external filter images,
displacement, turbulence, lighting, morphology, convolution, arithmetic composite,
other blend modes, backdrop-filter and CSS filter-function lists. Recursive effects
and forward/missing result names retain categorized outcomes.

<a id="g9-supplied-font-labels-and-text-paths"></a>

### G9: supplied-font labels and text paths — row 2

Retain supplied standalone static TrueType face 0, catalog/style/coverage fallback,
bounded pair positioning and default Basic Latin ligatures: Latin/DFLT default
language, one unfiltered type-4 lookup, format-1 subtables, coverage 1/2, two to
eight components in font order. Nonzero spacing/positioned-span boundaries prevent
merging. General shaping/other lookup flags/types/formats remain explicit boundaries.

Retain horizontal LTR ASCII/tested precomposed Latin and covered supplementary
scalars; text/CDATA/whitespace/nested tspan order; inherited/per-span family, size,
weight, style, paint and visibility; scalar/list x/y/dx/dy/rotate; start/middle/end
anchors, letter/word spacing, explicit lines, textLength spacing/spacingAndGlyphs,
alphabetic/middle/hanging baselines, affine glyph maps, ordinary fill/stroke paints
and solid underline/overline/line-through. Filled-label decoration is qualified.
Paths retain one local open/closed G3 path per run, supplied advances/kerning,
length/percentage startOffset, anchors/tangents, method align and spacing exact,
curved/transformed source paths and measured out-of-range/zero-length outcomes.
Positioned spans and dashed/non-scaling/stroke-before-fill/text-path strokes have
completed-sheet witnesses; preserve uniformly mapped solid strokes and shared replay.

| Required remaining behavior | Dependency / concrete acceptance / completion |
| --- | --- |
| Stroked decorations and decorated paths | Existing decoration fragments/layout/source replay. Freeze a direct stroked decoration and a curved decorated path using supplied public fonts. Complete when both semantics are exact on Windows/Linux, preserving filled decorations and existing path/stroke witnesses. |
| Unverified skew/reflection, stroke-paint or instance glyph maps | Reuse qualified translation/rotation/nonuniform cases; add a focused label/path panel only for an uncovered independent map. Complete when every still-unverified promised map has exact acceptance, with valid degenerate-path outcomes recorded. No font/size/transform grid. |
| OVERLAP_COMPOUND, SCALED_COMPONENT_OFFSET and UNSCALED_COMPONENT_OFFSET | Existing bounded composite decoding/fitting. Freeze public synthetic overlapping/transformed-offset components. Complete when all three flag semantics are exact with affected ownership/failure checks. Preserve completed parent-hint metrics, USE_MY_METRICS, nested accents, Windows LCD/dropout/contours and Linux light fitting. Implement finite admitted label semantics; executed unsupported opcodes/formats reject explicitly without a general VM grid. |
| Private deployment validation | Implemented host/catalog and public font-pack contract. Execute the finite regular/bold/italic/fallback requests under the [private rules](#authorized-private-font-testing). Complete with sanitized outcomes for each request, reproducible in-scope defects converted to public synthetic acceptance and resolved. Private success alone cannot establish public admission. |

Preserve [composite metrics](tests/Lokad.Svg.VisualTests/SvgCompositeMetricsTests.cs),
[frozen contours](tests/Research/curved-font-documents/diagnostics/composite-metrics-contours.json)
and [ownership regressions](tests/Lokad.Svg.Tests/OpenTypeFontCompositeRasterTests.cs).
Outside scope: wrapping, bidi/joining/complex scripts, general GSUB/GPOS/mark
positioning, vertical text, path stretch/auto spacing, variable/color/bitmap fonts,
CFF/CFF2, WOFF/WOFF2 and machine-font discovery. Missing required programs/glyphs
cannot disappear or silently substitute a machine font. Font/host contracts below apply.

## Release completion (S17)

Release is complete only when **all** of these conditions hold for the finished
revision of the promised useful subset. No release is established by a
document/test count or a status paragraph alone.

1. Rows 1–4 are closed against the finite G2–G9 boundaries: each remaining
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
- Keep **routine incremental Release build plus complete tests below ten minutes
  per platform**.
  The [latest receipt](artifacts/maintenance-review-20261012/pattern-content-qualified-results.json)
  records **7,803 tests/platform** in **71.7s Windows / 227.0s Linux**, including
  normal build/startup and complete tests. Both meet the target. Keep each
  receipt's workload/methodology limits; these are no speedup claims.
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

**Required private validation, attached to row 2 (G9):** freeze one regular, one
bold, one italic and one fallback request before execution. Use the unchanged
deployed manifest to prepare their required programs; inventory their relevant
formats, coverage, composites and positioning
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
length policies and the complete path sheet stay closed. Only row 2 and its
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
