# Gradient factory colors and viewport clips

The [capture](capture.json) retains 5,009 exact cases and 1,123 deferred cases,
sharing 3,256 SVGs and 1,395 native PNGs by content hash. It covers linear and
radial factory boundaries, pad/repeat/reflect, straight-color interval means,
partial alpha, flat offsets, 256 stops, rectangle/path/glyph replay, instances,
spatial maps, nested viewports, masks, filters and two output scales.

Native factory classification uses definition coordinates before placement and
the inclusive float threshold `1/32768`. Pad selects the terminal stop;
repeat/reflect integrate straight RGBA over stop intervals before premultiplying.
Constant paints keep their material behavior through opacity and glyph replay.
Viewport clips preserve the drawing bitmap; inherited clips around filter output
remain separate from the filter's input context.

Each source has its own native image from three healthy, stable Chrome launches
per host. Supplied glyphs retain separate Windows/Linux images. Tests verify
source/image hashes and reconstruct the embedded
[Noto font](../supplied-font-labels/fonts/NotoSans-Regular.ttf) input from the
[catalog](../static-css-document/font-pack.json).

`knownDifferences` retain numeric, mask-text, focal-circle and radial-vector
admission work, plus historical filter/clip differences. They establish neither
support nor permanent rejection contracts. Native candidate/control images can
differ; each tested source is compared with its own image.
