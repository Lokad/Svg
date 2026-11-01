# Direct and referenced clip children

Three original CC0 sources isolate multiple local references, mixed direct and
referenced children, and forward geometry with fractional placement. Each input
has three agreeing healthy native captures on Windows and Linux, frozen before
the implementation change. The [manifest](capture.json) records source, browser
and reference hashes. Identical platform PNGs share storage; their capture
receipts remain separate.

The [rendering tests](../../Lokad.Svg.VisualTests/SvgDeferredClipReferenceTests.cs)
use one canonical conversion and require exact native pixels. All three sources
previously rejected as unsupported. [Mechanism tests](../../Lokad.Svg.Tests/SvgDeferredClipTests.cs)
protect ordered staging across separated child ranges, the existing 42/43-child
boundary/replay transition, and nonpainting references beside visible siblings.
One mixed-source case extends the existing
[parser ownership checks](../../Lokad.Svg.Tests/SvgCurvedClipSourceTests.cs):
sampled memory refusal, raster refusal, cancellation, source preservation, cleanup
and retry. The API ownership matrix remains a separate responsibility.

Capture with the [Chrome tool](../../../tools/README.md#capture-and-reference-rules)
using a 128×96 transparent SVG image, intrinsic viewport and DPR 1. Run three fresh
processes per host and retain healthy startup and cleaned-profile receipts.
Inputs and PNGs are independently authored/captured evidence; do not regenerate
references to fit renderer changes. Clip-resource intersections and referenced
object-box units are separate open qualification areas.
