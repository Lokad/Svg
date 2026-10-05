# Lokad.Svg — pixel-exact corporate-logo delivery plan

Updated 2026-10-05. The latest validated library checkpoint is **`57c6a7e3`**:
the bounded line/quad/cubic clipping kernel and its independent controls are
committed, but rendering does not yet call the kernel. The latest change to
rendered pixels remains **`429ea226`**, which admits retained cubics independently
of flattened chord count. It follows operation-preserving paint (`c9f0eea2`) and
contained-edge scan-window separation (`9e77c0fa`). **Next: integrate clipped-edge
construction into shared contour preparation.** Ford remains zero exact of nine;
the first corporate checkpoints and S17 remain open.

The reviewed renderer baseline
`f633c86bc5df0584edd1c4741c0a2218d49d3a32` is historical. Curve/walk separation,
rejected endpoint models, full-canvas paint routing and mask/run controls remain
preserved checkpoints; their evidence is in the records linked below.
The September 28 baseline and review remain historical evidence in
[the review](docs/REVIEW_2026_09_28.md) and Git. R01–R41 and S1–S17 retain
their identities. This plan owns the current execution queue and exit criteria;
checkpoint summaries below retain their dates, while detailed experiments stay in
[the rasterization record](docs/RASTERIZATION_DESIGN.md), document evidence in
[the inventory](docs/INVENTORY.md), and timings in
[the benchmark record](docs/BENCHMARKS.md).

**Objective:** render most ordinary, static, self-contained corporate SVG logos,
including outlined wordmarks, pixel-exact against the pinned browser oracle.
The S17 target is **at least 80% exact documents**, both overall and within each
corporate-mark/outlined-wordmark stratum of a frozen representative cohort.
Every declared variant of a counted document must be pixel-exact; the document
target never permits pixel tolerance. Preserve the pure-managed,
dependency-free library contract.

The first product checkpoint remains an independently selected flat curved
corporate mark and an outlined wordmark with counters, each exact at every
declared size/background. Neither checkpoint nor S17 has been established.
Ford (`inv-002`) is the active wordmark target; another sourcing campaign is
not a prerequisite to improving the shared renderer.

## 1. Current baseline and its limits

| Current validated evidence | Assessment |
| --- | --- |
| Fresh Windows locked restore / Release solution build | Nine projects, zero warnings/errors. |
| Fresh full tests | **3,156 passed**: 3,045 core, 105 visual-project, six FontProbe; zero failed/skipped. Test counts are not image counts. |
| Fresh memory and delivery checks, at the clipping-kernel checkpoint | All 25 memory scenarios pass; runtime/symbol package inspection and isolated installed-consumer checks pass. The package retains the adapted code's BSD notice. No runtime package dependencies, native assets or packaged SVG fixtures. |
| Executed fixture manifest | **611 entries = 493 exact + 113 known-difference renders + five rejections**; 360 base documents / 251 variants. Synthetic coverage is not corporate-logo coverage. |
| Executed research manifest | **67 probes = 66 known-difference renders + one rejection**. Useful exact controls graduate to fixtures with their provenance. |
| Fresh independent-file Default-options sweep | **92 files = 88 render + four MalformedInput rejections** (`inv-007`, `inv-012`, `inv-014`, `inv-020`). Renderability does not establish fidelity or all-variant completion. |
| Latest recorded Ford all-variant evidence | Small 250×100, intended 500×200 and large 1000×400 × transparent/white/black: **zero exact of nine**. Intended-transparent: **433 changed pixels**, max channel error 128, max alpha error 54, 433 alpha mismatches and 3,176 total alpha error. Paint improved all nine; scan-window separation was neutral. One-chord cubic admission improves all six small/intended variants and preserves all three large PNGs. |
| Representativeness | 57 of the 92 inventory headings identify flags or flag badges/icons. Corporate marks and outlined wordmarks need a separate frozen cohort; the whole inventory is not a representative corporate denominator. |
| Not refreshed by the baseline/checkpoint gates | Linux/remote CI, a full browser-reference refresh, full independent inventory variant regeneration, BenchmarkDotNet throughput, private-font metrics and pixels. The bounded overlap captures below are separate evidence. |

