# Local benchmark baseline

This is the initial local baseline for the current supported subset. It is a
comparison point for follow-up measurements on the same environment, not a
performance gate or a claim about representative SVG workloads. The `ShortRun`
job has three measured iterations, so its confidence intervals remain broad.

## Reproduction

```powershell
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks -- -j Short -f "*"
```

BenchmarkDotNet writes the complete raw log, CSV and HTML report under its
ignored `BenchmarkDotNet.Artifacts/` directory. Preserve a result separately
when comparing revisions; do not infer a regression from one run on a different
machine or runtime.

## Baseline captured 2026-09-20

- BenchmarkDotNet 0.14.0; .NET SDK 10.0.401; .NET 10.0.12 x64 RyuJIT.
- Windows 10 22H2 (10.0.19045.6456), Intel Xeon W-2223 3.60 GHz, four physical
  / eight logical cores, concurrent workstation GC.
- `ShortRun`: one launch, three warmups and three measured iterations.
- Each input is one explicitly sized SVG with a full red rectangle. Input setup
  and caller destination allocation occur outside the timed method. The stream
  methods reset the same preallocated caller-owned `MemoryStream` before every
  conversion.

| API form | 1×1 | 64×64 | 512×512 |
| --- | ---: | ---: | ---: |
| Owned memory | 15.82 μs / 6.13 KB | 124.30 μs / 22.83 KB | 3,381.13 μs / 1,046.57 KB |
| Caller memory | 15.75 μs / 5.95 KB | 113.48 μs / 22.48 KB | 3,523.61 μs / 1,040.31 KB |
| Stream | 12.85 μs / 5.98 KB | 113.76 μs / 22.33 KB | 3,447.98 μs / 1,034.29 KB |
| Async stream | 37.32 μs / 6.71 KB | 167.90 μs / 22.99 KB | 3,729.32 μs / 1,034.97 KB |

Each cell is mean time followed by BenchmarkDotNet managed allocation per
conversion. It is allocation traffic, not retained memory or `RenderBudget`
peak. The probe scenarios described in the root README cover the separate
process/outcome evidence; they do not make an OS working-set measurement into a
renderer memory limit.

## Baseline captured 2026-09-21 (R19 measurement slice)

- BenchmarkDotNet 0.14.0; .NET SDK 10.0.401; .NET 10.0.12 x64 RyuJIT.
- Windows 10 22H2 (10.0.19045.6456), Intel Xeon W-2223 3.60 GHz, four physical
  / eight logical cores, concurrent workstation GC.
- `ShortRun`: one launch, three warmups and three measured iterations.
- These are pre-optimization baselines for the R19 source-inspection findings.
  They do not authorize a pooling or caching change on their own.

Reproduction:

```powershell
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks -- -j Short -f "*SvgParseBenchmarks*"
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks -- -j Short -f "*FontIntegrityBenchmarks*"
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks -- -j Short -f "*SvgTextBenchmarks*"
```

### Style-heavy admission (`SvgParseBenchmarks`)

- `ConvertAttributeHeavyShapes` converts one 64x64 SVG with 64 rectangles, each
  carrying a full admitted attribute set (`id`, `class`, `x`, `y`, `width`,
  `height`, `rx`, `ry`, `fill`, `fill-opacity`, `stroke`, `display`,
  `visibility`, inline `style`), through the public owned-memory API. Input
  setup occurs outside the timed method.
- `DiscoverStyledTextRequests` runs internal font-request discovery over one SVG
  with a root stylesheet rule, group-inherited style and eight styled `text`
  elements with nested `tspan` spans (XML whitespace, scalar `letter-spacing`,
  stylesheet-selected span class). The eight texts deduplicate to one request.
  Font acquisition is not included.

| Method | Mean | Allocated |
| --- | ---: | ---: |
| `ConvertAttributeHeavyShapes` | 317.16 μs | 92.1 KB |
| `DiscoverStyledTextRequests` | 25.61 μs | 30.51 KB |

Full ShortRun columns: `ConvertAttributeHeavyShapes` Gen0 21.4844 / Gen1 0.9766;
`DiscoverStyledTextRequests` Gen0 7.2327 / Gen1 none. Each cell is mean time
followed by BenchmarkDotNet managed allocation traffic, not retained memory or
`RenderBudget` peak.

### Repeated integrity verification (`FontIntegrityBenchmarks`)

- `VerifyProgramHash` hashes one synthetic caller-owned program (`byte(i * 31)`)
  at 4,096 and 1,048,576 bytes via the shared `SvgFontProgramIntegrity` path,
  returning the 32-byte digest length so the hash is retained. Input
  setup occurs outside the timed method; no provider or snapshot work is
  included.

| Program bytes | Mean | Allocated |
| --- | ---: | ---: |
| 4096 | 19.93 μs | 344 B |
| 1048576 | 5,086.66 μs | 347 B |

After returning the digest bytes instead of a fresh hex string per verification
(manifest bytes are parsed once at catalog load):

| Program bytes | Mean | Allocated |
| --- | ---: | ---: |
| 4096 | 18.52 μs | 192 B |
| 1048576 | 4,534.44 μs | 197 B |

The per-verification saving is the discarded hex string (about 150 bytes); hashing
time is unchanged within noise. Manifest comparison semantics are unchanged.

Allocation was the fresh hex identity per verification plus hashing traffic;
elapsed time scales with program length. This does not measure acquisition,
snapshot field validation, cancellation latency or repeated/distinct program
behavior.

### Supplied-font text phases (`SvgTextBenchmarks`)

- The input is one 64x64 SVG with eight styled `text` elements (inherited family
  and size, scalar `letter-spacing`, XML whitespace modes, stylesheet-selected
  span class, nested `tspan` spans), all rendering the admitted scalar `A`.
  Font bytes are an embedded 342-byte synthetic triangle font, so the benchmarks
  share the admitted test font without duplicating its builders or touching
  private assets. The catalog, provider, options and reusable snapshot are built
  once outside the timed methods.
- `CreateSnapshotAsync` runs font-request discovery, resolution, acquisition and
  verification per iteration, disposing the snapshot each time. The async state
  machine is included in the measurement.
- `LowerStyledText` lowers the admitted scene from a reused snapshot without
  raster or PNG encoding.
- `ConvertPreparedStyledTextAsync` runs the end-to-end supplied-font path:
  prepared-document creation plus conversion through raster and encoding.

| Method | Mean | Allocated |
| --- | ---: | ---: |
| `CreateSnapshotAsync` | 30.40 μs | 32.04 KB |
| `LowerStyledText` | 55.30 μs | 140.88 KB |
| `ConvertPreparedStyledTextAsync` | 300.14 μs | 190.34 KB |

Full ShortRun columns: `CreateSnapshotAsync` Gen0 7.5684 / Gen1 none;
`LowerStyledText` Gen0 33.3862 / Gen1 0.1221;
`ConvertPreparedStyledTextAsync` Gen0 44.9219 / Gen1 none. Allocation is
managed traffic per operation, not retained memory or `RenderBudget` peak.
Timings are local ShortRun evidence with broad confidence intervals, not
cross-platform guarantees or proof of any single allocation site share.

### Remeasurement after the style-read short-circuit (`f6ad5e4`)

Same environment and ShortRun job, measuring the first R19 optimization. No
resolved delta: confidence intervals overlap throughout and allocation traffic
is byte-identical, so no speedup is claimed. The change is retained as
strictly-less-work with identical semantics (full suite green). After segmented PNG staging the same benchmark measures 301.78 μs / 92.26 KB: the +0.16 KB is segment bookkeeping, times within noise.

| Method | Before | After |
| --- | ---: | ---: |
| `ConvertAttributeHeavyShapes` | 317.16 μs / 92.1 KB | 296.60 μs / 92.1 KB |
| `DiscoverStyledTextRequests` | 25.61 μs / 30.51 KB | 23.70 μs / 30.55 KB |
| `CreateSnapshotAsync` | 30.40 μs / 32.04 KB | 30.24 μs / 32.04 KB |
| `LowerStyledText` | 55.30 μs / 140.88 KB | 54.44 μs / 140.88 KB |
| `ConvertPreparedStyledTextAsync` | 300.14 μs / 190.34 KB | 267.31 μs / 190.34 KB |

