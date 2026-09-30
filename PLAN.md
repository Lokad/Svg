# Lokad.Svg — pixel-exact corporate-logo delivery plan

Reviewed 2026-09-28 against `8a9991e47ed8e897c3547bbf69fdf0fbeba6088f`.
This revision incorporates the previously uncommitted plan and supersedes its
stale queue. Detailed findings, fresh reproductions and verification are in
[the review](docs/REVIEW_2026_09_28.md). R01–R39 and S1–S17 retain their identities;
R40 records the calibration-evidence correction and R41 the degenerate-paint defect.

Execution priority updated 2026-09-29 after focused source inspection against
`8f6dc652`: finish/checkpoint the implementation owner's current bounded slice,
then concentrate on row 4's shared scan converter. The reviewed baseline below
remains historical; this priority update makes no new rendering or timing claim.

**Objective:** render most ordinary, static, self-contained corporate SVG logos,
including outlined wordmarks, pixel-exact against the pinned browser oracle.
“Most” means independently measured document coverage. It never permits pixel
tolerance. Preserve the pure-managed, dependency-free library contract.

The next work is implementation. There are enough existing assets and retained
counterexamples to proceed. The first product checkpoint is an independently
selected flat curved corporate mark and an outlined wordmark with counters,
each exact at every declared size/background; it is not the S17 release claim.

## 1. Current baseline and its limits

| Evidence at the reviewed revision | Assessment |
| --- | --- |
| Fresh locked restore / Release solution build | Nine projects, zero warnings/errors. |
| Fresh full tests | **2,602 passed**: 2,496 core, 100 visual-project, six FontProbe; zero failed/skipped. Test counts are not image counts. |
| Fresh memory and delivery checks | All 25 memory scenarios pass; runtime/symbol package and isolated installed-consumer checks pass. |
| Executed fixture manifest | **331 entries = 249 exact + 77 known-difference renders + five rejections**; 148 variants / 183 base entries. Synthetic coverage is not corporate-logo coverage. |
| Executed research manifest | **79 probes = 66 known-difference renders + 13 rejections**. Positive controls must be retained too; see R27. |
| Inventory | **92 independent files**. Fresh Default-options re-render: 46 render / 46 reject. Recorded amended-recipe dispositions: 27 labelled exact / 21 known-difference / 44 unsupported. These are different option sets. |
| All-variant evidence | Only inv-029, inv-034 and inv-053 have recorded completion of all declared small/intended/large × transparent/white/black combinations. The recorded 27/92 tally is not an all-variant rate. |
| Representativeness | 26 of the 27 exact-labelled entries are flags; the other is Bumps n01. No general corporate-logo or wordmark success rate is established. |
| Fresh independent probes | Mapped half-pixel polygon clip exact; overlapping stroke outlines overcount fringe coverage; degenerate objectBoundingBox gradient path stroke paints 24 opaque black pixels where Chrome paints none. Three stable captures each. |
| Not fresh evidence | Linux/remote CI, full inventory browser regeneration, BenchmarkDotNet throughput, private-font metrics and pixels. Do not imply those were rerun. |

Chrome remains **153.0.8010.52 win64, recipe 3**, with the existing executable hash
and Chromium revision field 1681091. Historical recipe-2 references stay identified
as such. The matching release-tag Skia pin is **f8b66b7597c4cc859d3ed190e9c6872241e6721c**;
the recent main-revision `8b8c3872` investigation does not supersede it (R40).

The tests deliberately lock known wrong pixels as well as successes. Returning
PNG, accepting a new spelling, a same-renderer twin, and matching a mismatch tuple
are not independent proof of exactness. The current API can return inexact output;
keep the preview's support/fidelity boundaries explicit without inventing a new
approximate tier or silently changing public behavior.

## 2. Execution decisions and ownership

1. **Use one implementation owner for the renderer queue.** The next agent taking
   implementation owns the active row through its tests and documentation. Work
   serially across `SvgConverter`, geometry lowering, reference resolution and
   shared style/mapping types. Do not split those files between concurrent writers.
   A second review may be read-only; delegation is not required by this plan.
2. **Check actual state, not historical ownership.** Before edits, inspect HEAD,
   status and live coordination. Committed work from a previous session is not
   a file lock. Preserve genuine concurrent/uncommitted edits; use a separate
   worktree for an experiment if necessary and coordinate only the overlapping
   slice. At review start the only tracked modification was the PLAN draft, now
   incorporated here. No product-code conflict was observed.
3. **Make row 4 the next sustained implementation priority.** Rows 1 and 2 are
   complete; preserve their repairs. Finish or leave a buildable checkpoint of
   the current bounded slice before changing direction. Implement spatial
   winding/parity and matched straight-edge scan conversion through the shared
   contour boundary, following the active work order below. Further individual
   shape/width/reference admissions wait until that checkpoint unless needed by
   the scanner or to repair a regression.
4. **Do not execute the old six-site floor256 switch.** It conflates coverage,
   byte-opacity multiplication and composition. Row 4 now owns the remaining
   coverage calibration, retaining row 3's translucent/composition controls.
   The release-tag source has been rechecked; extend that source trace only for
   the specific operation being implemented.
5. **Separate engineering from release decisions.** The numeric S17 coverage target
   and representative corporate cohort remain to be agreed before claiming “most”.
   They do not block defect repair, shared raster architecture, or the first
   outlined-wordmark checkpoint. No date or percentage is inferred from tests.

Make small, buildable commits with the regression, relevant support documentation
and actual validation. Update the current row/status in place. Detailed trials
belong in the existing design record; Git holds the implementation history.
Do not append another sequence of census-only or “all safe lanes exhausted”
entries. After two experiments without a mechanism-level improvement, record the
remaining uncertainty and choose the next architectural step. A real blocked slice
does not block independent repairs elsewhere in this queue: record the concrete
blocker and return condition before switching. An unresolved scanner hypothesis
alone is not a reason to resume a sequence of unrelated admission fixes.

## 3. Ordered implementation queue

Rows describe a sequence and dependencies, not one large rewrite. Finish each
bounded slice with a buildable commit. Re-evaluate inventory dispositions after
changes that affect them; do not start another broad sourcing campaign first.

