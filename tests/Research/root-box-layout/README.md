# Root CSS box layout

The [original CC0 controls](capture.json) freeze independent browser output before
root border and padding admission. Each has three agreeing healthy captures on
Windows and Linux. `padding-130` and `padding-132` now have exact
[canonical assertions](../../Lokad.Svg.VisualTests/SvgRootPaddingReferenceTests.cs).
The fourteen border contexts now have exact
[canonical assertions](../../Lokad.Svg.VisualTests/SvgRootBorderReferenceTests.cs).
The capture-time status fields remain historical provenance.

The controls separate responsibilities:

- `meet`, `slice` and `none-alpha` distinguish SVG viewBox fitting from the CSS box,
  including border color alpha, root opacity and the canvas backdrop.
- `overflow-default` and `overflow-hidden` retain the shifted content clip. Equal
  images preserve distinct authored overflow states.
- `padding-130` and `padding-132` straddle a CSS layout subpixel boundary.
- `asymmetric` observes independent edge widths/colors and their corner join.
- `box-sizing` retains the fixed-size image context separately from `meet`;
  their equality does not qualify other sizing contexts.
- `hidden-border` keeps the content inset while hiding border paint; an explicitly
  visible child still paints. `none-display-border` preserves image-root generation.
- Four `fractional` contexts use the same source at densities .75, 1, 1.5 and 2,
  separating fractional image projection, border snapping and padding layout.
  The 2× control retains an enclosing 36×24 native screenshot and a declared
  35×23 comparison window; the native PNG is never resized.
- `empty-border` keeps border paint and source opacity without SVG children.

The shared correction must keep canvas paint, CSS box paint and SVG content maps
distinct, including clipping and opacity bounds. Do not cancel an SVG viewBox map
to reconstruct a CSS box map, infer layout from final ink, or adjust frozen pixels.
Root filters and root clip paths remain explicit scope limitations.
The [padding](../root-padding/README.md) and [border](../root-border/README.md)
controls share placement, snapping, clipping and box paint stages.

[Recorded outcomes and model requirements](../svgweb/root-css-box-layout.json)
preserve all sixteen failing-before contexts on both hosts.

[Density controls](../root-image-density/README.md) qualify the capture mode against
existing supported output. Reproduce each layout control with the manifest's image
viewport, physical screenshot size and output scale through
[capture-chrome](../../../tools/README.md#capture-and-reference-rules).