### PNG buffering across sinks and entropies (`PngBufferBenchmarks`)

- Encodes 512x512 low-entropy (solid opaque red) and high-entropy (deterministic
  LCG bytes) pixels through all four output forms at the exact encoded length,
  reporting BenchmarkDotNet time plus managed allocation traffic per conversion.
  Companion peak methods run the same encodes against an owned ledger and return
  its accounted peak (current returns to zero in every case). Cancellation paths
  are owned by unit tests and the probe, not timed here.
- High entropy after segmented staging: owned traffic (2,077.48 KB) is the ~1 MB payload
  plus the final copy with no geometric copies (previously 5,119.77 KB with Gen2 pressure);
  caller memory additionally skips the final result (1,052.41 KB traffic, 1,051,463 B peak).
  Streams are unchanged (about 132 KB). All speeds are within noise of each other. After sharing chunk CRC, pixel validation and transparent-row filtering (`53323e3`), the same ShortRun measures high-entropy owned 65.35 ms / 2,077.61 KB, caller memory 66.80 ms / 1,052.41 KB, streams 58.48 ms / 131.95 KB and 59.53 ms / 132.33 KB, with low-entropy forms unchanged to the byte: allocation traffic is identical and times are within noise, so no performance effect is claimed in either direction.

| Method | Mean | Allocated | Accounted peak |
| --- | ---: | ---: | ---: |
| `EncodeHighEntropyOwned` | 64.78 ms | 2,077.45 KB | 2,099,077 B |
| `EncodeHighEntropyCallerMemory` | 59.99 ms | 1,052.32 KB | 1,051,463 B |
| `EncodeHighEntropyStream` | 64.24 ms | 132.11 KB | 100,353 B |
| `EncodeHighEntropyStreamAsync` | 61.67 ms | 132.39 KB | 100,417 B |
| `EncodeLowEntropyOwned` | 1.24 ms | 15.16 KB | 12,451 B |
| `EncodeLowEntropyCallerMemory` | 1.25 ms | 8.95 KB | 8,246 B |
| `EncodeLowEntropyStream` | 1.29 ms | 18.88 KB | 14,337 B |
| `EncodeLowEntropyStreamAsync` | 1.36 ms | 19.25 KB | 14,401 B |

### Glyph decode, transform and copy phases (`SvgTextBenchmarks`)

- The same triangle `A` (one contour) measured in three slices sharing one setup:
  `DecodeGlyphContours` decodes cmap/expansion/points, `TransformAndCopyContours`
  transforms into a flat array then copies per-contour arrays from reused decoded
  storage, and `DecodeTransformAndCopyContours` runs the full path. Values scale
  with segment count; the copy phase is a small share of a small glyph.

| Method | Mean | Allocated |
| --- | ---: | ---: |
| `DecodeGlyphContours` | 674.0 ns | 2,192 B |
| `TransformAndCopyContours` | 151.2 ns | 688 B |
| `DecodeTransformAndCopyContours` | 795.9 ns | 2,680 B |

## Baseline captured 2026-09-30 (post row-4 landings)

- BenchmarkDotNet 0.14.0; .NET SDK 10.0.401; .NET 10.0.12 x64 RyuJIT AVX-512.
- Windows 10 22H2 (10.0.19045.6456), Intel Xeon W-2223 3.60 GHz, four physical
  / eight logical cores, concurrent workstation GC, High performance plan.
- `ShortRun`: one launch, three warmups and three measured iterations with broad
  confidence intervals; same reproduction command as above.

### Conversion (same red-rectangle inputs as 2026-09-20)

| API form | 1x1 | 64x64 | 512x512 |
| --- | ---: | ---: | ---: |
| Owned memory | 28.09 us / 15.8 KB | 142.98 us / 32.49 KB | 3,292.60 us / 1,054.21 KB |
| Caller memory | 28.45 us / 15.63 KB | 136.33 us / 32.14 KB | 3,295.25 us / 1,048.02 KB |
| Stream | 22.93 us / 15.46 KB | 119.61 us / 32.34 KB | 3,198.67 us / 1,058.16 KB |
| Async stream | 34.56 us / 16.26 KB | 154.13 us / 33.07 KB | 3,392.14 us / 1,058.95 KB |

The 512 cells match the 2026-09-20 table within noise; the 1x1 and 64x64 deltas sit
inside their broad ShortRun intervals and claim no regression in either direction.

### Curve-heavy conversion (first measurement, no earlier baseline)

- `ConvertOutlinedWordmark` converts one outlined wordmark with counters through
  the public owned-memory API.
- `ConvertGradientClip` and `ConvertIsolatedOpacity` convert one gradient-clipped
  scene and one isolated-opacity group respectively.

| Method | Mean | Allocated |
| --- | ---: | ---: |
| `ConvertOutlinedWordmark` | 1,449.21 us | 5,965.08 KB |
| `ConvertGradientClip` | 59.44 us | 24.57 KB |
| `ConvertIsolatedOpacity` | 53.56 us | 22.05 KB |

The wordmark corridors about 6 MB of managed traffic per conversion with Gen2
collections, which is the first measured many-contour allocation data point for
the row-2 exit evidence. It is traffic, not retained memory or `RenderBudget` peak.

### Parse, text and glyph re-measurement

| Method | Mean | Allocated |
| --- | ---: | ---: |
| `ConvertAttributeHeavyShapes` | 779.0 us | 246.53 KB |
| `DiscoverStyledTextRequests` | 31.93 us | 31.29 KB |
| `CreateSnapshotAsync` | 34.53 us | 32.56 KB |
| `LowerStyledText` | 74.67 us | 155.67 KB |
| `ConvertPreparedStyledTextAsync` | 225.00 us | 212.90 KB |
| `DecodeGlyphContours` | 669.3 ns | 2,192 B |
| `TransformAndCopyContours` | 149.7 ns | 688 B |
| `DecodeTransformAndCopyContours` | 777.0 ns | 2,680 B |

A false regression alarm on `ConvertAttributeHeavyShapes` (317.16 us / 92.1 KB on
2026-09-21) was bisected across the window and exonerated: the f6ad5e4c product
remeasures 246.53 KB today with bit-identical source, input and locked harness
versions, and six runs across 09-22 to HEAD agree on 246.53 KB to the byte, so
current-era product allocation is stable and the old table is not comparable in
this environment. Time varies run to run while allocation stays deterministic.

## Inventory-shaped conversion (2026-10-01)

- BenchmarkDotNet 0.14.0; .NET 10.0.12 x64 RyuJIT (same machine family as the baseline table).
- `ShortRun`: one launch, three warmups and three measured iterations; confidence intervals stay broad, so read orders of magnitude, not deltas.
- New `SvgInventoryBenchmarks` inputs (committed alongside): a tiny 8x8 triangle mark, a 737x371 wordmark letter-pair fragment with curve unions (public-domain SDL wordmark content), and dense 800x480 dragon curves (public-domain Wales flag content). Each benchmark converts to owned memory under `SvgRenderOptions.Default`, so destination bytes ride the allocation figure.
- Native-size inv-050 (intrinsic 2880x2880) renders under default limits in about 717 ms with about 34 MB managed-allocated (fresh-process probe measurement, dominated by the 33 MB destination surface): no default-limit denial to record, the row-4 analytic charging keeps it in budget.

| Method | Mean | Allocated |
| --- | ---: | ---: |
| `ConvertTinyMark` | 32.21 us | 18.65 KB |
| `ConvertWordmarkPair` | 1,971.83 us | 1123.71 KB |
| `ConvertDenseDragon` | 253,303.72 us | 3025.75 KB |

The wordmark pair stays near 2 ms because its opaque content covers a small fraction of the canvas; the dragon pays about 660 ns per canvas pixel across dense flattened curves with analytic interval coverage. These are throughput anchors for row-4/row-5 work, not gates.

## Cubic state separation and edge accounting (2026-10-04)

