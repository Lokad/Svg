# Radial source-map controls

These [captured inputs](capture.json) retain object-box reflection as a paint
matrix, keeping gradient coordinates separate from shape and instance placement.
They also cover interior focal-coordinate precision on rectangle fills.

The 124 cases use 122 SVGs and 119 native PNGs. They cover six shape types,
horizontal/vertical/double reflection, local gradient maps, templates, spreads,
alpha and fractional output scales. Retained use, symbol, pattern and mask
controls compose another reflection or shear, including nonzero focal radii.
Unchanged user-space controls accompany the regressions.

Each input is compared with its own native image; mathematically equivalent
spellings can have different native pixels. Three healthy fresh browser launches
per host produced identical PNGs, with Windows and Linux agreeing byte for byte.
Repeated sources and reference images are shared by exact content hashes.
