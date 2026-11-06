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

The [status audit](resolved-known-differences.json) selects another 499 historical
inputs now exact on both hosts: 232 routine controls and 267 individual
research cases. No admitted historical differences remain. It separately tracks 432 documented
admission boundaries (144 direct mask-text children and 288 outside focal circles).
These categories do not turn historical rejections into permanent contracts.
[The initial audit](../release-baselines/paint-claim-trace.json) and
[reflection correction](../release-baselines/gradient-reflection.json) bind the results
and unchanged native authority. The [source-request correction](../release-baselines/offset-source-request.json)
closes 128 clip/offset interactions through shared capture bounds. The
[geometry clip correction](../release-baselines/geometry-paint-clip-domain.json)
keeps incoming geometric paint clips separate from source-picture viewport clips
and constructs thin lines within their integer clip before fixed-point conversion.
It closes 51 further inputs while preserving the independently referenced source-context
bank. Candidate/control images can differ; every source keeps its own target.
No reference or tolerance was rewritten.

[Native device domains](../release-baselines/native-clip-device-domains.json)
retain a source picture's complete extent and an isolated layer's logical clip
independently of cropped addresses, closing nine instanced-viewport fill cases.
[Sampling destination bounds](../release-baselines/filter-sampling-device.json)
close the remaining 24 shader/placement interactions. Complete retained clips seed
sampling within the current destination; nonopaque filters use their painted-output
layer. Geometry capture keeps its separate ink request. Independent opacity-placement
controls preserve the distinction between owned storage and logical paint clips.
