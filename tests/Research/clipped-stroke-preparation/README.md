# Clipped stroke preparation

The [capture manifest](capture.json) retains 28 independent SVG inputs and
unchanged native PNGs from three healthy, stable processes on each host. The
[baseline](before-correction.json) records nineteen failing-before regressions
and nine exact controls. Different path graphs remain separate even when their
reference images share storage.

The [coverage index](coverage-index.json) includes all twelve original stroke
inputs, preserving open/closed path and line eligibility differences. Sixteen
controls cover signed fixed-range neighbors, cap extent, ordinary and
non-scaling strokes, vertical clipping, fractional placement, source opacity,
nested viewports, retained quadratics and neighboring conic subdivisions.

Finite source geometry may exceed the edge format before bitmap clipping.
Every emitted endpoint and control must fit both bitmap-local and translated
canvas fixed coordinates before edge allocation. Uncut construction retains its
range restriction. Retained raw controls determine native convexity without a
sampled-vertex veto; source concavity and placed-control checks still apply.

Direct [mechanisms](../../Lokad.Svg.Tests/SvgClippedCoordinateRangeTests.cs)
test admission, nonfinite cached rejection and local/canvas range boundaries.
The [cache contract](../../Lokad.Svg.Tests/SvgCpuRasterContextTests.cs) checks
one-time classification, repeated setup and unchanged run dispatch.
Two selected [ownership witnesses](../../Lokad.Svg.Tests/SvgClippedStrokeOwnershipTests.cs)
exercise opacity-layer and nested-viewport allocation, refusal, work limits,
cancellation, cleanup and retry. Each rendering input uses its own reference
through canonical conversion; the general API matrix retains destination and
prepared-document coverage.
