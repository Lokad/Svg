# Colour programs and final coverage

These independently authored CC0 controls keep a terminal offset's pending colour
program until image sampling and destination clipping. Rounding the matrix output
to bytes first changes low-alpha clip pixels, including through a zero-offset alias.

[Ten canonical cases](../../Lokad.Svg.VisualTests/SvgFilterColorProgramCoverageReferenceTests.cs)
cover fractional, integer and zero offsets, a matrix-only control, white paint and
coloured output in both operating spaces. Three trailing aliases separately protect
zero offset, zero blur and single-input merge. [Capture recipes](capture.json) retain
three healthy, profile-cleaned native processes per case on Windows and Linux:
60 independent captures, with exact comparison and unchanged reference bytes.

One [direct mechanism](../../Lokad.Svg.Tests/SvgFilterColorProgramCoverageTests.cs)
pins alpha rounding after coverage. One ownership case exercises the existing
refusal/cancellation/cleanup/retry helper; core output contains only its source and
[hash slice](ownership.json). No API matrix is repeated across rendering fixtures.

[Qualification](../svgweb/filter-color-program-coverage.json) records before/after
results and hosted-prefix closure. All nine declared static/font graph prefixes
are exact on both hosts. Hosted payloads remain in memory; animation and the
original font environment are outside that context's acceptance claim.
