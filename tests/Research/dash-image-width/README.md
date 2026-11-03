# Bitmap-local dash construction

The renderer matches all seven independently captured controls on both hosts.
[Qualification](qualification.json) records source/binary identities, runtime and
package checks, failing-before results and the unchanged reference PNGs.

| Controls | Responsibility |
| --- | --- |
| `narrow.svg`, `wide.svg` | The same line through different compositor windows. Native output differs by sixteen pixels in their common region. |
| `thin.svg`, `round.svg` | Distinct subpixel hairline and wider outline allocation/paint paths. |
| `position-phase.svg` | Fractional placement, source `pathLength` calibration and dash offset. |
| `shader-clip.svg` | Wider shader paint and clipping, preserving an already exact interaction. |
| `non-scaling.svg` | Separate transformed centerline and inverse host placement. |

Owned source verbs and patterns survive until the construction clip is known.
Source length calibrates the pattern before phase-preserving line/rectangle
culling. The inverse uses the narrowed matrix and bitmap origin. Thin strokes and
wider outlines share dash construction and keep their existing paint paths;
glyph-strike filtering stays before canvas replay.

[Mechanisms](../../Lokad.Svg.Tests/SvgDashCullingTests.cs) cover numerical and
contour boundaries directly. Three [ownership contracts](../../Lokad.Svg.Tests/SvgDashSourceOwnershipTests.cs)
cover refusal, cancellation, retry, copies and transfer. Rendering uses one
canonical conversion per fixture. Equal images do not make the paths interchangeable.

The [viewport experiment](raster-window.json) and [stroke-route study](stroke-routes.json)
retain the original investigation. The complete declared [ny1 diagnostic](../svgweb/ny1-static-review.json)
is exact on both hosts; original invalid CSS and font admission are separate.
These authored inputs contain no hosted geometry or external resources and use
the repository license. Follow the [capture rules](../../../tools/README.md#capture-and-reference-rules).
