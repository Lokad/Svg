# Filter source opacity

This original CC0 control recomposes a thin curve, scaled aliases, large authored
coordinates, copying Gaussian kernels and nested opacity. It contains no hosted
SVG content. The [manifest](capture.json) retains three fresh agreeing, healthy,
profile-cleaned captures on each host. Both hosts share one reference image.

The discrepancy remains **open**: 161 differing pixels, including 150 alpha
differences with a maximum alpha error of seven. Transparent output exposes the
source coverage differences that an opaque background can hide.

Use the [capture procedure](../../../tools/README.md#hosted-svgweb-inventory) with
a 128.3×96.2 CSS image viewport and a 128×96 screenshot. Keep the source, reference
and exact comparison unchanged during investigation. This is admitted native
evidence for an unresolved case; it is not an ordinary passing rendering test.

[Investigation and closure criteria](../svgweb/filter-source-opacity.json) retain
the relevant counterexample: changing bitmap origins can fix one small control
while breaking an existing reference. Paint windows, bitmap origins, sampling
and byte storage need separate ownership contracts.
