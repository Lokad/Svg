# Fixed geometry lengths

The original CC0 [control](mixed-absolute-units.svg) combines fixed units on
rectangles, circle/ellipse axes, a stroked line, local-use placement, a nested
viewport, mask regions and user/object-space pattern grids. The
[manifest](capture.json) records three agreeing healthy native captures per host,
frozen before implementation. Identical platform PNGs share storage.

One [canonical rendering test](../../Lokad.Svg.VisualTests/SvgGeometryLengthReferenceTests.cs)
checks exact pixels. [Direct length mechanisms](../../Lokad.Svg.Tests/SvgGeometryLengthTests.cs)
cover conversion, resolved range, signs, contiguous tokens and the local-use
numeric reread. [Rotation mechanisms](../../Lokad.Svg.Tests/SvgCenteredRotationTests.cs)
protect the native translation lanes before and after matrix promotion. The
representative [prepared-font contract](../../Lokad.Svg.Tests/OpenTypeFontTests.TraversalMetadata.cs)
checks pattern selection, one acquisition and replay without a fixture/API matrix.

The rotated fractional pattern exposed a translation-stage bug even with all
physical lengths replaced by numeric pixels. It remains in the frozen control;
preserving the rotation-centre sequence closes it without sampling adjustments.
[Shared-model and original validation](../svgweb/geometry-fixed-lengths.json)
also records the centimetre-based hosted original without retaining its payload.
