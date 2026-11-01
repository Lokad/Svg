# Nested patterns

The own CC0 [control](inputs/nested-grid.svg) combines forward template aliases,
shared child content, nested rectangle/polygon paints and separate tile placement.
The [manifest](capture.json) records three agreeing healthy, profile-cleaned native
captures per host. The [rendering test](../../Lokad.Svg.VisualTests/SvgNestedPatternReferenceTests.cs)
uses one canonical conversion and exact pixels.

The [glyph control](inputs/nested-glyphs.svg) covers direct and path text painted
by a child pattern. It uses the original CC0
[PlainKern program](../kerning-precedence/README.md); the manifest records the
font bytes and browser embedding recipe. Windows and Linux references remain
separate because their glyph rasterization differs. The rendering check uses
the supplied font through the same canonical conversion path.

[Mechanisms](../../Lokad.Svg.Tests/SvgNestedPatternTests.cs) cover ordering,
shared owners, cycles through aliases and masks, and content-depth boundaries.
Existing pattern text tests retain direct/text-path interactions; prepared-font
selection and [memory refusal/retry](../../Lokad.Svg.Tests/SvgNestedPatternOwnershipTests.cs)
cover their distinct ownership paths.

[Qualification](../svgweb/nested-patterns.json) keeps the complete SVGWeb original
separate: it renders, but its remaining colour differences are still open.
Use the [capture procedure](../../../tools/README.md#hosted-svgweb-inventory);
preserve reference bytes and exact comparisons.