The original review and full-canvas checkpoint retain their dated evidence under
ignored `artifacts/review-2026-10-04/` and `artifacts/span-dispatch-20261004/`.
The latter banks 79 exact routing configurations, one group-opacity-white failure
and the graduated integer overlap. The subsequent dispatch investigation banks
12 independent configurations; all 12 are now exact without replacing references.

The paint checkpoint adds 16 CC0 sources / 24 configurations, each with three
stable, healthy fresh-process captures: 15 exact and nine known differences.
Eleven existing known fixtures graduate to exact. The exact research joint-split
control graduates to fixtures with its original PNG and provenance; its former
research files remain historical. All 435 previously exact complete PNGs and all
existing render/reject dispositions are preserved. The pre-bank comparison covers
689 cases; the updated bank executes **713 = 549 fixtures + 72 research + 92
Default inventory cases**. Comparisons, capture triples, resource ledgers and
delivery checks are under ignored `artifacts/paint-operations-20261004/`.
Committed manifests and the standard tools remain the portable regression sources.

The scan-window checkpoint adds 16 CC0 sources / 18 configurations, all exact,
each with three stable, healthy fresh-process captures. Five research controls
graduate with their original PNG/provenance. All 462 previously exact complete
PNGs and every render/reject disposition are preserved. The pre-bank comparison
covers 713 cases; the updated bank executes **731 = 572 fixtures + 67 research +
92 Default inventory cases**. Evidence is under ignored
`artifacts/scan-window-20261004/`. Full independent inventory variants and corporate
coverage remain unmeasured; four changed Default inventory outputs lack matching
oracle evidence.

The one-chord cubic checkpoint adds ten CC0 sources / 12 configurations with
36 stable, healthy fresh-process captures: eight exact and four known differences.
Nine configurations were declared before implementation; the three paired-material
holdouts were declared after the first trial and before their independent captures.
All 572 existing fixture PNGs, all 67 research results and every disposition are
preserved. The updated bank executes **743 = 584 fixtures + 67 research + 92
Default inventory cases**. Eleven Default inventory PNGs change without matching
captures in that sweep; Ford has separate all-nine evidence, while the other ten
outputs' fidelity is unmeasured. Evidence is under ignored
`artifacts/cubic-admission-20261005/`; this is shared renderer progress, with no new
independent corporate checkpoint or S17 rate.

The clipping-kernel checkpoint verifies 160 independent native inputs
and all 334 emitted pieces, including raw float bits, order and verb type. It
banks 19 CC0 SVG sources / 27 declared configurations with 81 stable, healthy
fresh-process browser captures. All 27 currently render with known differences;
references retain those failures before integration. All 743 prior results and
all nine Ford PNGs are unchanged; the updated bank executes **770 = 611 fixtures
+ 67 research + 92 Default inventory cases**. Kernel work/iteration limits and
cancellation are tested, but the renderer does not yet call it. Ignored evidence
is under `artifacts/analytic-clip-20261005/`; the native piece data, SVG controls
and references are portable regressions. Shared preparation, edge storage and
walk-window integration still need their own resource and fidelity proof.

Chrome remains **153.0.8010.52 win64, recipe 3**, with the recorded executable
hash and Chromium revision field 1681091. Historical recipe-1/recipe-2
references retain their identities. The matched release-tag Skia pin remains
**f8b66b7597c4cc859d3ed190e9c6872241e6721c**; the main-revision comparison
`8b8c3872` does not supersede it (R40). Baseline/checkpoint comparisons reused
committed references. The fresh recipe-3 calibration controls in section 3 do not
constitute a full oracle refresh.

The tests deliberately retain known wrong pixels as well as successes. PNG
output, admitted syntax, same-renderer twins and matching mismatch tuples are
not independent proof of exactness. The API can return inexact output; keep
the preview's support/fidelity boundaries explicit. Oracle over-coverage,
asymmetry and backend dispatch are compatibility requirements wherever they
affect a counted document, even when our geometry is closer to ideal area.

## 2. Execution decisions and ownership

1. **Use one implementation owner for the renderer queue.** Work serially across
   `SvgConverter`, geometry lowering, reference resolution and shared style/
   mapping types. A second review may be read-only; delegation is not required.
2. **Check actual state before edits.** Inspect HEAD, status and live
   coordination. Previous committed work is not a lock. Preserve concurrent
   changes and use an isolated worktree when an experiment overlaps them.
