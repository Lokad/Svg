# S17 release criteria and decisions

**Standalone qualification is complete (2026-10-17)** for substantive revision
`0a0843ec`, after the six audit corrections and approved F1 fixture replacement.
All 26 unchanged upstream documents are exact on Windows/Linux. The
[finished receipt](../artifacts/full-fixture-replacement-20261017/standalone-qualified-results.json) maps [PLAN's five release conditions](../PLAN.md#release-completion-s17)
to complete evidence. [Terminal CI](https://github.com/Lokad/Svg/actions/runs/38986391708) passes **8,068 tests/platform**
(5,553 core, 2,509 visual, six FontProbe transport), package/notices, isolated
installed consumers and required tooling. Local normal build plus complete tests
took **59.5s Windows / 226.3s Linux**. Public regular/bold/real-italic/fallback
evidence and seven-witness measurements are reused; existing resource limits,
cancellation, cleanup, borrowed ownership and approximate accounting are preserved.

[INVENTORY](INVENTORY.md#f1-retirement-dispositions-2026-10-17) records retirement
of old corporate/selected/authored-sheet membership and count/percentage gates.
Focused regressions and independent font/parser/API/resource/benchmark fixtures
remain. T3-private's four deployed-manifest requests remain visibly pending before
production adoption. No compatibility with that deployment is claimed.
Documentation-only finalization reuses this exact substantive-source CI audit;
no second hosted qualification is needed.

**Approved criteria change:** private access is not a standalone prerequisite.
T3-private's original four regular/bold/italic/fallback requests against the
unchanged deployed manifest remain pending until configuration is available and
are required before production adoption. Public success cannot pass that gate
or establish deployment compatibility. Endpoints, credentials and caches remain
host-owned; ordinary builds/CI stay independent of private configuration. Existing
manifest compatibility and font-format/shaping boundaries remain unchanged, with
no other-renderer comparison, dependency, coordination or shared management project.

The source/reference freezes remain binding. F1 replaces the old numerical
membership and percentage gates with the finite upstream selection, unchanged
platform references and preserved focused/API/resource/font protections.
[PLAN](../PLAN.md#release-completion-s17) owns the five required release conditions;
document counts alone cannot establish completion. Pinned Chrome remains the sole
pixel oracle, with exact decoded RGBA8 comparison and RGB ignored only when both
alphas are zero. An admitted failure is implementation work; no tolerance or
candidate-driven reference change closes it.

The superseded corporate selection/80% decisions and native curve/clip/numeric
checkpoints remain in [their historical record](https://github.com/Lokad/Svg/blob/0a0843ec/docs/S17_DECISIONS.md).
Current coverage dispositions live in [INVENTORY](INVENTORY.md); detailed renderer
mechanisms and measurements live in [RASTERIZATION_DESIGN](RASTERIZATION_DESIGN.md)
and [BENCHMARKS](BENCHMARKS.md). They add no new completion prerequisite.
