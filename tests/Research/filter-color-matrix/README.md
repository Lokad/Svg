# Color-matrix stages

These original CC0 controls exercise all four color-matrix modes through the existing
filter graph. [Capture recipes](capture.json) retain byte-exact sources, independent
Windows/Linux images and three healthy, profile-cleaned processes per case/host:
27 controls and 162 native captures. No reference comes from the implementation.

| Responsibility | Controls |
| --- | --- |
| Straight channels, alpha and clamping | identity, alpha-scale, rgb-to-alpha, alpha-to-rgb, clamp, source-alpha |
| Transparent input and primitive extents | transparent-offset, crop |
| Mode coefficients and operating space | saturate, hue-rotate, luminance-alpha, linear |
| Graph identity and rounding boundaries | named-chain, matrix-chain, cropped-chain |
| Drawing maps and source views | affine-matrix, affine-before, affine-after, affine-offset, flat-offset |
| Uniform paint and consumers | uniform, affine-uniform, matrix-merge, matrix-composite, matrix-zero-alias, matrix-blur, blur-matrix |

Matrix chains retain each clamp and transfer without an intermediate byte image.
Pending color operations borrow immutable scene coefficients until sampling. A
consumer that resolves an image keeps its original draw placement and finite view;
capture-grid expansion must not change that view. Color evaluation precedes
image-edge coverage. Uniform paint keeps its floating lanes. These distinctions
protect real interactions even when two simpler images happen to agree.

[Direct mechanisms](../../Lokad.Svg.Tests/SvgFilterColorMatrixTests.cs) cover
coefficient/count/token boundaries, cancellation, straight-channel arithmetic,
transparent input and output bounds. One
[ownership contract](../../Lokad.Svg.Tests/SvgFilterColorMatrixOwnershipTests.cs)
uses the existing parser/raster refusal, cancellation, cleanup and retry helper.
It copies only the affine-offset source and [hash slice](ownership.json) into core
test output. [Rendering tests](../../Lokad.Svg.VisualTests/SvgFilterColorMatrixReferenceTests.cs)
use one canonical conversion per source with the existing native-control helper.

[Qualification](../svgweb/filter-color-matrix.json) records source/binary identities,
failing-before cases, timings and the hosted-original replay. The
[SVG filter specification](https://www.w3.org/TR/filter-effects-1/#feColorMatrixElement)
defines the matrix modes; native captures independently pin numerical staging.
