# Filter source recording coordinates

Sources recorded in their owning filter's map already belong to that layer's
coordinates. Reusing the layer map avoids cancelling independently rounded
inverse/source matrices. The rule applies to the recorded source rather than
selecting a shape type.

The three [inputs](capture.json) retain independent healthy, byte-stable
Windows/Linux page captures from three fresh processes each. They cover a
neutral sRGB ellipse, a fractional linear-RGB offset and an unfiltered control.
The existing linear-RGB neutral ellipse remains tested with its original
[reference](../offset-merge-inputs/references/affine-image-47.png).
The [coverage index](coverage-index.json) accounts for all four actual inputs
without duplicating that identical SVG/options/reference case.

[Before-correction evidence](before-correction.json) distinguishes the published
fractional-offset defect from the neutral-ellipse regression caught during
qualification. Neither native reference is changed to accept a different result.
[Rendering tests](../../Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs) use
one canonical conversion per input; the independent transformed-curve, resource
and ownership controls remain in the [source/composition bank](../filter-merge-image-draw/README.md).
