# Clip sources and intersections

Twelve original CC0 sources isolate ordered direct/reference children, forward
geometry, normalized object boxes, resource and child intersections, and
point-list winding. Transformed controls separate resource/use/source maps,
direct and referenced children, rectangular and curved parents, enclosing target
clips and the allocated mask bitmap. Each input
has three agreeing healthy native captures on Windows and Linux, frozen before
the implementation change. The [manifest](capture.json) records source, browser
and reference hashes. Identical platform PNGs share storage; their capture
receipts remain separate.

The [rendering tests](../../Lokad.Svg.VisualTests/SvgDeferredClipReferenceTests.cs)
use one canonical conversion and require exact native pixels. The original four
sources previously rejected as unsupported. [Mechanism tests](../../Lokad.Svg.Tests/SvgDeferredClipTests.cs)
protect ordered staging across separated child ranges, the existing 42/43-child
boundary/replay transition, nonpainting references beside visible siblings,
separate units across deferred definitions, and integer construction bounds
after normalization in the referencing canvas. A direct path exhibits the same
enclosure discrepancy as the referenced source; this is shared construction logic.
The [boundary tests](../../Lokad.Svg.Tests/SvgClipBoundaryTests.cs) exercise
intersection geometry, coincident edges, winding, containment and empty results
directly. [Dependency tests](../../Lokad.Svg.Tests/SvgClipBooleanTests.cs) retain
cycles, shared dependencies and empty/short point lists. Five selected inputs extend the existing
[parser ownership checks](../../Lokad.Svg.Tests/SvgCurvedClipSourceTests.cs):
sampled memory refusal, raster refusal, cancellation, source preservation, cleanup
and retry, including normalized paths, temporary rectangle boundaries and painted
child unions. The API ownership
matrix remains a separate responsibility.

Capture with the [Chrome tool](../../../tools/README.md#capture-and-reference-rules)
using a 128×96 transparent SVG image, intrinsic viewport and DPR 1. Run three fresh
processes per host and retain healthy startup and cleaned-profile receipts.
Inputs and PNGs are independently authored/captured evidence; do not regenerate
references to fit renderer changes. Resource intersections resolve before AA when
they have a path boundary. Painted unions keep the resource clip's canvas and
child effect contexts separate: collapsing their coverage changes edge alpha.
The target's enclosing clip constrains target ink; the independent mask constructs
within the allocated layer before that clip trims the target.
The [stage observations](replay-stages.json) record the separate exploratory
native vector trace and its limits; canonical PNG controls remain authoritative.
