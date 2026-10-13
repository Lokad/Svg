# Lokad.Svg — finite renderer delivery plan

Updated 2026-10-13. This plan owns remaining work and completion decisions;
[SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md) owns admission,
[INVENTORY](docs/INVENTORY.md) owns document evidence,
[RASTERIZATION_DESIGN](docs/RASTERIZATION_DESIGN.md) owns mechanisms and diagnostic
history, and [BENCHMARKS](docs/BENCHMARKS.md) owns performance evidence.
Historical checkpoints remain in those records and Git rather than forming a
second execution queue here. The finite-plan revision is retained. The subsequent qualified `inv-146`
correction preserves every original SVG/font/reference byte and default resource
limit; completed work stays separate from the required exits below.

## Qualified work to preserve

| Commitment | Qualified state and evidence |
| --- | --- |
| Frozen corporate cohort | **27/27 documents / 258/258 variants exact on Windows and Linux**, including 13/13 marks and 14/14 outlined wordmarks. Membership, bytes, options, backgrounds and references are frozen in the [cohort manifest](tests/References/inventory/corporate-cohort-v1.json) and [current measurement](tests/References/inventory/corporate-cohort-current.json). |
| Selected development documents | **50/50 complete**, including the unchanged whole `inv-146`. Preserve every preceding declared output in the [gap ledger](tests/References/inventory/svg-gap-cases.json) and the separate [supplied-font document banks](tests/Research/offset-merge-document/README.md). These failure-selected inputs establish no representative success rate. Controls have separate denominators. |
| Original acceptance documents | Original marker and drop-shadow fixtures **2/2 complete**. Original supplied-font live-label and text-path fixtures **2/2 complete** under their declared public-font contracts. See the [marker bank](tests/Research/marker-instance-content/manifest.json), [filter bank](tests/Research/filter-documents/manifest.json), [label bank](tests/Research/supplied-font-labels/README.md) and [text-path bank](tests/Research/supplied-font-text-paths/README.md). |
| Shared rendering mechanisms | Completed heart/I Ching, patterns, curved and outlined marks, native strokes, static paint order, finite masks/clips and existing filter documents stay exact. Their remaining combinations are identified below; completed source replay, hinting, winding, preblend and layer fixes are not prerequisites to requalify. See the [design record](docs/RASTERIZATION_DESIGN.md) and existing research manifests. |
| Supplied-font work | SVG Translate, `inv-127`, `inv-145`, both original acceptance documents and their useful glyph/layout/mask controls are complete. Preserve original fonts, aliases, options, intended-font proof and independent Windows/Linux references. See [supplied-font documents](tests/Research/supplied-font-documents/README.md), [curved labels](tests/Research/curved-font-documents/README.md) and [group masks](tests/Research/mask-group-documents/README.md). |
| Qualified merge correction | Complete `inv-146`, all eight preceding oval/text controls and four [canonical merge controls](tests/Research/filter-merges/README.md) are exact on both platforms. Complete Release checks pass **7,459 tests/platform** (5,146 core / 2,307 visual / six FontProbe), zero warnings/failures/skips; all preceding cases are retained, with the existing graph-limit test expanded to two inputs. [Qualification receipt](artifacts/maintenance-review-20261012/merge-qualified-results.json). The merge delivery at 82dc9206 has [green hosted Windows/Linux CI](https://github.com/Lokad/Svg/actions/runs/38464311300), including memory, browser/font, package and installed-consumer gates. Two GitHub status reads established the terminal result; no further polling or unchanged qualification is needed. |
| Resource/API/package protections | Keep bounded preparation/raster/encoding, denied work/allocation, during-phase cancellation, partial-owner cleanup, immutable borrowed bytes, single-use prepared ownership and output integrity. Existing [API contract](docs/API_CONTRACT.md), installed-consumer and package checks remain applicable while unchanged. Final release evidence must match the finished revision. |

Previously stated blockers for completed library/heart/pattern documents,
scaled nested uses, inert outlined typography, rotated solid ovals and direct
uniformly mapped solid glyph strokes are retired. The non-scaling root-viewBox
**width map** is repaired; its remaining oval **coverage** discrepancy is still
required work. Earlier claims that hosted CI is missing for delivered corrections
are stale; final-release CI is a separate gate.

## Delivery order

Select each shared change by the retained document or measured gap it advances.
Finish every declared output against unchanged references, diagnosing with small
synthetic cases. No document IDs, source coordinates or observed failure pixels
may become renderer predicates. Extend geometry, style, source replay, paint maps
and bounded layers; preserve the native distinctions required for exact pixels.
Group IDs identify responsibilities, not serial implementation prerequisites.
Exhaustive G2 combinations do not block G3–G9 or the next document.

The **next three deliverables are items 2–4**: shared stroke/paint maps,
clip/mask composition, then remaining static effects/markers, in that order. Each can use small
buildable commits with its affected tests/documentation; a partial mechanism fix
is progress, not completion of its item or support group.

| Required remaining item | Missing behavior and dependencies | Concrete acceptance and completion condition |
| --- | --- | --- |
| 2. Finish shared stroke and paint-coordinate gaps (G2/G3/G4) | Non-scaling curved coverage, remaining fractional effective instance widths, nonuniform curve/stroke/paint maps and shader-painted source strokes. Depends on existing source contours/stroker and composed paint maps; share one stroke route across paints. | Close [non-scaling oval](tests/Research/oval-source-replay/manifest.json) (98 differing pixels, max alpha 7 at the qualified baseline), retained zero-area shader strokes, gradient-line and fractional-use-width cases on unchanged references. Use rotated-symbol gradient and nonuniform curve twins to protect admission/map semantics. A complete retained export demonstrating any remaining mechanism is declared before editing. Item exits when these gaps, G3/G4 and G2 width/map behavior have positive exact witnesses, with no prior exact regression; remaining G2 scope/composition follows item 3. No Cartesian combination quota. |
| 3. Finish scoped clip/mask composition (G2/G5) | Overlapping clip children, remaining transformed/unit clip maps, nested definition scopes, custom mask regions, mask-container opacity and remaining leaf/group/use composition. Depends on item 2 only where its maps are actually needed; reuse existing bounded scene/layer ownership. | Close [composed cubic clip](tests/Research/curved-path-clips/composed-group-cubic.svg), object-box clip witnesses, retained nested-viewport paint and `mask-region-half-translated` cases. Declare one licensed complete export combining an identified remaining mask/clip gap with ordered group/use content. Exact original outputs plus focused overlap/unit/scope/opacity controls and bounded cleanup close the finite G5 exit below. Completed `inv-136/137/141/145` stay regression witnesses. |
| 4. Finish finite static effects and marker gaps (G8) | Small/anisotropic blur, fractional offsets, flood/composite/blend and remaining result/region/color-space graphs; remaining marker directions/containers. Depends on the completed `inv-146` source bounds and items 2–3 only for affected geometry/composition. | Close [translated rectangle source](tests/Research/filter-regions/translated-rectangle-source.svg) (85 pixels, max channel 1) and [transparent displaced blur](tests/Research/filter-regions/displaced-blur-transparent.svg) (six pixels, max channel 14 / alpha 1). Freeze a colored alpha composition with `over`/`in` and normal/multiply/screen, and an arc/polyline marker example. Finish retained complete exports exercising each missing mechanism; exact positive witnesses for the finite G8 subset and bounded graph/surface/work regressions close this item. |
| 5. Finish static CSS and embedded images (G7) | Remaining selectors/cascade locations, presentation propagation and enumerated PNG/sampling/maps. Depends on shared G2/G5 scopes and G3 maps; preserve completed late rules, inert geometry typography and paint order. | Existing image/compound-selector cases plus a multicolor translucent PNG edge under fractional meet/slice and a root/nested/late-rule export affecting referenced content match frozen outputs. Authored override, CRC/decode denial and corrupt/missing image outcomes pass. The G7 subset below has representative positive witnesses and explicit unsupported boundaries. |
| 6. Finish supplied-font label/path gaps (G9) | Lists, length adjustment, spacing/baselines/decorations, remaining affine/paint/stroke/path combinations and queued composite semantics. Depends on existing font pipeline and G3/G4/G5 maps/paint; no host-font discovery. | A supplied-font label diagram covers mixed spans/fallback, position/rotate lists, lengths and baselines; an open/closed transformed curved-path example protects offset/anchor/out-of-range semantics. Retain AV/space/accent/composite and stroke/decoration controls. Existing intended-font proof and independent metrics/pixels apply. Exact finite G9 witnesses plus bounded preparation/layout/stroke failure cases close the item. |
| 7. Finish pattern dependencies and release (G6/S17) | Remaining pattern unit/map/opacity and image/text/filter content dependencies, then finished-revision delivery evidence. Vector improvements can land earlier when they advance a document; image/text/filter tiles follow the needed G7–G9 slices. | Original eight completed pattern documents remain exact. A retained transformed tile export exercises fractional phase, group/use opacity and the missing dependent content; border/alpha/inheritance/recursion controls close G6. Then satisfy every release condition below, update actual support contracts and publish categorized residuals. Document counts alone cannot close this item. |

For an acceptance example not yet retained, acquire only a document that
demonstrates the named missing behavior. Declare original bytes, license, output,
options and supplied face before screening/capture; freeze healthy references
before production edits. A complete document may cover several requirements.
If no redistributable export exists, use an original CC0 complete diagram and
focused controls. Do not grow the corpus to satisfy counts or exhaust combinations.

## Required support boundaries and finite exits

These are the promised useful subset, including remaining combinations that
currently reject. A current rejection is not an approved scope deferral. Each
group closes when every listed mechanism has a representative exact positive
case, the named retained defects are resolved, its negative boundaries are
categorized and affected resource/output regressions pass. Cover independent
semantics and discovered interactions, not every shape × paint × placement × API.
Keep an unverified behavior open; completed document counts do not prove it.

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
- Remaining witnesses: nonuniform root curve and rotated-symbol gradient gates,
  reflected/magnified pattern instances, fractional use width, mixed closed/open
  zero-area shader strokes, gradient lines and non-scaling oval coverage.
  Existing [use paints](tests/Research/use-paint-properties/manifest.json),
  [native replay](tests/Research/native-path-replay/manifest.json),
  [paint recording](tests/Research/paint-recording/manifest.json) and
  [oval source](tests/Research/oval-source-replay/manifest.json) retain the evidence.

### G5: viewports, clips, masks and composition

Nested SVG/symbol viewBox/aspect maps and percentage bases; local paint definitions;
G3 clipPath shapes in userSpace/objectBoundingBox coordinates, ordered overlapping
children and compound/winding behavior; alpha/luminance masks with both region and
content unit systems, transformed finite regions and implicit root viewport bases;
nested leaf/group/use opacity, clips and masks. Include mask-container non-unit
opacity and remaining nested-definition/style locations. Distinguish definition
scope, object bounds, visual stroke bounds and percentage bases. Forward binding
and source order must agree; bounded surfaces preserve native AA/BW consumers.
Retained custom-region, object-box and composed-curve cases are positive exits,
not permanent rejection allowances. Evidence: [object-box clips](tests/Research/object-bounding-clips/manifest.json),
[local mask references](tests/Research/mask-local-references/manifest.json),
[group masks](tests/Research/mask-group-documents/README.md) and the design record.

### G6: patterns

Ordered G3 vector leaves, groups and local uses with solid/gradient paint and
opacity; both pattern/content unit systems, viewBox/aspect mapping, fractional
finite tile origins/sizes and composed affine transforms. Embedded images and
filtered/text content are required G7–G9 dependencies. Preserve tile-border,
phase, alpha, transformed fill/stroke and inheritance behavior without unbounded
repeat-count work. Recursive pattern/paint dependencies reject explicitly.
The [vector content](tests/Research/pattern-vector-content/manifest.json),
[viewBox](tests/Research/pattern-viewbox/manifest.json) and
[instance](tests/Research/pattern-instance-content/manifest.json) banks protect
completed work; their exact cases do not close dependent tile content.

### G7: embedded PNGs and static CSS

- PNG: non-interlaced 8-bit RGB/RGBA, grayscale/grayscale-alpha and indexed
  palettes, including tRNS transparency, from base64 data URIs. Keep chunk/CRC/
  decompression validation. Finite image dimensions/positions, viewBox percentage
  bases, all meet/slice alignments, `none`, fractional/general affine placement,
  clipping/masking/opacity use shared bounded decode/sampling/maps.
- CSS: existing ID/class/attribute/selector lists plus SVG element,
  element+ID/class, descendant and child selectors across supported root/nested
  SVG/definition/instance trees. Static rules apply in source order with
  specificity, `!important`, inline precedence and inheritance for the
  presentation/font properties enumerated in G2–G6/G9; retain parsed geometry
  width/height declarations. `paint-order` supports normal and distinct
  fill/stroke/markers lists, appending omitted operations in default order;
  marker participation depends on G8. Duplicate/unknown tokens reject explicitly.
- Preserve completed inert geometry typography validation and late stylesheet
  prepass. Their value boundaries are in [SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md)
  and [inert typography](tests/Research/inert-typography/manifest.json).
- Outside scope with explicit outcomes: interlaced/16-bit/color-managed PNG,
  JPEG/GIF/WebP/external image fetching; CSS imports/media/supports, pseudo
  classes/elements, sibling combinators, layout, variables, animation and web fonts.

### G8: markers and finite static effects

- Markers: local start/mid/end on G3 line/polyline/polygon/path and instances;
  userSpaceOnUse/strokeWidth, refX/refY, finite sizes, viewBox/aspect mapping,
  angle/length units, fixed/auto/auto-start-reverse orientation; ordered
  vector/group/use content with ordinary paint, source-order paint participation,
  overflow attributes/CSS and container presentation. Existing line and
  straight/quadratic/cubic path roles/inheritance are complete. Finish arc
  directions, polyline/polygon vertices and instances, remaining units and CSS
  overflow/container combinations. Measure zero-length/closed-path outcomes;
  missing/recursive marker references have explicit errors.
- Effects: `feGaussianBlur`, `feOffset`, `feFlood`, `feComposite` over/in,
  `feMerge`, `feBlend` normal/multiply/screen and `feDropShadow`; SourceGraphic,
  SourceAlpha and earlier local results; filter/primitive regions, both unit
  systems, sRGB/linearRGB and local CSS `filter:url(#id)`. Existing identity and
  three-box/large blur, integer offset, black shadow, grouped geometric sources
  region/unit controls and ordered local offset merges are qualified. Finish
  small/anisotropic/zero kernels,
  fractional offsets, colored shadows/composition, graph/default/result handling,
  clipped halos, rotated/skewed filter maps, other containers/instances, nested
  filters and clip/mask/marker/text/image interactions. These are required
  mechanisms, with representative interactions rather than exhaustive products.
- Allocate bounded intermediate regions and charge real blur/composition work;
  preserve ordered premultiplied arithmetic, cancellation and partial cleanup.
  The [filter-region bank](tests/Research/filter-regions/manifest.json) retains
  two numerical defects; the complete `inv-146` merge witness is now exact.
  Retain its source/parent-device rounding and padded capture bounds alongside
  the four canonical merge controls.
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
- Paths: one local retained open/closed G3 path per run, supplied advances/kerning,
  length/percentage startOffset, anchors/tangents, method align, spacing exact;
  curved/transformed source paths, measured out-of-range/zero-length outcomes.
  Keep admitted curved labels and scalar positioned spans closed. Dashed,
  non-scaling, stroke-before-fill and text-path strokes remain pending required
  combinations; uniformly mapped direct solid strokes are already qualified.
- Keep queued component flags and the retained metric-dependent composite parent
  horizontal-hint discrepancy visible. Admitted USE_MY_METRICS, nested accents,
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

1. All required remaining items and every finite G2–G9 mechanism above have their exact acceptance
   witnesses and explicit unsupported outcomes. No unapproved scope deferral or
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
   Hosted Windows/Linux CI is green for the substantive delivery revision;
   inspect once after completion and reuse that result, avoiding repeated polling.
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
  The latest finished suite measured about **128s Windows / 160s Linux**;
  the preceding measured Linux build was 5m06s. See the current qualification
  receipt and build log for this delivery; builds are included in the routine target. These are measurements,
  not a fixed timing guarantee. Record one existing before/after timing when
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
S01's completed tranche and S02/S04–S12 remain closed. S07–S09 shipped at
`57d1c44b`; S10–S12 at `c15167af`, with green hosted Windows/Linux evidence.

| Review ID | Bounded change | Acceptance / measurement |
| --- | --- | --- |
| S01 | **First tranche complete.** Separate visual cases from output-API permutations in existing visual tests. | Preserve every distinct input, viewport, scale, background and platform reference; compare pixels through a canonical API. Retain a small representative raw/prepared six-sink contract matrix, complex and supplied-font scenes, and independently useful API/feature regressions. Measure the complete routine suite once before/after. |
| S02 | **Complete.** Transform decoded glyph segments directly into final contour arrays; remove the temporary transformed array and copy. | Existing glyph-outline tests and TransformAndCopyContours benchmark: identical multi-contour/mapped output, lower allocated bytes, preserved validation, cancellation and failure cleanup. The segment probe observes actual transformation. |
| S03 | **Deferred after measurement; prototype removed.** Reuse font-mask preblend tables only when representative profiling justifies it. | The retained public Noto Sans label benchmark measures complete conversion with acquisition outside timing. Revisit after S07/native text profiling identifies table construction as material; then preserve byte-identical tables/references, arithmetic, bounded storage and real work. No cache is a release prerequisite. |
| S04 | **Complete.** Prepare immutable gradient segment coefficients once per effective replay. | Arbitrary/repeated-stop, alpha, spread, scale, linear/radial references remain exact. The retained rainbow checker improves in the measured end-to-end comparison; binary search, endpoints, float/FMA/division order and dithering are preserved. |
| S05 | **Complete.** Reuse immutable definition/attribute strings and aggregate reservations with common ownership. | Definition/use-heavy and attribute-heavy parser allocations decrease; forward references, style replay, material denial and cleanup are preserved. Equal Name/LocalName and already-owned values share storage; span copies remain, without global interning or a new arena framework. |
| S06 | **Complete.** Read CSS element identity once per cascade resolution when rules exist. | Existing stylesheet/combinator and frozen visual checks pass on both platforms. Styled parsing/conversion measurements show a small timing change within overlapping uncertainty ranges and unchanged allocation. Selector order, specificity, work/cancellation checks and the cheap no-rules route remain intact; no selector index/cache. |
| S07 | **Complete.** Prepare Windows dropout bounds and immutable sampling maps once per glyph; reuse horizontal directed crossing sets within a row. | Exact native thin-stroke, vertex, dropout, curve, mapped/composite and supplied-font references remain unchanged. Complete checks and existing memory probes pass on both platforms. Two public-label comparisons show a 30–34% local time reduction for about 16 KiB more allocation. Optional storage yields under memory pressure; vertical gathering remains per pixel. Vertical scan reuse is a conditional future investigation, not this item's exit requirement. |
| S08 | **Complete.** Prepare the fallback glyph's identical eight quadratic subdivision endpoints once per glyph. | Verified Windows fallback conversion is 29–35% faster in two local comparisons for about 0.54 KiB more allocation. Sampling, winding, numeric rejection, cancellation and cleanup remain protected; optional preparation yields at the original tight work boundary and under memory pressure. Empty drawable regions do not prepare. Native-fitting throughput is outside this claim. |
| S09 | **Complete bounded tranche.** Reuse one tile across consecutive compatible rectangle fills, including lowered stroke bands and repeated instances. | Content identity, source/mapped boxes, dimensions, viewBox, native maps and paint must match. Lookahead avoids cold retention; scope changes and other draws release the owner. Measured stroke/repeated-fill allocation falls 13%/37%, with local timing benefits; different-tile uncertainty is reported below. Fractional sampling, phase, alpha and exact references stay unchanged. Wider polygon/cross-boundary reuse remains conditional, beyond this within-operation exit. |
| S10 | **Complete.** Avoid unarmed AsyncLocal lookup/write in glyph coverage, polygon fan and glyph-outline probes. | AsyncLocal context notifications synchronize a thread-local callback cache; inherited children, parallel scopes, awaits and suppressed flow retain isolation. Unarmed entry microbenchmarks improve without allocation; full checks pass on both platforms. Scope disposal is idempotent. No renderer arithmetic or Release-only behavior changes. |
| S11 | **Complete.** Share axis-aligned gradient/radial/pattern line-stroke admission and effective parsed values in the existing stroke parsing layer. | Existing cross-paint line/stroke cases and complete platform checks pass. Zero width returns first; authored validation, radial dispatch, effective dash/cap/pathLength order and paint-specific gates are preserved. Width is parsed once per admission. No generic renderer abstraction or throughput claim. |
| S12 | **Complete.** Remove four unreferenced internal helpers after rechecking current tracked C# sources. | AppendStrippedContour, SvgRootLength.ToAbsoluteOrNull, SvgStyleSheet.GetIdentifierAttribute and SvgRoot.BeginOpacityScope had no callers. Release build and 1,040 affected existing tests pass; no absence tests or throughput claim. |
| S13 | Conditional: broaden the one-glyph raw decode cache only if a realistic nonconsecutive label still benefits after S02. | Measure lookup/retained-memory cost first. If justified, use a small byte-bounded per-run/conversion cache keyed by prepared font and glyph index; preserve leases, immutable decoding and per-position contours. Otherwise leave deferred. |
| S14 | Conditional: reuse immutable validated TrueType table/profile preparation; investigate reusable VM initialization separately. | Profile an existing supplied-font label, beyond the synthetic triangle-font benchmark. Preserve lazy validation, target/size/transform distinctions, mutable CVT/storage/graphics state and composite semantics. Do not share a live mutable VM across glyphs; defer unless a bounded change proves useful. |


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

**Optional/deferred maintenance:** S03 preblend-cache prototype was removed after
public-label comparisons **5.49 → 6.43 ms** and **6.59 → 6.86 ms**, allocation
**142.15 → 142.51 KiB**, without established benefit. S13 wider glyph cache,
S14 table/profile reuse, vertical scan reuse and wider polygon/cross-boundary tile
reuse require representative profiling before reconsideration. Speculative
optimization, pooling/global caches and new test/evidence infrastructure are
deferred from release. Existing source/PNG/font lease and prepared-owner
protections are not redesign targets merely because a copy is visible.

**Scope-deferral decisions:** the maintenance deferrals above are explicit and
narrow no G2–G9 promise. There are **no newly approved feature-scope deferrals**
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
| Shared per-file sources coalesce concurrent downloads, cache successful bytes and use at most three attempts for transient transport failures. Manifest reads cap at 1 MiB; font reads cap at the smaller of the declared size and 64 MiB. | Put transport, bounded caching and retry in an opt-in test host/provider. Test concurrent reuse, cancellation and recovery with synthetic transport responses. Keep Svg's existing 32 MiB aggregate borrowed-program and 256-face caps explicit; the source's per-file cap does not replace them. |
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

Historical assessment (2026-09-20; not repeated in this review): read the
configured source without printing its location; streamed the unchanged manifest
under 1 MiB and checked the requested
identifier; fetched one selected regular face-0 program under its declared size
and Svg's aggregate limit; verified exact length and SHA-256. It has the standalone
TrueType signature and `head/maxp/hhea/hmtx/cmap/loca/glyf` table entries, plus
`kern` and `GPOS`, and no `fvar` entry. These are initial format observations, not
table validation, shaping support or a whole-pack inventory. All downloaded bytes
stayed in memory. No Svg catalog/snapshot conversion, OoxPdf test run, browser
capture or pixel comparison was performed in that assessment. No new private source access was needed for the 2026-09-21
review.

Execute S7 through the following concrete steps:

1. Retain the implemented opt-in generic local test host accepting a supplied manifest/byte source
   or externally configured endpoint and pack identifier, with no real defaults
   or platform configuration discovery in tracked code. Reuse unchanged assets;
   retain private hashes/provenance locally. Share selection and source behavior
   with the public OoxPdf contract without importing platform implementation.
2. Parse the actual manifest with Svg, prepare only requested programs, and build
   a private inventory of formats, faces/styles, coverage, composite flags and
   positioning tables. Compare catalog selection with OoxPdf for representative
   requests. Report unsupported semantics explicitly; do not rewrite the pack or
   silently substitute a machine font. Turn reproducible defects into synthetic
   tracked tests and fix them under S3–S6 before admitting wider text behavior.
3. Generate small synthetic label inputs covering curves, counters, composites,
   AV pairs, spaces, real styles and fractional origins/sizes. Feed identical
   verified font bytes to Svg and an SVG-image-local browser font embedding.
   Use missing/corrupt/substitution
   controls to prove intended-font use. Retain three fresh-process captures,
   independent layout metrics and exact RGBA comparisons in private artifacts.
4. Record sanitized failure categories, feature gaps and measured outcomes here.
   A private mismatch must remain visible even if public tests pass. Private
   success may support local investigation, but public corpus/package admission
   needs independently redistributable fixtures and reproducible public evidence.

The requirements below mix implemented foundations and remaining targets. A
single-use retained prepared document exists (R21's lifetime defect is repaired);
general family/coverage fallback and shaping are not thereby implemented.

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

Horizontal LTR Latin labels, initially ASCII/tested precomposed Latin, using supplied
static standalone TrueType face 0. Validate SFNT/ranges, `head`, `maxp`,
`hhea`/`hmtx`, `cmap` 4/12, `loca`/`glyf`, plus optional `name`/`OS/2` metadata
structures.
Include simple curves, implied points and bounded composite placement; gate
unsupported formats/flags.

Preserve text/CDATA/span order and admitted whitespace across boundaries. Support
inherited font/paint/visibility, scalar x/y/dx/dy, alphabetic baseline,
start/middle/end anchors, advances, bounded default Latin standard ligatures and
required pair positioning (`kern` and explicit bounded GPOS pairs). Test AV,
spaces/punctuation, accents/composites,
supplementary scalars, mixed faces/sizes/colors and fallback boundaries.
Implement or reject features affecting output. Add styled/positioned `tspan`, spacing and
explicit line positioning for labels.

Position/rotate lists, `textLength`/`lengthAdjust`, the enumerated extra baselines,
remaining affine/paint text combinations, decorations and
closed/transformed text-path combinations remain G9 work. Direct uniformly mapped
solid fill/stroke labels and retained curved paths are qualified; do not reopen
them. The finite G9 exit above identifies the remaining behavior.
The original positive horizontal local text path is complete. The remaining items'
initial exclusion does not remove them from the final queue. Automatic wrapping,
bidi/joining/complex scripts, unadmitted mark positioning, general GSUB/GPOS,
vertical text, variable/color/bitmap fonts, CFF/CFF2 and WOFF/WOFF2 remain outside
that intended text subset.

### Host/oracle/corpus acceptance

Demonstrate a standalone host with async bounded nonseekable stream intake,
preserved XML/BOM bytes and growth-time input limits; a public stream-input overload
is not initially required. Host buffers/shared caches are separate budgets. Bound
concurrent loads, isolate caller cancellation and allow retry. Output awaits
backpressure, propagates failures and leaves streams open; hosts own partial-output
cleanup/transactional publication. Include real labels, not only transport tests.

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
oracle results are not canonical Linux evidence. The historical prompts of 200 targeted cases and
10–20 diagrams are optional coverage guidance, not acquisition tasks or release
quotas. Add cases only for identified gaps under the validation policy above.

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
