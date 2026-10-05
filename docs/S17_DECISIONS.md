# S17 release criteria and decisions

Updated 2026-10-05: shared clipped-edge preparation follows the independently
verified kernel (`57c6a7e3`), retained one-chord cubics, operation-preserving paint
and contained-edge scan windows. Six small/intended Ford variants are exact;
the three large variants improve but remain known differences.
[PLAN.md](../PLAN.md) is the current execution queue. Rows 3–7 still have
implementation and fidelity exits; the earlier claim that rows 1–7 were complete
within architectural limits is withdrawn. The first exact curved corporate mark
and outlined wordmark checkpoint, and S17 itself, remain unachieved.

Current validated evidence: 613 fixture entries = 495 exact + 113 known-difference
renders + five rejections across 362 base documents and 251 variants; 67 research
probes = 66 known-difference renders + one rejection. Fresh Windows locked restore,
Release build and 3,054 core + 115 visual + six FontProbe tests pass. All 25 memory
scenarios, package inspection and isolated installed-consumer checks pass at the
clipped-preparation checkpoint. The Default inventory retains 88 renders +
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

Ford is now exact in all six small/intended variants: 250×100 and 500×200 on
transparent/white/black. Shared clip-built construction improves the three large
1000×400 variants to 4,900/4,956/4,404 differing pixels. Large-transparent retains
51,395 total alpha error, max channel/alpha error 128/80 and no opaque/hole flips.
The wordmark exit remains **all nine exact**, so the document cannot enter an
exact-document numerator. Original references, capture provenance and every current
candidate identity are portable under `tests/References/inventory/`; nine ordinary
tests preserve exact successes and the three retained failures.

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
The next wordmark slice integrates bounded raster contexts before changing
recurrence; the three large complete PNGs are still the same retained failures.
See [PLAN](../PLAN.md) and [the design record](RASTERIZATION_DESIGN.md).

S17 additionally requires reproducible per-variant identities/diffs, categorized
residuals, representative latency/allocations and fresh Windows/Linux delivery
and platform-appropriate oracle evidence. Meeting a numeric rate alone does not
close those gates.
