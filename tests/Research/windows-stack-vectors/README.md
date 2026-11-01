# Stack projection vectors

The original CC0 [font recipe](font-recipe.json) and [SVG](stack-vectors.svg)
distinguish loading supplied F2Dot14 components from normalizing a new line vector.
Renormalizing rounded `GPV` output changes a valid component from 15426 to 15425.
Windows `SPVFS` preserves its supplied words, replaces the dual projection and
leaves the freedom vector independent. Non-unit and zero probes record native
compatibility behavior; the OpenType contract requires normalized input.

[Capture provenance](capture.json) pins the unchanged input/program and separate
Windows/Linux references, with three agreeing fresh healthy processes per host.
Missing-font and changed-geometry probes establish supplied-program use.
Linux light fitting retains its independent result.

The numerical [relative-move input](movement/relative-move.svg),
[program](movement/Move.ttf) and [Windows marker](movement/windows.png)
establish the following movement independently: three fresh captures read final
X=107, while the former managed normalization gives 108. The recipe records the
original points and bytecode. This companion supplies a direct mechanism's golden
value; it adds no rendering-suite path or Linux movement claim.

Eight [mechanism cases](../../Lokad.Svg.Tests/TrueTypeHintingTests.StackVectors.cs)
protect signed words, rounded unit vectors, dual replacement, freedom, cleanup
and symmetric/relative-move round trips. The existing classic normalization cases
retain their values under the classic target. One canonical
[rendering test](../../Lokad.Svg.VisualTests/SvgWindowsScanBoundaryTests.cs)
covers the supplied program. Existing masks and references remain unchanged.
