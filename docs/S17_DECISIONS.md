# S17 release criteria and decisions

Updated 2026-10-07. Bitmap-local uncut edge construction makes SG Rotation
Leipzig exact in all nine frozen variants on Windows and Linux. The cohort is
**24/27 exact documents**, marks **12/13**, wordmarks **12/14**, with 237 exact
and 21 known comparisons across 258 variants. Only its three large PNGs change;
all 234 earlier exact images and every other complete comparison remain.
The original numerical release criterion is met. The active fidelity target is
27/27 documents and 258/258 variants on both platforms; remote CI remains open.

Exact documents are Ford, SDL, UAAR, SimpleX, Omega, Eisenbahn, Rede Manchete,
ROC HCVS, Red Cross, SG Rotation Leipzig, Tokyo Tsukurou, Artsy, Party of Changes,
YouTube, Liputan6, NASA, Blur, Nike, Toyota, Pepsi, Target, IKEA, Google and Coca-Cola.
Membership, inputs, options and references remain frozen.
[PLAN.md](../PLAN.md) owns the current queue and validation totals;
[the design record](RASTERIZATION_DESIGN.md#contained-bitmap-edge-construction-2026-10-07)
explains the repair. Detailed diagnostics stay under ignored artifacts.

The three remaining failed documents stay in the denominator:

| Document | Failed variants | Current pixel difference |
| --- | ---: | --- |
| SimpleLogin (`inv-001`) | Nine | Four through 22 pixels; transparent variants include alpha errors up to 26. |
| Zeppelin (`inv-007`) | Three small | 26 RGB-only pixels, maximum channel error five. |
| IBM (`inv-099`) | Nine | 209 through 942 pixels; transparent alpha errors reach 23. |

These are measured residuals against unchanged references. Their mechanisms and
broader coverage remain implementation work; no pixel difference is waived.

The preceding renderer completed all 258 frozen recipes through the synchronous
owned-memory BenchmarkDotNet ShortRun. Intended-size medians across variant means
are 16.37 ms for marks and 10.52 ms for wordmarks, with median allocation traffic
1.46 and 1.00 MiB.
[The complete-cohort baseline](BENCHMARKS.md#frozen-corporate-cohort-baseline-2026-10-07)
records sizes, maxima, machine scope and confidence limits. This is a warmed
Windows measurement, with no speedup or service-latency claim. Fresh local
Windows and Linux build, tests, memory, package consumers, browser/font tools
and artifact consistency pass. All 258 Linux variants have independent fresh
reference triples: 774 stable, healthy, cleaned captures with no failures or gaps.
Remote CI is pending.

## Q1 - numeric exact-document target: selected

The measurement is fixed: the fraction of documents in the frozen representative
corporate cohort for which **every declared variant is measured and pixel-exact**
against its pinned oracle. The target is **at least 80% exact documents**, both
overall and within each corporate-mark/outlined-wordmark stratum. Round each
required document count upward. This is a release criterion, not a measured
success rate. The current frozen-cohort measurement meets this numerical
criterion, without establishing the remaining release requirements.

Rejects, known differences, oracle failures and unmeasured variants remain in
the frozen denominator and cannot enter the exact numerator. An exact fixture
entry percentage or a historical single-variant inventory tally cannot replace
this document measure. Report separate mark/outlined-wordmark strata so one
does not conceal the other; flags and calibration probes have their own counts.

## Q2 - representative corporate cohort and variant freeze

Use independently selected corporate marks and outlined wordmarks from the
retained inventory to define the cohort and hold-outs before tuning further
inputs. The whole 92-file inventory is not the corporate cohort: 57 headings
identify flags or flag badges/icons, and other entries are symbols/diagrams.
Keep those useful controls separate. Do not select documents based on a passing
candidate or remove hard documents after a failure.

The freeze records document IDs, source/input hashes, strata, logical viewports,
output scales, resulting dimensions, backgrounds and oracle refresh policy.
The [2026-10-05 manifest](../tests/References/inventory/corporate-cohort-v1.json)
now freezes 27 documents / 258 explicit recipes: every one of the 19 retained
brand/organization exports plus eight predeclared fresh evaluation holdouts.
Primary strata contain 13 marks and 14 outlined wordmarks, requiring at least
11 and 12 exact documents respectively, alongside the overall 22-document
minimum; both strata together require at least 23 exact documents. All cases of a
document must pass. Fifteen large cases exceeded the
original oracle transport cap at freeze. The 2026-10-06 bounded transport
extension supplies stable references for all fifteen, without changing variants.
The retained exports
were measured historically, so they are development inputs rather than unseen
holdouts. The new eight receive no document-specific tuning.

Only root dimensions, existing root-style sizing and an explicit added viewBox
for scaled viewBox-less variants may be materialized. Both renderer and browser
consume those viewBox-added variants; original source files and historical
references stay unchanged. Tests verify unchanged bytes outside the root start
tag and preserve all non-sizing root attributes. Fixed viewports and hashes
prevent a successful candidate from redefining its reference. See the
[inventory record](INVENTORY.md#frozen-corporate-cohort-2026-10-05) for the primary
classification, original source/license evidence and representativeness limits.

## Oracle compatibility remains implementation work

There is no quirk budget, pixel tolerance or decision to relabel mismatches exact.
Slanted-band/arc coverage, asymmetry, cap-tip slivers, tessellation/snap phase,
remaining paint routing and paint/dither/group-composition
residuals remain categorized implementation requirements for affected documents.
Closer agreement with ideal geometry does not establish pinned-browser equality.
A target below 100 percent can leave documents failing in the denominator; it
cannot waive their pixel errors or alter their references.

Ford is exact in all nine declared variants: 250×100, 500×200 and 1000×400 on
transparent/white/black. The final path-point density repair closes 10/10/7 changed
pixels, with transparent alpha error 16 → 0 and no opaque/hole flips. The six
prior exact PNGs preserve complete identity. Nine ordinary tests require exact
output against the original references and capture provenance under
`tests/References/inventory/`. This establishes the outlined-wordmark checkpoint;
it does not establish a representative cohort or general rendering fidelity.

The following clipped-preparation results are historical checkpoints; the current
path-point density baseline is summarized above and at the end of this record.

The 160 compiled-native inputs / 334 pieces remain the clipping-kernel evidence.
Shared preparation now counts clipped verbs in a charged stack-only pass, reserves
complete edge/list/row storage, then constructs them in a second charged pass.
Existing contact/retention gates and fractional paint coverage remain. Every prior
exact complete PNG and render/reject disposition is preserved. One of the 27
predeclared clip configurations graduates to exact; 26 remain known differences.
Two subsequent stroke witnesses were declared after implementation and before
capture: the fractional clipped band is exact, while the star remains inexact.
All six fresh captures are healthy and stable. The resulting 772-case bank has
613 fixtures, 67 research probes and 92 Default inventory cases. It does not
replace the corporate cohort or full independent variants.

Mixed results remain recorded: the quadratic clip phase's alpha error grows
4,012 → 4,242 and the dashed-cubic research probe grows 579 → 596. Eleven changed
Default inventory PNGs have no matching capture in that sweep. No reference was
regenerated from the candidate. Kernel, contour and output tests, memory/package
checks and local resource/timing ledgers prove this bounded slice's validation;
they do not close general coverage, quad/convex/stroke/layer fidelity, representative
throughput or Linux delivery. A later traced investigation attributes the first
large-size mismatch to the CPU raster tile construction clip: the existing shared
clipper matches the original row-6 tile interior when given the observed context.
Bounded contexts now reproduce the original row-6 interior through the converter
and preserve all six exact PNGs. At that checkpoint the first remaining event was (740,30); the subsequent
horizontal wrapper repair below closes it.
The updated bank executes 788 cases (629 fixtures, 67 research, 92 Default cases),
including 16 CPU controls with 48 healthy stable captures. Four are exact, eleven
retain differences, and the original two-child pattern remains unsupported.
Twenty-nine Default inventory outputs change without matching captures in that
sweep; their fidelity remains unmeasured. Fresh output/resource/cancellation tests,
25 memory scenarios and package/installed-consumer checks pass. Dense/native-stop
local timing improves in the first measured order, with higher total allocation;
this establishes no representative performance or Linux claim.
See [PLAN](../PLAN.md) and [the design record](RASTERIZATION_DESIGN.md).

Horizontal clip wrappers now preserve native pair forwarding as ordinary runs.
The 18 predeclared controls have 54 healthy stable captures: fifteen solid
configurations are exact, while three shaders retain differences. Seven fixture
failures and one original research probe graduate without replacing references.
At the horizontal-wrapper checkpoint the bank executes 806 cases (648 fixtures, 66 research, 92 Default cases).
All previous exact PNGs/dispositions hold. Twenty-six changed Default outputs
lack matching captures in that sweep; two quadratic clip controls gain three
alpha-error units each and remain failures. Ford still has six exact of nine,
with 2,835 transparent alpha error and zero opaque/hole flips. The next event at that checkpoint is
(375,269); the subsequent mapping investigation below closes it. The 3,198 tests, 25 memory and package/consumer checks
pass; this does not close the wordmark, cohort, Linux or performance gates.

Axis-aligned path mapping now follows 180 independent native float vectors. It
closes (375,269); toggling intersection dispatch on identical prepared edges does
not change that event. All 648 previous fixture PNGs, 66 research results, 522 exact
PNG identities and Default dispositions hold. Twelve fresh mapping controls have
36 healthy stable captures and are exact, producing an 818-case bank (660 fixtures,
66 research, 92 Default cases). Eleven changed Default PNGs lack matching captures;
their fidelity remains unmeasured. Six Ford variants remain exact; large variants
are 137/137/123 differences, with 2,734 transparent alpha error and no opaque/hole
flips. At that checkpoint, relative-coordinate rounding still had intended-size
counterexamples and the next event was (269,187).
All 3,202 tests, 25 memory and package/installed-consumer checks pass. Dense charged
work grows about six percent; local matching timings and peaks are retained.
This establishes no complete mapping, wordmark, cohort, Linux or performance exit.

The subsequent authoring/CTM discriminator explains that counterexample: float
relative arithmetic matches every recorded authored point, but the actual captured
transform differs from a normally parsed decimal cast to float. Injecting the
captured transform together with float-relative authoring preserves all six exact
PNGs and gives 10/10/7 large differences, transparent alpha error 16. This changes
the diagnostic input; it does not update production, the original-input baseline,
manifest counts or any release status. Small/intended CTMs are inferred from the
large recording. The next shared slice implements compatible numeric reading and
relative arithmetic together, with independent controls and the full existing
nonregression/delivery gates. See [the portable discriminator](../tests/References/inventory/inv-002-authoring-map-discriminator.json)
and [PLAN](../PLAN.md).

S17 additionally requires reproducible per-variant identities/diffs, categorized
residuals, representative latency/allocations and fresh Windows/Linux delivery
and platform-appropriate oracle evidence. Meeting a numeric rate alone does not
close those gates.

## Historical coupled numeric-reading checkpoint (2026-10-05)

The shared reader now reproduces the actual large CTM and all 436 captured authored
points from the original Ford input. All six exact complete PNGs are preserved;
large differences fall 137/137/123→10/10/7 and transparent alpha error 2,734→16,
without opaque/hole flips. The (269,187) witness is closed; ten transparent pixels
remain at Y=278–393, starting at (829,278). The historical injected input is not
used by production or the original-input regression tests.

Thirty-one independent numeric vectors and 90 stable healthy browser captures
support the repair. Thirty configurations retain 12 exact, 12 known differences
and six existing transform/instance/general-affine rejections. Multi-contour parity
and closing adjacency preserve `inv-040` admission and its previous PNG. Six
research probes graduate with original references. All 660 previous fixture PNGs,
all 534 previously exact PNGs and all Default dispositions remain unchanged.
The expanded bank executes 848 cases; 14 changed Default outputs lack matching
captures in that sweep, while Ford retains its separate nine-variant oracle bank.

Fresh local delivery/resource gates pass; matched measurements retain their local
scope in [BENCHMARKS](BENCHMARKS.md). Ford remains six exact of nine and cannot
enter an exact-document numerator. The first independent curved mark/wordmark,
frozen representative cohort, platform/performance and S17 exits remain open.

## Retained path-point density checkpoint (2026-10-05)

The charged cached raw count respects admitted retained spans and preserves chord
fallback. Clipped and uncut dispatches use the original path-point count against
the actual walk height. All ten native witnesses and all nine original Ford
references are exact. Restoring flattened-count dispatch fails the native witness
and three large variant tests. The 24 predeclared controls retain 15 exact, three
known differences and six UnsupportedFeature rejections, with 72 stable healthy
captures; original references and all prior exact PNGs/dispositions are preserved.
The bank executes 872 = 720 fixtures + 60 research + 92 Default cases. Four changed
Default PNGs have no matching oracle in that sweep. General event/mapping,
quad/convex/stroke/layer fidelity, the curved mark, representative cohort,
performance and Linux remain open; the 80% policy and denominators are unchanged.
