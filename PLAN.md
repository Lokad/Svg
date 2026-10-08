# Lokad.Svg — renderer fidelity and remaining SVG support

Updated 2026-10-09. **27/27 corporate documents and 258/258 variants remain exact
on Windows and Linux**, against each platform's frozen browser references.
Marks are 13/13; outlined wordmarks are 14/14. Membership, input bytes, options
and references stay frozen. This complete matrix remains the regression gate.

**All five retained online gap documents are complete**, through all six outputs
on both platforms, against their unchanged declared transparent references:

| Complete retained document | Declared output | Shared correction |
| --- | --- | --- |
| Illustrator heart (`inv-105`) | 512×401 | Background declaration validation and transformed rectangle source replay. |
| I Ching library (`inv-106`) | 280×720; all 79 symbols/uses | Complete path-prefix recovery, affine viewport clips and retained native contours. |
| Pattern example (`inv-103`) | 200×200 | Bounded ordered vector tile scenes and native premultiplied paint. |
| Inkscape PatternA (`inv-104`) | 178×135 | Local forward template inheritance and fractional source-grid/texture mapping. |
| SVG mask.svg (`inv-107`) | 744×1052 | Root-local definitions, shared bounded shader-mask replay, viewport region and clipped-layer paint coordinates. |

Each document has a stable, healthy, cleaned Linux browser capture triple matching
its frozen reference byte-for-byte. The [gap manifest](tests/References/inventory/svg-gap-cases.json)
retains sources, license evidence, outputs and platform provenance. **Documents
completed: 5/5 in that manifest**, separate from the preserved corporate
**27/27 / 258/258**. The existing radiation symbol (`inv-094`) now supplies a sixth
active deliverable: six of its nine previously declared outputs are exact, with
three 1200×1200 outputs still differing by 24 RGB-only pixels each. The
[radiation bank](tests/Research/radiation-symbol/manifest.json) retains every original
input and frozen reference. **Active document queue: 5/6 complete**; the pending
radiation symbol is not counted as complete.
These failure-selected examples establish no representative success rate.

The new [pattern inheritance bank](tests/Research/pattern-inheritance/manifest.json)
has **17 controls: 15 exact and two explicit unsupported boundaries** through six
outputs. The earlier vector-content bank retains 17 exact and one rejection.
Controls diagnose and protect mechanisms; they do not count as completed documents.

The frozen 64×64 `iching-library/nested-rotated-opacity` control is now exact.
Instance opacity surrounds the instantiated affine viewport clip, matching native
operation order; seven independent [focused twins](tests/Research/nested-affine-opacity/manifest.json)
are exact through six outputs. The I Ching bank now has 23 exact and two explicit
rejections. This closes the measured defect, without claiming the general G5 exit.

The new [gradient-mask bank](tests/Research/gradient-masks/manifest.json) has
**20 controls: 16 exact and four explicit unsupported boundaries** through six
outputs. Shared scene replay retains native curves, paint and opacity; luminance
uses premultiplied color and mask shaders retain the clipped layer's bitmap origin.
These controls and their resource sweeps protect the completed mask document.

**Next complete document: `inv-094`, radiation warning symbol.** Finish its frozen
300/600/1200 × transparent/white/black outputs: **6/9 exact**, with the remaining
24-pixel large-size defect inside G3/G5. Diagnose that native contour/coordinate
map before changing another admission gate; add a focused twin for the shared
correction. This dependency does not require exhaustive G2 completion. The
retained tiger's general affine map remains a following G3 candidate.

The original admitted fixture/research fidelity repairs are complete. The remaining
queue covers static, self-contained SVG within the original product scope:
existing rejected examples, newly discovered rendering defects and the finite
G2–G9 support exits below. Completing the five gap documents does not close these
groups or authorize dropping their promised combinations. More verified
public-domain/CC0 documents should supply examples where the remaining queue
lacks a complete export. S17 remote CI remains a separate release requirement.

This plan owns priorities and finite exit criteria. [SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md)
owns admitted subsets and observed gates; [INVENTORY](docs/INVENTORY.md) owns
document provenance; [RASTERIZATION_DESIGN](docs/RASTERIZATION_DESIGN.md) retains
mechanisms, controls and rejected models; [BENCHMARKS](docs/BENCHMARKS.md) owns
timing evidence. Earlier checkpoints remain in those records and Git. R01–R41
and S1–S17 retain their identities. The September 28 review is historical evidence
in [the review](docs/REVIEW_2026_09_28.md).

## 1. Validated checkpoint and limits

| Validated checkpoint evidence | Assessment |
| --- | --- |
| Windows/Linux Release solution builds | Nine projects each, zero warnings/errors. |
| Full tests, including corporate selection/measurement | **5,515 passed on each platform**: 4,138 core, 1,371 visual-project, six FontProbe; zero failed/skipped. Test counts are not image counts. |
| Memory and delivery checks | Existing parser/raster work, memory-denial, cancellation/retry, geometry/paint and six-output regressions pass. Six new template/grid inputs sweep every parser/discovery work denial, sampled/peak memory denial and raster work boundaries. A 1,024-link forward chain resolves iteratively; aliases borrow one promoted tile scene with independent grids. Missing/cyclic/duplicate/negative templates and oversized tiles release owners; full-document early/middle/late work and peak-memory denials release partial owners before retry. Five mask inputs also sweep parser/discovery work, sampled/peak memory and raster work boundaries; anonymous, unused, duplicate and rejected masks release staging owners. Borrowed replay survives cancellation/retry, and the complete mask document releases early/middle/late work and peak-memory denials. Nested affine-opacity also sweeps parser/discovery work, sampled/peak memory denial, raster work boundaries and cancellation/retry; clip and opacity retain distinct bounded owners. Prior mechanism details retain their dated scope in the design record. Remote CI remains open. |
| Executed fixture manifest | **1,410 entries = 1,391 exact + zero known-difference renders + 19 rejections**; 799 base documents / 611 variants. The original rotated mark graduates against a new independent browser reference; its source/options and all preceding exact assets remain unchanged. |
| Executed research manifest | **65 probes = 64 exact + zero known-difference renders + one rejection**. Three multiple-child symbol cases graduate from the erroneous MalformedInput rejection; other research outcomes and PNGs stay unchanged. |
| Supplemental recorded-paint inventory | **149 controls = 132 exact + 17 valid-input rejections**, checked through all six outputs with unchanged measured outcomes. These mechanism controls remain separate from the original bank, new development sources and frozen corporate cohort. |
| Legacy independent-file Default sweep | **92 files = 92 render**. 91 Default reports/PNGs stay unchanged; `inv-094` improves from 1,081 differing pixels to exact at its unchanged 600×600 output. The preceding STOP-sign and `inv-013` exact results / `inv-014` residual remain preserved. These Default outputs do not establish corporate fidelity or a representative rate. |
| Corporate cohort freeze | **27 documents / 258 variants**, 13 marks / 14 wordmarks. Membership, sources, root-only materializations, options and references stay unchanged; every variant has a stable reference. |
| Corporate measurement | **258 rendered comparisons = 258 exact on each platform**, zero known differences/rejections. Every corporate PNG and complete comparison remains unchanged under the shared repairs. **27/27 documents**, marks **13/13**, wordmarks **14/14**. Full fidelity target achieved; S17 remote CI remains open. |
| Performance and platform limits | Symbol accumulation, fill bindings and retained instance arrays charge bounded parser/scene storage and copy work; viewport restore charges clip setup work. Source stroke identity uses existing recorded-paint padding (68 bytes); draw commands stay 72 bytes and geometry 272 bytes. Binding arrays reserve their actual element size. Rectangle recognition owns no storage; closed rectangle outlines use bounded stack scratch and the existing budgeted output list, without generic stroker scratch. Frame coverage owns no storage; row and pixel/operation work is charged before painting, and empty interiors are skipped. Affine viewport clips register budget-owned native contours and charge a full-canvas RGBA scratch tape (four bytes per output pixel per active affine clip), plus bounded intersection/replay work. Isolated opacity retains separate existing surfaces; no quantitative performance claim. Earlier [cost records](docs/BENCHMARKS.md) keep their dated scope. Remote CI, broader workloads and private-font metrics/pixels remain unproved. |

