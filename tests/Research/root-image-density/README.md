# Image output density

[Original CC0 inputs](capture.json) protect the image boundary independently
of bitmap allocation and SVG content. Four density contexts retain
three agreeing healthy captures per host at output densities .75, 1, 1.5 and 2.
They share the unchanged 17×11 CSS image viewport, with explicitly rounded physical
screenshot dimensions. Fractional right and bottom coverage remains observable.
A fifth [overflow control](fractional-overflow.svg) keeps the integer device image
viewport inside the allocated bitmap at .75 density. Its final row is transparent;
the preceding row retains fractional right-edge AA.

[Canonical conversion](../../Lokad.Svg.VisualTests/SvgRootImageDensityReferenceTests.cs)
checks each density once. [Mechanisms](../../Lokad.Svg.Tests/SvgRootImageMapTests.cs)
separately protect projection, destination rounding and recorded maps. These are
existing rendering contracts, independently qualified through the capture tool's
explicit density mode. Box layout has separate [border](../root-border/README.md)
and [padding](../root-padding/README.md) controls.

Reproduce with [capture-chrome](../../../tools/README.md#capture-and-reference-rules),
passing the manifest's physical dimensions, image viewport and output scale. Keep
the source bytes, references and tolerances fixed.
