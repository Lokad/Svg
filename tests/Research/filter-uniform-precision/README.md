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

Routine rendering retains 4,532 cases, preserving graph topology, operands,
color space, crops, backdrops, placement and scale. The [research selection](research-cases.json)
retains the other 2,512 numerical/context combinations as individually discovered
`Category=Research` tests, included in unfiltered CI. Selection groups alpha
values by zero, opaque, tiny (at most 1/255), low, midpoint, high and near-opaque
(at least 254/255), separately for numeric, percentage, exponent and hex grammar;
RGB/HSL channels and source structure stay distinct. It does not compare images.
New captures remain routine unless explicitly added to this list.

[Direct mechanism tests](../../Lokad.Svg.Tests/SvgAlphaBoundaryMechanismTests.cs)
check all 1,278 distinct transparent-background flood color/opacity token pairs
against native alpha bytes. [Vectors](../../Lokad.Svg.Tests/TestData/filter-alpha-boundaries.json)
retain original source/reference SHA-256 provenance. Backdrop and graph
composition remain rendering responsibilities; no source or reference was removed
or regenerated. See the [validation commands](../../README.md).
