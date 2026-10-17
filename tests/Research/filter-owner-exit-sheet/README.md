# Filter owner/source regressions

The [manifest](manifest.json) retains the qualified owner/source cases and six
focused F1.5 controls. Whole-sheet membership is retired under [fixture retirement](../../../docs/INVENTORY.md#f1-retirement-dispositions-2026-10-17);
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
[Qualification](../../../artifacts/full-fixture-replacement-20261017/effect-order-qualified-results.json)
records 523 affected core / 1,963 visual-resource checks per platform, including
source/tile denial, cancellation, cleanup and retry.

Earlier owner/source evidence remains in the dated [qualification](../../../artifacts/maintenance-review-20261012/filter-owner-qualified-results.json).
It covers affine sources, masked/marker-bearing owners, viewport reentry and
bounded filtered patterns. E3 retained-instance effects and public fonts are
qualified separately; the [support contract](../../../docs/SUPPORT_MATRIX.md#supported-svg-subset)
records the finite boundaries and completed standalone qualification.
