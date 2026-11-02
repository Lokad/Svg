# Open affine filter capture case

The original CC0 [stroke](stroke.svg) uses one quadrilateral, an affine map and
one Gaussian blur. Its large viewport crosses a compositor bitmap boundary.
[Three healthy captures per platform](capture.json) produce identical references.
The current renderer differs at two pixels on each platform, including one alpha
byte; straight RGB differences grow when those small alpha values are unpremultiplied.
Exact comparison remains required.

This is an open research case with a recorded failing result. Promote one canonical
regression with the shared correction. Moving the same drawing into a small cropped
viewport changes its coordinate context and currently passes; it is not a replacement
for the large-viewport case.

The [filter investigation](../svgweb/car-filter-isolation.json) separates source
capture, kernel evaluation and restoration. Identical captured alpha inputs blur
exactly in the measured native controls; that does not establish equality of the
native and managed source captures. No hosted SVG geometry is retained here.
