# Image output density

[One original CC0 input](boundary.svg) protects the image boundary independently
of bitmap allocation and SVG content. Four [native controls](capture.json) retain
three agreeing healthy captures per host at output densities .75, 1, 1.5 and 2.
They share the unchanged 17×11 CSS image viewport, with explicitly rounded physical
screenshot dimensions. Fractional right and bottom coverage remains observable.

[Canonical conversion](../../Lokad.Svg.VisualTests/SvgRootImageDensityReferenceTests.cs)
checks each density once. [Mechanisms](../../Lokad.Svg.Tests/SvgRootImageMapTests.cs)
separately protect projection, destination rounding and recorded maps. These are
existing rendering contracts, independently qualified through the capture tool's
explicit density mode. They do not establish root border or padding support.

Reproduce with [capture-chrome](../../../tools/README.md#capture-and-reference-rules),
passing the manifest's physical dimensions, image viewport and output scale. Keep
the source bytes, references and tolerances fixed.
