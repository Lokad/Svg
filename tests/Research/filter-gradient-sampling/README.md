# Filter gradient sampling

These independently authored CC0 controls preserve the two gradient regressions
from the source-origin investigation. All input and native PNG bytes are unchanged.
[Capture recipes](capture.json) record three fresh, healthy, profile-cleaned
processes per input on Windows and Linux.

A drawable's recording bounds enclose its local visual rectangle before placement.
A filter-output hint encloses in filter parameter space. Their union determines
the source image and gradient dither origin; captured ink and shader coordinates
keep their existing roles. The shared paint record retains that draw extent
without another allocation, sampler branch or replay pipeline.

Three [mechanism cases](../../Lokad.Svg.Tests/SvgRecordedSourceBoundsTests.cs)
cover fractional rectangle/path bounds, negative coordinates and reflection while
preserving unrounded object bounds and scene cleanup. Two
[canonical rendering cases](../../Lokad.Svg.VisualTests/SvgFilterGradientSamplingReferenceTests.cs)
protect the offset-source fast path and the general color-matrix graph. Core
output needs no additional fixture copies or API matrix.

[Qualification](../svgweb/filter-gradient-sampling.json) retains failing-before
evidence and timings. The declared hosted static/font diagnostic is now exact
through [shared bitmap restoration](../svgweb/filter-destination-composition.json);
truncated offset prefixes retain a separate crop fringe. Neither animation nor
the original font environment is qualified.