Both platforms pass Release builds and 5,515 tests. The original bank retains
1,567 reports and 1,547 PNGs: 1,546 PNGs and every outcome stay unchanged;
the only changed image is the newly exact 600×600 radiation symbol. Its large
outputs remain measured defects, protected against their unchanged references.
Windows/Linux banks agree. Every corporate result stays unchanged and exact
against its platform's own references. All five retained gap documents are exact;
the next radiation deliverable is 6/9 exact. G2–G9 and S17 remote CI stay open.

## 2. Execution decisions

1. Inspect HEAD and working changes before editing. Preserve concurrent work;
   use one owner across geometry, reference resolution and shared styles.
2. Implement shared SVG semantics. No document IDs, source coordinates or observed
   failure pixels may become implementation predicates. The eight corporate
   holdouts remain evaluation inputs.
3. Preserve directed source commands, retained curves, final device mapping,
   analytic winding, direct paint versus accumulated runs, byte clip coverage and
   bounded layer ownership. Their existing exact controls are regression gates.
   Retain rejected models in the design record; do not repeat them without new evidence.
4. Diagnose the first observed blocker before removing a gate. Admission is not
   fidelity: a newly accepted document may expose another rejection or wrong pixels.
   Keep its original source and reference until every declared variant is exact.
5. Make small, buildable commits with affected tests and documentation. Scale
   resource/platform checks to the changed contracts under section 5. Ordinary
   refactoring needs no metadata refresh or repeated checkpoint write-up.
6. Preserve the stronger achieved 27/27 and 258/258 matrix. S17's original minimum
   remains 80% exact documents overall and within each frozen corporate stratum;
   it does not authorize losing current exact results. See [S17_DECISIONS](docs/S17_DECISIONS.md).

## 3. Ordered support queue

The audit uses executed fixture/research/recorded-paint failures and actual parser
gates. Group numbers identify support responsibilities, not sequential prerequisites.
Select the next shared implementation change by the retained document it
advances. The five online gap documents and the nested affine-opacity defect
are complete; finish every declared radiation-symbol output next. Exhaustive G2 completion
does not block G3–G6. Keep other promised combinations visible in their group exits.
Prioritize composed transforms, paint-coordinate mapping and bounded pattern
content where they remove several gates. Extend the existing geometry and
immutable source replay; preserve direct-shape, native contour, shader and layer
distinctions needed for exact pixels. Separate implementations for each
shape/paint/instance combination are not the plan. Regression repairs may
interrupt document work. The identified fidelity repairs under historical rows
1–7 are complete; remaining feature/admission work now maps to G1–G9. Their
landed mechanisms remain regression requirements.

| Priority | Work | Failure class | Dependencies / stages |
| --- | --- | --- | --- |
| G1 | **Declaration validation landed.** Inert exporter presentation values share one validator. | Original syntax-location inconsistency resolved; the heart is now complete | Remaining nested style/root selector gates stay G5/G7; S9 |
| G2 | **Solid/shader use and container paint, stroke inheritance and rendered-group styling implemented.** Complete remaining practical symbol/instance combinations. | User-space percentage bases repaired; scaled nested viewport uses, other locations and affine combinations remain open | Definition ownership, expansion and bounded reference graph; S9/S10 |
| G3 | **Heart and I Ching documents complete.** Complete remaining curved geometry and static maps | Non-uniform curve maps, transformed strokes/paints and remaining affine gates | Source-curve replay and paint-coordinate maps; S10/S11 |
| G4 | **Fractional rectangle-stroke fidelity repaired.** Complete paint-server strokes on ordinary geometry | Three retained rectangle comparisons and twelve new frame controls are exact; broader paints retain admission gates | G3 where transformed; shared source stroke construction and paint bounds; S12/S13 |
| G5 | **Fractional viewport clipping and native symbol stroke fidelity repaired.** Complete nested viewport paints, general clips and masks | Retained native-stroke comparisons are exact; clip-crossing and further reference/coordinate/composition gates remain | G2/G3, bounded layers and reference scopes; S9/S14 |
| G6 | **Both retained patterns complete.** Finish the promised content, unit and affine combinations | Remaining static paint combinations | G2/G3/G5, bounded tile rendering; S13/S16 |
| G7 | Embedded PNGs and the enumerated static CSS subset below | Missing image/cascade combinations; retain an acceptance export before implementation | Explicit data only, bounded decode/resampling and shared cascade; S9/S16 |
| G8 | Static markers and the enumerated filter/blending subset below | Missing static features; lower priority than outlined logos | G3–G5, bounded intermediate surfaces; S16 |
| G9 | Supplied-font labels and the enumerated text-path subset below | Separate experimental feature work; no implicit host font discovery | Section 6, caller-owned snapshots and shared glyph contours; S7/S15 |

### Document deliverables and accounting

The completed-document ledger above records every frozen declared output; the
[gap manifest](tests/References/inventory/svg-gap-cases.json) retains inputs,
options, references and platform provenance.
Count a document only after every declared output is exact. Record completed
documents and outstanding blockers beside control results, separately by
platform where oracle coverage differs. Do not add backgrounds or sizes to an
existing document's denominator to make a partial success look complete.
Further variants may be declared as separate development cases before comparison.
Use the same retained reference throughout the correction; platform fidelity
requires a matching pinned browser reference under section 5.

The heart and complete 79-instance I Ching library are exact; neither closes
G2/G3/G5. The library required valid path-prefix recovery, composed viewport
clips, inert descriptions and preservation of native contour identity through
source replay. Its compound Yin/Yang control was already exact before the repair.
Keep the rotated-symbol gradient rejection (G3) visible; the nested-affine-opacity
defect (G5) is repaired on its unchanged reference. Both complete patterns are exact after shared vector content,
local template inheritance and fractional mapping. These completed documents
close neither the remaining G6 checklist nor any other finite group exit.
Acquire more public-domain/CC0 failures when these documents no longer supply
the next useful in-scope gap; keep already exact downloads out of the failure queue.

### G1 — exporter declarations: shared validation landed

Attributes and admitted inline/stylesheet declarations now share allocation-free
validation of `new`, `accumulate` and `inherit`. `new` accepts up to four finite
rectangle numbers with nonnegative dimensions; negative origins, scientific
notation and zero dimensions are admitted. Numeric partial rectangles disable
backdrop access under SVG 1.1 and are admitted too. Malformed values cannot hide behind
duplicate declarations or `!important`; active filters still reject. Root, group,
leaf, use, viewport-attribute and inherited locations have six-output controls;
font discovery agrees and tight denial/cancellation releases owners.

