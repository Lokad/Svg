# Gradient factory colors and viewport clips

The unchanged [capture](capture.json) retains 5,201 original exact cases and 931
historical differences, sharing 3,256 SVGs and 1,395 native PNGs by content hash. It covers linear and
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

The [status audit](resolved-known-differences.json) selects another 283 historical
inputs now exact on both hosts/runtimes: 16 routine controls and 267 individual
research cases. It separately tracks 216 admitted differences (212 clip/filter
compositions and four near-threshold spatial gradients) and 432 documented
admission boundaries (144 direct mask-text children and 288 outside focal circles).
These categories do not turn historical rejections into permanent contracts.
[Qualification](../release-baselines/paint-claim-trace.json) binds the results and
unchanged native authority. Candidate/control images can differ; every tested
source keeps its own target. No reference or tolerance was rewritten.
