# Filter image restoration

These thirty original inputs use independent Windows/Linux recipe-3 page
captures from pinned Chromium 153.0.8010.52. Each reference has three byte-identical,
healthy fresh-process captures. [Metadata](capture.json) retains source/image hashes.
Nineteen cases failed before the restoration correction; eleven are positive controls.

The cases distinguish floating color-transfer output, stored sRGB images,
materialization before non-unit opacity, and picture-backed pattern restoration.
They retain source alpha, constant-gradient/solid controls, offset chains,
single-input and multi-input merges, unused branches, and a nontransparent backdrop.
The three operating-space pairs have independently unequal native edge colors,
with equal alpha. Their differences remain checked alongside each exact image.

The [rendering tests](../../Lokad.Svg.VisualTests/SvgFilterStageReferenceTests.cs)
use one canonical conversion per input. Numerical transfer, parser and ownership
checks remain separate; destination forms stay in the representative API matrix.
See the [capture procedure](../../../tools/README.md#capture-and-reference-rules),
[related clip controls](../filter-clip-ownership/README.md), and repository
[MIT license](../../../LICENSE.txt).
