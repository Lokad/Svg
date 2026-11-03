# Uniform child anchors in shared text layout

The original CC0 [control](control.svg) places the same child `text-anchor:end`
over direct text and a local text path. Joining these ranges retains that anchor
even though their selected face and size match the root. Source-style compaction
uses the same font-and-anchor comparison as extraction; [physical-font shaping](../text-shaping-boundaries/README.md)
keeps its separate face/size boundaries.

[Two direct mechanisms](../../Lokad.Svg.Tests/OpenTypeFontTests.TextAnchor.cs)
protect middle/end chunk coordinates, borrowed position storage and cleanup.
[One canonical image](../../Lokad.Svg.VisualTests/SvgTextAnchorReferenceTests.cs)
protects the complete join with the shared [Noto Sans program](../supplied-font-labels/fonts/NotoSans-Regular.ttf)
under its [OFL license](../supplied-font-labels/fonts/OFL.txt).
[Three healthy captures per platform](capture.json) retain native differences.
Existing anchor staging, prepared replay and path measurement contracts remain
representative ownership coverage; no fixture/API matrix is added.

[Closure evidence](../svgweb/text-shared-anchor.json) records failing-before
pixels, focused checks and affected integration. Case mapping remains separate
unfinished work.
