# Gradient spread stages

The [387 captured cases](capture.json) use 195 SVGs and 285 native PNGs.
Three radial mirror regressions distinguish float operation order at a channel
rounding boundary, including composed paints. Controls cover negative, integer,
half and fractional periods on radial and linear gradients, direct and inherited
definitions, rectangles, paths, glyph fills/strokes and two output scales.

Each source has its own native image. Three healthy browser launches per host
produced identical PNGs. Shape references agree across Windows and Linux;
glyph cases retain each platform's native coverage. Shared sources and images
are deduplicated by content hash.

Font captures embed the [committed Noto bytes](../supplied-font-labels/fonts/NotoSans-Regular.ttf)
under `ReviewFont`; conversion uses the [supplied catalog](../static-css-document/font-pack.json)
and the `serif` family. Tests verify the font and reconstructed native input hashes.
