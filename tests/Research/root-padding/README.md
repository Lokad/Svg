# Root padding

The eight original CC0 [controls](capture.json) retain three agreeing healthy native
captures per host. [Canonical conversion](../../Lokad.Svg.VisualTests/SvgRootPaddingReferenceTests.cs)
also checks the two previously frozen fractional-padding inputs in
[root box layout](../root-box-layout/capture.json).

Each context protects a distinct interaction:

- `padding-viewbox`: unequal fixed insets, fractional outer-box projection, viewBox
  fitting, transformed shapes, gradient paint and root opacity.
- `padding-source-alpha`: root content clipping around a child filter and opacity,
  with canvas paint outside its source image.
- `padding-clip-density`: a snapped CSS content clip at 1.5× density, distinguished
  from a fractional SVG content translation.
- `padding-percent`: the image-container width percentage base and fractional root
  image size, independently of output pixel dimensions.
- `padding-empty`: unpainted content keeps the canvas backdrop and image boundary.
- `padding-priority`: edge-specific cascade, important declarations, visibility and
  nonapplicable group/nested viewport padding.
- `padding-coincident-clips`: image and content edges multiply their AA coverage
  separately, including the corner, over a colored canvas.
- `padding-quantized-zero`: nonzero specified padding retains content clipping even
  when the resolved insets round to zero on the CSS layout grid.

[Direct mechanisms](../../Lokad.Svg.Tests/SvgRootBoxLayoutTests.cs) keep shorthand,
units, boundaries, priority and numerical stages separate. One
[ownership test](../../Lokad.Svg.Tests/SvgRootPaddingOwnershipTests.cs) protects
analytic clipping, refusal, refunds and retry; one prepared-font contract protects
validation before acquisition and matching output maps. The
[record](../svgweb/root-css-padding-layout.json) preserves failing-before evidence.
Root border painting remains open.