Compared the reviewed renderer `f633c86b` (DLL SHA-256
`2F88005F62089F8359771EE1E71310CE10963701C1FCF6FF67881378B7D4F792`)
with the state-separation checkpoint starting at `5c4af69c` (DLL SHA-256
`A97877188599FB2074F4EA6E74145575C4147FEA46C723E9F89F3A0F20B56BD6`).
The same public owned-memory harness and input hashes ran on Windows x64,
.NET 10.0.12. Each case had 32 warmups and three measured batches, followed by
a second pair of processes in reverse renderer order. Batches used 100 tiny,
ten Ford, three dense and three native-size conversions. Ford explicitly uses
500x200 transparent; the other cases use Default options. This is a local probe,
not a fresh BenchmarkDotNet run.

The table gives the observed range over both orders, per conversion. Timings
vary substantially: the apparent native-size slowdown in the first pair disappears
in reverse order, and the old dense renderer varies from 339 to 472 ms. These runs
do not establish a throughput gain or a stable regression. Managed allocation
traffic overlaps, consistent with the added integer fitting existing object
alignment. PNG hashes match for every case.

| Input | Before time (ms) | After time (ms) | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Tiny triangle | 0.108–0.129 | 0.108–0.133 | 20,200–20,202 | 20,200–20,202 |
| Ford `inv-002` | 14.12–17.55 | 15.55–22.59 | 805,354–808,508 | 805,354–808,508 |
| Dense `inv-082` | 339.07–472.10 | 341.30–352.24 | 5,666,176–5,668,330 | 5,666,096–5,668,160 |
| Native-size `inv-050` | 430.43–466.60 | 433.06–462.40 | 33,906,632–33,906,957 | 33,906,658–33,906,712 |

Separately, the internal ledger records unchanged charged work through parsing
and rasterization (encoding work is excluded). Accounted peaks include encoding.
The higher Ford/dense peaks repair an under-reservation: the edge envelope now
covers full live-state objects, reference growth and retained-curve expansion
before allocation. These are accounting changes, not measured allocation growth.

| Input | Unchanged work through raster | Before accounted peak (bytes) | After accounted peak (bytes) |
| --- | ---: | ---: | ---: |
| Tiny triangle | 218 | 132,536 | 132,536 |
| Ford `inv-002` | 1,255,251 | 458,076 | 554,012 |
| Dense `inv-082` | 34,881,400 | 2,311,021 | 3,035,885 |
| Native-size `inv-050` | 76,736,404 | 33,628,051 | 33,628,051 |

All four inputs still render under Default limits. The full comparison preserves
597 fixture/research/inventory dispositions and PNG bytes, plus all nine Ford
variants. Local harnesses, source/input/output identities, measurements and ledger
reports are under the ignored `artifacts/curve-state-20261004/` directory.

## Full-canvas paint routing resource checkpoint (2026-10-04)

Compare the pre-repair `f6a96315` renderer with the integrated full-canvas
solid-paint repair on the same Windows x64 .NET 10.0.12 inputs and owned-memory
harness. Each case has 32 warmups and three batches: 100 tiny, ten Ford, three
dense and three native-size conversions per batch. Ford uses explicit 500x200
transparent options; the other three use Default. Repeat in reverse renderer
order after the final code changes. The first pair overlapped other validation;
the second pair ran sequentially. These local probes do not replace BenchmarkDotNet.

The table spans both orders. The wide ranges and changed machine load prevent
a stable throughput conclusion: even the baseline native-size input shifts
from 434–437 ms to 519–552 ms. Allocation traffic records the added retained
paint/scene metadata, with occasional batch variation; it is not peak live
memory. Complete output PNGs match before/after for these four inputs.

| Input | Before time (ms) | After time (ms) | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Tiny triangle | 0.119–0.157 | 0.124–0.205 | 20,200–20,202 | 20,288–20,290 |
| Ford `inv-002` | 15.76–21.73 | 16.73–22.03 | 805,354–808,508 | 805,650–808,804 |
| Dense `inv-082` | 345.85–397.81 | 339.81–427.08 | 5,666,149–5,668,464 | 5,670,000–5,672,661 |
| Native-size `inv-050` | 434.28–551.97 | 406.47–565.30 | 33,906,658–33,907,218 | 33,907,056–33,907,720 |

The final internal ledger retains the same parsing/preparation/raster work on
these non-eligible inputs. Accounted peaks include encoding and grow slightly
because parser/scene arrays reserve runtime value sizes after metadata changes.
On this x64 runtime, `SvgGeometry` is 200 bytes and the embedding `SvgPatternTile`
is 232 bytes. The pattern dictionary now reserves the tile's runtime size plus
conservative entry/key/bucket overhead before growth; its old fixed 128-byte
per-entry estimate did not cover those values. A focused denial regression
rejects a first table growth whose tile values alone exceed the budget.

| Input | Unchanged work through raster | Before accounted peak (bytes) | After accounted peak (bytes) |
| --- | ---: | ---: | ---: |
| Tiny triangle | 218 | 132,536 | 132,576 |
| Ford `inv-002` | 1,255,251 | 554,012 | 554,028 |
| Dense `inv-082` | 34,881,400 | 3,035,885 | 3,036,181 |
| Native-size `inv-050` | 76,736,404 | 33,628,051 | 33,628,107 |

All four continue to render under Default limits. The new solid analysis charges
each inspected source command and preflights the final pixel fill; it allocates
no additional conversion surface. Cancellation checkpoints cover each row and
bounded inner-loop intervals, with a deterministic during-row ownership test.
The 25 child-process memory scenarios and package/installed-consumer gates pass.
The four-input ledger does not claim optimizer performance for eligible scenes,
representative corporate throughput or Linux parity.

Harnesses, source/input/output identities, both-order measurements and the final
ledger are under ignored `artifacts/span-dispatch-20261004/`. The design record
lists the independent 80-configuration capture family and its remaining layer
failure; the fixture gate is the portable regression source.

## Operation-preserving analytic paint resource checkpoint (2026-10-04)

Compare the `30c6f4a3` production renderer with the integrated operation-preserving
paint/float-capture checkpoint on Windows x64 .NET 10.0.12, using the same four
inputs and owned-memory harness. Each renderer has 32 warmups and three batches:
100 tiny, ten intended Ford, three dense and three native-size conversions. Run
both after→before and before→after renderer orders after replay cleanup. The first
pair overlaps regression validation; the second pair runs sequentially.
These local measurements do not replace
BenchmarkDotNet or establish representative corporate throughput.

| Input | Before time (ms) | After time (ms) | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Tiny triangle | 0.120–0.404 | 0.119–0.148 | 20,288–20,290 | 20,288–20,290 |
| Ford `inv-002` | 15.297–19.740 | 16.644–22.719 | 805,650–808,804 | 814,250–817,197 |
| Dense `inv-082` | 355.770–363.763 | 363.169–520.614 | 5,670,000–5,672,744 | 5,748,738–5,749,776 |
| Native-size `inv-050` | 457.744–558.929 | 629.405–646.535 | 33,907,056–33,907,824 | 34,036,912–34,039,536 |

Tiny/Ford/dense ranges overlap or vary with renderer order/load. Native-size output
is consistently slower in these local pairs; that regression is retained as an
open replay-cost issue, not dismissed as noise. Opaque full-coverage copying,
avoiding unarmed execution-context probe reads and eliminating unused collapsed
coverage calculations reduce the initial 795–898 ms candidate to the range above.
The cleanup preserves all 713 banked outputs and dispositions. It does not remove
the remaining native-size cost or establish representative throughput. Allocation
traffic includes bounded event/span buffers and changed encoding, not peak live
memory. Tiny output is unchanged; Ford/dense/native-size output changes.

The internal ledger records the same input hashes and parser work. Charged work
through raster includes the new pixel indices, fringe event recording/replay and
growth/copy costs; encoding is excluded from that work column. Accounted peaks
include encoding and therefore also reflect changed PNG sizes.

| Input | Before → after work through raster | Before → after accounted peak (bytes) |
| --- | ---: | ---: |
| Tiny triangle | 218 → 218 | 132,576 → 132,576 |
| Ford `inv-002` | 1,255,251 → 1,261,164 | 554,028 → 559,396 |
| Dense `inv-082` | 34,881,400 → 34,929,573 | 3,036,181 → 3,044,921 |
| Native-size `inv-050` | 76,736,404 → 90,740,576 | 33,628,107 → 33,626,765 |

