# Radial paint captured from a parent bitmap

The independently authored CC0 [control](capture.svg) contains one rectangular
path, a translucent radial paint and one Gaussian blur. Its fractional logical
viewport and large coordinates distinguish the parent bitmap's rounded drawing
matrix from direct placement into compact filter storage.

[Three healthy native captures per platform](capture.json) retain exact references
at 1024 × 1120 pixels, with a logical image viewport of 1024.25 × 1120.125. The
[canonical rendering test](../../Lokad.Svg.VisualTests/SvgRadialFilterCaptureReferenceTests.cs)
compares complete PNG output without cropping or tolerances. Numerical coordinate
and ownership boundaries keep their existing direct and representative tests.

Before the correction, four alpha bytes differed by one on each platform. The
unfiltered and opaque-paint controls were exact; replacing the blur with zero
left one alpha difference. Those diagnostic variants isolate capture from the
kernel and do not replace the nonzero-blur regression. The
[closure evidence](../svgweb/parent-capture-drawing.json) records their distinct
roles and the complete original-file replay. No hosted SVG geometry is retained.
