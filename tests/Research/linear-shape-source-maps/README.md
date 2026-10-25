# Linear paints on retained shapes

The [capture record](capture.json) covers linear-gradient fills on circles,
ellipses and rounded rectangles. Definition coordinates, local gradient maps and
shape placement remain separate, including through templates and instances.

The 119 inputs include 90 source-map regressions, 15 legacy controls, six filtered
fills and eight empty-shape cases. They cover both gradient unit systems,
pad/repeat/reflect spread, rotation, shear, reflection, fractional output scales,
direct/use/symbol/pattern/mask consumers, and independent solid, dashed or painted
strokes. Empty radii and dimensions retain transparent output.

Each input is compared with its own complete native image. Shape and path
spellings can produce different native pixels. Three healthy fresh browser
launches per host produced identical PNGs; Windows and Linux also agreed byte
for byte. The 119 SVGs share 101 reference files, deduplicated by exact PNG bytes.