3. **Preserve direct paint and accumulated-run operations, rows 3–5.** Curve/walk state separation
   preserves quarter-grid output. The full-canvas solid-paint repair now preserves
   native rectangle identity and original fill-opacity through scene/reference
   lowering, with independent opacity, paint, mapping and routing controls.
   Independent width/storage controls now distinguish native mask and run routes.
   The shared operation/material boundary now preserves direct paints and pending
   additive runs, with distinct uniform/shader arithmetic. Paint improves every
   Ford variant. The contained-edge scan-window repair closes the retained curve
   controls while leaving Ford unchanged. Retaining one-chord cubics now improves
   six Ford variants; remaining curve-event and clipped-edge errors stay active.
   General composition remains open; its layer residuals stay in rows 3 and 7.
4. **Preserve the landed repairs and exact controls.** Do not repeat the old
   six-site floor256 replacement, globally disable live cubics, snap the final
   coverage mask globally, or desnap endpoints alone. Native run-flush snapping
   applies only to pending additive runs; direct fringe paints bypass that flush.
   Each has retained counterexamples. Further syntax admissions wait unless
   the active target demonstrates a dependency; regression repairs may interrupt.
5. **Keep release policy separate from implementation.** S17's exact-document
   target is 80%, overall and in each corporate stratum. The representative
   cohort and its explicit variants still need to be frozen and measured; that
   evidence blocks a coverage claim, not renderer work. There is no
   pixel-tolerance or oracle-quirk budget. The aligned policy record is
   [S17_DECISIONS.md](docs/S17_DECISIONS.md).

Make small, buildable commits with relevant regression evidence, support
documentation and actual validation. Update the current row/status in place.
Git retains previous checkpoints; the design record retains experiment details.
After two experiments without a mechanism-level improvement, record the
remaining uncertainty and choose the next architectural step. Do not substitute
another census, source audit or mismatch-maxima refresh for implementation.
Record a concrete blocker and return condition before switching slices.

## 3. Ordered implementation queue

Rows retain their identities and dependencies. A landed mechanism is not a
completed stage: the remaining exit evidence below governs completion.

| Order | Current state and work boundary | Remaining exit evidence |
| --- | --- | --- |
| 1 | **Complete: bounded semantic repairs.** S12 miter threshold and R41 zero-area objectBoundingBox gradient path strokes are repaired. | Preserve acute-limit branches, degenerate path/use and userSpace controls; no reopened defect. |
| 2 | **Shared contour/paint ownership boundary landed.** R30/R38/R39 preparation, mapping and reservation repairs support the scanner. | Preserve reserve-before-allocation, growth peaks and ownership transfer; audit new traversal/state storage, denial, cancellation, disposal and six-output parity. Existing foundations are not a new rewrite. |
| 3 | **Full-canvas solid-paint routing landed; general composition fidelity remains open.** Opaque coverage correction, premultiplied source-over and source-opacity precision are retained. | Preserve the 79 exact new routing configurations and the graduated integer overlap. General coverage/alpha and layer composition need independent controls; group-opacity on white still differs. |
| 4 | **Active with rows 3 and 5: shared scan conversion.** Spatial membership, analytic rows, contact/straight-source union, operation-preserving mask/run paint, contained-edge scan windows and one-chord cubic admission have landed. The clipped line/quad/cubic kernel is verified independently. | Integrate clipped-edge construction when complete path/control bounds escape the clip, including Ford's first path. Native clipping additionally splits cubics at X extrema even where their geometry fits. Preserve operation order and bounded storage; general fractional events, crossings, counters and overlaps remain open. |
| 5 | **Open with row 4: retained curves and final device mapping.** Double maps, retained controls, live non-convex cubic stepping, curve/walk state separation and one-chord cubic admission have landed. | Preserve transform/instance and large-translation evidence. The original uncut tip's setup/advance and its existing-edge scan operations match compiled native code. Native clipped construction explains its first mismatch; integrate that shared preparation boundary before changing recurrence. Fractional-event, quad/convex/stroke work retains its separate fallback/gates until proved. |
| 6 | **Partial: curve-aware stroke completion.** Width/cascade, cap/join/dash, zero-width and straight-source union repairs are retained. | True curve tangents/dash cuts, curved/dashed overlap and seam handling, remaining round/miter/hairline/pathLength/non-scaling cases through the common kernel. Mathematical outline agreement alone does not close Chrome pixels. |
| 7 | **Partial: practical exports, paints and bounded layers.** Many inventory blockers, affine paint maps, use/reference bindings and bounded composition paths have landed. | Remaining clip/mask/style/paint combinations, alpha/dither/group composition and supported transform gates, with source-order binding, temporary-surface bounds and per-input oracle evidence. |
| 8 | **Not achieved: independent milestone and delivery, S8/S17.** The 92-file inventory and all-variant records are useful evidence, not a release rate. | First exact curved corporate mark and outlined wordmark; then a frozen representative cohort meeting the 80% exact-document target overall and per corporate stratum, complete declared variants and fresh platform/package/resource/performance evidence. |