| Order | Work and owner boundary | Exit evidence |
| --- | --- | --- |
| 1 | **Completed semantic repairs:** S12 miter threshold; R41 zero-area objectBoundingBox gradient path strokes. Preserve the two landed fixes and their witnesses. | Acute limit-1/default/limit-10 branch tests; degenerate path/line and userSpace controls; fresh retained oracle comparison, unchanged exact corpus, reviewed mismatch improvements. |
| 2 | **Completed shared contour boundary:** R30/R38/R39. Reuse the landed preparation, ownership and mapping boundary for all four paints; preserve reserve-before-allocation and explicit ownership transfer as traversal changes. | Behavior-preserving fixture/research results, six-output equality, denial/cancellation/disposal tests, measured tiny/many-curve and wordmark allocation behavior. |
| 3 | **Coverage calibration continues in row 4:** S11/R40. Keep mask quantization, paint alpha and composition separate; preserve the landed opaque coverage correction. | Opaque quarter/half/full coverage, translucent 127/128 controls, both axes, all paints, overlaps and backgrounds; every existing exact case remains exact. No bulk six-site rounding replacement. |
| 4 | **Active priority: shared scan conversion:** R34/S11. Start with straight-edged opaque fills; resolve spatial winding/parity and matched edge coverage, then route the four paints and stroke outlines through the common traversal. Replace full per-pixel fan scans. | Disjoint opposite contours, nested counters, crossings, touching/coincident edges, duplicate/overlapping strokes, held-out phases/slopes, charged work and during-phase cancellation. Both semantics and per-input oracle fidelity required; checkpoint below. |
| 5 | **Retained curves and final device mapping:** R35/R37/S10. Build on the landed double affine carrier. Retain verbs/controls through containers and use/symbol resolution, lower with the complete map and calibrate on row 4's raster path. | Equivalent direct/viewBox/instance encodings; fractional/scientific coordinates, rotations/skews/reflections/nonuniform scales; extrema/overshoot and scalable prepared scenes; flat curved mark and wordmark progress on existing independent exports. |
| 6 | **Curve-aware stroke completion:** S12. Preserve landed traversal, join, width and closed-seam controls. Use retained curves for dash-cut tangents, bounded conic handling and smooth-curve versus authored joins; finish remaining width cases through the shared kernel. | Retained dash-offset, seam, round-cap/join and rounded-rectangle cases, acute limits, zero-length/hairline cases, pathLength and non-scaling controls; no width admission based only on parser readiness. |
| 7 | **Measured practical-export blockers and bounded layers:** S9/S13/S14. Reuse shared geometry for diagonal/arbitrary clips and a bounded group-composition path for opacity/masks; complete needed styles/references and paint-alpha calibration. Prioritize inv-050, inv-085, inv-094 and other retained first blockers. | Independent exporter semantics; mixed fill/stroke/paint/use/clip/opacity across sizes/backgrounds; source-order and per-object binding correct; temporary-surface bounds and cleanup measured. |
| 8 | **Independent milestone and delivery:** S8/S17. Reconcile frozen variants and corporate strata continuously; claim the milestone only after exact all-variant results and fresh platform/package/resource evidence. | Exact outlined wordmarks and flat curved marks, agreed document target, categorized residuals, portable reproducible reports and consumer checks. |

### Active work order: shared scan conversion (2026-09-29)

This work order supersedes the next-step ordering in the 2026-09-28 row-4 design
note, while retaining its evidence and per-input oracle controls. The source
inspection confirms the shared mechanism; another broad source survey is not a
prerequisite. Implement independently in managed C# under the existing contract.

1. **Build a bounded straight-edge scanner.** Consume the existing device contour
   payload and fill rule. Maintain ordered active edges and signed crossings;
   determine nonzero/even-odd membership over spatial intervals before computing
   coverage. Handle edge endpoints, crossings and coincident/touching boundaries
   consistently. Start with opaque fills and retain exact rectangle/band paths;
   retained curves and a new scene framework are not prerequisites.
2. **Match the numerical construction at that boundary.** Use the pinned Skia
   edge setup, scan events and coverage/alpha operations to form explicit
   hypotheses. Check fractional phases, slopes and both axes against the browser.
   Exact continuous area or tiny-skia supersampling alone does not establish
   Chrome equality; paint opacity and source-over rounding remain separate.
3. **Integrate one traversal across paints and outlines.** Emit bounded coverage
   rows/spans for the existing solid, linear, radial and pattern paint evaluation.
   Scope migration by supported geometry invariants, never fixture identities.
   Stroke pieces within one paint operation need spatial membership; distinct
   SVG paint operations still composite in painter order. Do not substitute a
   maximum, clamped sum or global union for those separate semantics. Preserve
   each duplicate-stroke input's own reference, including Chrome's small delta.
4. **Measure and complete the slice.** Reserve edge, event and coverage storage
   before allocation, including growth peaks. Charge setup, sorting/intersections,
   edge advances, spans and painting; check cancellation during these phases and
   verify cleanup after denial. Use the existing harness for six-output parity,
   fixture/research comparison and the complete frozen variants required by
   section 5 for a broad coverage change. Retain exact controls and explain every
   changed known-difference image before updating its ratchet.

**Checkpoint:** review the first buildable integrated slice, or after 24–48 hours
of sustained work from starting this work order, whichever comes first. This is
an engineering decision checkpoint, not a deadline, release ETA or permission to
stop. Record the implementation commit, candidate identities and full comparisons
for existing overlap/duplicate, disjoint-opposite, counter, crossing/touching and
quarter/half/tenth-coverage probes. Declare held-out slopes/phases before tuning;
include translucent 127/128 and background controls when integrating paints.
Report semantic improvements separately from exact-pixel graduations and gaps.

Measure elapsed time, allocated bytes and charged work on the same before/after
inputs and options: a tiny mark, many-contour/wordmark cases and native-size
inv-050 (record default-limit denial explicitly). Use existing benchmark/resource
tools; lower ledger counts alone are not throughput evidence. A prototype may
remain experimental while failures persist; it is not an approximate product tier.
If failures remain at the checkpoint, record the specific unresolved mechanism
and the next implementation experiment. Do not refresh mismatch maxima or resume
unrelated admissions as a substitute for resolving that mechanism.

After this checkpoint, carry the common scanner through the remaining row-4 exit
evidence, then retain curves/final transforms (row 5), complete curve-aware strokes
(row 6), and reuse bounded layers for the measured clip/mask blockers (row 7).
Regression repairs and demonstrated dependencies may interrupt this order; record
their dependency and return to the active row when they are resolved.

### Row 1: bounded repairs that unblock implementation

`SvgStrokeOutline.EmitJoin` computes a half-normalized miter ratio but compares it
to the full limit. Normalize displacement to half-width consistently (or use the
equivalent correctly scaled comparison). The matched Skia source and the retained
acute witnesses agree on the missing factor. Keep inner/outer joins, limits at the
threshold, and diagonal coverage separate. A fix to the branch choice need not
make every antialiased spike exact to be a valid existing-behavior repair.

R41's reproducer is a horizontal **path**, `M2,8 H14`, with width-2 gradient stroke
and default objectBoundingBox units. Chrome paints none; Lokad.Svg paints black.
The equivalent `<line>` currently rejects, so a line-only regression misses it.
Fix paint applicability using original geometric bounds, not expanded stroke
bounds. Cover vertical paths, linear/radial paints, instancing and nondegenerate
userSpace controls. Do not lose this case because it is “semantic, not calibration”.

Status 2026-09-28: row 1 is complete in two commits. The miter comparison now tests the full displacement-to-half-width ratio (new limit-2 regression alongside the retained limit-1/limit-4 branches; acute research ratchet improved from 50 pixels at max error 255 to 46 at 36 as the sole mover in a 66-probe sweep). Degenerate objectBoundingBox linear/radial path strokes now paint nothing through a shared rasterizer guard on the original paint box, with horizontal, vertical, radial, use-instance and userSpace-band regressions retained. Post-fix degenerate candidates are fully transparent against the pinned empty oracle, and the full gate holds 2502 core plus 100 visual plus 6 font green with no exact-corpus change. The line-spelling rejection for objectBoundingBox gradient strokes is unchanged.

### Rows 2–4: shared coverage and ownership

Retain one owned contour payload with ends/topology and a typed paint binding.
Use one mapping/coverage path for solid, linear, radial and pattern fills/stroke
outlines. Preserve exact rectangle/band fast paths. Consolidate incrementally
around the defects; a new general scene framework or file-renaming campaign is
unnecessary. Do not add another geometry kind for each fill/stroke combination.

Reserve before allocating points, ends, roles, transformed arrays and growth
copies; include simultaneous old/new storage and transfer reservations with
ownership. Existing raster mapping reservations, definition cleanup, reduced
curve scratch and work charging are repairs to preserve, not tasks to redo.
Eliminate spelling-dependent straight-path sizing and repeated dash parsing as
these paths are touched. Pooling is optional and must account retained storage.

