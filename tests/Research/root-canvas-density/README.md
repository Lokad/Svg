# Canvas paint at fractional image boundaries

The [controls](capture.json) preserve three fresh healthy captures per host at
1.5× density, through one [canonical conversion](../../Lokad.Svg.VisualTests/SvgCanvasDensityReferenceTests.cs)
per context. Transparent and opaque host backgrounds share the existing
[empty canvas source](../root-css-box/canvas-empty.svg); a fractional image-size
control retains its own source.

Canvas rectangle coverage and image-boundary coverage combine before the solid
paint store. Substituting a packed constant shader changes edge alpha and RGB.
The [padding empty control](../root-padding/padding-empty.svg) separately keeps
the background box larger than the clipped image. Equal interiors cannot prove
these boundary contexts redundant. References and tolerances remain fixed.

[Mechanisms](../../Lokad.Svg.Tests/SvgAlphaBoundaryMechanismTests.cs) also preserve
zero bitmap coverage at the opaque-source boundary. The
[layout/paint record](../svgweb/root-css-padding-layout.json) retains original failures
and numerical stages; the existing ownership contract now checks native RGBA edges.