### Active work order: integrate clipped-edge construction (2026-10-05)

**Implemented mechanism.** Small native path bounds select an accumulated mask;
larger bounds preserve ordered direct single/pair/full-span paints separately from
pending additive coverage. Only pending runs snap at flush. Solid paints use
round/255 initial premultiplication and operation-specific coverage arithmetic,
including the opaque-black pair specialization. Linear/radial/pattern samples
retain their shader route through the same shared boundary. Capture output uses
the pinned float unpremultiplication/store order. This does not migrate the
separately gated quad/convex/stroke geometry or close general layer composition.

Full spans retain one shared descriptor with pixel indices; fringe events retain
their order relative to that descriptor. The initial per-pixel full-span model
was rejected because it lost native-size `inv-050` Default work admission.
The compact model restores admission without raising limits or changing pixels.
Reservations precede allocation, cover old/new growth peaks and release on denial
or cancellation. Recording, indexing, overlap replay, copying and traversal are
charged; cancellation preserves its token during growth and replay. Ten focused
tests cover material arithmetic, compact spans, denial, growth peaks and cleanup.
Native-size `inv-050` still has a measured replay slowdown; retain its matching
before/after timing and work evidence and address that cost before release.

**Measured result.** All 12 original dispatch controls and the 12 new solid
material/background configurations are exact. Two blue triangles and a 768-path
all-byte-alpha/color grid are exact. Six shader controls, two ellipse controls and
the separately retained rectangle alpha/color grid remain known differences.
The latter guards the axis-aligned route; the polygon grid is a separate input
identity, not a replacement reference. Native solid/pattern ellipse captures
also disprove the old same-renderer identity assertion; both failures stay banked.

| Ford size | Changed pixels, before paint → window → current (transparent / white / black) | Transparent alpha error, before paint → window → current |
| --- | --- | --- |
| 250×100 | 592/576/469 → 268/268/230 → **222/222/185** | 2,842 → 1,945 → 1,457 |
| 500×200 | 1,200/1,186/945 → 550/548/476 → **433/432/364** | 6,761 → 5,092 → 3,176 |
| 1000×400 | 5,729/5,670/5,173 → 5,194/5,249/4,739 → **5,194/5,249/4,739** | 57,568 → 56,482 → 56,482 |

At the paint checkpoint, the retained cubic twin falls from 56 changed pixels /
120 alpha error to two / six; the cubic crescent and two quadratic controls also
improve. The scan-window repair makes all four exact. No compared changed
oracle-backed fixture/research case or Ford variant has a 0-to-255/255-to-0 alpha
flip. The result is not uniformly better: 15 circle/arc/curved-stroke cases gain
alpha error, including scaled-circle 373 → 374 changed pixels and 8,644 → 8,691
alpha error. Their new candidate identities and complete comparisons remain known
failures. The design record retains both directions; no tolerance or reference
regeneration is allowed. Group-opacity-white still differs on 16 pixels.

**Landed window boundary and remaining dependency.** The matched native source
uses path bounds for dispatch/storage and the actual clip for run walking. The
mask route additionally tightens its window to path bounds. The shared renderer
now separates these values for raw edges whose complete path/control bounds fit
inside the clip. Path-sized buffers and mask/run dispatch stay intact. All 18
independent phase/scale/window/material configurations are exact; five existing
research controls graduate. Unconditionally applying clip bounds regresses three
previously exact clipped rounded-rectangle backgrounds: native clips edges before
walking paths that cross the clip. Those paths retain the existing coverage-clip
route until clipped-edge construction is implemented and validated. This repair
establishes neither general clipping nor a leading Ford cause: all nine Ford
complete PNGs are unchanged.