The R34 winding-role and nonzero crossing-split repairs help separated contours.
They do not implement general spatial membership. `FinishStrokeFill` currently
bypasses validation and lifts zero roles; summing unsigned areas can double a
shared fringe. The 2026-09-28 coincident-contour merge reduces the duplicate
diagonal-stroke probe to 50 changed pixels at max error 10, versus max error eight
for a single stroke; it does not establish pixel exactness. Crossing and partial
overlaps still sum pending spatial union. The browser itself
has a small single/duplicate delta, so compare each input to its own reference.
No fixture-specific deduplication or per-pixel correction is acceptable.

Separate membership from the matched antialiasing rule. Exact continuous area is
not Chrome analytic-AA; more precision or supersampling does not close the known
diagonal residuals. Use the matched release's active-edge/fixed-point construction
as a scoped investigation route. Account edge setup, ordering/intersections,
contour-vertex scans, spans, painting and cancellation; a contour-pair count alone
does not cover inner vertex scans. Preserve held-out failures and exact controls.

Status 2026-09-28: row 4 first slice complete for the coincident class. Bitwise-identical contours merge to first occurrence in both fill preparation sites before roles and reservations, with pair charging and per-vertex copy accounting; touching, nested, crossing, rotated and reversed contours keep existing combination. The duplicate probe falls to max error 10; the merge fires nowhere else. Full gate holds 2516 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass. Crossings and partial overlaps stay spatial-union work.

Status 2026-09-29: row 4 second slice complete for opaque mask quantization. Opaque paints truncate coverage times 256 (clamped) per the release-tag Skia source while translucent keeps its verified rounding; the 0.75 anchor moves 191 to 192 with 0.25/0.5/1.0 agreeing. Three quarter-edge probes graduate exact alongside the viewbox-mark-24 graduation (341 entries = 260 exact + 81 unsupported). Curve/stroke ratchets re-measured across the family, broadly improving at fringe level; structural residuals (circle-mark 145 to 147, ellipse-mark 149 to 144) ride the row-4 fixed-point model, not calibration. Full gate holds 2524 core plus 100 visual plus 6 font green; memory and package checks pass.

Status 2026-09-29: row 4 third slice complete for point-touch contour resolution. Touch-only sets subdivide edges at interior touch vertices with bitwise copies and split self-touching rings into lobes sharing one vertex, with a blocked split signal for shared-tip spikes, degenerate remainders and missing occurrences because the split-loop guard cannot see defects inside a single loop; fill and evenodd nesting skip bitwise-shared test vertices for strictly interior samples while stroke roles stay frozen. Full 92-document re-probe flips inv-008 to byte-exact and inv-045 to a disc-region known difference with inv-061 advancing past simplicity to closed paths and zero drift elsewhere. Full gate holds 2548 core plus 100 visual plus 6 font green; memory and package checks pass. Crossings, overlaps and multi-crossing stars stay planar-split work.

Status 2026-09-29: row 4 fourth slice complete for inter-loop planar crossing resolution. Phase-one intra-loop splits keep their paths; when the split-loop guard still sees strict crossings between loops under nonzero fill, a second sweep subdivides both edges at each computed crossing until none remains, each round strictly reducing the crossing count, while the unchanged guard still rejects touches, overlaps and degenerates and even-odd never enters the sweep; resolved sets skip bitwise-shared nesting vertices. Full 92-document re-probe flips inv-058 to a disc-region known difference with inv-044, inv-086 and inv-089 advancing to transform, work-limit and use-refs gates and zero drift elsewhere. Overlap summation wraps past full coverage on single-fill overlapping loops, queued as spatial-union work. Full gate holds 2556 core plus 100 visual plus 6 font green; memory and package checks pass. Mixed non-coincident sets, coincident overlaps, even-odd multi-crossings and stroke unions stay queued.

Status 2026-09-29: row 4 fifth slice complete for coincident-merge ordering. Bitwise-identical contours merge to first occurrence before classification instead of after splitting, where the CoincidentOverlap flag made the fill-path merge unreachable; the stroke path is unchanged. Full 92-document re-probe shows zero disposition drift with identical byte counts. Diagnosis: inv-001 path 0 keeps overlap plus touches between near-identical arc variants (overlap union work), and inv-026 group g56 fails on 1e-13 dust from unclosed-curve endpoints (proven tolerance work). Full gate holds 2557 core plus 100 visual plus 6 font green; memory and package checks pass. Coincident overlaps, even-odd multi-crossings and stroke unions stay queued.

Status 2026-09-29: row 4 diagnosis without product change. Parsed signs on the mixed crossing-plus-touch probe read 1,-1,1, proving the shared-pixel residual is opposite-role subtraction through the shared first vertex (Abs-masked elsewhere), not accumulator wrap; correcting the role cannot improve fidelity (clamp lands equally far on the other side), so spatial union stays the queued fix and the skip flag stays scoped. inv-050 advances past the stylesheet gate to the Default raster work limit at intrinsic 30in with explicit-size comparison ruled out by the viewport contract. Full gate holds 2557 core plus 100 visual plus 6 font green.

Status 2026-09-29: row 4 union-mapping probe work without product change. Separated same-fill diagonal overlaps already compose correctly through role-weighted fan-area summation with clamping (axis-aligned twins and diagonal pairs render byte-exact or within single-level fringe); only coincident partial-edge sharing and mixed touch/crossing sets reject at classification, confirming the queued spatial-union work sits in lowering-time contour surgery, not the rasterizer. One diagonal-overlap configuration graduates into the research corpus as a 1-pixel ratchet. Full gate holds 2635 core plus 100 visual plus 6 font green with no exact-corpus change besides the research admission; memory and package checks pass.

Status 2026-09-29: row 4 sixth slice complete for shared scan conversion. A bounded straight-edge membership scanner serves all four polygon raster loops with per-row direction-encoded crossings sweeping per-contour signed winding into interval totals (roles weight nonzero magnitudes, signs weight even-odd magnitudes, plain accumulates signed winding), mirroring the fan kernel combinations so evaluated pixels keep existing values bitwise; a parity sweep mistook even-winding lenses for outside and dropped one acute-apex miter pixel, now healed with a lens regression test. Full 92-document re-probe shows zero disposition drift with byte-identical renders against the clean HEAD worktree. Full gate holds 2644 core plus 100 visual plus 6 font green; memory and package checks pass. Coincident overlaps, even-odd multi-crossings and stroke unions stay queued.

Status 2026-09-30: row 4 probe work without product change. A faithful scratch port of the pinned-Skia convex+mask straight-edge walker fits the integral triangle exactly across mirror/rotated/16x16/shallow-apex/white/black oracle configs with zero mask-overflow violations, but fractional phases keep pure-alpha split-strip fringe residuals (fractional 10 px max 42, half-grid 10 px max 44, blue-apex 13 px max 64, slopes 5-11 px); no-snap, tie-down, inward-event, no-X-snap and offset variants all worsen, and even/mult X modes agree bit-for-bit on retained 256th phases. Next discriminators are sub-256th captures and the edges-walker port before any production traversal work. Sub-256th triples (2026-09-30) pin the truncation X path: x/y-shifted triangles reproduce the integral oracle bytes under both modes while the snap-boundary probe goes exact only under mult, and three discriminator probes graduate into the research corpus (86 entries). The edges-walker port for the non-convex family stays next before any production traversal work. The edges-walker port (2026-09-30) goes byte-exact on the retained bowtie and same-fill overlap oracles once the close-edge rule selects mask assign-vs-add, resolving the 1-ulp fringe signature; production traversal work carrying the pinned construction into the shared scanner stays next. A verbatim re-read corrects the fDY branch (inverse table only below full-slope 1024, exact division above): fractional and half-grid go exact and blue-apex/slopes fall to 5 tip-sliver pixels each. Tip-sliver analysis, then production traversal work carrying the pinned construction into the shared scanner, stays next. Per-pixel tracing addresses the frontier at 14 of 18 configs exact with 20 tip-sliver pixels open (all verified faithful to the checked-out construction); production traversal work stays next, motivated by product blue-apex 22 px max 24 vs port 5 px max 4.

