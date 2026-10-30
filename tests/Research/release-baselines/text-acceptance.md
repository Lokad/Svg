# Original text acceptance

The [declaration map](../text-node-boundaries/original-acceptance.json) retains 76
original SVG declarations: 38 fragment/anchor/fitting inputs, the original R49
hidden-return pair and 36 paint-restoration calibration inputs. Source bytes,
candidate/control roles, ordering, font selection, scale and original observations
remain explicit. These synthetic project inputs use the [repository license](../../../LICENSE.txt);
the supplied [Noto Sans face](../supplied-font-labels/README.md) retains its OFL notice.

Fresh independent Chrome captures use the recorded platform pins, recipe 3,
transparent backgrounds and device scale 1. Each declaration has three healthy,
byte-stable launches per host, with successful profile cleanup. Failed launches
were excluded. Native input hashes bind the embedded font and capture-only family
binding; the bank's font-provenance checks reconstruct that input independently.
Fresh Windows captures also agree with their preserved original captures where
those originals exist: all 38 fragment inputs and the hidden-return pair.

The [canonical bank](../text-node-boundaries/capture.json) adds 65 individually
discovered rendering cases. Eleven declarations share canonical cases only after
exact source, native-input, scale and font identity checks: two already existed,
and nine duplicate flat controls share their matching calibration input. Every
original declaration remains in the map. The existing 594 cases and references
are unchanged; the bank now has 659 cases and no known differences.

All 38 candidate/control pairs retain independent evidence. Nine pairs have
unequal browser images on each host; none is treated as an equality oracle.
Every declaration has different Windows/Linux pixels, so both references remain.
The current renderer matches all 76 declarations exactly on both hosts under
both supported runtimes, without changing text layout or tolerances.

[Rendering checks](../../Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs) use
the existing canonical raster path. The representative complete-PNG paths and
[38 ownership/replay contracts](../../Lokad.Svg.Tests/SvgTextNodeMetricsTests.cs)
remain separate. [Integrity checks](../../Lokad.Svg.VisualTests/SvgOriginalTextAcceptanceTests.cs)
protect declaration coverage, source/options identity, native hashes, stable
launches, both platform images and every pair's equality or difference.

Contract discovery now defers font bytes, catalog parsing and capture JSON until
execution. Three fresh sequential Windows .NET 11 processes enumerate the same
38 IDs against identical fixtures in isolated before/after probes. Median discovery
falls from **30.86 ms / 4,333,952 allocated bytes** to **0.58 ms / 7,696 bytes**.
The ordered `method:id` identity hash remains
`9c54f3f4a68fa5bec3789a2f586ebdbfc39485fcb06d29f61dbcbbd02012fece`.
Discovery also succeeds without any fixture files; before, static initialization
failed on the missing font. Assertions and contract inputs are unchanged.

Affected validation passes 38 contracts and 665 rendering/provenance checks per
host/runtime combination. Single-run wall times, including host, whole-assembly
discovery and TRX but excluding build:

| Host/runtime | Contracts | Rendering/provenance |
| --- | ---: | ---: |
| Windows .NET 11 | 8.17 s | 15.18 s |
| Windows .NET 10 | 10.06 s | 13.62 s |
| Linux .NET 11 | 10.94 s | 16.57 s |
| Linux .NET 10 | 9.19 s | 13.16 s |

These are validation costs, not renderer performance or evidence of an end-to-end
speedup. Broader R48/R49/R60/R61 precision/raster acceptance and the R103/R104/R108
residuals remain open; this cohort does not establish their closure.
