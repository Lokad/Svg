# Radial source-map controls

These [captured inputs](capture.json) retain object-box and user-space reflection
as paint matrices, keeping gradient coordinates separate from shape and instance placement.
They also cover interior focal-coordinate precision on rectangle fills.

The 1,003 cases use 661 SVGs and 639 native PNGs. They cover six shape types,
horizontal/vertical/double reflection, local gradient maps, templates, spreads,
alpha and fractional output scales. Retained use, symbol, pattern and mask
controls compose another reflection or shear, including nonzero focal radii.
Near-circle focal points distinguish user-space reference matrices from folded
coordinates; unchanged user-space controls accompany the regressions.
Centered and contained inner circles use native float conical coordinates.
Symbol and filtered controls preserve their recording maps, while zero-sized
viewports remain empty without requiring an inverse paint map.
Local focal-circle translations, scales and rotations retain definition
coordinates through direct and inherited paints. Collapsed nested viewports
also cover general local maps that previously rejected empty drawings.
Near-center and tangent cones retain their native coordinate dispatch and
visibility through single-color paints, glyph fills/strokes and half-pixel planes.
Mixed-color cone boundaries retain native spread, clamping and glyph coverage.
Decimal, signed, exponential and percentage coordinates keep each spelling's
native image, including the CSS decimal conversion at threshold boundaries.

Each input is compared with its own native image; mathematically equivalent
spellings can have different native pixels. Three healthy fresh browser launches
per host produced identical PNGs. Shape references agree across Windows and Linux;
font cases retain separate images for their native glyph coverage profiles.
Repeated sources and reference images are shared by exact content hashes.
Font captures embed the [committed Noto bytes](../supplied-font-labels/fonts/NotoSans-Regular.ttf);
conversion uses the [supplied catalog](../static-css-document/font-pack.json).
Tests verify the font and reconstructed native input hashes.
