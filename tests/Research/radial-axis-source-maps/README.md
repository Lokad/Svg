# Radial source-map controls

These [captured inputs](capture.json) retain object-box and user-space reflection
as paint matrices, keeping gradient coordinates separate from shape and instance placement.
They also cover interior focal-coordinate precision on rectangle fills.

The 244 cases use 209 SVGs and 207 native PNGs. They cover six shape types,
horizontal/vertical/double reflection, local gradient maps, templates, spreads,
alpha and fractional output scales. Retained use, symbol, pattern and mask
controls compose another reflection or shear, including nonzero focal radii.
Near-circle focal points distinguish user-space reference matrices from folded
coordinates; unchanged user-space controls accompany the regressions.
Centered and contained inner circles use native float conical coordinates.
Symbol and filtered controls preserve their recording maps, while zero-sized
viewports remain empty without requiring an inverse paint map.

Each input is compared with its own native image; mathematically equivalent
spellings can have different native pixels. Three healthy fresh browser launches
per host produced identical PNGs, with Windows and Linux agreeing byte for byte.
Repeated sources and reference images are shared by exact content hashes.
