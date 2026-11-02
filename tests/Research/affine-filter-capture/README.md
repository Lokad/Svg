# Affine filter coverage beside a bitmap border

The original CC0 [stroke](stroke.svg) uses one quadrilateral, an affine map and
one Gaussian blur. Its large viewport crosses a compositor bitmap boundary.
[Three healthy captures per platform](capture.json) produce identical references.
One canonical conversion matches those references exactly on both platforms.

The [direct edge control](edge.svg) and [independent captures](edge-capture.json)
pin coverage 182 and 75 beside the left bitmap border. Six [mechanism cases](../../Lokad.Svg.Tests/SvgAnalyticStorageWindowTests.cs) separate
the full row, a row excluding that border, and a single owned column. One ownership
case protects span-scratch admission, refusal cleanup and retry. The [canonical image](../../Lokad.Svg.VisualTests/SvgAffineFilterCaptureReferenceTests.cs) keeps the complete viewport. Moving the drawing
into a small cropped viewport changes its coordinate context and cannot replace
the large-viewport regression.

The [mask-window correction](../svgweb/mask-scan-window.json) separates the complete
path/bitmap scan window from output-row storage. The [filter investigation](../svgweb/car-filter-isolation.json)
retains the remaining original-file gaps. No hosted SVG geometry is retained here.