All four retain Default admission. Native-size work increases materially and stays
within the existing total work limit including encoding. The initial model stored
one full-span event per pixel and exceeded that limit; it is rejected. The landed
compact model indexes one shared span descriptor per covered run, merges it with
fringe paints by sequence and charges further overlap separately. Memory reserves
use actual value sizes and include both arrays during growth/copy. Ten focused
arithmetic/ownership facts, all 25 memory scenarios and installed-package gates pass.
Local measurement, ledger, source/input/output identities and both renderer orders
are retained under ignored `artifacts/paint-operations-20261004/`.

## Scan-window resource checkpoint (2026-10-05)

Compare `c9f0eea2` with the final contained-edge scan-window repair on the same
Windows x64 .NET 10.0.12 host and four inputs above. Each fresh harness process
uses 32 warmups and three batches: 100 tiny, ten intended Ford, three dense and
three native-size conversions. Baseline measurements precede the final candidate;
a second final-candidate run precedes the repeated baseline. Final timings run
without concurrent tests or delivery checks. These are local observations,
not representative throughput or a replacement for BenchmarkDotNet.

| Input | Before time (ms) | After time (ms) | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Tiny triangle | 0.115–0.132 | 0.111–0.133 | 20,288–20,290 | 20,288–20,290 |
| Ford `inv-002` | 14.953–21.683 | 16.125–19.114 | 814,250–817,404 | 814,250–817,404 |
| Dense `inv-082` | 338.987–357.055 | 346.947–364.258 | 5,748,712–5,748,800 | 5,748,712–5,750,373 |
| Native-size `inv-050` | 602.143–621.075 | 576.923–622.127 | 34,036,912–34,038,560 | 34,036,912–34,037,928 |

Timing ranges overlap for all four. Allocation envelopes are recorded as measured;
the scan window adds no buffers and does not expand path-sized rows. These pairs
do not remove the native-size replay slowdown demonstrated by the preceding paint
checkpoint against its own baseline. That cost remains open before release.

Matching input hashes, parser work, whole PNG hashes, work through raster and
accounted peaks are identical before/after on all four:

| Input | Unchanged work through raster | Unchanged accounted peak (bytes) |
| --- | ---: | ---: |
| Tiny triangle | 218 | 132,576 |
| Ford `inv-002` | 1,261,164 | 559,396 |
| Dense `inv-082` | 34,929,573 | 3,044,921 |
| Native-size `inv-050` | 90,740,576 | 33,626,765 |

All four retain Default admission. Canvas containment checks accompany already
charged point/control mapping; explicit-clip classification precharges its extra
control traversal and checks cancellation at bounded intervals. Mask/run dispatch
and storage remain path-sized. Fixed-point window bounds saturate safely when a
caller configures a canvas wider than the existing analytic coordinate range.
The expanded regression covers a 70,000-pixel logical canvas without surface
allocation. All 2,991 tests, 25 memory scenarios and runtime/symbol package plus
installed-consumer gates pass for the final renderer. No Linux or representative
performance refresh is claimed.

Local source/input/output identities, capture triples, comparisons, final delivery
checks and resource records are under ignored `artifacts/scan-window-20261004/`:
`before-measure`, `final-after-measure`, `final-after-reverse-measure`,
`final-before-reverse-measure`, `ledger-before.json` and `final-ledger-after.json`.
The final assembly SHA-256 is
`0bd0de4d088f84791209134be1753ee6018d40e3411c1cd875274cc1a33d92f0`;
the baseline assembly is
`742f5c6b8806907c67033a9d9cd9048dca6f78598ca25bed0db5276802fa69c2`.

## One-chord cubic resource checkpoint (2026-10-05)

Compare `9e77c0fa` with retained one-chord cubic admission on the same Windows x64
.NET 10.0.12 host and four inputs. Each fresh harness process uses 32 warmups and
three batches: 100 tiny, ten intended Ford, three dense and three native-size
conversions. Run before→after, then after→before, sequentially after the validation
and delivery processes finish. These local observations do not establish
representative throughput or replace BenchmarkDotNet.

| Input | Before time (ms) | After time (ms) | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Tiny triangle | 0.110–0.187 | 0.112–0.160 | 20,288–20,290 | 20,288–20,290 |
| Ford `inv-002` | 15.476–24.214 | 17.517–26.049 | 814,250–817,404 | 814,194–817,348 |
| Dense `inv-082` | 348.583–442.337 | 343.496–350.239 | 5,748,712–5,750,394 | 5,748,704–5,749,938 |
| Native-size `inv-050` | 610.381–624.839 | 586.425–590.313 | 34,036,912–34,039,570 | 34,036,912–34,039,544 |

Tiny/Ford/dense time ranges overlap; native-size times decrease in these local
pairs. They do not resolve the earlier paint replay slowdown against its own
pre-paint baseline. That issue remains open before release. Allocation traffic
also reflects changed PNG sizes; the cubic repair adds no buffers or reservation
capacity. All four accounted peaks are unchanged.

| Input | Before → after work through raster | Unchanged accounted peak (bytes) |
| --- | ---: | ---: |
| Tiny triangle | 218 → 218 | 132,576 |
| Ford `inv-002` | 1,261,164 → 1,267,660 | 559,396 |
| Dense `inv-082` | 34,929,573 → 34,929,955 | 3,044,921 |
| Native-size `inv-050` | 90,740,576 → 90,740,612 | 33,626,765 |

Input hashes and parser work match. Tiny/native-size whole PNGs are unchanged;
Ford/dense PNGs change. Cubic setup attempts, including empty pieces, are charged
before their result is inspected; the added retained edges use the existing
complete-span reservations. All four retain Default admission. The 743-case bank
preserves all previous fixture/research outputs and every disposition. All 2,993
tests, 25 memory scenarios and runtime/symbol package plus installed-consumer gates
pass. Linux and representative performance remain unrefreshed.

Ignored evidence is under `artifacts/cubic-admission-20261005/`: `before-measure`,
`after-measure`, `after-reverse-measure`, `before-reverse-measure`, `ledger-before.json`
and `ledger-after.json`. The baseline source matches HEAD `9e77c0fa`, also embedded
in its assembly informational version; its rebuilt assembly SHA-256 is
`d31aa1a15b935723bbdf38aa6a68975b7ca1d37c058575c7dda4d1e859371b82`.
The final candidate assembly SHA-256 is
`7b99ab2c112b84a5a00a6015c099641eb5b08569821c25494d89debe51125c98`.

## Clipping-kernel resource boundary (2026-10-05)

The bounded clipping kernel is independently verified but not yet called by
contour preparation. Its caller-owned 27-piece scratch and charged numerical/root
search phases add no conversion buffers or work on the current rendering path.
Fresh four-input ledger observations exactly match the one-chord checkpoint:
work through raster is 218 / 1,267,660 / 34,929,955 / 90,740,612, and accounted
peaks are 132,576 / 559,396 / 3,044,921 / 33,626,765 bytes, in tiny/Ford/dense/
native-size order. Input identities, parser work and whole PNGs also match.

These observations do not prove the future integration's additional storage,
setup or traversal costs. Reserve actual clipped-piece/edge storage and its growth
peaks, charge any sizing pass and construction, then measure both renderer timing
orders and allocation traffic before landing integration. No fresh throughput or
Linux result is claimed here, and the earlier native-size replay slowdown remains
open. Ignored evidence is `artifacts/analytic-clip-20261005/kernel-ledger.json`;
all 25 existing memory scenarios and the kernel's work/cancellation tests pass.

## Clipped-preparation checkpoint (2026-10-05)

Compare the saved `e8a8820a` assembly with shared clip-built preparation on the
same .NET 10 Windows x64 environment. Run sequentially after build/test/capture/
package jobs finish, in both orders: baseline → candidate, then candidate →
baseline. Each process uses 32 warmups per input and three measured batches
(100 tiny, ten Ford, three dense, three native-stop conversions per batch).
Values below are median per-conversion milliseconds and managed allocated bytes.
These short local measurements retain run-order variation and establish no
representative throughput or universal speedup.

