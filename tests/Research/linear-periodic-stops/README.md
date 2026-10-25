# Periodic gradient stop ranges

The [capture](capture.json) contains 1,702 exact cases and 32 deferred glyph
controls, sharing 870 SVGs and 310 native PNGs by content hash. Linear and radial
paints vary stop ranges, implicit endpoint padding, two/three-stop ramps,
pad/repeat/reflect, both coordinate units, rectangles, circles, supplied glyphs,
instances and two output scales. Historical affine shape/stroke controls retain
their own images.

Spreading uses native float coordinates before stop-range normalization. Stops
inside `(0,1)` keep the full repeat period and pad the uncovered intervals.
Linear and radial replay share the same absolute-coordinate ramp.

Every source has three healthy, stable native captures per host. Candidate and
control images can differ; each case uses its own reference. Glyph captures embed
the [committed Noto bytes](../supplied-font-labels/fonts/NotoSans-Regular.ttf),
with separate Windows/Linux images and reconstructed native-input hashes. The
renderer uses the [supplied catalog](../static-css-document/font-pack.json).

The deferred controls expose a separate glyph-preblend difference involving
duplicate endpoint stops. They establish neither support nor a rejection contract.
