# Filter source and output clips

These fourteen original inputs retain independent Windows/Linux recipe-3 page
captures from the pinned Chromium. Each reference has three byte-identical,
healthy fresh-process captures; [metadata](capture.json) records their hashes.
Thirteen inputs failed before the ownership correction. The source-only clip is
an exact positive control.

An ancestor clip surrounds a child filter's output. A source-local clip remains
inside the filter and moves with its pixels. Separate references protect both
orders, double clipping, zero displacement and SourceAlpha. Forward declarations,
object-bounding rectangles, filtered leaves, own masks, retained use and pattern
devices cover distinct lowering paths even when images coincide.

The [rendering tests](../../Lokad.Svg.VisualTests/SvgFilterStageReferenceTests.cs)
use one canonical conversion for each input. Separate
[mechanism and ownership tests](../../Lokad.Svg.Tests/SvgInheritedClipIsolationTests.cs)
check linear sibling-scope work, empty output intersections, refusal, cancellation,
cleanup and retry. Destination forms remain in the representative API matrix.
See the [capture procedure](../../../tools/README.md#capture-and-reference-rules)
and repository [MIT license](../../../LICENSE.txt).
