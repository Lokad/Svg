# Raster windows and dashes

These authored inputs contain the same line, colour and dash pattern. Only the
outer width changes from 40 to 600. Both use a 384-pixel height at density 1.

The library preserves the shared 40×384 region exactly. Pinned native screenshots
change sixteen pixels there on both hosts. The original [captures](capture.json)
and PNGs remain unchanged.

[Raster-window evidence](raster-window.json) separates page width from SVG width
and compares block `img` with inline SVG. Sixty healthy captures confirm that the
page's raster viewport selects the difference while DOM geometry stays identical.
A short viewport reproduces the same difference. The capture recipe and measured
matrices are recorded alongside the screenshots; no canvas path is involved.

The existing compositor grid and native phase-preserving line culling, applied
before dash measurement, reproduce the complete wide control exactly. The same
research replay removes all twelve pixels in the complete declared ny1 diagnostic
on both hosts. Text, curve interpolation and independent references stay intact.
Production integration remains open: retain source/pattern ownership until the
construction clip is known, then qualify thin and outline strokes and their maps.
These controls remain research evidence until that shared stage is implemented.

[Stroke-route controls](stroke-routes.json) extend the same mechanism to subpixel
hairlines and wider outlines. The frozen `thin.svg` and `round.svg` inputs differ
from native by twelve and thirty-seven pixels respectively; culling replay closes
both on each host. Wider butt/square controls stay exact. These distinct paths
must retain their existing dispatch rather than share a hairline-only correction.

The [ny1 diagnostic](../svgweb/ny1-static-review.json) isolates its twelve residual
pixels to one line, after removing the other 99 graphic elements. These controls
contain no hosted geometry, fonts or external resources. Inputs use the repository
license. Follow the [capture procedure](../../../tools/README.md#capture-and-reference-rules)
with matching physical and image-viewport dimensions.