The 713-case pre-bank comparison changes 22 PNGs: 13 known fixtures, five research
controls now exact and four Default inventory cases without matching oracle
evidence (`inv-009`, `inv-017`, `inv-032`, `inv-094`). All prior exact PNGs and
dispositions are preserved. Nine known fixtures gain small amounts of alpha
error; four improve. Their complete current comparisons and original references
remain banked, with zero opaque/hole flips in every changed oracle-backed case.
The design record retains both directions and the rejected unconditional model.

**Landed cubic admission boundary.** The native builder consumes every retained
cubic verb independently of how many chords a flattening would produce. Both
managed substitution paths incorrectly required at least two chords; they now
retain one-chord cubics too. Every cubic setup attempt, including an empty piece,
is charged. Existing complete-span reservations cover the extra edges; no buffers
or limits change. Two focused mask/run cases fail when the old gate is restored.
The independent shoulder changes from eight pixels / 228 alpha error to exact;
its fractional phase remains 48 pixels but improves from 278 to 50 alpha error.
Three paired translucent/background holdouts change from 14 pixels each to exact.
Stem controls remain known differences at 6/6/4 pixels, and exact neck/scaled
controls stay exact. The bank retains all eight exact and four failing controls.

Six Ford variants improve without opaque/hole flips; all three large PNGs stay
unchanged. The small/intended geometry has seven nonhorizontal one-chord cubics,
while the large geometry has only two horizontal one-chord cubics. The first
intended-size mismatch at (495,60) belongs to a two-chord tip and is unchanged.
This establishes a shared cubic fault, not an all-nine Ford repair or wordmark exit.

**Demonstrated next dependency: native clipped construction.** Compiled pinned
code matches the uncut tip's setup/advance at all three sizes and the scan/coverage
operations from identical prepared edges. Ford's first path nevertheless has a
control at Y=200.667 on the intended 200-pixel canvas, so the native builder clips
the entire path. Its clipper splits cubics at Y and X extrema, including this tip
inside the clip. The managed builder splits only at Y extrema. Replacing just
the tip with its two native clipped pieces changes diagnostic alpha at (495,60)
from 129 to the oracle's 128. Diagnostic native verb construction also produces the
oracle's 212 at (247,30), versus the renderer's 229, and 111/53 at large-size
(989/990,120), versus 114/66. These are measured construction witnesses, not a
landed renderer repair or full-image/all-nine evidence.

**Kernel foundation verified.** Typed clip bounds, float points and ordered
line/quad/cubic pieces now preserve native X/Y chopping, root search, projection
and winding in caller-owned scratch. All 160 compiled-native cases match: 159
inputs were declared before implementation, and one targeted root-search witness
was added and independently captured afterward. Disabling cubic X chopping fails
38 of the original 159 cases. Work is checked before numerical phases and root-search
iterations, and cancellation preserves its token. This internal kernel is not yet
on the renderer's path, so no existing pixels or resource ledgers change.

**Next concrete slice.** Integrate native clipped-edge preparation at the shared
mapped contour boundary, with an explicit clip context and preserved winding.
Construct X/Y-monotonic curve pieces and required clipped/projected edges before
walking; keep path storage/dispatch separate from the walk window. Reserve the
complete additional piece/growth storage and charge chopping, root finding,
construction and traversal with bounded cancellation before integration. Preserve
the current mask/run material operations across all four paint consumers.
The independent control-bounds/actual-clip, phase/scale and four-material families
were declared before the kernel and are now banked with unchanged browser references.
Verify those, the existing clipped rounded-rectangle return
witness, every exact fixture and all nine original Ford variants. The second Ford
path stays on native uncut construction; its sampled native verb-built operations
already match, so this dependency is not a claim to explain every remaining pixel.
Do not repeat the falsified global endpoint, float-cast or density changes;
enabling all intersection checks after paint leaves Ford and the 54 local controls
byte-identical. Clipped-edge construction is now the demonstrated row-4/S14
dependency of the active wordmark, with the rounded rectangle as an additional
return witness.

