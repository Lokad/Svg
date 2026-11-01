# Filter source opacity

This original CC0 control recomposes a thin curve, scaled aliases, large authored
coordinates, copying Gaussian kernels and nested opacity. It contains no hosted
SVG content. The [manifest](capture.json) retains three fresh agreeing, healthy,
profile-cleaned captures on each host. Both hosts share one reference image.

The [canonical rendering test](../../Lokad.Svg.VisualTests/SvgFilterSourceOpacityReferenceTests.cs)
is exact on both hosts. The frozen implementation fails 161 pixels, including
150 alpha differences. Tight parameter-space visual bounds and the allocated
opacity bitmap's origin protect source capture; later clips retain that origin.

Use the [capture procedure](../../../tools/README.md#hosted-svgweb-inventory) with
a 128.3×96.2 CSS image viewport and a 128×96 screenshot. Keep the source, reference
and exact comparison unchanged. [Native layer observations](native-layer-observations.json)
distinguish the opacity allocation from the filter's source-picture hint.

[Investigation and closure criteria](../svgweb/filter-source-opacity.json) retain
the preserved affine group-opacity and antialiased parent-clip counterexamples.
Clip coverage uses stored-image extents; opacity uses painted-output extents.
The complete hosted original remains open. The integer-viewport companion changes
only root and CSS image dimensions to 128×96. Six agreeing captures establish its
434 differing pixels and 400 alpha differences on both hosts, with maximum alpha
error one. It is admitted open evidence; the canonical test asserts only the closed
fractional control. Other opacity variants remain exploratory.
