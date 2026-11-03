# Canonical marks, ligatures and fitting

The original CC0 [control](control.svg) uses the shared [Noto Sans program](../supplied-font-labels/fonts/NotoSans-Regular.ttf)
under its [OFL](../supplied-font-labels/fonts/OFL.txt). Its four rows distinguish
mark-to-ligature chains, contextual versus ordinary kerning with a default-ignorable
barrier, and both SVG length-adjust modes. [Three healthy captures per host](capture.json)
were frozen before library comparison; preserve the complete source and references.

The [canonical regression](../../Lokad.Svg.VisualTests/SvgTextMarkReferenceTests.cs)
compares the complete image exactly on each platform. Cluster-complete fitting and
the shared canonical glyph-path route protect both length-adjust modes without
altering the references. [Shaping evidence](../svgweb/text-canonical-shaping.json)
keeps the failing-before outcomes and direct mechanism/ownership coverage.
