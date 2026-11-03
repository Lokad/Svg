# Source style and font shaping boundaries

The original CC0 [control](control.svg) changes only `text-anchor` between `f` and
`i`. The first character's anchor governs the chunk; the same selected face and
size retain continuous ligature shaping. Original style, node and positioning
intervals remain available independently of the physical font intervals.

The [canonical rendering test](../../Lokad.Svg.VisualTests/SvgShapingIntervalReferenceTests.cs)
uses the shared public [Noto Sans program](../supplied-font-labels/fonts/NotoSans-Regular.ttf)
under its [OFL license](../supplied-font-labels/fonts/OFL.txt).
[Three healthy captures per platform](capture.json) retain platform differences.
[Direct mechanisms and one ownership contract](../../Lokad.Svg.Tests/OpenTypeFontTests.ShapingIntervals.cs)
protect anchor-only joining, actual size boundaries, invalid source intervals and
ligature-staging refusal/cleanup/retry. [Closure evidence](../svgweb/text-shaping-boundaries.json)
records the former Windows/Linux pixel failures and qualification.

The separate [case-expansion comparison](../svgweb/text-casing-model.json) is
exploratory research. Its original inputs and observed PNGs preserve the difference
between source-character indexing and the pinned browser's transformed-text
behavior. These observations do not admit a case-mapping implementation or replace
the renderer's original source-addressing contract.