`inv-105` no longer rejects its Illustrator root CSS declaration. Its subsequent
G3 rectangle gate is repaired too; the full declared document now matches the
unchanged reference. Nested SVG `style` attributes and root-targeting stylesheet
selectors retain separate admission gates under **G5/G7**. The G1 validator alone
does not claim those wider CSS combinations.
See [the repair](docs/RASTERIZATION_DESIGN.md#shared-exporter-background-validation-2026-10-08).

### G2 — symbols and instances

One owned definition now accumulates ordered direct symbol leaves. The retained
`research-mask-leaf-multi-leaf-symbol` and `original-number-transform-instance`
families are exact on transparent/white/black backgrounds. Real duplicate IDs
remain malformed. Child-ID references borrow geometry owners without acquiring
the enclosing symbol's viewBox or opacity. Authored child/symbol fills survive
use overrides; unstyled fill channels inherit from use and its ancestors. Child,
symbol and use opacity retain separate composition scopes. Empty symbols render
nothing. Native-backed curves replay original commands under admitted uniform
magnification; flattened content without source commands retains its gate.

Sixteen independent controls cover order, mixed leaves, forward references,
copies, fill inheritance/overrides, symbol styles, opacity, aliases, viewport
defaults and magnified curves. All are browser-exact through six outputs; all
six original comparisons are exact without reference replacement. Four new
exhaustive work/memory sweeps, duplicate-ID cleanup and during-assembly
cancellation/retry protect ownership and bounded expansion.

The location repair admits direct root/rendered-group symbols through the same
non-rendering definition route as `defs`. Definition ancestors do not transfer
their transform, opacity, fill or visibility into clones. IDs are optional;
anonymous definitions retain child-ID owners through scene construction. Title,
description and metadata subtrees do not draw or register geometry; active
content remains rejected and structural/node/work limits still apply. Font
discovery shares symbol location and late-stylesheet rules, ignores symbol text
and retains later direct-text requests. Eleven new controls are browser-exact;
the three unchanged nested group/backward-use/forward-use controls now render
exactly through the retained-content route.

Solid fills now retain authored paint separately from inherited color and fill
opacity. Each clone resolves these properties from its use context while keeping
explicit leaf/symbol values and the leaf's own display/visibility. The same
resolved paint controls geometry and recorded alpha. Sixteen further controls
are exact through six outputs; four new exhaustive work/memory inputs exercise
newly recolored native owners and partial cleanup.

Nested groups and local backward/forward uses retain typed ordered source nodes,
local transforms, independently inherited solid fill/color/opacity and container
display/visibility. Group, leaf and use ID aliases borrow their own source range;
referenced ancestors do not enter the clone. Iterative validation rejects circular
dependencies as MalformedInput, including unused/anonymous definitions, while
missing targets remain UnsupportedFeature. Repeated acyclic dependencies preserve
paint order. Expansion and sealing observe memory/work denial and cancellation.
All sixteen retained-content controls and three diagnostic twins are exact
after the shared viewport-opacity repair; eighteen additional controls are exact.

Use-level color and fill opacity now follow presentation, inline and admitted
stylesheet precedence. Nested use ranges retain their own authored properties
instead of replacing them with parent values. Source solid fills retain
precedence, including ordinary definitions and filled/stroked pairs; line strokes
ignore use fill. Source currentColor strokes resolve independently of fill opacity.
Eighteen controls repair preceding valid-input rejections; the existing
single-rectangle symbol shader control and the native inline twin stay exact.

**G2 remains open.** The three native stroke differences are now exact.
Numeric stroke opacity now inherits independently through ordinary/forward
definitions, symbols, nested groups/uses and child/group aliases. Authored source
opacity wins, including explicit one; source stroke color/rgba alpha and fill
opacity remain independent. Existing shader stroke channels resolve the inherited
opacity with the browser's byte boundary. Unstyled closed paths with `fill="none"`
retain authored centerlines, so adding a use stroke now paints straight and curved
sources correctly. The [81-case bank](tests/Research/use-stroke-opacity/manifest.json)
is now 70 exact and 11 rejected through six outputs. Nine fractional diagonal-line
cases remain G3/G4 and two late stylesheet cases remain G7. The three shader
rectangle definitions and rendered-group style cases now match their unchanged
references. The additional supported twins are separate inputs; these rejected originals stay frozen.

Stroke paint, width, caps, joins, miter limits and admitted dashes now inherit
through the same contexts. Source values win independently, including explicit
`none` and width zero. Definition leaves retain immutable attributes and cascaded
authored style; changed construction reuses the ordinary shape lowerers. Source
CSS precedence survives replay. A borrowed source sidecar preserves child styles
for previously flattened symbols. Aliases exclude the referenced ancestors.
The [99-case style bank](tests/Research/use-stroke-style/manifest.json) is 96 exact
and three rejected translucent shader-circle cases (G4). The preceding 81-case
opacity bank is now 70 exact and 11 rejected, while the old inherited-symbol-stroke
case stays exact against unchanged bytes. Plain rectangle non-miter joins and
existing shape-specific stroke gates remain explicit.

Identifier-bearing rendered groups now retain typed source nodes rather than
recovering geometry from baked scene snapshots. Styles, fill rules and local
transforms resolve through the same iterative instance contexts and ordinary
shape lowerers; source ancestors stay outside aliases. Leaf/use mask batches
reuse the shared mask binding route. The [123-case group bank](tests/Research/rendered-group-styles/manifest.json)
has 114 exact and nine rejected comparisons. Three opacity and three clip boundaries
remain G5; three original transformed inline rectangles remain G3. The three
inherited shader-opacity controls now match their unchanged references. Nine
thin-oval controls repair incorrect browser pixels through shared native hairline construction. Eleven
new resource inputs cover replay, nested masks, empty forward targets and cleanup.

Use fills now carry budget-owned local paint URLs through the same source replay
and iterative contexts. Source-authored solid, none and shader fills win; numeric
fill opacity resolves independently at the shader's byte boundary. Native paths,
circles and rounded rectangles reuse ordinary lowerers. Shader rectangle instances
preserve solid borders, including independently resolved currentColor. The
[150-case bank](tests/Research/instance-shader-opacity/manifest.json) is entirely
exact through six outputs on both platforms. Its 450 browser captures are healthy,
cleaned and byte-stable; eight older failures graduate without asset changes.
Twelve additional resource inputs protect construction, tile mapping and filtering.

Root/group/link/symbol shader fills now inherit through independently retained
container paint. The [192-case bank](tests/Research/container-shader-inheritance/manifest.json)
is exact through six outputs on both platforms; all 576 browser captures are
healthy, cleaned and byte-stable. The baseline rejected 183, rendered four
incorrectly and matched five. Fractional point-list shader fills reuse native
source contours; direct pattern rectangles carry container placement, and tile
fill alpha survives storage and replay. Fifteen resource inputs and two retained
placement gates protect discovery, lowering, mapping and retry.

The [81-case percentage-base bank](tests/Research/viewport-percentage-bases/manifest.json)
has 78 exact controls and three retained scaled-viewport use rejections through six
outputs on both platforms. Root and nested viewBox dimensions supply user-space
percentage bases to local uses, nested viewports and admitted images. Percentage
roots use their explicit containing viewport or existing intrinsic defaults when
there is no viewBox. Sibling scopes restore the previous base. Discovery retains
later font requests after known viewBox-based nested percentages. The old
dimensionless-root/omitted-nested-size rejection now has a positive regression
with unchanged geometry and three independent browser controls. All 243 retained
full-image captures are healthy, cleaned and byte-stable. Nine cropped captures
remain diagnostic oracle failures; they are excluded from references. Eight
resource inputs and four focused checks preserve the existing resource contracts.

Remaining G2 completion checklist, independent of the next document:

- [ ] Admit local uses in scaled nested viewports; the three retained percentage
  controls establish the gate. Preserve viewBox bases, clipping and source replay.
- [ ] Share references and authored/inherited styles across root, `defs`,
  rendered groups, symbols and nested SVG locations, including ordinary leaves,
  group/symbol/use targets, backward/forward references and child aliases.
- [ ] Compose static instance maps through G3 and viewport/opacity/clip/mask
  boundaries through G5; preserve source precedence and ancestor exclusion.

The completed heart needed no `use` dependency. The complete I Ching library
needed composed affine symbol viewport clips and source-contour replay across
its 79 instances; those dependencies are implemented. The next pattern example
does not require exhaustive G2 completion. For a document that needs new uses,
declare focused viewport/authored-override twins before production editing and
capture; keep mechanism denominators separate. G2 exits when this finite location,
reference and composition checklist is supported with exact retained examples,
categorized missing/cyclic-reference outcomes and unchanged resource contracts.
An untested combination remains open, even after a document completes.
See [the percentage-base repair](docs/RASTERIZATION_DESIGN.md#viewport-percentage-bases-2026-10-08).
See [the container-shader slice](docs/RASTERIZATION_DESIGN.md#container-shader-inheritance-and-tile-paint-2026-10-08).
See [the instance-shader slice](docs/RASTERIZATION_DESIGN.md#instance-shader-fills-opacity-and-pattern-placement-2026-10-08).
See [the rendered-group slice](docs/RASTERIZATION_DESIGN.md#rendered-group-styles-and-native-source-replay-2026-10-08).
See [the stroke-style slice](docs/RASTERIZATION_DESIGN.md#instance-stroke-paint-width-and-mechanics-2026-10-08).
See [the stroke-opacity slice](docs/RASTERIZATION_DESIGN.md#stroke-opacity-at-instance-time-2026-10-08).
See [the repair](docs/RASTERIZATION_DESIGN.md#ordered-symbol-children-and-native-instance-replay-2026-10-08).
The [location/ownership follow-up](docs/RASTERIZATION_DESIGN.md#non-rendering-symbol-locations-anonymous-owners-and-descriptions-2026-10-08) records the current repair.
The [solid-fill presentation repair](docs/RASTERIZATION_DESIGN.md#symbol-solid-fill-presentation-at-instance-time-2026-10-08) records the new fidelity fixes.
The [retained-content slice](docs/RASTERIZATION_DESIGN.md#retained-symbol-groups-and-local-reference-graphs-2026-10-08) records the expansion and the subsequently repaired clipping residual.

### G3 — curved geometry and transforms

The complete CC0 `inv-106` I Ching library now matches its stable 280×720
reference. Diagnosis corrected the earlier attribution: its Yin/Yang compound
curves were already exact; `Hexagram_U4DCA`'s `z0` tail caused the first rejection.
Drawable paths now retain completed segments before malformed syntax under the
SVG path error-handling rule, without hiding full-input command/work limits or
cancellation. Source replay preserves native single/compound bar contours across
fractional and rotated placement. Affine viewport composition is shared with G5.
The new rotated-symbol gradient still hits its axis-aligned paint gate; keep
that finite follow-up open. See [the completed library](docs/RASTERIZATION_DESIGN.md#i-ching-library-path-prefixes-and-affine-symbol-clips-2026-10-08).

The heart and `original-logo-rotated-mark` now render exactly. Plain fill-only
rectangles under admitted rotations/reflections and composed non-uniform maps
share fractional straight-contour lowering, retaining native convex shape identity
and source float endpoints. Admitted rendered-group aliases, group clips and
independent fill/element opacity keep their existing shared routes. The fifteen
focused controls are nine exact and six explicit rejections: general matrix and
singular syntax, group targets inside defs, transformed leaf clips, shader fills
and active strokes. The prototype's leaf-clip mismatch remains diagnostic;
the original axis-aligned clip cannot represent transformed clip geometry.
Three resource sweeps, full-heart denial/retry and clip-gate cleanup pass.
See [the completed document](docs/RASTERIZATION_DESIGN.md#illustrator-heart-and-transformed-rectangle-fills-2026-10-08).

`original-number-general-map` and the retained tiger reject
general matrix transforms. `original-curve-root-quad-none` rejects non-uniform
viewBox/output curve mapping. Reflected pattern groups and magnified use add
shape/paint/instance gates. Complete these through one composed affine map,
preserving authored curves, stroke semantics and paint coordinates. The finite
geometry exit covers rect/rounded rect, circle/ellipse, line, polygon/polyline and
retained line/quadratic/cubic/arc paths; nonzero and even-odd compound fills;
translation, rotation, skew, reflection and non-uniform affine scaling at root,
group, leaf and admitted instance placement. Degenerate finite maps must have an
explicit nonpainting or categorized outcome, without invalid owners. Perspective
maps are outside this static SVG subset. The heart and I Ching documents are
complete; the retained general-map and non-uniform curve cases remain promised. Exit: these retained documents/families and focused map controls
are exact without normalized or flattened source substitution; no corporate or
prior exact regression.

### G4 — fractional stroke fidelity and paint-server combinations

The three `use-paint-properties/current-color-rectangle-stroke-*` comparisons
are exact on unchanged references after replacing four fractional bands with one
ordered frame. Their preceding 85/81/85-pixel residuals exposed different seam
and corner coverage. Twelve new controls become exact too, covering fractional
placement, scaled thin strokes, collapsed interiors and solid strokes over shader
fills on three backgrounds. Three clip-crossing controls retain their explicit
admission gate. The inline transformed rectangle twin rejects separately.
See [the repair](docs/RASTERIZATION_DESIGN.md#ordered-rectangle-stroke-frames-2026-10-08).

The nine `zero-area-stroke-*` linear/radial/pattern controls reject mixed closed
rectangle rings and an open diagonal as requiring closed axis-aligned integer
contours. The two `line-bounds-*-square-width6-fill-none` controls reject ordinary
gradient lines. Solid native outlines already render their admitted controls
exactly. Extend the shared stroke-to-paint route rather than adding paint-specific
geometry kernels. Include circle/ellipse dash paint, caps and non-uniform placement
as measured follow-ups. The finite stroke exit covers the G3 geometry subset
with solid/linear/radial/pattern paints, butt/round/square caps, miter/bevel/round
joins, miter limits, finite dash arrays/offsets and independent fill/stroke opacity.
Non-uniform maps preserve SVG stroke construction; no separate paint-specific
stroker. Exit: the retained mixed-subpath, gradient-line and shader-oval cases and
focused boundary twins are exact, with ownership/budget checks passing.

### G5 — viewports, clips, masks and composition

`symbol-content/group-alias-use` and its inline diagnostic now match unchanged
references. The shared repair defers inherited viewport coverage until layer
restoration and selects image-shader arithmetic when the clip crosses the bitmap.
Eighteen independent fractional/integral, nested-scope and translucent-content
controls are exact on three backgrounds. The earlier clip-placement-only trial
remains rejected evidence. See [the repair](docs/RASTERIZATION_DESIGN.md#isolated-opacity-under-symbol-viewport-clips-2026-10-08).
The native symbol stroke controls now match unchanged browser pixels after
recognizing closed rectangle paths before generic stroking. Their inline twin
remains exact; thirty new controls cover all joins, both directions, collapsed
interiors, collinear/zero lines and non-rectangle fallback cases. The browser's
AA clip/RLE dispatch stays intact. The discarded contained-path binary-clip
trial remains negative evidence. Broader local/nested viewport and use-opacity
combinations still need their own controls; this repair does not close G5.
See [the repair](docs/RASTERIZATION_DESIGN.md#native-closed-rectangle-path-strokes-2026-10-08).

Affine symbol viewport rectangles now register native convex clip contours,
reuse force-RLE coverage and apply byte clip coverage per paint operation.
Source contours survive fractional instance replay; hairline strokes borrow the
same clip tape. Ordinary and isolated-group controls are exact, preserving native
bitmap origin and image-shader restore arithmetic. The frozen
`iching-library/nested-rotated-opacity` is now exact: instance opacity surrounds
the viewport clip instead of combining both into one restoration. Seven focused
twins are exact; the I Ching bank is 23 exact plus two explicit rejections.
See [the correction](docs/RASTERIZATION_DESIGN.md#nested-affine-viewport-and-instance-opacity-order-2026-10-09).
General clip intersections and nested composition remain within the finite G5 exit.

`inv-107` is complete at 744×1052 against its unchanged Windows/Linux reference.
Root/defs local masks share bounded source lowering and the existing immutable
vector-scene owner used by patterns. One admitted linear/radial-painted leaf
replays into the mask surface; alpha masks read alpha, and luminance masks retain
premultiplied RGB conversion before outer opacity. The viewport supplies default
user-space region percentages; explicit regions clip the inner layer before
shader dithering. Copied legacy contours retain quadratic/cubic spans. Twenty
controls protect the measured subset (16 exact / four unsupported); transformed
spatial target leaves, objectBoundingBox content, recursive and multiple-child
contents remain explicit boundaries. They remain promised in the finite exit.
Default user-space regions currently require a known viewport; implicit root
viewport bases remain queued in that same exit.
The radiation symbol is now exact in six of nine declared outputs; finish the
large-size 24-pixel contour/map defect next. See [the mechanism record](docs/RASTERIZATION_DESIGN.md#shared-gradient-mask-replay-and-copied-contours-2026-10-09).

`original-aa-clip-admitted-shader` rejects a gradient defined inside a nested
viewport as undefined. The material-pair family rejects patterns in scaled
viewports. `mask-region-half-translated` rejects a transformed custom mask region;
nested SVG style attributes, retained percentage-mask and nested-opacity examples
need their own admission slices. Distinguish definition scope, object bounds, visual stroke bounds and
viewport percentage bases. The finite exit covers nested SVG/symbol viewBox
mapping and percentages; local paint definitions; local clipPath geometry using
G3 shapes in userSpace/objectBoundingBox coordinates; alpha and luminance masks
with both region/content unit systems; transformed finite regions; and nested
leaf/group/use opacity, clips and masks. Forward local references and source-order
binding must agree. Exit: the retained families and focused intersecting/nested
scope controls are exact with bounded temporary surfaces and token-preserving
cleanup. Discovered AA-mask/cache differences are defects inside this exit,
not grounds for rebasing references or declaring the group complete.

### G6 — patterns

`inv-103` is complete against its unchanged 200×200 reference on Windows/Linux.
`original-cpu-raster-cubic-pattern` also graduates against its original reference.
Bounded ordered direct vector leaves now lower through the existing source/scene
machinery, with solid/gradient paint, admitted strokes and independent opacity.
Tile surfaces remain premultiplied; native constant-paint rounding and opaque Src
coverage interpolation remain distinct from ordinary solid rendering. The
eighteen controls retain seventeen exact results and one recursive-paint rejection.
Source vector grids retain finite fractional origins/sizes and translate-only
pattern transforms. Positive-axis referencing/output maps rasterize a bounded
integral texture, then sample its original fractional period with native float
coordinates and ordered repeat filtering. Retained native curves admit the
non-uniform mapping required inside these tile surfaces; outer shape gates remain.

`inv-104` is complete against its unchanged 178×135 reference on Windows/Linux.
Local `href`/`xlink:href` templates resolve backward/forward chains iteratively;
explicit attributes and local content win independently. Aliases borrow tile
scenes and retain their own dimensions/phase. The seventeen controls retain
fifteen exact and two external-template/object-unit rejections. Missing/cyclic
local templates have an explicit UnsupportedFeature outcome; duplicates and
negative dimensions remain MalformedInput. Three fresh Linux browser captures
match the full original reference byte-for-byte.

Groups/local uses inside tile content, reflected/general maps, content/viewBox/
bounding-box units and nested viewport gates remain open. Broader source-grid,
instance and stroke combinations still need measured controls; completion of the
two documents closes neither this checklist nor the finite exit below.
Extend the bounded vector tile scene with composed pattern transforms,
viewBox/content units and objectBoundingBox units in measured
slices. The finite tile-content subset is ordered G3 vector leaves, groups and
local uses with solid/gradient paint and opacity, rendered through the existing
source/scene machinery. Include fractional finite tile origins/sizes, both pattern
unit systems, content units, viewBox/aspect mapping and composed affine transforms.
Recursive pattern/paint dependencies must have categorized rejection. Embedded
images and filtered/text tile content remain explicit dependencies on G7–G9,
not silently dropped promises or a prerequisite for the two retained pattern
documents. Exit: both originals and the retained multi-child case render exactly;
focused tile-border, phase, alpha, transformed fill/stroke and inheritance controls
pass without tile-size or repeat-count-dependent unbounded work.

### G7 — embedded PNGs and static CSS

These are planned boundaries, not claims of current admission. Before production
work, retain a licensed complete export for each new image/CSS mechanism and
declare its output/options; existing exact examples stay regression controls.

- **PNG content:** non-interlaced 8-bit RGB/RGBA, grayscale/grayscale-alpha and
  indexed palette PNGs, including supported tRNS transparency, supplied by base64
  data URI. Preserve chunk/CRC/decompression validation; add multicolor and
  translucent content. Map finite image positions/dimensions, viewBox percentage
  bases, all meet/slice alignments, `none`, fractional/general affine placement,
  clipping, masking and opacity through the shared mapping and sampling route.
- **CSS matching:** existing ID/class/attribute and selector-list forms plus SVG
  element selectors, element+ID/class, descendant and child combinators on the
  supported SVG tree, including root, nested SVG, definitions and instance replay.
  Apply static stylesheets in source order wherever admitted, including the two
  retained late-stylesheet failures. Preserve specificity, `!important`, inline
  precedence and inheritance for the presentation/font properties enumerated in
  G2–G6/G9; include SVG geometry width/height declarations already parsed today.
- **Acceptance:** retain the existing embedded-image and compound-selector
  examples; add a multicolor alpha-edge image under fractional meet/slice and an
  export whose root/nested cascade and late rules affect referenced content.
  Include an authored override and missing/corrupt-image boundary control.
- **Explicit unsupported outcomes:** interlaced/16-bit/color-managed PNGs,
  JPEG/GIF/WebP and external image fetching; imported stylesheets, media/supports
  rules, pseudo-classes/elements, sibling combinators, CSS layout, variables,
  animation and web-font discovery. These are outside this group's intended
  subset; no silent fallback. The data-only resource boundary remains section 5.

Exit: complete acceptance documents match unchanged references, current exact
results stay exact, and decode, style matching, resampling and cleanup are bounded.

### G8 — markers and finite static effects

- **Markers:** local marker-start/mid/end on G3 line/polyline/polygon/path
  geometry and their instances; userSpaceOnUse/strokeWidth units; refX/refY,
  finite marker sizes, viewBox/aspect mapping, fixed angle, `auto` and
  `auto-start-reverse`; ordered G3 vector/group/use content with ordinary paints.
  Define zero-length segments, closed paths and recursive references explicitly.
- **Effects:** `feGaussianBlur`, `feOffset`, `feFlood`, `feComposite` (`over`/`in`),
  `feMerge`, `feBlend` (`normal`/`multiply`/`screen`) and `feDropShadow`. Inputs are
  SourceGraphic, SourceAlpha and earlier local results; filter/primitive regions,
  both filter unit systems and sRGB/linearRGB arithmetic are part of acceptance.
  Allocate bounded intermediate regions and charge blur/composition work before
  execution. Reuse the existing layer and premultiplied-paint machinery.
- **Acceptance:** finish `original-logo-marker-mark` and
  `original-logo-filter-mark` against their frozen references. Add a complete
  licensed arrow diagram and shadowed export, with focused tangent/closed-path,
  stroke-width, clipped-halo, anisotropic-blur, alpha and blend witnesses.
- **Explicit unsupported outcomes:** backdrop inputs, external filter images,
  displacement, turbulence, lighting, morphology, convolution, arithmetic
  composite, other blend modes and CSS backdrop/filter syntax. Recursive effect
  references reject; forward/missing result names have categorized outcomes.

This list resolves the previous "selected filters" boundary; it preserves the
promised marker/drop-shadow work without claiming every SVG filter primitive.
Exit requires complete-document equality and bounded expansion/surfaces/work,
including cancellation and denied allocations, with every prior exact result kept.

### G9 — supplied-font labels and text paths

Section 6's first useful milestone remains the starting point, not the final G9
exit. Preserve caller-supplied standalone static TrueType face-0 programs,
catalog/style/coverage fallback and the existing bounded pair-positioning subset.

- **Labels:** horizontal LTR ASCII/tested precomposed Latin and covered
  supplementary scalars; text/CDATA/whitespace and styled/positioned nested tspan
  order; inherited/per-span family, size, weight/style, paint and visibility;
  scalar/list x/y/dx/dy/rotate, start/middle/end anchoring, letter/word spacing,
  explicit line positions and `textLength` with spacing/spacingAndGlyphs.
  Include alphabetic/middle/hanging baselines, affine glyph maps, ordinary fill/
  stroke paints and solid underline/overline/line-through decorations.
- **Text paths:** one local retained open/closed G3 path per run, supplied glyph
  advances/kerning, length/percentage startOffset, anchors and tangent placement;
  method `align`, spacing `exact`. Curved and transformed paths must use the
  shared geometry, not substituted outlines. Out-of-range placement and zero
  path length need explicit measured outcomes.
- **Acceptance:** finish `original-logo-live-text` and
  `original-logo-text-path` with an explicitly supplied, redistributable face
  matching the browser embedding. Retain a complete label diagram and curved
  label export. Add AV/space/accent/composite, mixed-span/fallback, list/anchor/
  length/baseline and path-offset witnesses. Section 6 requires intended-font
  proof, separate metrics and pixels, and matching platform references.
- **Explicit unsupported outcomes:** automatic wrapping, bidi/joining/complex
  shaping, general GSUB/GPOS or mark positioning, vertical text, text-path stretch/
  auto spacing, variable/color/bitmap fonts, CFF/CFF2, WOFF/WOFF2 and host-font
  discovery. Missing required programs/glyphs cannot disappear or silently fall
  back to machine fonts. Private evidence alone cannot close public admission.

Exit: the complete public documents and finite label/path subset are exact, with
bounded preparation/layout/contours and all resource/output contracts preserved.
Text paths are planned work, rather than an unspecified "if needed" deferral.
Any later proposal to defer an enumerated G2–G9 item must identify it explicitly
and leave it open; a supported document does not authorize shrinking group scope.

### Development sourcing and release work

The original fidelity repairs are complete; the plan now covers unsupported
SVG within the original static, self-contained product scope in section 5.
Refresh G2–G9 as retained examples reveal remaining gaps and their dependencies.
New fidelity defects may interrupt the support queue; fixing an admission gate
alone does not close a case. The group clipping/opacity and native rectangle-stroke
interruptions are resolved; numeric stroke opacity is now exact for the admitted
instance controls. Rendered-group styling is now exact for the admitted slice;
instance shader fills and independent opacity are now exact too. Container shader
fill inheritance is exact for the admitted slice. Root/nested viewBox percentage
bases are repaired. The Illustrator heart, I Ching library and both patterns are
complete, and the retained nested affine-opacity mismatch is repaired. The new
CC0 mask example is the next complete-document deliverable; choose shared support
changes by the complete retained documents they advance. Keep the
remaining instance combinations visible without making them a prerequisite for G3–G6.

1. Audit actual parser/renderer gates and existing rejected examples against the
   product scope. Select the next shared feature combination from demonstrated
   failures; seek new examples where the queue lacks a useful development case.
2. Search public-domain or CC0 SVGs in small batches, starting with the existing
   Wikimedia Commons and Openclipart sources. Verify each asset's license evidence
   and screen for distinct in-scope gaps. Keep rejections and incorrect renders;
   already exact examples need not enlarge this failure-selected corpus. There
   is no corpus-size target. Sources with ambiguous rights, external resources or
   out-of-scope behavior stay outside it.
3. Retain untouched downloads in `tests/Fixtures/candidates/` and record source,
   creator, license links, retrieval date, feature tags and explicit render options
   in the existing gap manifest. Record any root-only sizing separately; preserve
   authored geometry. Obtain a stable browser reference from three sandboxed
   captures using the existing tooling, then add the case to offline visual tests.
4. Repair shared semantics and rerun the complete retained document against its
   unchanged references. Record further blockers instead of declaring success at
   the first gate. Once every declared variant is exact, keep the case as a
   regression and revise the remaining queue from the observed gaps.

Reuse unchanged assets and references. This development corpus remains separate
from the frozen 27-document / 258-variant regression gate and establishes no
representative success rate. Future representative cohorts must be selected
before comparison.

Remote Windows/Linux build/test/package/resource CI evidence remains S17's open
release gate. The frozen corporate matrix is exact locally on both platforms.
Publishing the reviewed branch is a separate authorized action; lack of remote runs does not
block the support queue. No release or general SVG compatibility claim follows
from the four failure-selected online documents.

## 4. Findings and stage ledger

### Active findings

| ID | Remaining work |
| --- | --- |
| R26 | Preserve the complete frozen first cohort; broader representativeness and remote CI remain open. Failure-selected additions are development inputs. |
| R27 | Execute every declared case and retain admission separately from fidelity. Original fixture/research rendering differences are resolved; newly admitted support gaps require their own unchanged-reference comparison. |
| R28 | Preserve inverse paint maps; remaining pattern/coordinate combinations are G3/G5/G6. |
| R30 | Keep shared contour preparation and coverage across four paints. Consolidate further only where a demonstrated dependency requires it. |
| R31 | Existing capture/transport/cleanup and CI artifact validation are retained; remote evidence remains S17. Git identifies implementation, asset hashes protect inputs/oracles/tool downloads. |
| R33 | Preserve clipping, path/control bounds and AA paint-clip provenance; broader clip/mask combinations are G5. |
| R34 | Preserve exact analytic contact/winding, density and zero-area native stroke contours. New stroke combinations are G4; general browser fidelity needs new cases. |
| R35 | Preserve source curves and bitmap-local float mapping; general affine/instance admissions are G2/G3. |
| R36 | Extend practical inheritance and typed style state only through measured G1/G2/G7 blockers. |
| R37 | Complete remaining transform/instance gates under G3, with independent semantic controls. |
| R38 | Use actual-size bounded storage and measure meaningful allocation changes; no repository-wide rewrite. |
| R39 | Reserve before allocation/work, bound expansion and check cancellation. Audit every new owner, transfer and failure path. |

### Original finding dispositions and retained regressions

| IDs / stages | Disposition |
| --- | --- |
| R01/R02 | Glyph/contour visibility, sibling/CDATA/empty-text and XML-tail fixes retained. |
| R03/R08/R10 | Original shared style/admission and selected text/outline semantics repaired; wider text is S7/S15. |
| R04/R12; S1–S5 | Original sizing, cleanup, glyph ownership, numeric/aggregate work and XML workspace/copy/membership repairs retained. New geometry work is R38/R39. |
| R05/R06/R07/R09/R10/R11/R16 | Original matrix/cmap/face/identity/lifetime/diagnostic repairs retained. Ordinary-font fidelity is not thereby proved. |
| R13; S7 | Intended-font metrics/pixels remain open; synthetic glyphs and source access do not close them. |
| R14/R15/R19 | Sharing/cache/typed-size/allocation repairs retained; remaining consolidation is R30/R38. |
| R17/R18 | Discovery reservation and original eligibility/root-count parity repaired. |
| R20/R21; S6 | PNG first-write/output limit, common encoding kernels and disposable single-use prepared-scene lifetime repairs retained. |
| R22/R23/R31a | Original provenance, pixel comparison, CDP transport/cleanup and later sandbox startup/diagnostics/deadline repairs retained. |
| R24/R25 | Channel-wrap and definition-ancestor transform defects repaired. Residual alpha and broader inherited semantics have their active owners. |
| R27 original branch | Manifest now executes both renderable and rejected unsupported entries. Remaining fidelity reporting is above. |
| R29/R32 | Decoder reserve-before-copy, proportional image/tile work, shared gradient stops, corrupt-zlib categorization and critical-chunk policy repaired. Per-wrapper granularity and lenient late tRNS remain recorded limits. |
| R40 | Release-tag source provenance and opaque coverage calibration corrected. Preserve the matched pin and operation-specific trace; remaining numerical fidelity is S11, not an open provenance defect. |
| R41; S12 bounded repair | Zero-area objectBoundingBox gradient path strokes and the miter-threshold branch are repaired. Preserve path/use, userSpace and acute-limit witnesses; row 1 is complete. |

### Stage completion criteria

| Stage | State / remaining exit |
| --- | --- |
| S1–S5 | Core foundations retained. Preserve parser/API, memory/work and output contracts. |
| S6 | Disposable single-use preparation retained; preserve resource leases and six outputs. |
| S7 | Supplied-font hosts exist; actual-pack metrics/pixels remain section 6 and G9. |
| S8 | First frozen corporate cohort measured completely on Windows/Linux: 27 documents / 258 exact variants, with selection limits and reusable references. New failure sourcing is separate. |
| S9 | Partial styles/references/viewports; G1/G2/G5/G7 close measured static export gaps. |
| S10 | Retained path commands/mapping foundations exist; G2/G3 complete missing shape/affine/instance combinations. |
| S11 | Identified original renderer mismatches resolved. General browser equality remains unproved; every newly admitted family needs independent phase/scale/paint/alpha controls. |
| S12 | Native source strokes, dashes, joins/caps and zero-area winding retained; G4 completes practical paint/shape/transform combinations. |
| S13 | Shared float gradients/premultiplied paint retained; G4/G6 and new measured composition cases govern completion. |
| S14 | Rectangle clips, masks and bounded isolated composition exist; G5 governs broader regions, content and nested viewport combinations. |
| S15 | Experimental supplied-font text, lower priority than outlined logos; section 6 / G9. |
| S16 | Narrow embedded PNG/pattern support; G6/G7/G8 cover measured static extensions. |
| S17 | Full 27/27 and 258/258 platform-reference fidelity achieved, local delivery evidence retained. Remote CI remains open; no claim of general or representative SVG success. |

## 5. Product contract, evidence and completion

Keep one pure-managed C# net10.0 library for Windows/Linux. Retain the project's
MIT license and the adapted clipping code's BSD license/notices in
[THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt). Bounded managed source
adaptations follow section 7. Preserve zero third-party runtime packages, native
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
| Renderer-accounted live storage | 256 MiB; not process/host memory. Audit every changed R38/R39 ownership/allocation path. |
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

### Validation and evidence policy

- Tests check observable behavior. Git identifies our implementation; pinned
  revisions and URLs identify upstream code. Do not record hashes of source code,
  scripts, local adapters, our builds or packages, or diagnostic reports. Asset
  hashes remain useful for verifying fixtures, expected images, fonts and pinned
  tool downloads.
  Keep dependency lockfile integrity hashes.
  Ordinary code edits and rebuilds require no reference metadata refresh.
  Remove obsolete implementation fingerprint checks and manifests instead of
  refreshing them; retain necessary fixture and oracle asset identities.
  Tests must not fail merely because implementation bytes changed. Update
  behavioral expectations only when the intended behavior changes.
- Commit reusable inputs, expected behavior and necessary oracle provenance.
  Keep detailed run reports, pixel dumps, resource ledgers and timing batches under
  ignored artifacts. Update existing documentation when findings change a contract,
  implementation decision or release assessment. A renderer edit needs no new
  evidence manifest, per-file fingerprint or repeated historical summary.
  Add focused regressions for demonstrated defects; test and capture counts are
  evidence summaries, not quotas or prerequisites for an ordinary repair.
- Scale validation to the change. Documentation edits need consistency and diff
  review. Code changes need a Release build and affected tests; changed semantics
  or numerical behavior need relevant regression cases. Run resource, package,
  performance and platform checks when those areas change or for release claims.
  Reuse valid results for unchanged inputs and contracts. Do not rerun the full
  delivery checklist for each experiment; validate the production repair before
  landing it, and run full delivery checks when their contracts change or at release.
  Current exact references must remain exact. Changes to known mismatches require
  full before/after comparison and explanation, not merely refreshed maxima.
  Run the required complete checks once on the finished correction; do not repeat
  unchanged qualification solely to update PLAN or refresh documentation.
- Preserve exact controls. Graduate useful original research cases into the exact fixture
  corpus with their oracle provenance. Keep semantic failures as well as AA probes.
  Extend the existing harness rather than creating another testing system.
- Retain decoded candidate identity or complete diff evidence; mismatch count,
  maxima and bounding box do not uniquely identify an output. Verify browser,
  executable hash, recipe, platform, dimensions/options, background and input/asset
  identity against the appropriate historical/current pin.
- New references require three stable fresh-process captures with existing
  sandboxed tooling. Compare decoded RGBA8 exactly, ignoring RGB only when both
  pixels are fully transparent. Browser/recipe migration is deliberate; a failed
  candidate never authorizes replacing its reference.
- Exercise during-phase cancellation, tight remaining memory/work denial,
  cleanup after partial construction and six-output parity where affected.
  Extend existing memory/benchmark scenarios for curves, many contours, paints,
  clips and layers; do not infer performance from ledger-only observations.
- Re-render affected inventory inputs after narrow repairs; broad coverage,
  transform or composition changes require comparison across all declared
  variants. Run available variants and report the incomplete bank explicitly;
  missing/oracle-failure/unmeasured results cannot disappear from the denominator.
  Ordinary unit tests
  and library consumers remain offline and independent of browsers/private assets.
- Package/API changes require the isolated installed consumer; release claims
  require fresh Windows/Linux build/test/package/resource and platform-appropriate
  oracle evidence. A Windows reference is not automatic Linux fidelity evidence.

### S8/S17 accounting and remaining release decisions

Keep original asset files and hashes, source/license evidence and generator
provenance when known. Never redraw/simplify hard logos, select easier variants after failure, or
reuse retired inv-033/inv-036 IDs. Public-domain candidates are not contributor
CC0 originals; preserve their provenance and keep private assets untracked.

Turn inventory declarations into explicit options per variant: logical viewport,
outputScale, resulting dimensions and background, with input and materialized-input
hashes if capture requires materialization. Changing screenshot dimensions alone
crops a fixed-size SVG image; it does not implement scaling. The browser
transport now admits up to 8,388,608 pixels, with a screenshot-only
64 MiB reply allowance. Transport bounds never permit dropping a declared large
variant or changing the renderer's pixel limit.

Renderer outcomes and oracle availability are independent axes. The
[current measurement](tests/References/inventory/corporate-cohort-current.json)
records this cross-tabulation:

| Renderer outcome | Measured | Unmeasured | Transport gap | Total |
| --- | ---: | ---: | ---: | ---: |
| Renders | 258 | 0 | 0 | 258 |
| UnsupportedFeature | 0 | 0 | 0 | 0 |
| Total | 258 | 0 | 0 | 258 |

All 258 measured renders are exact on each platform.
All nine Target variants are exact against their original browser references;
Coca-Cola is also exact in all nine variants. Reference
availability and renderability remain independent axes. The first cohort's complete reference matrix closes its S8
measurement obligation, including browser behavior for rejected inputs. The 1,391
exact fixture entries remain separate from corporate comparisons.

Report render/reject separately from exact/known-difference/oracle-failure and
unmeasured. A document counts only when **all declared variants** are measured
and exact. Preserve historical narrower successes without counting their unmeasured
sizes. Preserve the completed corporate-mark/outlined-wordmark selection and
holdout freeze; retain flags, diagrams and other symbols as separate useful
strata. Keep every frozen document in its denominator, including
rejects, known differences, oracle failures and unmeasured variants. Fixture-entry
percentages and the legacy 92-file Default bank cannot replace corporate-document
coverage. Ford establishes the outlined-wordmark checkpoint; YouTube is the
selected curved-mark target and must retain all nine declared cases.

The achieved fidelity target is 27/27 exact documents and 258/258 exact variants on
Windows and Linux, including 13/13 marks and 14/14 outlined wordmarks. This is the
measured result for the existing frozen cohort; S17's original
release minimum remains at least 80% exact documents overall and in each of the
corporate-mark/outlined-wordmark strata (round required document counts upward).
The first corporate cohort and explicit variant recipe are frozen in the
[manifest](tests/References/inventory/corporate-cohort-v1.json). Existing renderer
outcomes did not filter membership: all 19 retained brand/organization exports
and eight predeclared fresh holdouts remain. The 22/27 rounded overall minimum
and the 11/13 mark plus 12/14 wordmark minima all apply: **23 exact documents is
the effective minimum** for this cohort. All 258 cases, including the fifteen former
transport-limit gaps and all former failing variants, retain their denominator.
Combined identities already have a
declared primary stratum. Public-domain source selection and the organizational
mix limit representativeness; future cohorts must be selected before measurement
as well.
Report the measured rate with this cohort's scope and selection limits even after
it meets the target. The target alone does not justify a “most logos” market
claim; broader representativeness and the remaining release gates need evidence.
There is no separate quirk budget: matching the pinned oracle remains
implementation work, and any document
with a mismatching variant cannot enter the exact numerator. A coverage target
below 100 percent may leave failed documents in the denominator; it never makes
their pixels exact. S17 also requires categorized residuals, reproducible oracle
evidence, representative latency/allocations and fresh delivery/platform gates.

Maintain API_CONTRACT, README and SUPPORT_MATRIX as current contracts. Keep
dimension rules, admitted combinations and fixture counts aligned with the
implementation. PLAN is the execution queue; INVENTORY holds document evidence,
RASTERIZATION_DESIGN holds hypotheses/results and BENCHMARKS holds measurements.
Neither another status paragraph nor another source audit is an implementation exit.

## 6. Font, text and host requirements retained

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
   Browser captures remain network-blocked; use missing/corrupt/substitution
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

### First useful text milestone

Horizontal LTR Latin labels, initially ASCII/tested precomposed Latin, using supplied
static standalone TrueType face 0. Validate SFNT/ranges, `head`, `maxp`,
`hhea`/`hmtx`, `cmap` 4/12, `loca`/`glyf`, plus optional `name`/`OS/2` metadata
structures.
Include simple curves, implied points and bounded composite placement; gate
unsupported formats/flags.

Preserve text/CDATA/span order and admitted whitespace across boundaries. Support
inherited font/paint/visibility, scalar x/y/dx/dy, alphabetic baseline,
start/middle/end anchors, advances and required pair positioning (`kern` and
explicit bounded GPOS pairs). Test AV, spaces/punctuation, accents/composites,
supplementary scalars, mixed faces/sizes/colors and fallback boundaries.
Implement or reject features affecting output. Add styled/positioned `tspan`, spacing and
explicit line positioning for labels.

Position/rotate lists, `textLength`/`lengthAdjust`, the enumerated extra baselines,
transformed/stroked text, decorations and text paths are later G9 gates, with
finite intended admission and acceptance examples in section 3. Their initial
exclusion does not remove them from the final queue. Automatic wrapping,
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
oracle results are not canonical Linux evidence. Work toward 200 targeted cases and
10–20 representative diagrams/illustrations as coverage prompts, not release quotas.

## 7. Optional reference sources

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

Historical source map (inspected 2026-09-29; paths are relative to each optional
checkout and are study references only):

- **Row 4:** tiny-skia `src/scan/path.rs::walk_edges` shows active-edge ordering,
  winding/parity and span emission. Its `src/scan/path_aa.rs` uses supersampling;
  use it for organization, not the Chrome numerical target. At the matched Skia
  revision, inspect `src/core/SkScan_AntiPath.cpp` for dispatch,
  `src/core/SkScan_AAAPath.cpp::aaa_walk_edges` for scan intervals/coverage, and
  `src/core/SkAnalyticEdge.cpp::setLine` for edge quantization. Source paths alone
  do not prove the browser's selected fast path; retained captures adjudicate.
- **Rows 5–6:** resvg `crates/usvg/src/tree/mod.rs::Path` retains path data, paint
  and transform/bounds separately; tiny-skia `path/src/stroker.rs::PathStroker`
  processes line/quadratic/cubic verbs and curve normals. Apply those boundaries
  within the existing C# ownership model; bounded numerical adaptations follow
  the policy above.
- **Row 7:** resvg `crates/resvg/src/render.rs::render_group` isolates bounded
  layers and tracks their origin; sibling `clip.rs` and `mask.rs` reuse traversal
  for effect content. Their composition pixels still need our pinned oracle.

Read a matched Skia file with
`git -C external/skia show f8b66b7597c4cc859d3ed190e9c6872241e6721c:src/core/SkScan_AAAPath.cpp`.
Do not switch the shared checkout or add a reference-source dependency.

Upstream success criteria and defaults are not ours: resvg tests allow channel
tolerance and can regenerate references from themselves; the test-suite Chrome
helper disables the sandbox; upstream resolvers may read host files or keep a
malformed path prefix. Keep our pinned captures, explicit providers, categorized
failures, strict comparison and bounds. Retain upstream notices with optional
checkouts and the required notices for adapted product code; bundled fonts/assets
have separate licenses. Source access does not authorize whole-renderer vendoring
or publication of private assets.

Use the existing [oracle tooling](docs/ORACLE.md),
[research record](docs/RASTERIZATION_DESIGN.md),
[comparison tool](docs/VISUAL_COMPARISON.md),
[inventory](docs/INVENTORY.md) and [benchmarks](docs/BENCHMARKS.md).
Do not build Chromium, fetch a renderer ecosystem or survey more reference
implementations as a prerequisite to the concrete queue.
