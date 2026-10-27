# Affine filter restoration

These 35 original inputs retain independent Windows/Linux recipe-3 page captures
from pinned Chromium 153.0.8010.52. Each reference has three byte-identical,
healthy fresh-process captures. [Metadata](capture.json) records source, PNG and
decoded-pixel hashes, dimensions, background, recipe and platform pins.

The cases separate captured storage, logical image bounds, requested output and
the nonzero bounds of an antialiased crop. Fourteen affine/source controls cover
operating spaces, scale, rotation, source-bound expansion and transparent tails.
Thirteen offset controls cover both axes, positive/negative sprite-admission ties,
source alpha and picture-backed consumers. Eight interactions retain negative and
reflected affine placement, offset chains, merge aliases, primitive crops and
nontransparent backdrops.

Each input uses its own native reference. The unfiltered control differs naturally
from the affine filter at 1,518 alpha pixels. The sRGB and linear-RGB outputs differ
at 151 alpha pixels; right, bottom and combined tail crops differ from the original
source-alpha input at 78, two and 80 alpha pixels. The rendering tests check those
contrasts as well as exact output, preserving the distinctions between the paths.

The [rendering tests](../../Lokad.Svg.VisualTests/SvgFilterClipOwnershipReferenceTests.cs)
use one canonical owned conversion per input. Direct sprite-boundary mechanisms
and representative replay/ownership contracts run separately; the rendering bank
does not repeat every destination form. See the [test responsibilities](../../README.md),
[capture procedure](../../../tools/README.md#capture-and-reference-rules), related
[restoration controls](../filter-restoration/README.md), and [MIT license](../../../LICENSE.txt).
