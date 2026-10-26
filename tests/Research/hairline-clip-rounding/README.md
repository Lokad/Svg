# Hairline clip rounding

The [capture](capture.json) retains sixteen font-free inputs: two short rotated
rectangles, each spelled as a rectangle and a closed path, through containing
and intersecting rectangular or complex clips. Rectangle and path lowerers
remain separate cases even where their native images agree.

One containing binary rectangle can preserve the adjacent-pixel paint operation.
AA clips do so when the entire expanded path fits one unchanged opaque row band.
Complex binary regions and changing AA bands retain scalar dispatch, including
under opaque stroke pixels. These operations differ by a byte for some
translucent fringes; comparisons remain exact.

Each input has its own browser reference from Chrome 153.0.8010.52. Three healthy
fresh launches per host agreed byte for byte, including across Windows/Linux.
The failing-before cases isolate the complex clip containing the second pose.
The other inputs protect clip intersections and the unchanged first pose.
Eight further symbol-band controls retain gradient fill, mask ownership,
separate partial stroke/fill opacity, zero-radius axes and fractional output
scaling. These reject pixel-local clip bypasses while preserving the existing
native images. Their selection records the interaction protected by each pair.
Direct coverage tests check binary rectangle and whole-path band dispatch; the
existing native vectors retain line boundaries, reversal, phases and subdivision.
