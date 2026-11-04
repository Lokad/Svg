# Curve and use ownership responsibilities

Baseline: `ccb2c5bdf75e4c6c1d701f4250ef224f90754ff5`. No production, authored SVG,
native reference, tolerance or text-layout changes occur in this pass.

[Curve admission](../../Lokad.Svg.Tests/SvgCurveOwnershipTests.cs) retains all 48
inputs in their original order. Every row previously passed `rejected=false`;
removing that parameter preserves the expected successful parse. The test retains
limits 1, peak−1, peak, peak+1 and every 2,048-byte step below peak, checks each
outcome, rejects unsupported children and verifies complete release after every
attempt. Its method and fourteen stale rejection labels now describe admission.
The [case mapping](curve-ownership-cases.csv) records every old/new identity, UTF-8
input hash and TRX case ID. All four runtime/host mappings agree; truncated display
identities resolve uniquely. Reversing only the documented names, parameter and
trait changes restores the entire original source.

Five [stroke-opacity contracts](../../Lokad.Svg.Tests/SvgUseStrokeOpacityTests.cs)
reuse the existing shared-budget helper. The 4,096-byte refusal sweep, peak−1,
exact cancellation type/token, cleanup and retry remain; retry now also compares
every RGBA byte with the first render. The original allowance, identity construction
map and null viewport remain. The helper and its other callers are unchanged.

The 59 affected cases remain individually discoverable: 55 Contract and four
Mechanism. The eleven non-curve case IDs and names are unchanged. Viewport replay
keeps its physical-clip, sibling, exact PNG, separate-allowance/refusal/retry checks;
the direct parsed-group fields remain a mechanism.

## Output assets

Core copies only the files its current consumers name. Complete visual banks keep
their sources and independent references in the repository. No core consumer
enumerates these output directories.

| Bank | Before | After |
| --- | ---: | ---: |
| use-paint-properties | 31 | 6 |
| symbol-viewport-opacity | 18 | 3 |
| symbol-content | 16 | 6 |
| symbol-presentation | 16 | 4 |
| symbol-locations | 14 | 3 |
| symbol-instances | 16 | 4 |
| use-stroke-opacity | 81 | 5 |
| Total | 192 | 31 |

Fresh output on both frameworks contains 729 SVGs / 496,064 bytes, previously
890 / 583,732: 161 fewer copies and 87,668 fewer bytes per output. Every retained
copy and both library binaries are byte-identical before/after. No stale output
was used to validate the narrowed lists.

## Validation

All 59 affected cases pass on both hosts under both supported runtimes. Single
functional observations include discovery, host startup and TRX, excluding build:

| Host/runtime | Before | After |
| --- | ---: | ---: |
| Windows .NET 11 | 8.76 s | 7.84 s |
| Linux .NET 11 | 13.53 s | 11.04 s |
| Windows .NET 10 | 8.14 s | 7.87 s |
| Linux .NET 10 | 10.24 s | 9.32 s |

These samples do not establish a statistical speedup or renderer throughput.
Layer filters select 55 passing contracts and four passing mechanisms.
Full fresh-output .NET 11 core passes 47,545 cases on each host (49.65 s Windows,
55.77 s Linux). Compared with the preceding full identity snapshot, all 47,497
other case IDs/names remain; the 48 curve IDs map exactly through the CSV above.
Both affected builds are warning-free. Broader CI/package qualification remains
separate from these local checks.