| Input | Baseline first ms / bytes | Candidate second ms / bytes | Candidate first ms / bytes | Baseline second ms / bytes |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.12675 / 20,288 | 0.12759 / 20,288 | 0.12595 / 20,288 | 0.12824 / 20,288 |
| ford | 21.82782 / 814,194 | 16.23303 / 816,602 | 15.56230 / 816,602 | 16.29280 / 817,322 |
| dense | 345.38657 / 5,749,104 | 349.32113 / 5,748,704 | 376.01257 / 5,748,792 | 347.90183 / 5,748,792 |
| native-stop | 592.00290 / 34,036,912 | 591.34813 / 34,036,965 | 590.80467 / 34,037,925 | 608.26900 / 34,036,965 |

Ford charged raster work grows **1,267,660 → 1,298,734**, including both clipping
passes and numerical/traversal/setup costs. Accounted peak falls **559,396 →
534,434 bytes** with actual clipped-piece sizing. Tiny retains 218 work / 132,576
peak bytes; dense retains 34,929,955 / 3,044,921; native-stop retains 90,740,612 /
33,626,765. Their complete PNGs remain unchanged. Ford's measured 500×200
transparent output becomes exact against its original reference.

The two-pass preparation uses stack piece scratch and reserves complete emitted
edge objects/list growth before allocation. It does not raise limits. All Default
inventory admissions, 25 memory scenarios and delivery checks pass. Timing and
allocation vary between orders; the earlier native-size paint replay slowdown
and representative BenchmarkDotNet throughput remain open. These observations do
not establish Linux, private-font or full corporate-variant performance.

Full batches, input and assembly identities, complete PNGs and matching work/peak
ledgers are under ignored `artifacts/clipped-preparation-20261005/`. The first
exploratory timing set is also retained there; the table uses the final assembly's
`delivery-timing-*` measurements. The previous paint/window/cubic/kernel records
retain their dated evidence.

## CPU raster context checkpoint (2026-10-05)

Compare the frozen `5e12e887` renderer with generic bordered CPU construction on
.NET 10 Windows x64, sequentially in both orders after capture/build/test jobs.
Each fresh process uses 32 warmups per input and three measured batches of 100
tiny, ten Ford, three dense and three native-stop conversions. Values are median
per-conversion milliseconds and managed allocated bytes; short local timings
establish no representative throughput or universal speedup.

| Input | Baseline first ms / bytes | Candidate second ms / bytes | Candidate first ms / bytes | Baseline second ms / bytes |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.12323 / 20,288 | 0.12328 / 20,288 | 0.12322 / 20,288 | 0.12391 / 20,288 |
| ford | 16.61234 / 817,109 | 16.68937 / 818,479 | 15.51870 / 816,602 | 15.86656 / 816,602 |
| dense | 338.62490 / 5,748,792 | 284.78667 / 7,232,744 | 284.40137 / 7,232,744 | 471.33313 / 5,748,704 |
| native-stop | 563.51737 / 34,036,965 | 417.91943 / 36,857,122 | 433.45647 / 36,856,725 | 596.18387 / 34,036,965 |

| Input | Charged work through raster, baseline → candidate | Accounted peak bytes, baseline → candidate |
| --- | ---: | ---: |
| tiny | 218 → 219 | 132,576 → 132,576 |
| ford | 1,298,734 → 1,298,880 | 534,434 → 534,434 |
| dense | 34,929,955 → 30,826,616 | 3,044,921 → 2,493,732 |
| native-stop | 90,740,612 → 73,302,547 | 33,626,765 → 33,630,409 |

Whole-path mask/run dispatch remains independent of bounded bitmap row storage.
The first whole-width-buffer trial denied Default native-stop work; intersecting
actual row storage with each bitmap restores admission without changing trial
Ford/control pixels or discounting real traversal. Classification/contact work
is cached once per mapped fill; context visits, clipping, validation, construction,
row clearing, event recording/growth/replay and painting remain charged. Every
reservation releases before the next context or on denial/cancellation.

Tiny and exact 500×200 Ford PNGs retain their identities. Dense/native-stop outputs
change without matching captures in the Default sweep; timing does not prove
fidelity. Tiled inputs allocate more total bytes because they construct several
individually bounded walkers. The dense accounted peak falls, while native-stop's
peak grows by 3,644 bytes; this is distinct from cumulative allocation traffic.
All unchanged Default limits, 25 memory scenarios, package inspection and isolated
installed consumers pass. Chromium policy is managed code with its BSD notice;
there are no runtime dependencies or native assets. Representative BenchmarkDotNet
throughput, earlier native-size replay costs, Linux and private-font performance
remain open. Complete batches, PNG/input/assembly identities, ledgers and delivery
results are under ignored `artifacts/cpu-raster-context-20261005/`.

## Horizontal clip paint checkpoint (2026-10-05)

Compare the frozen `bf0ceb1c` renderer with horizontal clip-wrapper forwarding on
the same .NET 10 Windows x64 environment, sequentially in both orders after
capture/build/test/package jobs finish. Each fresh process uses 32 warmups per
input and three measured batches of 100 tiny, ten Ford, three dense and three
native-stop conversions. Values are median per-conversion milliseconds and
managed allocated bytes; these short local observations establish no
representative throughput or universal speedup.

| Input | Baseline first ms / bytes | Candidate second ms / bytes | Candidate first ms / bytes | Baseline second ms / bytes |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.12467 / 20,288 | 0.12596 / 20,288 | 0.12479 / 20,288 | 0.12734 / 20,288 |
| ford | 19.58759 / 819,417 | 16.21926 / 818,897 | 16.21402 / 816,602 | 17.95603 / 819,730 |
| dense | 286.51157 / 7,232,664 | 285.54783 / 7,232,728 | 290.50693 / 7,232,728 | 288.45673 / 7,232,664 |
| native-stop | 438.65007 / 36,856,661 | 443.58433 / 36,856,384 | 449.52250 / 36,857,496 | 441.62403 / 36,856,725 |

| Input | Charged work through raster, baseline → candidate | Accounted peak bytes, baseline → candidate |
| --- | ---: | ---: |
| tiny | 219 → 219 | 132,576 → 132,576 |
| ford | 1,298,880 → 1,299,168 | 534,434 → 534,434 |
| dense | 30,826,616 → 30,826,660 | 2,493,732 → 2,493,732 |
| native-stop | 73,302,547 → 73,302,593 | 33,630,409 → 33,629,669 |

The complete horizontal control-bound traversal is charged once per preparation;
the cache and route flag add no heap buffers. Changed material/event bytes and PNG
output can change measured allocation traffic. Tiny and exact 500×200 Ford PNGs
retain identities. Dense/native-stop outputs change without matching captures in
the Default sweep; neither timing nor admission establishes their fidelity.
The native-stop peak falls 740 bytes; work and cumulative allocations remain
distinct measures. All Default limits/admissions, 25 memory scenarios and final
package/installed-consumer gates pass. Representative BenchmarkDotNet throughput,
the earlier replay cost, Linux and private-font performance remain open.

Complete batches, assembly/input/PNG identities and matching before/after work/
peak ledgers are under ignored `artifacts/clip-pair-route-20261005/`.

## Axis-aligned path mapping checkpoint (2026-10-05)

Compare the frozen `77ab776c` renderer with the bounded axis-aligned path map on
the same Windows x64 .NET 10.0.12 environment. Both orders run sequentially in
fresh processes after capture/build/test/package jobs finish. Each input uses
32 warmups and three batches of 100 tiny, ten Ford, three dense and three native-stop
conversions. These medians are local per-conversion milliseconds / managed bytes,
not representative BenchmarkDotNet throughput or a universal speed claim.

| Input | Baseline first ms / bytes | Candidate second ms / bytes | Candidate first ms / bytes | Baseline second ms / bytes |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.12646 / 20,288 | 0.13001 / 20,288 | 0.12484 / 20,288 | 0.12751 / 20,288 |
| ford | 16.77478 / 816,602 | 21.13423 / 816,628 | 16.83520 / 816,602 | 16.77998 / 819,730 |
| dense | 293.53320 / 7,232,781 | 312.62850 / 7,212,672 | 312.74030 / 7,212,672 | 288.72277 / 7,232,781 |
| native-stop | 442.82107 / 36,856,013 | 464.88880 / 36,855,272 | 427.22590 / 36,857,045 | 569.23880 / 36,856,762 |

