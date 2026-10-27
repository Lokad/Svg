# Raster devices and capture profiles

These ten original inputs use the pinned recipe-3 page screenshot: an unstyled
`img`, with transparent background and device scale one. Each Windows/Linux
reference has three byte-identical captures from healthy fresh processes.
[Capture metadata](capture.json) records source and image hashes.

The two ordinary page controls also retain independent Canvas `drawImage` images.
Their 124/544-pixel differences show why a Canvas probe cannot substitute for the
declared page oracle across compositor seams. Both profiles are preserved.

The eight pattern controls exercise ordinary and filtered gradients, fractional
displacement, a scaled viewBox and curved source geometry. A vector pattern owns
one raster image even when its consumer crosses compositor tiles. Seven cases
failed before that device policy was corrected. Each case uses one canonical
conversion against its own page reference; equal images do not remove different
source or allocation paths.

Two [ownership contracts](../../Lokad.Svg.Tests/SvgPatternDeviceTests.cs) cover
memory refusal, cancellation, cleanup, retry and prepared replay for a wide,
scaled pattern. Destination forms remain in the representative API matrix.
See the [rendering tests](../../Lokad.Svg.VisualTests/SvgRasterDeviceReferenceTests.cs)
and [capture procedure](../../../tools/README.md#capture-and-reference-rules).
The fixtures use the repository's [MIT license](../../../LICENSE.txt).
