# Uniform filter precision

The [capture](capture.json) retains 7,044 exact and 1,206 deferred cases,
sharing 5,274 SVGs and 2,417 native PNGs by content hash. Each input has three
healthy, stable captures on Windows and Linux; both platforms agree on these
font-free images. Candidate and control spellings keep their own references.

The cases cover color alpha and flood-opacity byte boundaries, sRGB/linearRGB,
merge/composite/blend arithmetic, zero blur, shadows, offsets, primitive crops,
element/group opacity, output scaling and affine placement. They distinguish
virtual uniform paint from sampled image values, finite image coverage from
compact pixel storage, and SVG layout bounds from stored-image bounds.

[Image tests](../../Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs) verify
source and reference hashes before comparing pixels. [Ownership tests](../../Lokad.Svg.Tests/SvgUniformFilterOwnershipTests.cs)
cover native pixels, memory/work denial, cancellation, cleanup and retry.
`knownDifferences` records remaining mismatches separately for each host;
these inputs do not assert equality with their controls.
