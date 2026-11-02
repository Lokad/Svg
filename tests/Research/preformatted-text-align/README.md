# Alignment of preformatted text

CSS `text-align` has no effect on preformatted SVG text; its used value is `start`.
[`text-anchor` and explicit positioning](../../../docs/SUPPORT_MATRIX.md#text-and-fonts)
continue to determine the drawing. The [SVG text specification](https://svgwg.org/svg2-draft/text.html#TextLayoutAlgorithm)
defines this boundary; auto-wrapped layout remains a separate feature.

The original CC0 [control](control.svg) combines inherited, inline and stylesheet
alignment with three anchors, a styled span and path text. It borrows the existing
[Noto Sans program](../supplied-font-labels/fonts/NotoSans-Regular.ttf) under its
[OFL license](../supplied-font-labels/fonts/OFL.txt). The host supplies that shared
program for the logical paths in [the catalog](font-pack.json).

[Three healthy captures per platform](capture.json) retain meaningful platform
differences. One [canonical rendering test](../../Lokad.Svg.VisualTests/SvgTextAlignmentReferenceTests.cs)
compares the full image. Direct [mechanisms](../../Lokad.Svg.Tests/SvgTextAlignmentTests.cs)
validate keywords, unused style state, unchanged anchor/font/scalar discovery, and
group/empty-symbol/painted-symbol admission. Existing ownership contracts stay
representative. [Evidence](../svgweb/preformatted-text-align.json) records before
failures, validation costs and the remaining original-file gates.
