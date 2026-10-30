# Resource-selection acceptance

The reviewed R74 font-selection failures are closed within the
[declared support boundary](../../../docs/SUPPORT_MATRIX.md). C04 nested SVG in
retained definitions remains an explicit limitation, with a painted native control;
this record does not claim support for it.

[Direct mechanism tests](../../Lokad.Svg.Tests/SvgPatternSelectionMechanismTests.cs)
retain all 512 original inputs from the three acceptance declarations: 96 early
pairs, 104 reachability pairs and 56 intrinsic-geometry pairs. The
[committed corpus](../../Lokad.Svg.Tests/TestData/pattern-selection-acceptance.json)
keeps every SVG unchanged, source hashes, source order, font-bearing resource form,
candidate/control role and expected selection. It declares 216 font-free inputs,
288 requiring `serif` and eight rejected singular transforms. No fonts, PNGs or
API matrix are needed to exercise those selection mechanisms.

The existing ownership class defers immutable font bytes and catalog parsing until
its contracts execute. Enumerating its 54 IDs with capture metadata alone failed
before that change because discovery read the font file; it now succeeds without
font resources. Source graphs, acquisition, native-pixel and cleanup assertions remain.

The frozen `76fe08b37e47d38c93cc4da988afc488532d9404` implementation selects
unnecessary fonts for 168 inputs; the other 344 meet their declarations, including
the eight explicit errors. Its measured assembly SHA-256 is
`3b037b1d6a6a6a74d01093ffa16344b47be2bd86ddac06e06e5e7f8739842a38`.
All 512 declarations pass with the current implementation.

| Original concern | Retained integration evidence |
| --- | --- |
| Zero geometry, stroke width and inactive paint channels | [Empty targets](../../Lokad.Svg.Tests/OpenTypeFontTests.EmptyPatternTargets.cs), [channel/cascade controls](../../Lokad.Svg.Tests/OpenTypeFontTests.InactivePatternChannels.cs) and [independent empty-source images](../../Lokad.Svg.VisualTests/SvgEmptyContourReferenceTests.cs). |
| Unused definitions, overridden group channels, switches and inherited suppression | [54 native source-selection inputs](../pattern-source-selection/capture.json), with both definition orders and separate source graphs. |
| Visible descendant overrides and sources used outside suppressed parents | The native bank's `source-visible-descendant` and `source-source-outside-opacity-parent` cases; the original corpus also retains both earlier live modes. |
| Child-content replacement, late references and source validation | [Pattern overrides](../../Lokad.Svg.Tests/OpenTypeFontTests.PatternOverrides.cs), including inherited-font and active-filter interactions. |
| Actual acquisition, borrowed storage, refusal, cancellation and retry | [Ownership contracts](../../Lokad.Svg.Tests/SvgPatternFontReachabilityTests.cs), retaining all original cases and assertions. |
| Nested pattern paints, singular sources and retained nested SVG | [Categorized pattern rejection](../../Lokad.Svg.Tests/SvgPatternDeferredPaintTests.cs) and [the native-backed C04 exclusion](../../Lokad.Svg.VisualTests/SvgTextInstanceCompositionReferenceTests.cs). |

Native images and platform differences remain unchanged. Candidate/control images
are never used to infer dependency equivalence. New selection checks complement
the existing native and ownership tests; no former regression is removed or moved
to research, and no new rendering/API cross product is introduced.

Three fresh sequential Windows .NET 11 processes measured the same 512-input
public raw/snapshot/prepared diagnostic at 3.72 seconds median and the direct
mechanism test selection at 6.75 seconds. Both include process startup; the latter
also includes whole-assembly discovery and TRX. The mechanism assertions take
about 0.53 seconds. These are different validation hosts, so they establish no
end-to-end speedup or renderer throughput claim. The benefit is versioned,
independently selectable mechanisms without acquiring fonts or rendering.

Affected qualification passes on Windows/Linux under both supported runtimes:
2,875 core checks and 274 native/provenance/capture checks per combination. All
2,363 original core and 274 native case identities remain; the 512 source mechanisms
are additions. Existing ownership assertions and independent references are unchanged.
