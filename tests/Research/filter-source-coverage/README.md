# Filter source coverage

These original SVGs distinguish source paint, stored graph images, cropped
aliases and blur materialization before fractional displacement. Each of the 59
inputs runs once through owned synchronous conversion against its own unaltered
Chromium 153.0.8010.52 image. The original [Canvas PNGs](images.json) and
[provenance hashes](capture-provenance.json) are preserved. Independent
[recipe-3 page captures](declared-profile.json) qualify all 59 inputs on Windows
and Linux, with three healthy, byte-identical fresh captures per input and host.
Every retained Canvas image matches its page counterpart exactly in decoded pixels.
Scaled font-free inputs have committed capture materializations with explicit root
dimensions and the original logical viewBox; that equivalence is checked per input.
The [capture procedure](../../../tools/README.md#capture-and-reference-rules)
keeps Canvas/page profiles separate when they naturally differ.
The fixtures use the repository's [MIT license](../../../LICENSE.txt).

Identical source bytes and render options are included once. Equal resulting
images do not remove different graphs: zero blur, one-input merge, zero offset,
explicit crops, SourceAlpha, source opacity and outer opacity retain their own
references. Small nonzero blurs can have a copying pixel kernel while still
materializing a different image; their references deliberately remain unequal
to direct offsets. No reference was rewritten to fit the implementation.

[Mechanism vectors](mechanisms.json) retain 20 native DOM deviation values and
90 half-covered texels from the independent integer-source and fractional-offset
images. Direct tests cover parsing, floating paint rounding and the effective-zero
axis boundary separately from rendering contexts. The `.02` and `2e-2` spellings
illustrate distinct native float-place-value rounding at the materialization
threshold. Numerical vectors do not repeat across API destinations.

Two alias contracts check work refusal, memory refusal, cancellation, cleanup
and retry, covering source paint and the separately owned linear sample buffer.
The representative API matrix remains responsible for destination and
prepared-document forms. The [canonical rendering test](../../Lokad.Svg.VisualTests/SvgFilterSourceCoverageReferenceTests.cs)
and [mechanism/ownership tests](../../Lokad.Svg.Tests/SvgFilterSourceCoverageTests.cs)
keep those responsibilities separate.