Status 2026-09-30: row 4 production walker slice complete for single-contour fills (checkpoint). New module src/Lokad.Svg/Rendering/SvgAnalyticCoverage.cs (870 lines, 0 warnings) ports the pinned-Skia AAA walkers (convex fast path plus winding-interval edges walker, mask assign/add selection, trapezoid/triangle alpha ops, truncation X-setup, quarter SnapY, X-snap) into all four polygon raster loops in SvgConverter.cs, gated to single-contour fills (multi-contour keeps fan). Eligibility adds full path bounds to the walker, canvas-containment (roundOut inside canvas, mirroring the pinned clip-contained branch), and self-contact exclusion via SvgContourStructure.ClassifyContour. Ledger 263 to 375 with three fan-probe tests renamed to emits_analytic_rows_without_fan (assert 0); use-instance pin 250 to 251 (pixel 9,1 alpha 251). Twenty-five straight-edge research probes graduate to exact fixtures (triangles, switches, layer-opacity, diagonal strokes, polyline, sub-256th/snap discriminators); eighteen research tuples update (all counts improve except documented max wobbles on curve/stroke probes: quadratic 13 to 35, rounded-rect 71 to 89, round caps 9 to 17 and 18 to 21, row-5/row-6 owned); quantum-tenth-edge bbox 15 to 10 lateral shift recorded. Fixture ratchets: circle 147 to 84, ellipse 144 to 82, arc 86 to 57 (max 27 to 40), quadratic 74 to 54, cubic 93 to 38, plus rounded/dash families (counts improve; radial/pattern-circle max 27 to 255/40 wobbles documented). Viewbox-scaled-circle regresses 361 to 373 (max alpha 74 to 90) and is recorded with its ratchet as the queued narrowing experiment (vertex-count/source-kind gate to keep flattened curves on fan per the straight-edge-first work order) rather than a silent admission. Full gate holds 2644 core plus 100 visual plus 6 font green with zero warnings; memory and package checks pass with zero .svg in the package. Fixture corpus grows 342 to 367 (261 to 286 exact); research corpus 86 to 61. Follow-up 2026-09-30: vertex-capacity narrowing investigated and closed with no code change (both circles carry 32-vertex single-contour contained signatures yet lean opposite ways; per-pixel A/B shows position-dependent fringe-phase shifts inherent to the pinned snap quantization, same class as tip-sliver residuals; see design record), and the full 92-document diff is complete (INVENTORY batch 139: 73 render plus 19 reject both trees, zero flips, 26 known-difference byte drifts, tally unchanged). Follow-up 2026-09-30: sliver-sweep probe work with no product change (20 sliver probes across phases/axes/contourings plus corrected sub-quantum floor probes plus a straight-dash cut probe, all with fresh 3/3-stable triples). All 10 sliver singles render byte-exact through the convex walker; all twins diverge by fringe quanta through fan exactly where Chrome dispatches to its edges walker (230 vs 231, 179 vs 180, snapped rows 230 vs 255); floor slivers go 0 single-contour but 1 and 3 multi-contour against fan 0 and 2; global round-half-up is falsified by v-single-100. No CoverageAlpha or clamp change. Corpus: 12 singles graduate to exact fixtures; 10 twins, 2 floor-twins and the dash probe enter research with measured tuples. Next: productionize the multi-contour edges-walker with twin/floor/dash/bowtie/overlap/inv-010-column acceptance (2026-09-30 update: landed below; the dash -0.003 phase reading is superseded — the probe goes exact through multi-dispatch with dash measurement exonerated, so no row-6 dash-phase work follows from it); fixture manifest now 379 equals 298 exact plus 76 known-difference plus five rejections; research corpus 74 probes.

Status 2026-09-30: row 4 multi-contour landing complete for contact-free sets (checkpoint). Prepare admits disjoint-or-nested multi-contour fills through the existing winding-interval edges-walker with per-contour area/vote filtering and a set-level contact scan; the single-contour sequence, formulas and charges are verbatim, so ledgers and single-contour behavior hold bitwise. Touching, overlapping, self-crossing and degenerate sets keep the fan path. Thirteen research probes graduate to exact fixtures (10 sliver twins, 2 floor twins, dash cut); ring/circle/ellipse stroke families improve with documented fringe maxima; the R34 disjoint-opposite pin advances 23 to 38 against Chrome 37. Bowtie, overlap-same-fill and inv-010 hold by design.

Full gate holds 2661 core plus 100 visual plus 6 font green with zero warnings; memory and package checks pass. Fixture manifest now 392 equals 311 exact plus 76 known-difference plus five rejections; research corpus 61 probes.

Follow-up 2026-09-30: stacked-vertical merge fixed to consume on partial combine per the pinned SkEdgeBuilder (bd1f7bfa), with an explicit shared-vertex fan gate and two regression tests. The shared-vertex gate duplicates ClassifySet touch detection, verified by the new test passing on the clean HEAD tree, and stays as an explicit guard with dedicated quadratic charging and the twin pin 68 to 104. Full gate holds 2663 core plus 100 visual plus 6 font green with zero warnings. Memory and package checks pass with no svg in the package. Coincident overlaps, even-odd multi-crossings and stroke unions stay queued as spatial-union work.

Status 2026-09-30: row 4 coincident-union slice complete for inter-loop sharing (checkpoint). Lowering subdivides coincident edges at overlap endpoints with bitwise copies, drops loops contained in larger same-direction loops, and splices loop pairs sharing exactly one duplicate edge, all nonzero-only with the split-loop guard arbitrating and opposite-direction containment kept for holes. The two diagonal union probes render with fringe-level tuples (4 px max 1, 9 px max 11) proven pixel-transparent against authored outlines, so residuals ride the single-contour walker. One coincident lock repurposed to the union outline with two new union tests plus a hole-preservation lock. inv-010 holds its 56 px dash tuple through stroke space while inv-001 and inv-063 stay gated. Full gate holds 2666 core plus 100 visual plus 6 font green with zero warnings. Memory and package checks pass with no svg in the package. Multi-edge sharing and stroke unions stay queued.

Status 2026-09-30: row 4 nested-sharing slice complete for opposite-direction spans (checkpoint).

Status 2026-09-30: row 6 dash-cap slice complete for curve-sourced cuts (checkpoint). Dash runs carry cut contexts with a hasCurves gate routing curve contours through chordal-parabola end tangents into the landed tangent overload while polyline paths keep chord caps bitwise. The 512px control falls 782 to 678 with maxima 255 to 84; dash ratchets move with improved or equal maxima except ellipse-dash alpha 119 to 128. Round-cap tuples and offset-zero exact fixtures hold. Full gate holds 2673 core plus 100 visual plus 6 font green with zero warnings. Memory and package checks pass with no svg in the package. True tangents for non-uniform curvature stay queued behind retained-curve data. The split-loop guard exempts exact-duplicate spans between nested loops, keyed on boundary-inclusive containment rather than direction, since winding roles already encode holes, islands and top-level paints; adjacent sharing stays rejected for union surgery. Rectangular and diagonal shared-span holes render byte-exact with the hole lock flipped to assert rendering. Full gate holds 2666 core plus 100 visual plus 6 font green with zero warnings. Memory and package checks pass with no svg in the package. Fixture manifest now 400 equals 319 exact plus 76 known-difference plus five rejections. Multi-edge sharing and stroke unions stay queued.

