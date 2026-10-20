# Filter owner/source regressions

The [manifest](manifest.json) retains the qualified owner/source cases and six
focused F1.5 controls. Whole-sheet membership is retired under [fixture retirement](../../README.md#retired-collections);
controls do not increase full-document counts. Original sources, options and
native reference bytes remain unchanged.

Effect order is filter -> mask -> clip -> opacity. A same-element clip surrounds
both filter and mask layers. A child clip remains inside its ancestor mask, so an
unclipped sibling stays visible. Retained uses register spatial mask content in
the final scene using the original mask id, composed map and target bounds.
Paint arithmetic and native source capture are unchanged.

The six MIT controls derive from the pinned resvg `filter-mask-clip` input;
[upstream notice](../upstream-static-documents/notices/resvg-MIT.txt) applies.
Three pair discriminators separate blur/clip, mask/clip and blur/mask. Group,
retained-use and ancestor-mask/sibling controls protect ownership. Their 36 native
captures were healthy, stable and cleaned on Windows/Linux before the relevant
comparisons/corrections. All six and the unchanged upstream document are exact.
Resource tests cover source/tile denial, cancellation, cleanup and retry.

Additional retained cases cover affine sources, masked/marker-bearing owners, viewport reentry and
bounded filtered patterns. E3 retained-instance effects and public fonts are
qualified separately; the [support contract](../../../docs/SUPPORT_MATRIX.md#supported-svg-subset)
records the finite boundaries and completed standalone qualification.
