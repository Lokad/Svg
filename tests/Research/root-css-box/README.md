# Root canvas and CSS box

The outer root's `background-color` paints the SVG image canvas independently of
element opacity and visibility. Borders are element paint and also move the content
origin. Their layout work remains open; a background rectangle cannot model both.

The original CC0 [controls](capture.json) retain three agreeing healthy native
captures per host. Admission and root-generation captures precede their corrections;
the child-filter capture adds independent interaction coverage. Each uses one
[canonical conversion](../../Lokad.Svg.VisualTests/SvgCanvasBackgroundReferenceTests.cs)
with exact pixels:

- [Source opacity](canvas-source-opacity.svg) keeps translucent canvas paint outside
  the root opacity layer while gradient and transformed shapes compose inside it.
- [Cascade and applicability](canvas-cascade-applicability.svg) preserves priority,
  `currentColor`, hidden-root visibility and inert shape/nested-viewport backgrounds.
- [Empty document](canvas-empty.svg) paints its canvas despite zero element opacity
  and hidden visibility, without constructing a scene.
- [Root generation](canvas-root-display.svg) keeps image content generated under
  root `display:none`, including pattern preflight, while an explicitly inheriting
  child stays hidden.
- [Child filter](canvas-source-alpha.svg) retains canvas paint as a backdrop,
  outside the filtered group's `SourceGraphic` and opacity.

[Direct mechanisms](../../Lokad.Svg.Tests/SvgCanvasBackgroundTests.cs) cover style
priority, root color/defaults, applicability and computed versus generated display.
One [preparation contract](../../Lokad.Svg.Tests/OpenTypeFontTests.TraversalMetadata.cs)
checks validation before acquisition, selected fonts and retry/replay. One
[ownership case](../../Lokad.Svg.Tests/SvgCanvasBackgroundOwnershipTests.cs) protects
borrowed image coverage and the output-only reservation, refusal and retry.
A separate [one-pixel rendering check](../../Lokad.Svg.Tests/PublicContractTests.cs)
protects the full-canvas solid-paint shortcut against dropping the earlier canvas
color; its RGBA reference has six independent healthy native captures.
[Original outcomes and box review](../svgweb/root-canvas-background.json) keep the
remaining border/layout work separate from closed canvas behavior.
The [layout controls](../root-box-layout/README.md) freeze those open behaviors
independently, with explicit output density and image/content boundaries.
The [padding slice](../root-padding/README.md) now shares box/content maps and
clipping. [Fractional canvas controls](../root-canvas-density/README.md) separately
protect background rectangle coverage before the image boundary and solid store;
the ownership test checks their native RGBA edges. Border painting remains open.