For row 3, retain geometric coverage, mask alpha, paint alpha, premultiplied
intermediates and final straight RGBA as distinct operations. The opaque 0.75
observation (192 in Chrome versus 191) motivates a coverage change. It does not
authorize replacing byte-opacity/compositor rounding. Match translucent controls
(including alpha 127), backgrounds and isolated groups before widening it.
`SkScan_AAAPath` itself contains both shifts and rounded partial-alpha operations.
Never replace oracle references or automatically regenerate every mismatch tuple
to make an experimental rule green.

Status 2026-09-28: row 2 is complete in four commits. Contour preparation is shared through one payload, retention pre-reserves scene storage with ownership transfer on all four fill paths, denial and release are covered per paint plus crossing splits and stroke outlining, raster mapping/bounds/clip/coverage dispatch is shared through one owned payload with per-pixel paint evaluation left per loop, and six-output parity is pinned over a four-paint combo. Gates held green throughout with no exact-corpus change. Per-pixel paint unification is intentionally deferred: it would need hot-path polymorphism, and row 4 replaces the fan-scan traversal anyway.

Status 2026-09-28: row 3 byte-rule calibration is redirected, not completed. Tenth-coverage adjudication proved no fixed mask-plus-combination rule fits the pinned cells, and the structural coverage-versus-alpha separation already holds at the shared boundary from row 2; translucent composition agrees with Chrome, and the opaque mask question belongs to the row-4 fixed-point model below. No bulk rounding change was made. Row 4 now carries a scoped plan in the design record with the duplicate-stroke pair retained as its first acceptance probes.

### Rows 5–7: coordinates, curves, strokes and composition

Define user, object, viewport, instance, device and paint spaces explicitly.
`SvgAffineTransform` already carries finite doubles and composed transforms; its
landed admissions are recorded below. `SvgDevicePoint` can still precede the root
map. Finish the remaining consumers and retained-curve lowering through that
carrier, preserving explicit integer fast paths and updating consumers together.
Preserve original geometry bounds for paint and clip binding; stroke/clipped/layer
bounds have separate purposes.

Keep verbs and controls until the complete device transform is available, or prove
a conservative tolerance under every remaining map. Current direct-use/symbol
magnification gates are sound restrictions, not completion of scalable instances.
Preserve authored segment/close boundaries and curve tangents for stroke joins
and dash cuts. Resolve instances while geometry is still local; replacing baked
geometry and then inverting ancestor transforms must not become the general
retained-path design. Reuse existing preparation/ownership boundaries and migrate
one geometry family at a time.
Test root/nested viewBox, outputScale, use/symbol frames, collinear overshoot,
reflections, singular/nonfinite maps and nonuniform stroke transforms. Mathematical
flatness bounds and independent endpoint/tangent tests do not establish S11 pixels.

Status 2026-09-29: row 5 first slice complete for fractional axis-aligned transforms. The carrier widened to finite doubles with int-range result checks (bitwise-identical for integers; extents keep their long-range bound); translate/scale/matrix arguments parse as finite doubles while rotate/skew functions, zero scales and effective-width integer discipline stay gated with exact use-instance uniformity. Three inventory dispositions flip (inv-004, inv-006, inv-072 render with curve-family known differences, numbers in their entries, all oracles re-verified 3/3 byte-identical) and three advance with zero regressions; one message reworded plus seven new transform unit tests. Full gate holds 2537 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-29: row 5 second slice complete for the general affine carrier. SvgAffineTransform replaces AxisTransform at every carrier site with rightmost-first composition under int-range ComposeChecked discipline; rotate() with optional center plus skewX()/skewY() parse while general matrix() stays gated. Path/polygon/polyline vertices, uniform strokes and dashes, circles and use-instance placement map generally; rects, true ellipses, arcs, patterns, gradients, clips, masks and images keep honest axis gates, and the orthogonal band path routes rotated contours to the diagonal fill. Group snapshots record ancestor-identity plus viewport presence and reject transformed-source group uses instead of misrendering baked site transforms; shape definitions lower under the element-local transform so use clones exclude definition ancestors. Full 92-document re-probe flips inv-015, inv-060 and inv-070 to known-difference renders (numbers in their entries, all oracles re-verified 3/3 byte-identical) and advances inv-031, inv-044 and inv-057 with zero other drift. Full gate holds 2598 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-29: row 5 third slice complete for transformed group-use composition. Snapshots record the source-group ancestor transform; expansion un-bakes collected geometries through its inverse (exact for translations, fringe-level otherwise) so the outer instance transform applies to group-local space, with honest gates for viewport scopes, singular maps, minified flattened curves, rectangle strokes, glyphs and paint-server content. Shape definitions lower under identity-inherited context so the element attribute applies exactly once (fixing a double-application that rotated transformed definitions twice, caught by pixel-level bisection against direct composition). Full 92-document re-probe flips inv-031 and inv-044 to known-difference renders with zero other drift; the inv-031 use render is byte-identical to a directly composed twin. Full gate holds 2605 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-29: row 5 fourth slice complete for nested viewport threading. The explicit viewport threads through converter, prepared documents, root parser and traversal into nested-svg base resolution; percentage root bases resolve in parent user units (viewBox size when present, else explicit viewport or default intrinsic sides) with absolute roots and absolute nested geometry byte-identical to before. Explicitly-closed rendered leaves with element-free subtrees render like the self-closing form (consumed with main-loop accounting) while any child element keeps the unsupported flag, preserving the animation-content contract. Full 92-document re-probe flips inv-027 to a known-difference render with zero other drift. Full gate holds 2610 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-30: row 5 fifth slice complete for weld tolerance on mapping dust. Fill lowering welds consecutive device joints within 1e-9 Chebyshev to the earlier bitwise value (no computed points; bitwise-equal joints keep the zero-length path; tight re-reservation mirrors the merge path) before identical-merge and classification, healing unclosed-curve endpoints that agree in source but diverge by observed 1e-13 dust after the finite-double affine map. Full 92-document re-probe flips inv-002 and inv-026 to known-difference renders with zero other drift; the Ford letter-counter ProperCrossing and the g56 dust-debris split both resolve, revising the batch-126 fractional-band diagnosis to predicate dust. inv-001, inv-057, inv-065 and inv-082 keep true diagonal-simplicity rejections for spatial-union work. Full gate holds 2650 core plus 100 visual plus 6 font green with no exact-corpus change besides the two inventory graduations; memory and package checks pass.

