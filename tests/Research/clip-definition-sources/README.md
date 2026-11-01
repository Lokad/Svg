# Direct and referenced clip children

Four original CC0 sources isolate multiple local references, mixed direct and
referenced children, forward geometry with fractional placement, and normalized
references through a skewed object box. Each input
has three agreeing healthy native captures on Windows and Linux, frozen before
the implementation change. The [manifest](capture.json) records source, browser
and reference hashes. Identical platform PNGs share storage; their capture
receipts remain separate.

The [rendering tests](../../Lokad.Svg.VisualTests/SvgDeferredClipReferenceTests.cs)
use one canonical conversion and require exact native pixels. All four sources
previously rejected as unsupported. [Mechanism tests](../../Lokad.Svg.Tests/SvgDeferredClipTests.cs)
protect ordered staging across separated child ranges, the existing 42/43-child
boundary/replay transition, nonpainting references beside visible siblings,
separate units across deferred definitions, and integer construction bounds
after normalization in the referencing canvas. A direct path exhibits the same
enclosure discrepancy as the referenced source; this is shared construction logic.
Two selected inputs extend the existing
[parser ownership checks](../../Lokad.Svg.Tests/SvgCurvedClipSourceTests.cs):
sampled memory refusal, raster refusal, cancellation, source preservation, cleanup
and retry, including the temporary normalized path owner. The API ownership
matrix remains a separate responsibility.

Capture with the [Chrome tool](../../../tools/README.md#capture-and-reference-rules)
using a 128×96 transparent SVG image, intrinsic viewport and DPR 1. Run three fresh
processes per host and retain healthy startup and cleaned-profile receipts.
Inputs and PNGs are independently authored/captured evidence; do not regenerate
references to fit renderer changes. Whole-resource and per-child intersections
within unions remain separate open qualification areas.
