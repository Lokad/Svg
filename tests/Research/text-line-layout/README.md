# Explicit lines and inert line height

The supported text layout uses authored positions and anchors. Validated CSS
`line-height` has no effect on these positions and retains no style state. The
original CC0 [control](control.svg) combines a positioned second line, inherited
and important declarations, a styled span, three anchors and inherited path text.
Its [capture recipe](capture.json) retains three healthy fresh captures per host.
One [canonical image test](../../Lokad.Svg.VisualTests/SvgLineHeightReferenceTests.cs)
compares the independent platform pixels; direct
[mechanisms](../../Lokad.Svg.Tests/SvgLineHeightTests.cs) cover values, token
boundaries, container/use admission and font discovery. No fixture/API product or
additional ownership matrix is needed for an allocation-free validation change.

Four original CC0 research inputs distinguish
[CSS pre](css-pre.svg), [legacy XML preservation](xml-preserve.svg),
[CSS pre with line height](css-pre-line-height.svg) and
[explicit baselines](explicit-lines.svg). The pinned browsers normalize the first
three to a single line; the library agrees after declaration admission. Explicit
lines produce a different image. These observations are exploratory controls,
not automatic-line-layout acceptance. The
[SVG2 definition](https://www.w3.org/TR/SVG2/text.html#TextLayoutAlgorithm)
preserves forced breaks in CSS pre and uses line height for leading. That layout
remains outside the current contract; do not turn browser agreement into a claim
that it is implemented. Legacy XML newline/tab normalization stays unchanged.

All inputs borrow the existing [Noto Sans program](../supplied-font-labels/fonts/NotoSans-Regular.ttf)
and [OFL license](../supplied-font-labels/fonts/OFL.txt) through the
[host catalog](font-pack.json); no font bytes are duplicated.
[Qualification](../svgweb/preformatted-line-height.json) records the source hashes,
before/after checks, native observations and the remaining SVGWeb gates.
