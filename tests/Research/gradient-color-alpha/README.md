# Gradient color alpha

The [capture](capture.json) contains 9,623 exact and 581 deferred cases, sharing
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
contract. The deferred cases record ordinary solid-paint precision and filter
arithmetic differences in `knownDifferences`, with separate results per host.