| Input | Charged work through raster, baseline → candidate | Accounted peak bytes, baseline → candidate |
| --- | ---: | ---: |
| tiny | 219 → 219 | 132,576 → 132,576 |
| ford | 1,299,168 → 1,299,452 | 534,434 → 534,434 |
| dense | 30,826,660 → 32,695,333 | 2,493,732 → 2,493,276 |
| native-stop | 73,302,593 → 73,300,601 | 33,629,669 → 33,629,691 |

The mapper adds no buffers or separate traversal. Numerical changes can alter
flattening and near-duplicate/contact cleanup, whose actual taken work remains
charged. Ford parser work grows 2,904→3,188; dense parser work grows
238,421→244,679, and total dense work grows about six percent. Dense accounted
peak falls 456 bytes; native-stop grows 22 bytes. Total allocation traffic and
peak live accounted storage remain distinct measures. Default limits/admissions,
25 memory scenarios and package/installed-consumer checks pass.

Tiny and exact 500×200 Ford PNGs retain identity; dense/native-stop outputs change
without matching captures in the Default sweep. Timing establishes no fidelity
claim for them. Full before/after batch, renderer/input/PNG identities, work/peak
ledgers and delivery evidence are under ignored `artifacts/lower-tile-count-20261005/`.
The final memory runner and runtime package contain the exact final renderer DLL
and complete Skia/Chromium notices. Representative throughput, earlier native-size
replay costs, Linux and private-font performance remain open.

## Coupled SVG numeric-reading checkpoint (2026-10-05)

Compare frozen `9e62e3dd` with the generic float numeric reader and bounded parity/
closing-adjacency repair on the same Windows x64 .NET 10.0.12 environment. Both
orders run sequentially in fresh processes after capture/build/test/package jobs
finish: 32 warmups per input, then three batches of 100 tiny, ten Ford, three dense
and three native-stop conversions. These medians are local per-conversion
milliseconds / managed bytes, not representative BenchmarkDotNet throughput.

| Input | Baseline first ms / bytes | Candidate second ms / bytes | Candidate first ms / bytes | Baseline second ms / bytes |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.12088 / 20,288 | 0.12382 / 20,288 | 0.12956 / 20,288 | 0.12267 / 20,288 |
| ford | 17.60753 / 817,988 | 17.19330 / 816,602 | 15.55862 / 816,674 | 19.12220 / 816,628 |
| dense | 321.06177 / 7,212,752 | 275.21973 / 7,184,808 | 269.33853 / 7,184,808 | 318.25920 / 7,212,672 |
| native-stop | 464.09583 / 36,857,184 | 455.80463 / 36,855,821 | 488.83080 / 36,857,413 | 555.88150 / 36,856,205 |

| Input | Charged work through raster, baseline → candidate | Accounted peak bytes, baseline → candidate |
| --- | ---: | ---: |
| tiny | 219 → 219 | 132,576 → 132,576 |
| ford | 1,299,452 → 1,300,919 | 534,434 → 534,434 |
| dense | 32,695,333 → 32,761,651 | 2,493,276 → 2,493,820 |
| native-stop | 73,300,601 → 73,300,782 | 33,629,691 → 33,629,655 |

The additional bounded, allocation-free numeric pass and transform traversal are
charged. Ford parser work grows 3,188→4,655; dense parser work grows
244,679→245,770. Numerical changes also alter taken flattening/cleanup work and
retained geometry storage: dense peak grows 544 bytes, native-stop falls 36 bytes.
Parity/closing classification adds no heap buffers. Tiny and exact intended Ford
PNG identities are preserved; dense/native-stop outputs change without matching
captures in the Default sweep, so these timings establish no fidelity claim.

Ford and dense are faster in both measured orders; tiny/native-stop variation
remains visible. Do not extrapolate a universal throughput claim from these four
inputs. All Default admissions/limits, 25 memory scenarios and runtime/symbol
package plus installed-consumer checks pass, with the unchanged complete
Skia/Chromium BSD notices. Local batch and input/DLL/PNG
identities, work/peak ledgers, full 848-case comparison and delivery evidence remain
under ignored `artifacts/native-numbers-20261005/`. Representative throughput,
replay costs, Linux and private-font performance remain open.

## Retained path-point density checkpoint (2026-10-05)

Compare the validated numeric renderer at `354136a3` (documentation parent
`54126bbd`) with the integrated count/admission validation on Windows x64
.NET 10.0.12. Both orders run sequentially in fresh processes after all capture,
build, test, package and bank jobs finish. Each input has 32 warmups and three
batches: 100 tiny, ten intended Ford, three dense and three native-size
conversions. Values are local median per-conversion milliseconds / managed
allocated bytes, not representative BenchmarkDotNet throughput.

| Input | Baseline first ms / bytes | Candidate second ms / bytes | Candidate first ms / bytes | Baseline second ms / bytes |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.13440 / 20,288 | 0.19619 / 20,288 | 0.14145 / 20,288 | 0.12409 / 20,288 |
| ford | 21.57744 / 818,479 | 28.41666 / 816,658 | 19.38129 / 819,730 | 16.20165 / 816,602 |
| dense | 345.34193 / 7,184,808 | 298.66123 / 7,184,728 | 280.79263 / 7,184,808 | 268.99057 / 7,184,781 |
| native-stop | 533.43037 / 36,854,970 | 487.82120 / 36,855,656 | 453.06250 / 36,856,669 | 449.98790 / 36,856,544 |

| Input | Charged work through raster, baseline → candidate | Accounted peak bytes, baseline → candidate |
| --- | ---: | ---: |
| tiny | 219 → 219 | 132,576 → 132,576 |
| ford | 1,300,919 → 1,301,211 | 534,434 → 534,434 |
| dense | 32,761,651 → 32,761,702 | 2,493,820 → 2,493,820 |
| native-stop | 73,300,782 → 73,300,833 | 33,629,655 → 33,629,655 |

Raw point counting adds one range-discovery and one merged-validation visit per
retained span, plus contour visits; the borrowed cache charges that classification
once. No heap buffers are added, all accounted peaks are unchanged and parser
work is unchanged. All four measured PNGs preserve identity; the newly exact large
Ford variant is separately validated rather than represented by the intended-size
timing input. Native-size `inv-050` remains admitted under unchanged Default limits.

Ford medians rise in both measured orders. Tiny varies, and dense/native-size
directions depend on order; these measurements do not establish a speedup or
representative latency. Keep the observed Ford cost and its uncertainty explicit
and measure shared preparation/replay and representative performance before release.
Local allocation traffic varies across runs while accounted peaks stay fixed.

All 3,222 tests, 25 memory scenarios, runtime/symbol package inspection and
installed-consumer checks pass, with unchanged complete Skia/Chromium BSD notices.
Full input/DLL/PNG identities,
batches, ledgers, negative-model tests, 872-case comparison and delivery evidence
are under ignored `artifacts/lower-residuals-20261005/`. The outlined-wordmark
checkpoint is established; representative throughput, Linux and S17 remain open.

## Raw control-point convexity checkpoint (2026-10-05)

Compare the retained-path-point density source at the reviewed `665ae500` baseline
with the integrated raw-control classifier on Windows x64 .NET 10.0.12. Both
orders run sequentially in fresh processes after capture, build, test, package,
bank and rollback jobs finish. Each input has 32 warmups and three batches: 100
tiny, ten intended Ford/YouTube, and three dense/native-stop/large-YouTube
conversions. Values are local medians per conversion; these six inputs do not
establish representative throughput.

