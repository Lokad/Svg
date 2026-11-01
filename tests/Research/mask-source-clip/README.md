# Mask source clips

The own CC0 [control](inputs/opaque-mask-bitmap.svg) paints a thin cubic across
a compositor bitmap inside the opaque part of a rectangular source clip.
The [manifest](capture.json) records three agreeing healthy, profile-cleaned
native captures per host. Its fractional viewport retains the native bitmap grid.

The [canonical test](../../Lokad.Svg.VisualTests/SvgMaskSourceClipReferenceTests.cs)
asserts exact pixels through one conversion path. Direct
[mechanism tests](../../Lokad.Svg.Tests/SvgOpaqueBitmapClipTests.cs) cover bitmap
opacity, partial coverage, cancellation and a hairline that escapes the bitmap.
[Closure evidence](../svgweb/mask-opaque-bitmap.json) also retains the original's
polygon/hairline interaction and broader mask qualification.

Use the [capture procedure](../../../tools/README.md#hosted-svgweb-inventory).
Preserve reference bytes and exact comparisons.
