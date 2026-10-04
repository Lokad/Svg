# S17 release criteria and decisions

Updated 2026-10-05: reviewed baseline `f633c86b`; current one-chord cubic checkpoint
follows operation-preserving paint (`c9f0eea2`) and scan windows (`9e77c0fa`).
[PLAN.md](../PLAN.md) is the current execution queue. Rows 3–7 still have
implementation and fidelity exits; the earlier claim that rows 1–7 were complete
within architectural limits is withdrawn. The first exact curved corporate mark
and outlined wordmark checkpoint, and S17 itself, remain unachieved.

Current validated evidence: 584 fixture entries = 493 exact + 86 known-difference
renders + five rejections across 355 base documents and 229 variants; 67 research
probes = 66 known-difference renders + one rejection. Fresh Windows locked restore,
Release build and 2,882 core + 105 visual + six FontProbe tests pass. All 25 memory
scenarios, package inspection and isolated installed-consumer checks pass at the
one-chord cubic checkpoint. The Default inventory retains 88 renders +
four MalformedInput rejections out of 92. These counts establish neither corporate
coverage, full independent-variant validation nor Linux delivery.

## Q1 - numeric exact-document target: selected

The measurement is fixed: the fraction of documents in the frozen representative
corporate cohort for which **every declared variant is measured and pixel-exact**
against its pinned oracle. The target is **at least 80% exact documents**, both
overall and within each corporate-mark/outlined-wordmark stratum. Round each
required document count upward. This is a release criterion, not a measured
success rate; the current evidence does not meet it.

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
Every declared small/intended/large × transparent/white/black combination needs
an explicit recipe. Missing captures and transport-limit failures remain gaps.
This release freeze is not a prerequisite to the bounded Ford renderer slice.

## Oracle compatibility remains implementation work

There is no quirk budget, pixel tolerance or decision to relabel mismatches exact.
Slanted-band/arc coverage, asymmetry, cap-tip slivers, tessellation/snap phase,
remaining paint routing and paint/dither/group-composition
residuals remain categorized implementation requirements for affected documents.
Closer agreement with ideal geometry does not establish pinned-browser equality.
A target below 100 percent can leave documents failing in the denominator; it
cannot waive their pixel errors or alter their references.

Ford remains zero exact of nine recorded variants. It uses opaque solid cubic
fills without stroke, dash or gradients. Curve/walk state separation and bounded
full-canvas paint routing are retained; rejected endpoint and float-cast variants
remain falsifications in the design record. Operation-preserving mask/run paint,
material quantization and float capture conversion now improve all nine Ford
variants: intended-transparent falls from 1,200 to 550 differing pixels and 6,761
to 5,092 total alpha error. All previous exact fixture PNGs are preserved. The 12
original dispatch controls are exact; 24 further material configurations retain
15 exact and nine known differences. Compact full spans preserve native-size
Default admission without raising limits. Some circle/stroke alpha errors worsen
and remain recorded failures; group-opacity-white remains inexact. Scan-window
separation now makes all 18 independent controls exact and graduates five research
controls, preserving all 462 prior exact PNGs. All nine Ford PNGs stay unchanged.
Paths crossing clips retain their earlier route until native clipped-edge
construction is implemented; unconditional clip-window migration is rejected.
Retained one-chord cubics now enter native stepping instead of straight-line
fallback. This improves six small/intended Ford variants, preserves all three
large PNGs and every prior fixture/research PNG, and leaves Ford zero exact of nine.
Intended-transparent changes from 550 pixels / 5,092 alpha error to 433 / 3,176;
small-transparent changes from 268 / 1,945 to 222 / 1,457. Twelve independent
configurations retain eight exact and four known differences, with healthy stable
capture triples. Nine were declared before implementation; the three paired-material
holdouts were declared after the first trial and before their independent captures.
The first Ford tip is a two-chord cubic, so its remaining event error is not
explained by this admission repair. Eleven Default inventory PNGs change; the ten
other than separately measured Ford lack matching fidelity evidence.
The next wordmark slice is the original first tip's cubic setup/advance state.
A bounded shared repair may land with independent progress and Ford nonregression; a Ford-repair
claim requires all-nine progress and its exit remains all-nine exact. See PLAN and
[the design record](RASTERIZATION_DESIGN.md) for evidence and remaining limits.

S17 additionally requires reproducible per-variant identities/diffs, categorized
residuals, representative latency/allocations and fresh Windows/Linux delivery
and platform-appropriate oracle evidence. Meeting a numeric rate alone does not
close those gates.
