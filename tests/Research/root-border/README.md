# Root border composition

The four original CC0 [controls](capture.json) retain three agreeing healthy native
captures per host. [Canonical conversion](../../Lokad.Svg.VisualTests/SvgRootBorderReferenceTests.cs)
checks each once alongside the fourteen frozen [box-layout contexts](../root-box-layout/README.md).

- `mixed-alpha` protects nested border opacity groups, unequal widths, padding,
  the canvas backdrop and root source opacity.
- `same-alpha` protects one opacity group with distinct edge colors and AA miters.
- `partial` protects the translucent side-union path, root `currentColor`, important
  stylesheet declarations and root opacity.
- `transparent` keeps an unpainted border's inset and diagonal color transition.

[Mechanisms](../../Lokad.Svg.Tests/SvgRootBorderLayoutTests.cs) cover declaration
forms, used-width rounding and separate border/content boxes. Two
[ownership paths](../../Lokad.Svg.Tests/SvgRootBoxOwnershipTests.cs) distinguish
an allocation-free frame from an owned border opacity layer, retaining refusal,
refunds and retry. Padding and borders share one prepared-font contract setup.

The [record](../svgweb/root-css-border-layout.json) retains failing-before evidence.
Other border styles and rounded borders remain outside the support contract.
