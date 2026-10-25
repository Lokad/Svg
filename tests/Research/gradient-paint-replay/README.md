# Gradient paint replay

The [capture](capture.json) retains 1,280 regressions and controls, plus 168 deferred
cases, using 728 SVGs and 214 native PNGs. It covers one-stop and zero-size linear
and radial paints, varied colors/alphas, rectangles, paths, glyph fills/strokes,
patterns, masks, instances, symbols, nested scopes, transforms and two output scales.
Combined pattern fills and gradient strokes retain independent opacity.
Zero-size factories retain constant color without dithering. Flat strokes keep
their source paint box: object-bounding-box gradients are invisible on horizontal
and vertical paths, while user-space gradients remain visible.

Each source has its own native image. Some symbol and direct paints have naturally
different pixels. Three healthy browser launches per host produced stable PNGs;
glyph cases retain Windows/Linux coverage profiles. Sources and images are shared
by content hash.

`cases` match native output on both hosts. `knownDifferences` retain evidence for
open instance-opacity, mask-text/affine-rectangle admission and affine Linux glyph
work; these references do not establish support or permanent rejection contracts.

Font captures embed the [committed Noto bytes](../supplied-font-labels/fonts/NotoSans-Regular.ttf)
under `ReviewFont`; conversion uses the [supplied catalog](../static-css-document/font-pack.json)
and `serif`. Tests verify the font and reconstructed native input hashes.
