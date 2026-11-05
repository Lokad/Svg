# Filter source capture demand

These original controls are dedicated to CC0-1.0. Both retain three unused large
blur, offset and shadow branches. One selects a color matrix; the other selects a
single blur. Their curved gradient source has fractional placement, protecting
capture origin, sampling and the distinction between one live stage and a chain.

[Captures](capture.json) pin three independent healthy Chrome captures on each
platform. [Rendering tests](../../Lokad.Svg.VisualTests/SvgFilterCaptureDemandReferenceTests.cs)
use canonical conversion and exact pixels. No existing reference changes.
[Allocation contracts](../../Lokad.Svg.Tests/SvgFilterCaptureDemandTests.cs) separately
exercise each unused halo, the single live blur and the shadow deviation diagnostic
under bounded source surfaces and memory. Existing graph tests retain secondary
and merge edges, region defaults, refusal, cancellation, cleanup and retry.

[Qualification](../release-baselines/filter-capture-demand.json) records the
failing-before cases and affected validation. Pixel dependency marking is shared
by capture and playback; declaration and region validation still visit every node.
