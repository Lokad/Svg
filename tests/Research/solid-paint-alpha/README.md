# Solid paint alpha and text opacity

The [capture](capture.json) retains 2,361 exact and 783 deferred cases, sharing
1,572 SVGs and 2,374 native PNGs. Each spelling has three healthy, stable captures
on Windows and Linux. Font cases use the [committed Noto Sans](../supplied-font-labels/fonts/NotoSans-Regular.ttf).

Coverage includes source alpha, inherited `currentColor`, fills/strokes, uses,
patterns, masks, glyph spans, decorations, text opacity, clips, deferred text
paths, backgrounds, affine placement and output scaling. Text opacity encloses
the composed glyph, stroke and decoration drawing.

[Image tests](../../Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs) verify
source, reference and reconstructed font-input hashes. [Ownership tests](../../Lokad.Svg.Tests/SvgSolidPaintOwnershipTests.cs)
cover memory denial, cancellation, cleanup and retry.
`knownDifferences` preserves the capture's deferred rendering and admission
observations per host, separately from this bank's qualified cases. Those
historical outcomes do not define current alias or span-opacity support.