A bounded shared repair may land with independent mechanism-level improvement and
all-nine Ford nonregression. A claim to repair Ford requires improvement on all
nine. This replaces the previous requirement that any scan-window repair improve
Ford: the measured window dependency is real but neutral on that wordmark; the
cubic admission repair improves six variants. Neither
acceptance rule changes the zero-difference wordmark exit or S17 denominator.

The integrated slice must preserve all exact fixtures, output API parity, existing
admission and categorized failures, cancellation/ownership behavior and Default
resource limits. Record complete candidate/reference identities, total and
directional alpha errors and opaque/hole changes for every affected banked image.
Run full fixture/research and available inventory variants, with missing captures
and unmeasured variants listed explicitly. The current 92-file Default sweep is
not full independent-variant validation; those gaps cannot shrink a denominator.
Measure matching before/after tiny/Ford/dense/native-size time, managed allocation,
charged work and peaks; local timings do not replace representative throughput.

The wordmark exit remains zero differences in all nine Ford variants. S17 still
needs a frozen representative corporate cohort meeting 80% exact documents overall
and per corporate stratum, every counted document's variants exact and fresh
delivery evidence. Continue rows 4–5, then the demonstrated curve-aware stroke and
paint/clip/mask/layer dependencies in rows 6–7. After two experiments without a
mechanism-level improvement, record the failed invariant and next architectural
step in the design record instead of expanding the calibration census.

## 4. Findings and stage ledger

### Active findings

| ID | Current disposition and remaining closure |
| --- | --- |
| R26 | The 92 independent exports suffice for current implementation priorities. Freeze a representative corporate-mark/outlined-wordmark cohort and reconcile all declared variants; historical exact labels and Default render counts are not S17 rates. Preserve failures and original assets. |
| R27 | Every fixture is executed. Still retain complete decoded candidate identity/diff, validate historical/current oracle identity, distinguish render/reject from fidelity, and graduate positive research controls instead of deleting them. |
| R28 | Original pattern/root-map repair and inverse gradient maps retained. Paint/alpha one-step residuals remain S11/S13. |
| R30 | Shared contour preparation, ownership and coverage traversal have landed across the four paints. Preserve that boundary while changing curve state; remaining repeated binding/typed-state work follows demonstrated dependencies, not a repository rewrite. |
| R31 | CI artifact-validator hooks, Windows sandbox setup/health, wrapper deadline/drain and atomic publication are repaired. One opt-in corpus capture/compare/report workflow and representative performance evidence remain; reuse existing tools. |
| R33 | Original scaled clip-coordinate fault repaired; fractional mapped coverage and exact independent controls exist. Contained raw edges use a distinct scan window. Escaping control bounds trigger native clipped construction, even when the tip fits: this is demonstrated on Ford. Implement that boundary before wider window migration; general clip combinations remain open. |
| R34 | Shared scan conversion, analytic coverage, contact/union, straight-source sibling-stroke separation, operation/material preservation, contained-edge scan windows and one-chord cubic admission have landed. Native clipped construction is the next demonstrated Ford dependency; uncut tip recurrence and sampled existing-edge operations match compiled native code. Complex parity/contact cases and curved/dashed stroke unions retain their gates or known differences. Preserve the exact twin-ring repair; rows 4/6. |
| R35 | Retained controls, device-map threading, live non-convex cubic fills, curve/walk state separation and one-chord cubic admission have landed. The mapping discriminator confirms large-translation float precision but rejects the measured casts as a leading Ford repair. Fractional-event fidelity, quad/convex/stroke migration and wider scalable-instance evidence remain bounded follow-ups; row 5. |
| R36 | Cascaded cap/join/dash/width values are carried and many spelling twins pass. Finish shared typed state and practical inheritance without reopening repaired attribute/inline cases. |
| R37 | Fractional/scientific curves, root sizing, relative-moveto repairs, similarity maps and arc-free path-fill skew admission have landed. Preserve the support matrix's remaining arc/stroke/instance and general-transform gates; new admissions require the active target's dependency evidence. |
| R38 | Maximum scratch allocations substantially reduced; continue actual-size storage, ownership transfer and measured many-curve/wordmark allocation work. |
| R39 | Mapping/definition lifetime and shared preparation reservation repairs are retained. Audit each new curve-state buffer, growth peak and setup/advance/coverage cost before allocation or work; preserve during-phase cancellation and cleanup. Passing existing memory scenarios does not prove every new allocation bounded. |

