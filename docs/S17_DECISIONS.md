# S17 release criteria and decisions

Updated 2026-10-04 against renderer revision `f633c86b`.
[PLAN.md](../PLAN.md) is the current execution queue. Rows 3–7 still have
implementation and fidelity exits; the earlier claim that rows 1–7 were complete
within architectural limits is withdrawn. The first exact curved corporate mark
and outlined wordmark checkpoint, and S17 itself, remain unachieved.

Verified review evidence: 432 fixture entries = 353 exact + 74 known-difference
renders + five rejections across 281 base documents and 151 variants; 73 research
probes = 72 known-difference renders + one rejection. Fresh Windows gates pass:
2,862 core + 105 visual + six FontProbe tests, 25 memory scenarios, package
inspection and isolated installed-consumer checks. The fresh Default-options
inventory sweep reports 88 renders + four MalformedInput rejections out of 92.
These counts establish neither corporate coverage nor Linux delivery.

Two release-policy choices remain open. They block a “most corporate logos”
claim, not implementation work or the first product checkpoint.

## Q1 - numeric exact-document target

The measurement is fixed: the fraction of documents in the frozen representative
corporate cohort for which **every declared variant is measured and pixel-exact**
against its pinned oracle. The numeric target has not yet been selected.

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
full-coverage translucent span dispatch and paint/dither/group-composition
residuals remain categorized implementation requirements for affected documents.
Closer agreement with ideal geometry does not establish pinned-browser equality.
A target below 100 percent can leave documents failing in the denominator; it
cannot waive their pixel errors or alter their references.

Ford remains zero exact of nine recorded variants. The latest asset record
identifies opaque solid cubic fills without stroke, dash or gradients, making it
the focused coverage target; this does not close composition on other inputs.
The selected next experiment separates curve evolution from walked edge state,
proves quarter-grid parity, then tests fractional events within the existing
live-cubic eligibility. See PLAN's active work order and
[the design record](RASTERIZATION_DESIGN.md) for the retained falsifications.

S17 additionally requires reproducible per-variant identities/diffs, categorized
residuals, representative latency/allocations and fresh Windows/Linux delivery
and platform-appropriate oracle evidence. Meeting a numeric rate alone does not
close those gates.
