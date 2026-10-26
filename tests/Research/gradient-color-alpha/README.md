# Gradient color alpha

The [capture](capture.json) contains 10,060 exact and 144 deferred cases, sharing
6,154 SVGs and 1,677 native PNGs by content hash. Percentage, numeric and encoded
alpha spellings retain their own browser references. The cases cover RGB/HSL,
function aliases, case, whitespace, exponent notation, every byte-alpha midpoint,
linear/radial gradients, stop opacity, glyph fills/strokes, patterns, masks,
filters, degenerate factories, instances, tiny full/partial canvases and two output scales.

Each source has three healthy, stable captures on Windows and Linux. Glyph
references retain each platform's image and use the [committed font bytes](../supplied-font-labels/fonts/NotoSans-Regular.ttf)
and [catalog](../static-css-document/font-pack.json). Tests verify source, PNG,
font and reconstructed browser-input hashes before comparing pixels.

Gradient stops retain native functional-color precision. Numeric fast parsing
and full parsing have different alpha quantization; RGB/hex byte normalization
also differs from HSL fast parsing. Public byte colors retain their existing
contract. The deferred cases record filter-consumer differences in
`knownDifferences`, with separate results per host. Additional
[solid-paint controls](../solid-paint-alpha/README.md) cover inheritance and text opacity.

Routine rendering retains 8,760 cases, including every extended numerical,
syntax and tiny-canvas boundary. The [research selection](research-cases.json)
retains 1,300 repeated numerical/context combinations as individually discovered
`Category=Research` tests, included in unfiltered CI. Selection keeps bank, XML
structure, geometry, paint role, font, scale and alpha regime/grammar distinct;
it does not infer redundancy from equal images. The regime rules are shared
with the [filter bank](../filter-uniform-precision/README.md). New captures stay
routine unless explicitly reviewed into the list. All original sources, native
references, per-platform differences and exact comparisons remain unchanged.
See the [validation commands](../../README.md).