| Input | Baseline first ms / bytes | Candidate second ms / bytes | Candidate first ms / bytes | Baseline second ms / bytes |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.15409 / 20,288 | 0.12301 / 20,288 | 0.12964 / 20,288 | 0.19864 / 20,288 |
| ford | 19.93719 / 819,730 | 16.71493 / 818,901 | 20.58201 / 816,628 | 21.63198 / 816,682 |
| dense | 281.24177 / 7,184,808 | 270.49823 / 7,184,808 | 424.39577 / 7,184,781 | 317.68283 / 7,184,808 |
| native-stop | 454.32303 / 36,856,592 | 444.49437 / 36,856,317 | 496.32897 / 36,856,714 | 575.41623 / 36,857,149 |
| youtube-intended | 8.84861 / 1,096,162 | 11.04585 / 1,043,336 | 18.26950 / 1,043,150 | 10.93398 / 1,095,659 |
| youtube-large | 28.92793 / 4,038,349 | 34.20333 / 3,952,485 | 59.37420 / 3,952,949 | 32.89683 / 4,037,584 |

| Input | Charged work through raster, baseline → candidate | Accounted peak bytes, baseline → candidate |
| --- | ---: | ---: |
| tiny | 219 → 219 | 132,576 → 132,576 |
| ford | 1,301,211 → 1,301,211 | 534,434 → 534,434 |
| dense | 32,761,702 → 32,761,735 | 2,493,820 → 2,493,820 |
| native-stop | 73,300,833 → 73,300,833 | 33,629,655 → 33,629,655 |
| youtube-intended | 1,056,315 → 1,009,278 | 981,810 → 943,153 |
| youtube-large | 3,533,088 → 3,249,021 | 3,731,795 → 3,733,811 |

Raw classification adds no buffers and is charged once per admitted convex
sampled contour. Tiny/Ford/native-stop work and every measured parser count are
unchanged; dense adds 33 charged units. YouTube changes from pre-stepped convex
curves to live interval walking: charged work falls at both sizes, intended peak
falls 38,657 bytes and large peak grows 2,016 bytes. Intended/large allocation
falls in both measured orders; those PNGs improve against their original
references. The other four measured PNG identities are unchanged.

YouTube medians rise in **both ordering pairs**, despite lower charged work and
allocation. The live-curve stepping/replay cost remains a performance dependency;
do not infer speedup from smaller ledgers or hide the measured slowdown. Ford is
faster in both pairs here, but its work/peak are unchanged and the order variation
in the other inputs remains substantial. These local measurements do not replace
representative BenchmarkDotNet latency/throughput or Linux evidence.

Fresh locked restore/Release build, all 3,244 tests, all 25 memory scenarios and
runtime/symbol package plus installed-consumer checks pass, with the complete
Skia/Chromium notices and new 2025 classifier notice in the
runtime package. Local input/DLL/PNG identities, all timing batches, six resource
ledgers, 896-case final bank, 258-case corporate comparison, rollback and delivery
evidence remain under ignored `artifacts/raw-convexity-20261005/`. The curved-mark,
representative performance, Linux and S17 exits remain open.

## Antialiased rectangle paint-clip checkpoint (2026-10-05)

Compare the validated raw-control source at documentation baseline `7aefe975`
with the shared AA paint-context/provenance implementation in `86554a94`
on Windows x64 .NET 10.0.12. The four measurements run sequentially in fresh
processes, baseline/candidate and candidate/baseline, after SVG capture/build/test/
bank jobs finish. Each input receives 32 warmups and three batches: 100 tiny, ten
intended Ford/YouTube, three dense/native-stop/large-YouTube conversions. Other
work on this shared host is not excluded. These are local median milliseconds /
managed allocated bytes per conversion, not representative throughput.

| Input | Baseline first ms / bytes | Candidate second ms / bytes | Candidate first ms / bytes | Baseline second ms / bytes |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.12715 / 20,288 | 0.12739 / 20,368 | 0.12805 / 20,368 | 0.17126 / 20,288 |
| ford | 17.63041 / 816,628 | 17.74300 / 818,591 | 18.18288 / 819,842 | 30.36778 / 816,682 |
| dense | 269.92783 / 7,184,808 | 276.93377 / 7,186,408 | 276.57890 / 7,186,328 | 440.56593 / 7,184,781 |
| native-stop | 451.32967 / 36,856,296 | 450.31773 / 36,855,701 | 696.20210 / 36,856,544 | 524.88460 / 36,857,464 |
| youtube-intended | 11.34257 / 1,041,988 | 11.27744 / 1,041,472 | 23.26621 / 1,041,172 | 11.82132 / 1,043,400 |
| youtube-large | 33.86247 / 3,951,250 | 35.37043 / 3,948,413 | 65.98493 / 3,948,674 | 35.97143 / 3,952,626 |

| Input | Charged work through raster, baseline → candidate | Accounted peak bytes, baseline → candidate |
| --- | ---: | ---: |
| tiny | 219 → 219 | 132,576 → 132,608 |
| ford | 1,301,211 → 1,301,211 | 534,434 → 534,450 |
| dense | 32,761,735 → 32,761,735 | 2,493,820 → 2,494,116 |
| native-stop | 73,300,833 → 73,300,833 | 33,629,655 → 33,629,711 |
| youtube-intended | 1,009,278 → 928,430 | 943,153 → 942,621 |
| youtube-large | 3,249,021 → 3,026,899 | 3,733,811 → 3,732,615 |

Four boundary visits are charged per context with a mapped clip. Rectangle
classification allocates nothing and is not cached with geometry. AA consumers
reserve the existing run-link owner even for small paths; denied remaining work
is checked before storage, and tight memory/work/cancellation tests release owners.
Per-boundary provenance grows the clip value from 32 to 40 bytes. Value-array/list
charges use the actual element size; clip dictionaries retain their conservative
128-byte entry charge. The measured unchanged-input peaks rise by 32/16/296/56
bytes for tiny/Ford/dense/native-stop; their work, parser counts and complete PNGs
are unchanged. Small managed allocation increases remain visible in the table.

YouTube's additive route reduces charged work by 80,848/222,122 units at intended/
large sizes and peak storage by 532/1,196 bytes. Parser work is unchanged. Its PNGs
improve against the original references but remain inexact. Large YouTube medians rise in both final ordering pairs. Intended YouTube
changes timing direction; tiny/Ford/dense/native-stop also vary with order and
host load. The slower runs remain evidence, and these observations establish
neither a speedup nor stable regression size. Representative latency, throughput
and previous live-curve replay costs remain release work.

Full timing batches, input/observer/renderer identities, six resource ledgers and
final gate records are retained under ignored `artifacts/aa-clip-dispatch-20261005/`.
The portable [checkpoint](../tests/References/antialiased-paint-clip-dispatch.json)
records the shared dispatch proof and its limits. The curved-mark, general
clip/layer fidelity, Linux and S17 exits remain open.

Fresh locked restore/normal nine-project Release build, all 3,257 tests, all 25
memory scenarios and runtime/symbol package plus installed-consumer checks pass.
Final package/consumer/probe/memory/timing identities match the candidate above.
The prior measurement set before the documentation metadata rebuild remains
separately archived; its renderer source and all image outputs are unchanged.


## Declaration-only DOCTYPE observations (2026-10-05)

These are matching local observations of the declaration-only input policy,
separate from BenchmarkDotNet throughput and the retained AA observations above.
Baseline source is `86554a94`; declaration-only admission is committed in
`1876cf6e`. The
[portable checkpoint](../tests/References/declaration-only-doctype.json) retains
source identity, full ledgers, all raw batches and both process orders.

All SVG validation jobs had terminated before observation. Four fresh processes
ran sequentially: baseline first / candidate second, then candidate first /
baseline second. Each case has 32 warmups and three batches (100 tiny, ten Ford or
intended YouTube, three dense/native-stop/large YouTube conversions per batch).
Inputs, options and output PNG identities are matched. Each timing cell below is
the median milliseconds per conversion; managed bytes use the same four-column
process order and are allocation traffic, separate from accounted live storage.