Status 2026-09-30: row 7 eighth slice complete for use-element stroke binding. Stroke and stroke-width admitted on use elements; the presentation resolver binds solid scaling strokes (paint-server, dashed, stroke-opacity and pathLength uses stay honestly gated) with use-over-definition join/miter mechanics; definitions retain the miter limit for unstyled single-contour closed paths with uniform-1 transforms (lexical single-Z closure check; join/cap/dash/vector-effect/scale deviations stay honestly gated); expansion outlines placed boundaries through the shared kernel with roles mirrored from stroke-fill retention, coexisting with the placed fill. Symbol, group and paint-server uses with stroke stay honestly gated. Supporting repair: fill-override binding narrowed to the paint channel so stroke-style-only definitions (Kenya #A miterlimit) stay fill-overrideable with zero change to admitted paths. Full 92-document re-probe flips inv-068 to a known-difference render and advances inv-063 past use-stroke to the pennon coincident-overlap gate with zero other drift. Full gate holds 2655 core plus 100 visual plus 6 font green with no exact-corpus change besides the inventory graduation; memory and package checks pass.

Status 2026-09-29: row 6 fourth slice complete for unpainted-fill bypass on stroke-only paths. ParsePath resolves shape once up front and skips fill lowering with empty geometry when the fill is unpainted and no paint-server stroke, mask or clip needs it, so degenerate subpaths skip fill validation while stroke lowering still outlines every subpath; mask, clip, paint-server and magnification behavior preserved through the exclusions and the shared outline gates. Full 92-document re-probe advances inv-057 and inv-061 past closed-path fill validation with zero other drift. Full gate holds 2613 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-29: row 7 mask-region slice complete for custom regions on masked groups. Backward masks with custom regions take the deferred path; regions resolve into root-user space through the captured ancestor frame and ride scope commands into clip-discipline raster blending (out-of-region pixels skipped, edge coverage scaled like clip boxes); scope emission no longer depends on opacity alone while nested-opacity calibration stays gated for opacity scopes. Full 92-document re-probe advances inv-094 past custom regions to single-rectangle mask content with zero other drift. Full gate holds 2616 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-29: row 6 fifth slice complete for odd-integer stroke admission. ParseStrokeWidth drops the evenness gate on the shared outline kernel (double math throughout, no parity branches; circles and rectangles already admitted odd widths) while line/rect-band and paint-server band strokes keep even discipline (fractional kernel widths land in the seventh slice below). Full 92-document re-probe flips inv-028 to exact and inv-041 to a known difference with zero other drift; six odd-stroke research probes graduate with fringe-level tuples. Full gate holds 2615 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.
Status 2026-09-29: row 6 sixth slice complete for zero-length subpath tolerance. The orthogonal tokenizer admits single-point contours and skips zero-length edges while solid unmasked path fills drop sub-three-point subpaths, so degenerate subpaths skip fill validation while stroke lowering still outlines every subpath; the stroke kernel lowers fully-degenerate open centerlines to cap-determined dots (flatness-bounded discs for round caps, axis-aligned squares for square caps, nothing for butt caps) while dashed outlining discards zero-length runs before stroking, preserving the dash-round-cap admitted tuple. Full 92-document re-probe flips inv-009 to a dot-region known difference with zero other drift. Full gate holds 2626 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.
Status 2026-09-29: row 6 seventh slice complete for fractional stroke-width admission. ParseStrokeWidth parses positive finite bare/px doubles under the existing non-positive gate while line/rect-band, circle and paint-server band strokes keep integer discipline at their direct ParseInteger sites (circles land in the eighth slice below); SvgAdmittedStroke.Width plus the five kernel lowering signatures widen to double with all downstream kernel math already double-clean and near-integer snapping preserved. Full 92-document re-probe flips inv-090 and inv-013 to known differences with three width-gate advancements (inv-064 to the raster work limit, inv-065 and inv-082 to diagonal simplicity) and zero other drift; six fractional-stroke research probes graduate with fringe-level tuples. Full gate holds 2628 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.
Status 2026-09-29: row 6 eighth slice complete for fractional circle-stroke admission. SvgStroke.Width widens to double with ParseCircleStroke reusing the kernel width parser while rect/line bands keep integer discipline at their direct ParseInteger sites plus checked integral casts on the four rect-band probe lowers; ring, dashed-circle and paint-circle lowerings already double-clean. Full 92-document re-probe flips inv-032 to a symbol-region known difference with zero other drift. Full gate holds 2632 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-29: row 6 seam-slice probe work without product change. Rebuilding the uncommitted 2026-09-28 closed-loop dash-seam analysis as a committed reconstruction (16x16 closed rect path, dasharray 4 4, dashoffset 2.5, butt caps) shows the historical 4-pixel corner residual no longer reproduces: the seam-crossing merged run plus the offset-0 control both render byte-exact against fresh 3/3-stable triples, so the configuration graduates into the exact fixture corpus instead of a mismatch ratchet. Full gate holds 2635 core plus 100 visual plus 6 font green with no exact-corpus change besides the graduation; memory and package checks pass.

Round caps/joins need a bounded weighted-conic representation or justified
conversion: the current flattener supports only polynomial quadratics/cubics.
Preserve authored corners through curve subdivision so smooth rounded rectangles
do not gain joins at every tessellation vertex. Circle/ellipse dash traversal,
closed-run seam merging and cap/curve tessellation are separate defects; the
straight dash kernel has positive evidence worth retaining. Revisit odd/fractional
widths only after their quantum, overlap and geometry witnesses are resolved.

Status 2026-09-28: row 6 first slice complete. The negative-angle traversal experiment was reverted the same day: a 512px single-dash control proved it mirrors the browser (our dash north of 3 o-clock, Chrome south), and its ratchet deltas were seam-phase luck on non-integer-period circumferences. The landed fix rotates the sampled loop so the angle-zero origin leads, with an identical cyclic edge set that leaves fills and solid strokes untouched; dash measurement now starts at the path start like open contours. The 512px control falls from 33476 to 794 changed pixels with mid-ring dash angles exactly matching Chrome (0 to 91.75 solid over the nominal 256-unit true arc). Ratchets stand at research butt 77, offset 80, aperiodic 35, circle-single 21 and ellipse-single 22 with maxima at fringe level, and fixtures circle-dash 94, circle-dash-offset 94, ellipse-dash 77 and ellipse-dash-offset 75. Full gate holds 2516 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass. Dash-end cap orientation and seam merging remain open.

Status 2026-09-28: dash placement is done; the residue is cap orientation. An exact sampler mirror plus pattern-walk replay proves dash cuts land within 0.1px of true arc while butt caps emit perpendicular to terminal chords (half-step tilt) against tangent-perpendicular Chrome cuts, which no polyline-level fix can reorient; the design record scopes the curve-aware cap slice with the 512px control as acceptance and no kernel or densification hack.

Status 2026-09-28: open-stroke stroking landed as a quad-plus-wedge rewrite. Pixel-complete reconstruction proves the browser unions per-segment butt quads with outer join wedges while the joined-polygon inner miter vertex carves the notch at full-pixel strength; an inner-butt single contour was tried and reverted the same day (cap-row diagonal cut, closed-hole regression). Miter, bevel and round open joins are done: concave sides close butt on right turns within ninety degrees with outer tip ears, round fans moved outer with a thirty-degree chord cap, and outline capacities cover the ear triples. Closed loops, spikes and caps keep their paths. The closed-loop dash-offset probe graduates exact (338th fixture, 256 exact); the full gate holds with no exact-corpus change; memory and package checks pass.

Continue from cascaded stroke values already present in `SvgComputedStyle`;
replace repeated raw-string interpretation with shared typed computed state.
Exercise attributes, inline/embedded CSS, important/default overrides, inheritance
and references with independent expected behavior. Required forward resources,
per-instance paint/clip binding, stylesheet location and harmless export metadata
belong to S9. Keep DTD/external entity/resource retrieval disabled.

R33 now maps clip boxes and applies fractional coverage on all four polygon paths;
the new half-pixel probe is exact. Preserve that behavior. Arbitrary clip paths,
clip-child union versus nested intersection, clip rules/units and use transforms
remain S14. inv-085's diagonal clip is not a rectangle-union shortcut. Status 2026-09-28: clip application scoped (ghost composite proves triangle-union geometry and part correctness with all 5762 residual pixels in the diagonal-AA family; needs contour intersection with union semantics from row 4; expected Czech-like known-difference render). Keep group
opacity isolated, support nested layers, crop surfaces with correct origins and
bounds, then implement the mask regions/content evidenced by inv-094. Constant
mask opacity is not general alpha/luminance masking. Status 2026-09-30: row 7 triangle-clip slice complete for single convex path content (leaf polygon intersection at resolve time with rect-like paths as boxes; inv-061 graduates known-difference at 4159 px max 29 with stroke-fringe residuals; multi-contour, groups, use narrowing and z-continuation stay queued).

Use one bounded isolated-layer operation for the required group effects: compute
conservative device bounds, retain the surface origin, render children, apply
clip/mask operations in the required order, then composite with group opacity.
Reuse normal geometry/paint traversal for mask content. Derive antialiasing bounds
from the admitted kernel rather than importing resvg's padding constant. Measure
peak live surfaces and work for nested groups; preserve object-space paint bounds
separately from layer bounds and validate clip-edge pixels against Chrome.

Paint work must share geometry. Keep corrected inverse paint maps and backward
cross-type gradient inheritance. Transparent-stop unpremultiplication, gradient
opacity/dither ordering, forward inheritance and combined paints need independent
evidence. Retain degenerate bounds/vector rules, stop ordering, spread and object
versus user-space semantics; replace arbitrary stop caps with configured bounds
only where measured inputs require it.

Status 2026-09-29: row 7 first slice complete for general fractional rect-coordinate admission. ParseRectangleCoordinate drops the quarter-pixel gate (one line) after a consumer audit proving the rectangle rasterizer evaluates exact analytic fractional coverage and all other consumers ride general-double backends with downstream gates preserved; fractional transform args and stroke widths stay on their integer carriers for rows 5 and 6. Exactly one inventory disposition flips (inv-042 renders with a rounded-region known difference, numbers in its entry, oracle triple re-verified 3/3 byte-identical); four locked rejection tests repurposed deliberately. Full gate holds 2524 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-29: row 7 second slice complete for rect-stroke fractional geometry and odd widths. ParseRectangleStroke drops the integer-geometry and even-width gates (two lines plus honest messages) after verifying the four-band tiling is exact and shared gradient/pattern probes ride the same bands; transform-identity, positivity, size-bound, dasharray-none and scaled-viewport gates preserved, and general outline-kernel odd widths stay row-6 work. Exactly one inventory disposition flips (inv-074 renders with a tick-curve known difference, numbers in its entry, oracle triple re-verified 3/3 byte-identical, per-pixel map proving box bands exact); one locked rejection test repurposed plus four new rect-stroke unit tests. Full gate holds 2528 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-29: row 7 third slice complete for deferred gradient href resolution. Missing-target inheritances stash as budgeted pending entries with exact consultation conditions (every currently-resolving input takes byte-identical paths); reentrancy-guarded eager drains plus defs-end and document-end drains resolve chains in dependency order while leftover targets throw the original registration error. Exactly one inventory disposition flips (inv-035 renders with a radial-glow known difference, numbers in its entry, oracle triple re-verified 3/3 byte-identical under the explicit-viewport amended recipe); two locked rejection tests repurposed plus two new tests for chains and cycles. Full gate holds 2530 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

Status 2026-09-29: row 7 fourth slice complete for editor namedview tolerance. Non-empty foreign namedview subtrees skip when every descendant stays outside the SVG namespace, with main-loop-mirrored structural limits, node budget, work, cancellation and symbol-depth rules; unknown foreign elements keep the unsupported flag by name allowlist, preserving the public-contract foreign-rect lock. Full 92-document re-probe flips inv-016 to a fractional-width known difference with zero drift elsewhere. Full gate holds 2562 core plus 100 visual plus 6 font green; memory and package checks pass. Wider editor vocabularies and text-paired metadata stay queued.

Status 2026-09-29: row 7 fifth slice complete for nested group-use composition. Identifier-bearing groups record scene geometry ranges at close over the append-only scene; group uses expand recursively inside-out with cycle rejection, staged references and slot-keyed extra indices; group opacity, clip and mask scopes stay honestly gated, and group-nested definition leaves register under the true-group depth rule. Full 92-document re-probe flips inv-089 to a star-edge known difference with zero drift elsewhere. Full gate holds 2570 core plus 100 visual plus 6 font green; memory and package checks pass. Non-group targets and scoped groups stay queued.

Status 2026-09-29: row 7 sixth slice complete for percentage root dimensions. SvgRootLength carries absolute pixels or viewport percentage from attributes and inline styles with CSS priority; percentages resolve against the explicit viewport or the default intrinsic size while nested, use and font bases keep honest nulls; display block paints like inline and stylesheet type/id attributes are tolerated. Full 92-document re-probe advances inv-002 past dimensions to polygon simplicity and inv-027 past style width to nested-viewport bases with zero other drift. A computed-style cascade variant was reverted the same day after polluting record equality. Full gate holds 2580 core plus 100 visual plus 6 font green; memory and package checks pass. Viewport threading and true-group style width stay queued.

Status 2026-09-29: row 7 seventh slice complete for direct-child clipPath routing. Definition traversal routes identifier-bearing direct-child clipPath elements through the same policy, content and registration path as defs-nested ones, mirroring shape definitions, while group-nested and anonymous clipPaths keep existing behavior and non-rectangle content stays gated behind contour-intersection union work. Full 92-document re-probe advances inv-085 past defined references to path content with zero other drift. Full gate holds 2635 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass.

## 4. Findings and stage ledger

### Active findings

| ID | Current disposition and remaining closure |
| --- | --- |
| R26 | Inventory sourcing is substantially complete for prioritization. Reconcile all-variant records and corporate representativeness; 27/92 is not S17 evidence. Preserve failures and original assets. |
| R27 | Every fixture is executed. Still retain complete decoded candidate identity/diff, validate historical/current oracle identity, distinguish render/reject from fidelity, and graduate positive research controls instead of deleting them. |
| R28 | Original pattern/root-map repair and inverse gradient maps retained. Paint/alpha one-step residuals remain S11/S13. |
| R30 | Four contour/paint owners and raster loops, string-based computed values and repeated binding remain. Bounded consolidation is row 2, not a repository rewrite. |
| R31 | CI artifact-validator hooks, Windows sandbox setup/health, wrapper deadline/drain and atomic publication are repaired. One opt-in corpus capture/compare/report workflow and representative performance evidence remain; reuse existing tools. |
| R33 | Original scaled clip-coordinate fault repaired; fractional mapped coverage present and one fresh independent case exact. Wider phases/scales/paints and path clips remain validation/extension work. |
| R34 | Disjoint orientation roles, bounded nonzero crossing splits and stroke-role repair landed. Inter-loop planar crossings admitted (pentagram, star/crescent families); coincident merge ordered before classification. Mixed non-coincident touch/crossing sets, coincident overlaps, complex evenodd geometry and spatial stroke unions remain open; row 4. |
| R35 | Root/output tolerance propagation, overshoot and unsafe magnification gates landed. Complete per-instance/device curve lowering is still architectural; row 5. |
| R36 | Cascaded cap/join/dash/width values are carried and many spelling twins pass. Finish shared typed state and practical inheritance without reopening repaired attribute/inline cases. |
| R37 | Fractional/scientific curved paths, fractional root sizing, percentage root dimensions and both relative-moveto repairs landed. General public affine transforms, restricted numeric grids and layered exporter gates remain. |
| R38 | Maximum scratch allocations substantially reduced; continue actual-size storage, ownership transfer and measured many-curve/wordmark allocation work. |
| R39 | Raster-map reservation, vertex-weighted raster work and definition lifetime repairs landed. Retained arrays still allocate before scene reservation; nested scan cost and new spatial work require charging. |
| R40 — new | Correct source provenance and narrow the quantization work order. Release-tag pin reverified in this review; later design/inventory claims still need correction with the next calibration slice. |
| R41 — new | Accepted degenerate objectBoundingBox gradient path stroke paints black instead of nothing. Fresh retained reproduction in the review; row 1. |

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

### Stage completion criteria

| Stage | State / remaining exit |
| --- | --- |
| S1–S5 | Core foundations and original repairs exist; preserve API, parser, memory/work and output contracts throughout. |
| S6 | Disposable single-use preparation exists; preserve resource leases and six-output behavior. |
| S7 | Generic local/stdin/HTTP supplied-font host exists; actual-pack and same-font metrics/pixels remain separate work under section 6. |
| S8 | 92 selected independent exports exist. Freeze and measure variants/cohorts; retain corporate marks separately from flags/symbols and calibration probes. |
| S9 | Narrow styles/references/viewports implemented; complete the actual exporter blockers through shared semantics. |
| S10 | Full path-command/affine kernels and fractional curves exist. Public transforms, topology and final device lowering remain incomplete. |
| S11 | Critical path: general Chrome-exact coverage/curves/composition unproved. Require exact held-out phase/scale/paint/alpha families. |
| S12 | Caps/joins/dashes/stroke outlines implemented in narrow subsets with retained errors. Finish rows 1 and 6 using common coverage. |
| S13 | Limited gradients/patterns and paint maps exist. R41, transparent/opacity calibration and required inheritance/combinations remain. |
| S14 | Rectangle clips, limited masks and isolated opacity exist. General clips, mask regions/content and bounded nested composition remain. |
| S15 | Live-text wordmarks are experimental, not required for outlined wordmarks. Follow section 6 and shared glyph contours, not a separate renderer. |
| S16 | Uniform embedded PNGs and narrow patterns exist. General raster decoding/resampling, multi-element patterns, markers, filters/blending and additional static CSS only for measured blockers. |
| S17 | Not achieved. Requires a frozen representative corporate cohort, agreed exact-document target, every declared variant exact for counted documents and fresh delivery evidence. |

## 5. Product contract, evidence and completion

Keep one MIT pure-managed C# net10.0 library for Windows/Linux: no third-party
runtime packages, native assets/PInvoke, System.Drawing, installed-font engine,
browser rendering service or other Lokad runtime dependency. Rendering consumes
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
| Renderer-accounted live storage | 256 MiB; not process/host memory. R38/R39 exceptions must be closed. |
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
- Preserve exact controls. This plan supersedes RESEARCH_PROBES.md's instruction
  to delete exact renders: graduate useful original cases into the exact fixture
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
  transform or composition changes require the complete frozen variant set.
  Record missing/oracle-failure/unmeasured results explicitly. Ordinary unit tests
  and library consumers remain offline and independent of browsers/private assets.
- Package/API changes require the isolated installed consumer; release claims
  require fresh Windows/Linux build/test/package/resource and platform-appropriate
  oracle evidence. A Windows reference is not automatic Linux fidelity evidence.

### S8/S17 accounting and remaining user decision

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
and exact. Reconcile the current 27 exact labels without erasing their narrower
successes or their unmeasured sizes. Freeze a corporate-mark/outlined-wordmark
cohort and hold-outs; retain flags and other symbols as separate useful strata.
Use existing inv-019/inv-040/inv-066 and independently chosen curved marks to drive
the first checkpoint, not additions optimized for the current renderer.

The user still needs to choose the numeric S17 exact-document coverage target
against that frozen cohort before a release claim. Until then, report measured
results and gaps without a “most logos” claim or ETA. This decision does not hold
rows 1–7. S17 also requires categorized residuals, reproducible oracle evidence,
representative latency/allocations and the fresh delivery/platform gates above.

Maintain API_CONTRACT, README and SUPPORT_MATRIX as current contracts. In
particular, reconcile stale fractional-dimension rejection prose and synthetic
counts. PLAN is the execution queue; INVENTORY holds document evidence,
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
| Case-insensitive requested/resolved family matching; italic match, closest 400/700 weight, then deterministic family/path/face tie-breaks; ordered configured fallback. | Existing Svg selection follows that order and accepts numeric SVG weights. Independent
conformance vectors now cover aliases, four real styles, fallback (`b0d4d5d`),
plus unused collection entries that parse and select without poisoning the catalog while
snapshots reject selected collection faces before acquisition. OoxPdf's bold request maps to 700; preserve SVG's numeric weight semantics. |
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
Keep `external/` optional, ignored and unreferenced: no submodule, build/test input,
runtime load, CI download, vendored/translated implementation or hidden Rust/C++/
Node dependency. Do not modify another worker's reference checkout to test its
absence. Ordinary build/test/package/consumer workflows need no sibling checkout.

| Inspected source / revision | Bounded use |
| --- | --- |
| resvg `75b6bbadd7999d0516dcd7153b4a321bfdf8670a` | `usvg` style/tree, use/clip/paint binding and renderer operation order; typed resolved state and distinct geometry/stroke/layer bounds. |
| tiny-skia `5d4754777746eef0828be166896eaf482c49f8f2` | Edge/span organization, compact paths, stroke/dash expansion and storage reuse. Its fourfold supersampling is not Chrome-exact analytic AA. |
| resvg-test-suite `d8e064337faf01bc5a9579187a56dbdbe3eacc72` | Semantic counterexample index. It overlaps heavily with resvg's bundled suite; neither is a separate real-logo cohort or our PNG oracle. |
| NanoSVG `239e102ec2c691f2902e20ace2ed36ee4a35cfe6` | Secondary small-parser/scanline comparison only; five-subsample output is not the exactness target. |
| Matched Skia `f8b66b7597c4cc859d3ed190e9c6872241e6721c` | Release-tag `SkScan_AAAPath`, blitter/draw routing, stroker and curve conversion for a specific S11/S12 hypothesis. Relevant AAA/stroker routines were freshly read in this review. |

The shelf's current Skia checkout `8b8c3872fbc03f025855db96ce683f34ec98a815`
is a main-revision comparison, not the browser release pin. Use explicit matched
source reads without changing it implicitly. resvg's registry tiny-skia dependency
also need not be byte-identical to the sibling checkout despite equal version text.

Focused source map for the active work order (inspected 2026-09-29; paths are
relative to each optional checkout and are study references only):

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
  within the existing C# ownership model rather than translating implementations.
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
failures, strict comparison and bounds. Keep upstream notices with optional
checkouts; bundled fonts/assets have separate licenses. Source access authorizes
study, not product vendoring or publication of private assets.

Use the existing [oracle tooling](docs/ORACLE.md),
[research record](docs/RASTERIZATION_DESIGN.md),
[comparison tool](docs/VISUAL_COMPARISON.md),
[inventory](docs/INVENTORY.md) and [benchmarks](docs/BENCHMARKS.md).
Do not build Chromium, fetch a renderer ecosystem or survey more reference
implementations as a prerequisite to the concrete queue.


