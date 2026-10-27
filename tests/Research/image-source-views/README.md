# Embedded image source views

The [capture manifest](capture.json) retains unchanged Chromium 153 PNGs from
three healthy, stable process captures for each input and platform. Authored
SVGs, explicit scaled capture inputs and per-platform hashes remain separate.

These 120 canonical rendering tests account for 126 original/acceptance roles
in the [coverage index](coverage-index.json). Six roles share a test only because
their authored bytes, options and native pixels agree on both hosts. Each
actual input has its own browser reference; unequal graphs remain independent.
The [baseline comparison](before-correction.json) preserves the pre-correction
results, including exact controls and already-correct uniform-image alpha cases.

The inputs cover both crop axes and alignment endpoints, fractional and
one-pixel views, transparency, opacity, intrinsic dimensions, output scaling,
reflection/shear, instances, patterns, clipping, masks and ordinary filter
consumers. Minification cases distinguish rounded decoded requests and ceil
dimensions from floor-sized sampling mips, including odd extents, one-pixel
axes, empty requests and picture mip interpolation. Source crop and destination
coverage are independent; reductions retain parent stride and premultiplied alpha.

[Mechanism tests](../../Lokad.Svg.Tests/SvgImageSourceViewTests.cs) exercise
placement, enclosure, stride, decode/mip selection, all eight reduction kernels
and cancellation directly. Three selected
[ownership inputs](../../Lokad.Svg.Tests/SvgFilterBitmapOwnershipTests.cs) protect
source-replacement resize, mip reduction and adjacent picture-level storage,
including refusal, work exhaustion, cleanup, retry and prepared memory replay.
The rendering bank uses one conversion path per input; general API destination
contracts remain in the representative matrix.
