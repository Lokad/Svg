# Lokad.Svg — renderer fidelity and remaining SVG support

Updated 2026-10-08. **27/27 corporate documents and 258/258 variants are exact
on Windows and Linux**, against each platform's retained browser references.
Marks are 13/13; outlined wordmarks are 14/14. Membership, input bytes, options
and references remain frozen. This complete matrix is the regression gate.

**The original renderer fidelity repair phase is complete.** All admitted
original fixture and research comparisons are exact against their original references.
The final repair in that phase preserves native stroke contours with zero total signed area
but nonzero local winding, including clipped construction. It changes one
original PNG; the other 1,538 PNGs and every corporate report/image stay unchanged.
See [the repair](docs/RASTERIZATION_DESIGN.md#zero-area-native-stroke-winding-2026-10-08).

The shared `enable-background` validator now admits `new`, `accumulate` and
`inherit` consistently through presentation attributes and the admitted inline/
stylesheet paths. The Illustrator heart (`inv-105`) advances to the rotated-
rectangle gate, **G3**, and remains rejected; no source or browser reference changes.

**G2 now retains ordered nested groups and local uses.** The three previously
rejected nested-content controls are exact against unchanged browser references.
All sixteen retained-content controls and three diagnostic twins now match
their unchanged references. Across the symbol banks, **83 comparisons are exact
through all six outputs**, including eighteen new viewport/opacity controls.
Reference graphs validate and expand iteratively with bounded storage and work.
Anonymous definitions, borrowed aliases and empty forward targets keep their owners.

**The fractional symbol-viewport clipping repair is complete.** The alias and
equivalent inline cases are exact against their unchanged references. Viewport
coverage applies once on group restoration, with the browser's separate sprite
and image-shader arithmetic; source ink bounds round before device mapping.

**G2 now admits solid use-level color and fill opacity.** Direct and forward
definitions, symbols, nested uses and aliases retain independently authored paint;
source fills win over use overrides. CurrentColor stroke markers and their separate
opacity survive instance mapping. Eighteen new fill/color cases are exact.
The use-paint bank is now **31 cases = 26 exact + five rejections**.

**The fractional rectangle-stroke repair is complete.** Its three retained
backgrounds now match unchanged browser pixels. The shared frame route preserves
outer hulls, integer interiors and inverse inner fringes as ordered operations;
twelve independent fractional, scaled-thin, collapsed-interior and shader-fill
controls also become exact. Three clipping controls retain their admission gate.

**The native symbol stroke repair is complete.** Its three retained backgrounds
now match unchanged browser pixels. Closed rectangle paths share native recognition
with filled paths and emit simple outer/inner contours before generic stroking.
All joins, the miter cutoff and collapsed interiors retain native construction;
thirty new controls are exact through six outputs. The inline twin stays exact.

**G2 now resolves numeric stroke opacity at instance time.** Literal, rgba and
currentColor solid strokes keep authored opacity independently of fill opacity;
existing shader strokes retain their native byte-alpha boundary. Nested groups,
uses and aliases inherit opacity without importing referenced ancestors. Unpainted
path definitions retain their centerlines for strokes added by use instances.
The new 81-case bank is **64 exact + 17 explicit rejections** through all six outputs;
its baseline was 75 rejections and six incorrect renders. Source/reference bytes
remain frozen, with three healthy, stable browser captures per case.

**Next, continue G2 with inherited stroke paint/width/mechanics and instance styling.**
Wider stroke/paint overrides, rendered-group styling, remaining locations and
viewport mapping keep G2 open. The transformed inline rectangle twin retains
its separate gate; broader transform and paint-server stroke combinations remain G3/G4.

Solid fill bindings now preserve a leaf's own display/visibility and resolve
`currentColor` and fill opacity independently at instance time. Ten of sixteen
new controls previously rendered incorrect pixels; all sixteen now match their
unchanged browser references through six outputs. This repair also covers child
aliases; nested group display and visibility now propagate through clones.
Broader stroke inheritance and instance styling remain open.

The remaining support queue covers static SVG within the original product scope:
repair newly exposed fidelity defects, remove erroneous admission gates, and fill
missing geometry, reference, paint and composition combinations below. Passing the frozen cohort does not
prove general SVG support. S17 remote CI remains a separate release requirement.

The first new online sourcing batch screens four public-domain/CC0 SVGs and
retains three distinct failures, with untouched downloads, root-only test inputs,
license evidence and stable browser references. The exact checkerboard is excluded
from this failure-selected development corpus. It establishes no representative
success rate. A follow-up CC0 I Ching library adds a compound-curve rejection,
bringing the retained online failures to four. The [inventory](docs/INVENTORY.md#original-scope-support-audit-and-online-batch-2026-10-08)
and [case manifest](tests/References/inventory/svg-gap-cases.json) hold the details.

This plan owns priorities and exit criteria. [SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md)
owns the admitted subsets and observed gates; [INVENTORY](docs/INVENTORY.md) owns
document provenance; [RASTERIZATION_DESIGN](docs/RASTERIZATION_DESIGN.md) retains
mechanisms and rejected models; [BENCHMARKS](docs/BENCHMARKS.md) owns timing evidence.
Earlier checkpoints remain in those records and Git rather than the active queue.
R01–R41 and S1–S17 retain their identities. The September 28 review is historical
evidence in [the review](docs/REVIEW_2026_09_28.md).

## 1. Validated checkpoint and limits

| Validated checkpoint evidence | Assessment |
| --- | --- |
| Windows/Linux Release solution builds | Nine projects each, zero warnings/errors. |
| Full tests, including corporate selection/measurement | **4,634 passed on each platform**: 4,014 core, 614 visual-project, six FontProbe; zero failed/skipped. Test counts are not image counts. |
| Memory and delivery checks | Forty-eight preceding exhaustive parser-work/memory ownership inputs pass. Five additional stroke-opacity inputs cover solid/shader channels, nested uses and newly stroked unpainted curves, including every parser work limit, memory denial, raster work boundaries and cancellation/retry. Eighteen graph, duplicate-ID, empty-target, deep-chain, expansion-denial, sealing-cancellation/retry and discovery checks pass. Partial forward insertion disposes unadopted native owners and reserves extra command storage before allocation. All 83 preceding six-output symbol comparisons stay exact; the 31-case use-paint bank preserves 26 exact and five unsupported outcomes through six outputs. Fifteen frame cases preserve twelve exact and three unsupported outcomes; frame work-denial checks release surface owners. All thirty rectangle-path stroke controls are exact through six outputs; 480 compiled native outlines match float bits. Every admitted work/memory denial for the three join styles releases output owners; cancellation/retry and four construction-map controls pass. The 81 new opacity cases preserve 64 exact and 17 unsupported outcomes through six outputs. Source fill/stroke roles, authored opacity precedence and independent alphas have focused checks; all 149 recorded-paint outcomes remain unchanged. Earlier broad resource, memory and installed-package evidence keeps its dated scope; remote CI remains open. |
| Executed fixture manifest | **1,410 entries = 1,389 exact + zero known-difference renders + 21 rejections**; 799 base documents / 611 variants. Three magnified-instance cases graduate against their unchanged references; other fixture outcomes and PNGs stay unchanged. |
| Executed research manifest | **65 probes = 64 exact + zero known-difference renders + one rejection**. Three multiple-child symbol cases graduate from the erroneous MalformedInput rejection; other research outcomes and PNGs stay unchanged. |
| Supplemental recorded-paint inventory | **149 controls = 132 exact + 17 valid-input rejections**, checked through all six outputs with unchanged measured outcomes. These mechanism controls remain separate from the original bank, new development sources and frozen corporate cohort. |
| Legacy independent-file Default sweep | **92 files = 92 render**. Every Default report and PNG stays unchanged, preserving the preceding matching-browser STOP-sign gain and earlier `inv-013` exact result / `inv-014` residual. These Default outputs do not establish corporate fidelity or a representative rate. |
| Corporate cohort freeze | **27 documents / 258 variants**, 13 marks / 14 wordmarks. Membership, sources, root-only materializations, options and references stay unchanged; every variant has a stable reference. |
| Corporate measurement | **258 rendered comparisons = 258 exact on each platform**, zero known differences/rejections. Every corporate PNG and complete comparison remains unchanged under the shared repairs. **27/27 documents**, marks **13/13**, wordmarks **14/14**. Full fidelity target achieved; S17 remote CI remains open. |
| Performance and platform limits | Symbol accumulation, fill bindings and retained instance arrays charge bounded parser/scene storage and copy work; viewport restore charges clip setup work. Source stroke identity uses existing recorded-paint padding (68 bytes); draw commands stay 72 bytes and geometry 272 bytes. Binding arrays reserve their actual element size. Rectangle recognition owns no storage; closed rectangle outlines use bounded stack scratch and the existing budgeted output list, without generic stroker scratch. Frame coverage owns no storage; row and pixel/operation work is charged before painting, and empty interiors are skipped. No added surfaces or quantitative performance claim. Earlier [cost records](docs/BENCHMARKS.md) keep their dated scope. Remote CI, broader workloads and private-font metrics/pixels remain unproved. |

Both platforms pass Release builds and 4,634 tests. All 1,567 original execution
reports and all 1,545 PNG byte sequences remain unchanged from the preceding
native rectangle-stroke checkpoint; Windows/Linux banks agree. Every corporate result
is unchanged and exact against its platform's own references. Four retained online failures
remain; the heart and new I Ching library are G3, the two patterns are G6. WSL
Ubuntu 24.04.3 uses isolated SDK 10.0.401 / runtime 10.0.12 and PowerShell 7.6.6.
Earlier package, memory and timing evidence keeps its dated scope; remote CI
remains open.

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
gates. The order below prioritizes shared combinations used by ordinary exports;
it is an engineering judgment, not a measured prevalence ranking. G3–G6 have
concrete retained failures; G2's first retained failures are now exact and its
remaining combinations need independent cases before implementation. G7–G9 are
bounded later work and need fresh development examples before their implementation slices are selected. Regression repairs may
interrupt this order. The identified fidelity repairs under historical rows
1–7 are complete; remaining feature/admission work now maps to G1–G9. Their
landed mechanisms remain regression requirements.

| Priority | Work | Failure class | Dependencies / stages |
| --- | --- | --- | --- |
| G1 | **Declaration validation landed.** Inert exporter presentation values share one validator; G2 is next. | Original syntax-location inconsistency resolved; the heart now exposes G3 | Remaining nested style/root selector gates stay G5/G7; S9 |
| G2 | **Solid use color/fill-opacity and numeric stroke-opacity inheritance implemented.** Complete remaining practical symbol/instance combinations. | Inherited stroke paint/width/mechanics, wider paint overrides, rendered-group style, other-location and viewport combinations remain open | Definition ownership, expansion and bounded reference graph; S9/S10 |
| G3 | Complete curved geometry and static transforms across shapes, curves and instances | Missing combinations behind shape-specific and non-uniform gates | Source-curve replay and paint-coordinate maps; S10/S11 |
| G4 | **Fractional rectangle-stroke fidelity repaired.** Complete paint-server strokes on ordinary geometry | Three retained rectangle comparisons and twelve new frame controls are exact; broader paints retain admission gates | G3 where transformed; shared source stroke construction and paint bounds; S12/S13 |
| G5 | **Fractional viewport clipping and native symbol stroke fidelity repaired.** Complete nested viewport paints, general clips and masks | Retained native-stroke comparisons are exact; clip-crossing and further reference/coordinate/composition gates remain | G2/G3, bounded layers and reference scopes; S9/S14 |
| G6 | Practical patterns: content, inheritance and coordinate systems | Missing static paint support | G2/G3/G5, bounded tile rendering; S13/S16 |
| G7 | Embedded raster images and remaining static CSS | Missing static features/subsets; capture an export before selecting a slice | Explicit data only, bounded decode/resampling and shared cascade; S9/S16 |
| G8 | Markers and a measured subset of filter/blending effects | Missing static features; lower priority than outlined logos | G3–G5, bounded intermediate surfaces; S16 |
| G9 | Supplied-font text, then text paths if needed | Separate experimental feature work; no implicit host font discovery | Section 6, caller-owned snapshots and shared glyph contours; S7/S15 |

### G1 — exporter declarations: shared validation landed

Attributes and admitted inline/stylesheet declarations now share allocation-free
validation of `new`, `accumulate` and `inherit`. `new` accepts up to four finite
rectangle numbers with nonnegative dimensions; negative origins, scientific
notation and zero dimensions are admitted. Numeric partial rectangles disable
backdrop access under SVG 1.1 and are admitted too. Malformed values cannot hide behind
duplicate declarations or `!important`; active filters still reject. Root, group,
leaf, use, viewport-attribute and inherited locations have six-output controls;
font discovery agrees and tight denial/cancellation releases owners.

`inv-105` no longer rejects its Illustrator root CSS declaration. It now reaches
`Only axis-aligned rectangle transforms are implemented.` under **G3**; the
original, root-sized input and browser reference remain unchanged. Nested SVG
`style` attributes and root-targeting stylesheet selectors retain separate
admission gates under **G5/G7**. This closes the shared declaration-validation
inconsistency, not the heart's complete rendering or those wider CSS gaps.
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
is 64 exact and 17 rejected through six outputs. Nine fractional diagonal-line
cases remain G3/G4, two late stylesheet cases remain G7, three shader rectangle
definitions remain G2/G4, and three rendered-group style cases remain G2. The
additional supported twins are separate inputs; these rejected originals stay frozen.

Next implement inherited stroke paint/width/mechanics and wider paint-server overrides,
rendered-group use styling, other locations and viewBox percentage-base/affine
combinations. Paint-server use fill opacity and inherited fill opacity on retained
shader definitions retain explicit gates; the new controls make both visible.
Select bounded slices from those failures; broader
transform maps stay G3 and deeper reference/composition scopes stay G5. Exit:
the selected practical combinations are exact on unchanged references with no
regression to current exact inputs or resource contracts.
See [the stroke-opacity slice](docs/RASTERIZATION_DESIGN.md#stroke-opacity-at-instance-time-2026-10-08).
See [the repair](docs/RASTERIZATION_DESIGN.md#ordered-symbol-children-and-native-instance-replay-2026-10-08).
The [location/ownership follow-up](docs/RASTERIZATION_DESIGN.md#non-rendering-symbol-locations-anonymous-owners-and-descriptions-2026-10-08) records the current repair.
The [solid-fill presentation repair](docs/RASTERIZATION_DESIGN.md#symbol-solid-fill-presentation-at-instance-time-2026-10-08) records the new fidelity fixes.
The [retained-content slice](docs/RASTERIZATION_DESIGN.md#retained-symbol-groups-and-local-reference-graphs-2026-10-08) records the expansion and the subsequently repaired clipping residual.

### G3 — curved geometry and transforms

The new CC0 `inv-106` I Ching library rejects its compound curved even-odd
Yin/Yang path through the shared path admission gate. Its 79 symbols and uses
retain original geometry and a stable 280×720 browser reference. This geometry
combination belongs to S10 alongside the maps below; removing the first gate
must not hide later failures.

`original-logo-rotated-mark` rejects a rotated rectangle, even though some path
rotations are admitted. `original-number-general-map` and the retained tiger reject
general matrix transforms. `original-curve-root-quad-none` rejects non-uniform
viewBox/output curve mapping. Reflected pattern groups and magnified use add
shape/paint/instance gates. Complete these through one composed affine map,
preserving authored curves, stroke semantics and paint coordinates. Exit: retained
rotation, skew, non-uniform and instance families become exact without normalized
or flattened source substitution; no corporate or prior exact regression.

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
as measured follow-ups. Exit: solid/linear/radial/pattern twins share source stroke
semantics, declared clips and placements are exact, and ownership/budget checks pass.

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

`original-aa-clip-admitted-shader` rejects a gradient defined inside a nested
viewport as undefined. The material-pair family rejects patterns in scaled
viewports. `mask-region-half-translated` rejects a transformed custom mask region;
nested SVG style attributes, retained percentage-mask and nested-opacity examples
need their own admission slices. Distinguish definition scope, object bounds, visual stroke bounds and
viewport percentage bases. Exit: each retained family and independent leaf/group/
use controls are exact with source-order binding, bounded temporary surfaces and
token-preserving cleanup. Full AA-mask/cache behavior remains a separate fidelity
requirement when a new example demonstrates it.

### G6 — patterns

`inv-103` uses a circle in a user-space pattern and rejects the child. `inv-104`
is an Inkscape export using a local `xlink:href` pattern with path content and
fractional translation; its first rejection is the inheritance attribute.
`original-cpu-raster-cubic-pattern` also retains a two-child rejection. Current
support is one bounded rectangle tile with integer user-space coordinates.
Add bounded multi-element vector tiles, local inheritance with cycle checks,
pattern transforms, viewBox/content units and objectBoundingBox units in measured
slices. Exit: these originals render exactly; independent tile-border, phase,
alpha, transformed fill/stroke and inheritance controls pass without tile-size
or repeat-count-dependent unbounded work.

### G7–G9 — later original-scope work

Embedded raster support must address non-uniform PNG content, image mapping and
resampling using explicitly supplied/data resources; arbitrary network/file
discovery remains excluded. Broader static CSS should follow real exporter
blockers and the existing cascade, not a browser DOM. The retained marker and
drop-shadow fixtures establish missing static features; choose a bounded marker
slice before measured filters/blending. Live text and text paths stay behind the
outlined-logo work and require the caller-supplied font evidence in section 6.
Exit criteria for each selected slice include unchanged reference equality,
bounded decode/expansion/surface work, correct rejection of unsupported active
features and preserved exact corporate results.

### Development sourcing and release work

The original fidelity repairs are complete; the plan now covers unsupported
SVG within the original static, self-contained product scope in section 5.
Refresh G2–G9 as retained examples reveal remaining gaps and their dependencies.
New fidelity defects may interrupt the support queue; fixing an admission gate
alone does not close a case. The group clipping/opacity and native rectangle-stroke
interruptions are resolved; numeric stroke opacity is now exact for the admitted
instance controls. Continue the remaining G2 combinations from retained failures.

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
from the four retained online failures.

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
measurement obligation, including browser behavior for rejected inputs. The 1,389
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

Position/rotate lists, `textLength`/`lengthAdjust`, more baselines, transformed/
stroked text and decorations need later gates. Initially exclude automatic wrapping,
bidi/joining/complex scripts, unadmitted mark positioning, general GSUB/GPOS, text
paths/vertical text, variable/color/bitmap fonts, CFF/CFF2 and WOFF/WOFF2.

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
