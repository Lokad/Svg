# Gradient stop preparation

The [capture](capture.json) contains 7,242 exact cases, sharing 3,626 SVGs and
1,933 native PNGs by content hash. It covers implicit endpoints, duplicate stops,
flat ranges, float-collapsed offsets, nearly uniform positions, four to 256 stops,
linear/radial paints, three spread modes, both units, partial alpha, shapes,
supplied glyph fills/strokes, instances and two output scales. Affine radial
controls retain their own terminal-padding references.

Every source has three healthy, stable browser captures per host. Proposed
canonical controls are independently captured. Glyph cases retain separate
Windows/Linux images, using the [committed Noto bytes](../supplied-font-labels/fonts/NotoSans-Regular.ttf)
and [catalog](../static-css-document/font-pack.json). Tests verify source, image,
font and reconstructed native-input hashes.

Normal ramps and glyph representative colors share native stop canonicalization.
Uniform intervals use fixed gap-count coefficients; arbitrary intervals retain
constant endpoint padding and skip coincident or nonfinite-reciprocal gaps.
Factory classification and weighted factory means use the original definitions.

The capture also records 4,608 DOM observations confirming that the collapsed
source offsets parse as `[.5,.5,.5]`. Its explicit controls use `[0,.5,.5,1]`.
These sources render identically in the pinned browser and are admitted by the
shared preparation path.