| Input | Baseline first | Candidate second | Candidate first | Baseline second | Managed bytes in the same order |
| --- | ---: | ---: | ---: | ---: | --- |
| tiny | 0.12707 | 0.13869 | 0.13370 | 0.17338 | 20,368 / 27,104 / 27,104 / 20,368 |
| ford | 22.73294 | 19.31786 | 23.44654 | 25.46767 | 816,794 / 857,010 / 854,074 / 816,794 |
| dense | 292.92953 | 387.74910 | 295.33653 | 280.86687 | 7,186,381 / 7,186,408 / 7,186,408 / 7,186,328 |
| native-stop | 506.66513 | 477.33187 | 582.39307 | 592.65300 | 36,856,176 / 36,878,997 / 36,878,600 / 36,856,698 |
| youtube-intended | 11.58502 | 12.74459 | 13.95636 | 13.01684 | 1,041,244 / 1,041,448 / 1,041,448 / 1,041,122 |
| youtube-large | 36.38517 | 37.37073 | 48.71673 | 52.46353 | 3,948,413 / 3,948,432 / 3,948,709 / 3,948,717 |

Tiny and large YouTube timing directions change with process order. Dense and
intended YouTube are slower in both ordering pairs despite no prolog or raster
change. Ford and native-stop are faster in both pairs despite the added prolog
allocation. These shared-host
observations establish no causal speedup or representative regression threshold.
The prolog allocation cost remains visible in the affected inputs and is retained
rather than selecting a favorable ordering.

| Input | Root parser work, baseline → candidate | Work through raster | Accounted peak bytes |
| --- | ---: | ---: | ---: |
| tiny | 33 → 34 | 219 → 220 | 132,608 → 163,840 |
| ford | 4,655 → 4,657 | 1,301,211 → 1,301,213 | 534,450 → 534,450 |
| dense | 245,770 → 245,770 | 32,761,735 → 32,761,735 | 2,494,116 → 2,494,116 |
| native-stop | 1,723 → 1,725 | 73,300,833 → 73,300,835 | 33,629,711 → 33,629,711 |
| youtube-intended | 357 → 357 | 928,430 → 928,430 | 942,621 → 942,621 |
| youtube-large | 463 → 463 | 3,026,899 → 3,026,899 | 3,732,615 → 3,732,615 |

The fixed additional prolog workspace is 32 KiB and is released before the main
reader is constructed. It raises the tiny accounted peak; geometry/raster peaks
dominate the other sampled inputs. Root parsing adds one or two work units for
the short comments/XML declarations in these examples; the existing separately
bounded output-scale probe also charges its prolog traversal and is outside this
ledger. The no-prolog dense and YouTube examples preserve work and peaks. Long
prologs charge proportional chunk traversal rather than this fixed short-input
cost. Resource/cancellation tests retain denial and cleanup evidence. Broader
throughput, platforms and release completion remain open.


## Convex rectangle-strip observations (2026-10-06)

Matching six-input ledgers preserve parser and raster work. Accounted peaks for
tiny/Ford/dense/native-size inputs are unchanged. Intended/large YouTube peaks
increase 942,621 → 942,639 and 3,732,615 → 3,732,655 bytes; encoded PNG lengths
increase 12,227 → 12,236 and 34,008 → 34,028 bytes. The repair adds no coverage
buffers. Complete observations are under ignored
`artifacts/triangle-paint-20261005/`.

One sequential baseline-first/current-second process pair uses the retained
32-warmup/three-batch harness and original six input/options recipes. Median
milliseconds are tiny 0.200 → 0.135, Ford 26.544 → 17.637, dense 293.640 → 276.281,
native-size 481.081 → 417.031, intended YouTube 11.385 → 11.068 and large YouTube
35.266 → 34.434. The single ordering and host load limit these observations;
there is no speedup or representative-throughput claim. Raw batch allocation/time
records remain in ignored artifacts rather than a new source hash inventory.


## Rectangle clip-byte observations (2026-10-06)

Matching six-input ledgers preserve parser/raster work and the tiny, Ford, dense
and native-size accounted peaks. Intended/large YouTube peaks grow by 16/4 bytes
to 942,655 / 3,732,659; PNG lengths grow 12,236 → 12,244 and 34,028 → 34,030.
The immutable coverage value adds no heap buffers. Complete ledgers and timing
batches remain under ignored `artifacts/boundary-clip-20261006/`.

One sequential baseline-first/current-second pair uses the original six recipes,
32 warmups and three batches per input, after capture/build/test/bank jobs finish.
Median milliseconds are tiny .137 → .136, Ford 16.981 → 16.618, dense
271.422 → 278.765, native-size 448.159 → 487.200, intended YouTube 11.301 → 11.683
and large YouTube 34.707 → 32.401. The native-size median increases in this pair;
the single ordering and shared-host variation do not establish a causal cost or
representative throughput. Managed allocation traffic is unchanged for tiny and
dense; other medians vary by -2,980 / +760 / +1,240 / -1,275 bytes for
Ford/native/intended/large respectively. Broader performance and Linux remain open.


## Orthogonal clip-lowering observations (2026-10-06)

Matching six-input ledgers preserve parser/raster work, accounted peaks and full
PNG identity. The bounded lowering change uses the existing polygon/clip route.
Denial/cancellation controls verify its storage cleanup separately. Raw ledgers
and timing batches remain under ignored `artifacts/rectangle-route-20261006/`.

Two sequential process pairs use the original six input/options recipes,
32 warmups and three measured batches. Cells below are median milliseconds.

| Input | Baseline first | Candidate second | Candidate first | Baseline second |
| --- | ---: | ---: | ---: | ---: |
| tiny | .134 | .132 | .133 | .139 |
| Ford | 16.144 | 16.776 | 17.403 | 18.346 |
| dense | 274.566 | 276.447 | 272.940 | 305.328 |
| native-size | 475.817 | 560.846 | 477.044 | 468.706 |
| intended YouTube | 11.108 | 17.738 | 10.935 | 10.806 |
| large YouTube | 34.860 | 91.484 | 33.922 | 33.947 |

The large YouTube slowdown in the first pair does not repeat in the opposite
order. Both complete pairs are retained; shared-host variation and unchanged
resource ledgers do not establish a causal speedup or regression threshold.
Managed allocation medians vary by at most 1,506 bytes in these pairs; tiny and
dense preserve their first-pair allocation counts. Representative throughput and
Linux remain open.

## Native dash/contact observations (2026-10-06)

The same six guard-input recipes preserve parser/raster work, accounted peaks
and full PNG identity. Two affected inputs add ROC at 200×200 and the Greek flag
at Default 600×400, both transparent. Their parser work remains 90 and 187;
admitting their orthogonal contacts changes the raster route and its storage.

| Affected input | Work through raster, before → after | Accounted peak bytes, before → after |
| --- | ---: | ---: |
| ROC | 1,369,079 → 166,641 | 163,840 → 175,130 |
| Greek flag | 5,381,002 → 836,498 | 971,763 → 972,802 |

One sequential baseline-first/candidate-second process pair uses 32 warmups
and three measured batches. The baseline is the preceding `3a09694b` build.
Batch sizes remain 100 tiny, 10 Ford/intended YouTube and three dense/native-size/
large YouTube, with 30 ROC and five Greek-flag conversions added. Cells are
median milliseconds per conversion and median managed allocation bytes.

| Input | Time before → after (ms) | Allocation before → after (bytes) |
| --- | ---: | ---: |
| tiny | .145 → .134 | 27,104 → 27,104 |
| Ford | 18.024 → 18.690 | 854,195 → 857,010 |
| dense | 279.512 → 280.163 | 7,186,408 → 7,186,408 |
| native-size | 432.508 → 499.537 | 36,879,618 → 36,880,824 |
| intended YouTube | 11.072 → 11.348 | 1,041,194 → 1,041,284 |
| large YouTube | 35.044 → 36.069 | 3,948,453 → 3,948,453 |
| ROC | 44.425 → 2.150 | 213,099 → 220,726 |
| Greek flag | 123.283 → 8.231 | 1,019,916 → 1,054,596 |

The affected-case timing improvement accompanies much lower charged raster work,
with small increases in accounted peaks. Native-size timing varies despite its
unchanged work ledger; this single-order shared-host pair does not establish a
general speedup or regression threshold. Allocation traffic is separate from
accounted peak memory. Raw ledgers, batches and the matching harness remain under
ignored `artifacts/dash-endpoints-20261006/`. Representative throughput and Linux
remain open.
