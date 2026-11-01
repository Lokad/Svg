# Filter source opacity

These own CC0 controls combine a thin curve, scaled aliases, large coordinates,
copying Gaussian kernels and nested opacity. They contain no hosted SVG content.
The [manifest](capture.json) retains three agreeing, healthy, profile-cleaned
captures per host and one shared native PNG per case.

The fractional and integer [canonical rendering tests](../../Lokad.Svg.VisualTests/SvgFilterSourceOpacityReferenceTests.cs)
are exact on both hosts. Tight parameter-space bounds and the allocated opacity
bitmap's origin protect source capture. Independently rounded device axes select
the bitmap-restore consumer. [Bounds evidence](../svgweb/filter-source-opacity.json)
and [restore mechanisms](../svgweb/group-opacity-stages.json) preserve their distinct
failing-before results and the existing affine-opacity/AA-parent-clip controls.

The [wide control](inputs/thin-curved-source-wide.svg) remains **open**: four
one-level alpha differences on both hosts. It changes only viewport and outer
placement. Exploratory direct-source painting is exact; the filtered instance
retains the error. Its admitted reference protects investigation, with a passing
rendering assertion to follow closure. The manifest records each viewport and
screenshot size; the wider context preserves a boundary absent from the small cases.

Use the [capture procedure](../../../tools/README.md#hosted-svgweb-inventory).
Preserve native reference bytes and exact comparisons. The complete hosted
original remains open; no support claim follows from these reduced controls.