### Completed original repairs: preserve their regressions

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
| S1–S5 | Core foundations and original repairs exist; preserve API, parser, memory/work and output contracts throughout. |
| S6 | Disposable single-use preparation exists; preserve resource leases and six-output behavior. |
| S7 | Generic local/stdin/HTTP supplied-font host exists; actual-pack and same-font metrics/pixels remain separate work under section 6. |
| S8 | 92 selected independent exports exist. Freeze and measure variants/cohorts; retain corporate marks separately from flags/symbols and calibration probes. |
| S9 | Styles/references/viewports and measured inventory admissions are implemented in explicit subsets. Remaining combinations follow independent corporate-cohort evidence and shared semantics. |
| S10 | Full path-command/affine kernels, retained controls, live non-convex cubics, curve/walk state separation and one-chord cubic admission exist. Fractional-event fidelity, remaining transform/instance gates and fallback migrations need per-input evidence. |
| S11 | Critical path: general Chrome-exact coverage/curves/composition unproved. Require exact held-out phase/scale/paint/alpha families. |
| S12 | Caps/joins/dashes/stroke outlines and the bounded row-1 repairs exist. Remaining curved/dashed union, tangent and antialiasing errors belong to row 6 using common coverage. |
| S13 | Gradient/pattern paint maps and premultiplied composition exist; R41 is closed. Bounded full-canvas paint routing is repaired in row 3; remaining coverage/paint alpha, dither, group composition and paint combinations need exact controls. |
| S14 | Rectangle clips, contained-edge scan windows, limited masks and isolated opacity exist. The clipping kernel is independently verified; renderer integration when path/control bounds escape the canvas or explicit clip remains open, including curves whose geometry fits. General clips, mask regions/content and bounded nested composition remain. |
| S15 | Live-text wordmarks are experimental, not required for outlined wordmarks. Follow section 6 and shared glyph contours, not a separate renderer. |
| S16 | Uniform embedded PNGs and narrow patterns exist. General raster decoding/resampling, multi-element patterns, markers, filters/blending and additional static CSS only for measured blockers. |
| S17 | Not achieved. Requires a frozen representative corporate cohort meeting at least 80% exact documents overall and per corporate stratum, every declared variant exact for counted documents and fresh delivery evidence. |

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

- Each implementation slice includes locked restore/Release build/tests,
  independent semantic/numeric/negative vectors and relevant documentation.
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

Keep original files, hashes, source/license evidence and generator provenance when
known. Never redraw/simplify hard logos, select easier variants after failure, or
reuse retired inv-033/inv-036 IDs. Public-domain candidates are not contributor
CC0 originals; preserve their provenance and keep private assets untracked.

Turn inventory declarations into explicit options per variant: logical viewport,
outputScale, resulting dimensions and background, with input and materialized-input
hashes if capture requires materialization. Changing screenshot dimensions alone
crops a fixed-size SVG image; it does not implement scaling. The current browser
transport's two-million-pixel limit is an oracle-tool constraint, not permission
to drop a declared large variant or change the renderer's pixel limit.

Report render/reject separately from exact/known-difference/oracle-failure and
unmeasured. A document counts only when **all declared variants** are measured
and exact. Preserve historical narrower successes without counting their unmeasured
sizes. Freeze a representative corporate-mark/outlined-wordmark cohort and hold-outs
from independently selected exports; retain flags, diagrams and other symbols as
separate useful strata. Keep every frozen document in its denominator, including
rejects, known differences, oracle failures and unmeasured variants. Fixture-entry
percentages and the whole 92-file inventory cannot replace corporate-document
coverage. Ford drives the active wordmark checkpoint; choose the curved-mark
checkpoint from the retained independent exports before tuning its pixels.

The S17 target is at least 80% exact documents overall and in each of the
corporate-mark/outlined-wordmark strata (round required document counts upward).
The representative corporate cohort and explicit variant recipe remain to be
frozen; existing renderer outcomes must not determine membership. This evidence
work does not block rows 1–7. Until the freeze and its measurements meet the
target, report measured results and gaps without a “most logos” claim or ETA.
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
