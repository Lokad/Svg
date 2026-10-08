# Local benchmark baseline

The full frozen-cohort baseline predates the latest renderer repairs;
see [the corporate baseline](#frozen-corporate-cohort-baseline-2026-10-07) and
[the current resource comparison](#hard-mask-region-and-layer-bounds-cost-2026-10-08).
The earlier calibration and implementation checkpoints below retain their dates.

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

Compared the reviewed renderer `f633c86b` with the state-separation checkpoint
starting at `5c4af69c`.
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
in its assembly informational version.

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
`1876cf6e`. Full ledgers, raw batches and both process orders are historical run
records under ignored `artifacts/doctype-20261005/` and in that Git revision.
The [regression inputs](../tests/References/declaration-only-doctype.json) retain
the four original fixture identities and their semantic comparison contract.

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

## Retained rectangle paint observations (2026-10-06)

Thirteen matching input recipes use the retained `186f2676` build and current
production: six prior guards, ROC/Greek flag, two 40×32 rectangle controls, clipped
8×8 pattern content, the 1024×12 material grid and 640×320 border band. All use
transparent output with the prior viewport/scale options. Parser work is unchanged
for every recipe. All eight prior guard/ROC/Greek PNG identities are unchanged;
the original material-grid and border-band references now match exactly.

`SvgRectangle` grows 64 → 112 bytes and `SvgGeometry` 200 → 248 bytes on Windows
x64. Runtime-size array reservations charge the retained clip before allocation;
there is no new coverage buffer. Tiny examples remain below the existing 163,840-
byte XML-reader peak. Edge preparation charges four visits, or eight with a clip,
including pattern tile content; admitted pixel work is charged before painting.

| Input | Work through raster, before → after | Accounted peak bytes, before → after |
| --- | ---: | ---: |
| tiny | 220 → 220 | 163,840 → 163,840 |
| ford | 1,301,213 → 1,301,213 | 534,450 → 534,546 |
| dense | 32,761,735 → 32,761,743 | 2,494,116 → 2,495,892 |
| native-stop | 73,300,835 → 73,300,835 | 33,629,711 → 33,630,047 |
| youtube-intended | 928,430 → 928,430 | 942,655 → 942,751 |
| youtube-large | 3,026,899 → 3,026,899 | 3,732,659 → 3,732,755 |
| roc | 166,641 → 166,641 | 175,130 → 175,178 |
| greek-flag | 836,498 → 836,502 | 972,802 → 972,898 |
| rect-corner | 1,034 → 1,042 | 163,840 → 163,840 |
| rect-contained | 678 → 686 | 163,840 → 163,840 |
| pattern-content | 103 → 115 | 163,840 → 163,840 |
| material-grid | 36,890 → 39,962 | 662,600 → 748,616 |
| border-band | 10,106 → 10,110 | 824,279 → 824,327 |

Two sequential fresh-process pairs run in opposite order, with eight warmups and
three measured batches per input. Batch sizes are 100 tiny/40×32/pattern controls,
10 Ford/intended YouTube/material grid, three dense/native-size/large YouTube,
30 ROC and five Greek flag/border band. Cells are median milliseconds per
conversion. Both pairs are retained; these shared-host timings are observations,
not representative throughput or a causal regression threshold.

| Input | Pair 1 baseline | Pair 1 current | Pair 2 current | Pair 2 baseline |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.148 | 0.139 | 0.156 | 0.177 |
| ford | 21.966 | 21.962 | 23.449 | 22.114 |
| dense | 301.165 | 296.792 | 309.461 | 301.793 |
| native-stop | 506.400 | 512.074 | 530.894 | 544.831 |
| youtube-intended | 11.802 | 12.121 | 12.721 | 11.192 |
| youtube-large | 38.485 | 37.806 | 40.097 | 36.707 |
| roc | 2.065 | 2.309 | 2.135 | 2.463 |
| greek-flag | 7.904 | 8.756 | 9.395 | 9.013 |
| rect-corner | 0.095 | 0.121 | 0.108 | 0.083 |
| rect-contained | 0.080 | 0.088 | 0.084 | 0.080 |
| pattern-content | 0.051 | 0.055 | 0.053 | 0.053 |
| material-grid | 6.224 | 5.600 | 5.667 | 6.018 |
| border-band | 3.140 | 2.210 | 2.112 | 2.289 |

Median managed-allocation traffic grows by 528 bytes for tiny/border band, 1,392
for Ford and 23,520 for dense; the material grid grows by about 319,180 bytes.
These costs include larger geometry arrays and parser growth rather than only
retained peak memory. No general speedup or performance target is established.
Raw ledgers, both process orders and reproduction harnesses remain under ignored
`artifacts/rect-paint-20261006/`. Git identifies code; input/output identities
protect measured artifacts. Representative performance and Linux remain open.

## Float rectangle shader arithmetic (2026-10-06)

Matched against the retained `9f3713c9` renderer on Windows x64 / .NET 10.0.12.
Sixteen recipes include the thirteen previous guards and three fresh 40×32
linear/radial/pattern opacity controls. Every parser-work, raster-work and
accounted-peak ledger is unchanged. Rectangle/geometry storage remains 112/248
bytes. Float samples are stack values and add no raster buffer allocation.

Two fresh-process pairs run sequentially in opposite order, with eight warmups
and three measured batches per recipe. Batch sizes match the preceding rectangle
record; the three new shader controls use 100 conversions per batch. Cells are
median milliseconds per conversion. These local observations do not establish
representative throughput or a causal regression threshold.

| Input | Pair 1 baseline | Pair 1 current | Pair 2 current | Pair 2 baseline |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.134 | 0.134 | 0.142 | 0.195 |
| ford | 18.241 | 20.602 | 24.164 | 22.302 |
| dense | 278.456 | 278.365 | 276.284 | 279.263 |
| native-stop | 471.610 | 461.359 | 483.264 | 481.682 |
| youtube-intended | 10.758 | 10.773 | 10.307 | 10.683 |
| youtube-large | 34.370 | 34.367 | 32.402 | 33.412 |
| roc | 1.968 | 1.934 | 1.868 | 2.000 |
| greek-flag | 8.355 | 8.960 | 8.175 | 8.344 |
| rect-corner | 0.104 | 0.105 | 0.099 | 0.104 |
| rect-contained | 0.083 | 0.081 | 0.080 | 0.083 |
| pattern-content | 0.051 | 0.051 | 0.054 | 0.053 |
| material-grid | 5.367 | 4.693 | 4.763 | 5.195 |
| border-band | 2.515 | 2.148 | 2.051 | 2.167 |
| shader-linear | 0.408 | 0.372 | 0.383 | 0.330 |
| shader-radial | 0.244 | 0.274 | 0.275 | 0.242 |
| shader-pattern | 0.180 | 0.229 | 0.221 | 0.167 |

The radial and pattern controls cost about 12–14% and 27–32% more conversion time in
both process orders; the linear result varies with order. Linear/radial managed
allocation traffic rises by 527/109 bytes on these inputs, including their changed
encoded output; pattern traffic differs by one byte. Greater precision has a
measured cost even though the work and peak ledgers are unchanged.

Raw ledgers, allocation traffic, process orders and reproduction harnesses remain
under ignored `artifacts/shader-paint-20261006/`. Git identifies implementation;
input/output identities protect the observations. The material repair improves
pixel fidelity without changing work-accounting units. Representative performance
and Linux evidence remain open.

## Source stop-opacity and shared polygon shaders (2026-10-06)

Matched against retained `e9758253` on Windows x64 / .NET 10.0.12. Twenty-one
recipes add the intended transparent SimpleLogin/SDL/Liputan 6 cases, the notched
pattern and offset-clamp controls to the preceding sixteen guards. Every parser-
work, raster-work and accounted-peak ledger is unchanged. Stop/rectangle/geometry
storage remains 16/112/248 bytes. Analytic material grows from 5 to 24 bytes as a
stack value; no new stored array or heap owner is introduced.

Two fresh-process pairs run sequentially in opposite order, with eight warmups
and three measured batches per input. Existing batch sizes are preserved; each
new corporate input uses ten conversions and each new synthetic input 100.
Cells are median milliseconds per conversion. These shared-host observations
do not establish representative throughput or a causal regression threshold.

| Input | Pair 1 baseline | Pair 1 current | Pair 2 current | Pair 2 baseline |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.134 | 0.145 | 0.140 | 0.135 |
| ford | 32.488 | 22.416 | 18.607 | 22.668 |
| dense | 285.545 | 513.909 | 275.219 | 275.836 |
| native-stop | 483.274 | 585.565 | 455.093 | 476.767 |
| youtube-intended | 11.586 | 21.955 | 10.757 | 11.026 |
| youtube-large | 36.088 | 66.982 | 33.395 | 34.512 |
| roc | 1.907 | 3.325 | 1.947 | 1.969 |
| greek-flag | 8.177 | 14.339 | 8.625 | 8.333 |
| rect-corner | 0.100 | 0.154 | 0.101 | 0.107 |
| rect-contained | 0.085 | 0.104 | 0.082 | 0.083 |
| pattern-content | 0.055 | 0.095 | 0.053 | 0.052 |
| material-grid | 4.921 | 8.170 | 4.941 | 4.842 |
| border-band | 2.078 | 3.444 | 2.246 | 2.085 |
| shader-linear | 0.373 | 0.656 | 0.383 | 0.367 |
| shader-radial | 0.290 | 0.613 | 0.282 | 0.277 |
| shader-pattern | 0.228 | 0.462 | 0.235 | 0.214 |
| inv-001-intended | 6.535 | 8.971 | 5.976 | 6.029 |
| inv-003-intended | 158.648 | 157.063 | 158.766 | 162.068 |
| inv-040-intended | 1.422 | 1.301 | 1.349 | 1.298 |
| polygon-pattern-notch | 0.229 | 0.154 | 0.165 | 0.226 |
| stop-offset-clamp | 0.336 | 0.200 | 0.214 | 0.339 |

The first current process shows a broad slowdown across unchanged controls that
is absent in the reversed order. This variation prevents attributing that pair
to the material change. Notched-pattern and offset-clamp controls take less time
in both orders; the corporate recipes do not show a consistent regression.
Tiny, Ford, dense and the synthetic shader controls preserve managed-allocation
traffic. Allocation variations are recorded alongside changed PNG sizes; these
measurements do not isolate their cause. Raw observations retain both timing orders.

Raw ledgers, allocation traffic, process orders and reproduction harnesses remain
under ignored `artifacts/shader-stops-20261006/`. Git identifies implementation;
input/output identities protect measured artifacts. Representative performance
and Linux evidence remain open.

## CPU bitmap-local gradient dithering (2026-10-06)

Matched against retained `44115e44` on Windows x64 / .NET 10.0.12. Twenty-five
recipes add the four fresh large-canvas dither controls to the preceding twenty-
one guards. All parser/raster work ledgers and the preceding 21 peak ledgers are
unchanged. The four new peak measurements vary alongside encoded PNG size;
the encoder accounts segmented compressed storage and its final output buffer.
Segment capacity means these amounts need not change by the same number of bytes.
No raster storage or owner is added; grid/stop/rectangle/geometry/material value
sizes remain unchanged.

| Input | Accounted peak bytes, baseline → current | PNG bytes, baseline → current |
| --- | ---: | ---: |
| cpu-dither-wide-linear | 311873 → 310947 | 24639 → 24206 |
| cpu-dither-tall-linear | 276741 → 276805 | 7103 → 7135 |
| cpu-dither-radial | 1047079 → 1046749 | 113750 → 113585 |
| cpu-dither-polygon-clip | 832509 → 833197 | 6139 → 6483 |

Two fresh-process pairs run sequentially in opposite order, with eight warmups
and three measured batches per recipe. Existing batch sizes are preserved; the
four new controls each use ten conversions per batch. Cells are median
milliseconds per conversion. Shared-host observations do not establish
representative throughput or a causal regression threshold.

| Input | Pair 1 baseline | Pair 1 current | Pair 2 current | Pair 2 baseline |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.157 | 0.143 | 0.138 | 0.147 |
| ford | 22.181 | 21.308 | 21.085 | 22.953 |
| dense | 305.548 | 308.216 | 301.762 | 298.950 |
| native-stop | 554.320 | 536.855 | 432.303 | 635.339 |
| youtube-intended | 12.592 | 12.595 | 11.908 | 13.418 |
| youtube-large | 40.594 | 39.830 | 34.139 | 42.476 |
| roc | 2.362 | 2.103 | 2.188 | 2.408 |
| greek-flag | 9.234 | 10.253 | 14.252 | 11.072 |
| rect-corner | 0.099 | 0.109 | 0.165 | 0.144 |
| rect-contained | 0.096 | 0.085 | 0.151 | 0.092 |
| pattern-content | 0.092 | 0.055 | 0.088 | 0.068 |
| material-grid | 5.177 | 5.245 | 7.027 | 5.627 |
| border-band | 2.107 | 2.168 | 2.143 | 3.454 |
| shader-linear | 0.441 | 0.408 | 0.595 | 0.556 |
| shader-radial | 0.349 | 0.333 | 0.436 | 0.457 |
| shader-pattern | 0.255 | 0.259 | 0.382 | 0.380 |
| inv-001-intended | 6.078 | 7.629 | 8.142 | 7.415 |
| inv-003-intended | 174.441 | 179.424 | 178.398 | 202.685 |
| inv-040-intended | 1.510 | 1.967 | 1.929 | 1.421 |
| polygon-pattern-notch | 0.159 | 0.273 | 0.153 | 0.190 |
| stop-offset-clamp | 0.218 | 0.239 | 0.260 | 0.232 |
| cpu-dither-wide-linear | 5.313 | 7.610 | 6.411 | 9.558 |
| cpu-dither-tall-linear | 4.710 | 5.106 | 4.387 | 5.245 |
| cpu-dither-radial | 22.148 | 25.459 | 22.021 | 24.290 |
| cpu-dither-polygon-clip | 5.752 | 6.896 | 6.256 | 6.098 |

Raw ledgers, allocation traffic, process orders and reproduction harnesses remain
under ignored `artifacts/gradient-stages-20261006/`. Git identifies implementation;
input/output identities protect measured artifacts. Representative performance
and Linux evidence remain open.

## Native gradient coordinates and normalized material (2026-10-06)

Matched against retained `af63b764` on Windows x64 / .NET 10.0.12. Twenty-nine
recipes retain the preceding 25 guards and add the four fresh coordinate controls.
All raster-only work ledgers and scene value sizes are unchanged: geometry 248,
rectangle 112, gradient stop 16 and analytic paint 24 bytes. The gradient sampler
caches its current bitmap matrix as draw-local stack state without a heap owner.
Three recipes each add six shared parser work units for the gradient-transform
numeric pass; cumulative work through raster includes those parser units.

| Input | Parser work, baseline → current | Work through raster, baseline → current |
| --- | ---: | ---: |
| inv-003-intended | 3629587 → 3629593 | 6366286 → 6366292 |
| shader-coordinate-affine | 37 → 43 | 204841 → 204847 |
| shader-coordinate-bbox | 51 → 57 | 248592 → 248598 |

Five accounted peaks vary by exactly twice the encoded PNG byte difference,
covering compressed storage and the returned buffer; the other 24 peaks are
unchanged. No scene or raster storage is added.

| Input | Accounted peak bytes, baseline → current | PNG bytes, baseline → current |
| --- | ---: | ---: |
| cpu-dither-radial | 1046749 → 1046769 | 113585 → 113595 |
| shader-coordinate-linear | 955875 → 955835 | 68148 → 68128 |
| shader-coordinate-radial | 1042049 → 1042043 | 111235 → 111232 |
| shader-coordinate-affine | 961843 → 961837 | 71132 → 71129 |
| shader-coordinate-bbox | 482161 → 482163 | 44141 → 44142 |

Two fresh-process pairs run sequentially in opposite order, preserving eight
warmups and three measured batches per recipe. New controls use ten conversions
per batch. Cells are median milliseconds per conversion. Several large gradients
are slower in both pairs; these observed costs are retained rather than treated
as representative throughput or a causal regression threshold. The host is shared
and the representative-performance exit remains open.

| Input | Pair 1 baseline | Pair 1 current | Pair 2 current | Pair 2 baseline |
| --- | ---: | ---: | ---: | ---: |
| tiny | 0.194 | 0.143 | 0.140 | 0.136 |
| ford | 22.380 | 23.566 | 25.151 | 23.101 |
| dense | 362.383 | 326.020 | 313.168 | 312.305 |
| native-stop | 670.042 | 521.373 | 510.800 | 515.782 |
| youtube-intended | 16.397 | 12.453 | 11.136 | 12.722 |
| youtube-large | 45.671 | 38.006 | 37.419 | 38.285 |
| roc | 2.850 | 2.092 | 2.133 | 2.341 |
| greek-flag | 11.205 | 9.480 | 8.432 | 10.204 |
| rect-corner | 0.149 | 0.118 | 0.134 | 0.141 |
| rect-contained | 0.103 | 0.085 | 0.122 | 0.092 |
| pattern-content | 0.079 | 0.054 | 0.071 | 0.054 |
| material-grid | 5.619 | 6.127 | 6.712 | 5.175 |
| border-band | 2.764 | 2.295 | 2.196 | 2.119 |
| shader-linear | 0.525 | 0.504 | 0.531 | 0.483 |
| shader-radial | 0.491 | 0.396 | 0.480 | 0.418 |
| shader-pattern | 0.245 | 0.190 | 0.168 | 0.260 |
| inv-001-intended | 8.603 | 6.966 | 6.971 | 7.742 |
| inv-003-intended | 212.058 | 188.833 | 179.593 | 193.376 |
| inv-040-intended | 1.943 | 1.454 | 1.686 | 1.443 |
| polygon-pattern-notch | 0.171 | 0.160 | 0.155 | 0.152 |
| stop-offset-clamp | 0.260 | 0.280 | 0.211 | 0.249 |
| cpu-dither-wide-linear | 7.346 | 8.678 | 6.957 | 5.971 |
| cpu-dither-tall-linear | 5.223 | 5.879 | 6.493 | 5.045 |
| cpu-dither-radial | 25.738 | 31.247 | 26.271 | 23.432 |
| cpu-dither-polygon-clip | 6.422 | 8.050 | 7.909 | 6.323 |
| shader-coordinate-linear | 21.474 | 25.592 | 21.632 | 20.077 |
| shader-coordinate-radial | 22.645 | 26.480 | 25.621 | 23.082 |
| shader-coordinate-affine | 22.187 | 23.858 | 23.908 | 20.249 |
| shader-coordinate-bbox | 15.173 | 16.077 | 16.401 | 14.152 |

Raw ledgers, allocation traffic and process orders remain under ignored
`artifacts/gradient-coordinates-20261006/`. Git identifies implementation;
input/output identities protect measured assets. No source/build fingerprints
are recorded. Linux and representative performance remain open.

## Bitmap-local straight edges and float root mapping (2026-10-06)

Matched against retained `8ed114a9` on Windows x64 / .NET 10.0.12. Thirty-four
recipes retain the previous 29 controls and add four fresh straight-edge/stroke
controls plus intended Artsy. All parser ledgers and scene value sizes are
unchanged: geometry 248, rectangle 112, gradient stop 16 and analytic paint
24 bytes. The clip context adds two origin coordinates as an immutable value,
without a heap owner. Both construction passes charge local point mapping and
kept fixed-edge translation. Twelve cumulative raster ledgers increase:

| Input | Work through raster, baseline → current | Increase |
| --- | ---: | ---: |
| ford | 1301213 → 1301233 | 20 |
| dense | 32761743 → 33341618 | 579875 |
| native-stop | 73300835 → 73348402 | 47567 |
| youtube-intended | 928430 → 928684 | 254 |
| youtube-large | 3026899 → 3027397 | 498 |
| greek-flag | 836502 → 837813 | 1311 |
| inv-001-intended | 138562 → 138940 | 378 |
| inv-003-intended | 6366292 → 6426168 | 59876 |
| bitmap-local-straight | 583289 → 583673 | 384 |
| bitmap-local-compound | 322378 → 322723 | 345 |
| float-polygon | 702209 → 702608 | 399 |
| inv-020-intended | 4905507 → 4909892 | 4385 |

The other 22 work ledgers are unchanged. Thirty-three accounted peaks are
unchanged. Artsy decreases 4,854,953 → 4,854,913 bytes, matching two copies of
the PNG's 35,129 → 35,109-byte difference. All 25 memory scenarios pass;
there is no added scene/edge allocation owner.

Two fresh-process pairs run sequentially in opposite order, with eight warmups
and three measured batches per recipe. Cells are median milliseconds per
conversion. The table shows the new controls and selected existing paths;
all 34 recipes, allocation traffic and exact batch data remain under ignored
`artifacts/corporate-edges-20261006/`. SDL and several unchanged paths show large
variation between processes on the shared host. These measurements do not
establish representative throughput or a causal performance regression.

| Input | Pair 1 baseline | Pair 1 current | Pair 2 current | Pair 2 baseline |
| --- | ---: | ---: | ---: | ---: |
| ford | 28.226 | 21.312 | 23.679 | 18.591 |
| inv-003-intended | 170.450 | 173.486 | 276.132 | 161.309 |
| shader-coordinate-linear | 21.067 | 21.757 | 20.422 | 20.402 |
| bitmap-local-straight | 7.587 | 7.934 | 7.490 | 7.292 |
| bitmap-local-compound | 6.354 | 6.721 | 5.951 | 5.978 |
| float-polygon | 9.228 | 8.874 | 8.841 | 7.888 |
| float-stroke | 0.689 | 0.607 | 0.555 | 0.537 |
| inv-020-intended | 47.066 | 50.035 | 46.485 | 44.931 |

Git identifies implementation; input/output identities protect measured assets.
No source/build fingerprints are recorded. Linux and representative performance
remain open.

## Native open-stroke resource checkpoint (2026-10-06)

Compare the retained `f7b7d713` renderer (also the rendering state at `55e6e562`)
with native open-stroke construction/winding on Windows x64, .NET 10.0.12. Forty
matched recipes retain identical input bytes/options. The shared public owned-
memory harness uses eight warmups and three measured batches per fresh process,
then repeats current → baseline after baseline → current. Tests, captures and
package validation finish before these sequential timing processes. Local ranges
below span all six batches; they do not establish representative throughput.
Git identifies the implementation; input/PNG identities protect measured assets.
Detailed ledgers, batches and declarations remain ignored under
`artifacts/stroke-construction-20261006/`.

Eight recipes charge less parser/raster work. The other 32 work ledgers are
unchanged. All accounted peaks remain unchanged except intended Rede Manchete:
its newly retained analytic context raises peak by 3,392 bytes. Geometry,
rectangle, gradient-stop and analytic-paint values remain 248/112/16/24 bytes.
No heap owner is added; native point construction uses value/local state and
existing caller-bounded outline storage. Directed retention removes redundant
outline points and separation work; the existing scanner reserves its actual
edge/row storage and releases denied setup contexts.

| Input | Parser work before → after | Work through raster before → after | Accounted peak bytes before → after |
| --- | ---: | ---: | ---: |
| Acute miter, 64×64 | 150 → 40 | 22,224 → 9,507 | 163,840 → 163,840 |
| Acute bevel, 64×64 | 124 → 41 | 22,505 → 8,522 | 163,840 → 163,840 |
| Rounded mark, 16×16 | 342 → 63 | 2,696 → 2,618 | 163,840 → 163,840 |
| Rede Manchete, 500×500 | 1,525 → 366 | 7,877,632 → 2,182,821 | 1,052,370 → 1,055,762 |
| Short miter, 16×16 | 180 → 47 | 1,652 → 1,095 | 163,840 → 163,840 |
| Square turn, 16×16 | 160 → 47 | 2,768 → 1,784 | 163,840 → 163,840 |
| ViewBox stroke | 154 → 44 | 23,129 → 22,769 | 163,840 → 163,840 |

| Input | Before ms | After ms | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Tiny triangle | 0.046–0.071 | 0.104–0.131 | 27,633–27,637 | 27,632–27,792 |
| Acute miter | 1.113–2.375 | 0.643–0.752 | 52,754 | 51,842 |
| Acute bevel | 0.993–1.153 | 0.588–0.881 | 51,626–51,629 | 51,594–51,597 |
| Rounded mark | 0.580–0.638 | 0.262–0.370 | 49,386 | 42,226 |
| Rede Manchete | 71.262–106.546 | 26.810–72.469 | 1,196,060–1,196,193 | 1,191,756–1,191,985 |
| ViewBox stroke | 0.566–0.646 | 0.534–0.595 | 94,952–94,953 | 90,402–90,409 |
| Ford | 20.381–22.937 | 20.460–25.339 | 858,498–858,570 | 858,402–858,562 |
| Dense dragon | 283.233–312.076 | 277.188–310.756 | 7,209,936 | 7,209,856–7,209,936 |
| Native-size stop sign | 557.839–624.536 | 527.813–622.925 | 36,879,890–36,882,829 | 36,879,954–36,883,096 |

Acute/round recipes improve locally; Rede Manchete time ranges overlap. The tiny
triangle timing is higher in both process orders despite identical work/output;
its cause is unresolved and requires separate profiling before a performance
claim. Ford/dense/native-size ranges overlap. Allocation traffic includes changed
PNG sizes and JIT/GC effects; ledger savings alone do not establish throughput.
General representative performance and the prior performance obligations remain
open. All inputs keep Default admission, with explicit intended viewports where
listed.

All 3,585 tests, nine-project locked Release build, 25 memory scenarios, Chrome/
validator checks and runtime/symbol package plus installed-consumer checks pass.
The 1,125-case bank and full corporate matrix preserve every prior outcome and
exact PNG. No mismatch count or alpha-error mass increases; the retained even
round join has a higher maximum while improving 49 → four pixels. Three fresh
square-cap/bitmap-bound controls remain inexact. Three four-million-point native
normalization cancellation studies observe the caller token in 1.02–2.81 ms.
Round conics, closed/curved/dashed construction, clip fidelity and Linux remain
open.

## Retained conic-fill observations (2026-10-06)

Fourteen identical input/options recipes compare the saved `8f75f8ce` assembly
with retained conic fills and live convex walking. Ledgers are separate from
local timing: eight warmups, three batches, in both process orders. Records stay
under ignored `artifacts/conic-retention-20261006/`; Git identifies code. No
source, adapter, assembly, package or diagnostic-report hashes are recorded.
Geometry/rectangle/stop/analytic-paint sizes remain 248/112/16/24 bytes and retained
quadratic spans remain 32 bytes. The eleven oval recipes use fewer parser work units;
three nonoval controls retain their work, storage and complete PNGs.

| Recipe | Parser work | Work through raster | Accounted peak bytes |
| --- | --- | --- | --- |
| tiny | 17 → 17 | 22 → 22 | 163,840 → 163,840 |
| circle-r2 | 36 → 34 | 291 → 352 | 197,112 → 199,340 |
| ellipse | 54 → 44 | 975 → 1058 | 197,304 → 199,724 |
| scaled-circle | 87 → 61 | 41349 → 44755 | 197,688 → 200,492 |
| large-circle | 149 → 91 | 75218 → 67447 | 198,456 → 202,028 |
| cartier | 999 → 759 | 31508 → 25553 | 216,064 → 221,156 |
| ford | 4657 → 4657 | 1301233 → 1301233 | 534,546 → 534,546 |

Conic metadata and live-edge construction increase the oval peaks by 2,228–5,092
bytes. Circle r2 and the ellipse add setup/raster work; the larger circle and
Cartier 318×80 observation reduce total work. Converted pieces use the existing
bounded buffer and two-pass edge reservation. The converter itself allocates no
heap storage. Denial/cancellation tests and all 25 memory scenarios pass.

Local timing ranges overlap for tiny inputs, circle r2, the larger circle,
Cartier and Ford. The 16×16 ellipse rises from 0.191–0.202 to 0.211–0.274 ms;
its ledger includes added conic conversion and live setup. Tiny unchanged-input
ranges are 0.086–0.109 versus 0.089–0.111 ms. Larger-circle timings are
1.991–2.380 versus 1.740–2.768 ms and Cartier 318×80 timings are
2.783–3.985 versus 2.903–3.090 ms. These are local observations, without a
representative throughput claim. Broader profiling and Linux remain open.

## Native rational round-stroke observations (2026-10-06)

Sixteen identical input/options recipes compare the saved `f327d578` assembly
with native round controls and signed contacts. Recipes preceded measurement.
Ledgers are separate from local timing: eight warmups, three batches, baseline/
current and current/baseline process orders. Studies stay under ignored
`artifacts/round-stroke-conics-20261006/`; Git identifies code. Geometry/rectangle/
stop/analytic-paint/quad-span sizes remain 248/112/16/24/32 bytes.

| Recipe | Parser work | Work through raster | Accounted peak bytes |
| --- | --- | --- | --- |
| tiny | 17 → 17 | 22 → 22 | 163,840 → 163,840 |
| ellipse | 44 → 44 | 1,058 → 1,058 | 199,724 → 199,724 |
| straight | 44 → 44 | 22,769 → 22,769 | 163,840 → 163,840 |
| cap-even | 45 → 74 | 523 → 631 | 163,840 → 163,840 |
| cap-fractional | 45 → 74 | 555 → 660 | 163,840 → 163,840 |
| cap-unit | 45 → 74 | 348 → 527 | 163,840 → 163,840 |
| join-diagonal | 46 → 68 | 1,346 → 1,600 | 163,840 → 163,840 |
| join-even | 46 → 68 | 1,316 → 1,544 | 163,840 → 163,840 |
| offset-dot | 46 → 70 | 2,595 → 1,432 | 163,840 → 163,840 |
| clipped-dot | 51 → 78 | 5,404 → 2,729 | 163,840 → 163,840 |
| diagonal-caps | 47 → 76 | 5,648 → 6,935 | 163,840 → 163,840 |
| reversed-join | 46 → 68 | 4,248 → 4,477 | 163,840 → 163,840 |
| overlapping-dots | 74 → 132 | 4,486 → 7,652 | 163,840 → 163,840 |
| reversal-join | 53 → 68 | 3,240 → 921 | 163,840 → 163,840 |
| rede-manchete | 366 → 581 | 2,182,821 → 2,259,531 | 1,055,762 → 1,092,370 |
| ford | 4,657 → 4,657 | 1,301,233 → 1,301,233 | 534,546 → 534,546 |

Native retention adds parser/setup work to round inputs. Dots/clipped dots and
the reversal reduce total work, while cap/join/overlap inputs and Rede Manchete
add work. Rede Manchete's peak increases by 36,608 bytes; the small inputs stay
under the same existing peak category. Reservations include the bounded eager
fallback points, lazy span columns and returned verbs; unchanged overall peaks
do not imply zero added allocation. All memory scenarios and denial/release
tests pass. Tiny, ellipse, straight and Ford retain their ledgers and PNGs.

| Recipe | Baseline ms | Current ms | Baseline allocated bytes | Current allocated bytes |
| --- | --- | --- | --- | --- |
| tiny | 0.081–0.114 | 0.083–0.102 | 24,696–24,698 | 24,696–24,698 |
| ellipse | 0.203–0.221 | 0.202–0.221 | 98,181–98,183 | 98,181–98,183 |
| straight | 1.257–1.792 | 1.231–1.569 | 90,344–90,352 | 90,344–90,352 |
| cap-even | 0.176–0.184 | 0.156–0.199 | 35,466–35,466 | 35,314–35,314 |
| cap-fractional | 0.180–0.191 | 0.160–0.212 | 35,528–35,530 | 35,384–35,386 |
| cap-unit | 0.166–0.171 | 0.146–0.199 | 34,722–34,722 | 35,322–35,322 |
| join-diagonal | 0.237–0.273 | 0.281–0.302 | 36,392–36,394 | 36,792–36,794 |
| join-even | 0.235–0.281 | 0.265–0.292 | 36,394–36,394 | 36,794–36,794 |
| offset-dot | 0.802–0.900 | 0.255–0.292 | 37,018–37,018 | 39,850–39,850 |
| clipped-dot | 1.551–1.657 | 0.319–0.349 | 38,088–38,123 | 40,114–40,114 |
| diagonal-caps | 0.432–0.486 | 0.644–0.674 | 47,360–47,528 | 47,760–47,768 |
| reversed-join | 0.436–0.556 | 0.535–0.579 | 47,136–47,304 | 47,536–47,544 |
| overlapping-dots | 0.388–0.451 | 0.576–0.617 | 48,752–48,920 | 48,848–48,856 |
| reversal-join | 1.213–1.247 | 0.222–0.248 | 38,608–38,765 | 39,656–39,664 |
| rede-manchete | 26.037–49.475 | 29.050–65.983 | 1,191,964–1,192,017 | 1,181,092–1,181,209 |
| ford | 19.475–21.758 | 16.335–25.724 | 858,408–858,620 | 858,568–858,716 |

Overlap-dot timings are higher in both orders; reversal is lower after signed
contacts replace fallback. Rede Manchete ranges overlap with wider current
variation. These local results do not establish representative throughput or
a general performance improvement. Allocation includes PNG encoding and runtime
effects. Thin-stroke regression, wider construction/clip fidelity, profiling and
Linux retain separate obligations.

## Retained-curve root-mapping observations (2026-10-06)

Nine accepted, identical input/options recipes compare the saved `563c1b17`
assembly with native float root mapping for retained curves. Recipes preceded
measurement; the initial rejected anisotropic quadratic recipe was excluded
before any measurement and has no raster-ledger or throughput claim. Ledgers
are separate from local timing: eight warmups, three batches, baseline/current
and current/baseline process orders after capture/testing completed. Studies stay
under ignored `artifacts/corporate-curve-events-20261006/`; Git identifies code.
Geometry/rectangle/stop/analytic-paint/quad-span sizes remain 248/112/16/24/32 bytes.

| Recipe | Parser work | Work through raster | Accounted peak bytes |
| --- | --- | --- | --- |
| tiny | 17 → 17 | 22 → 22 | 163,840 → 163,840 |
| cap-unit | 74 → 74 | 527 → 527 | 163,840 → 163,840 |
| cubic-meet | 91 → 91 | 2,696 → 2,696 | 237,156 → 237,156 |
| cubic-slice | 97 → 97 | 7,230 → 7,230 | 216,536 → 216,536 |
| nasa-small | 861 → 861 | 137,455 → 136,331 | 929,568 → 929,568 |
| nasa-large | 1,008 → 1,008 | 1,472,760 → 1,472,846 | 1,207,545 → 1,207,545 |
| nike | 297 → 297 | 1,229,354 → 1,229,354 | 1,454,293 → 1,454,293 |
| blur | 2,264 → 2,264 | 1,538,551 → 1,537,385 | 1,414,527 → 1,414,523 |
| ford | 4,657 → 4,657 | 1,301,233 → 1,301,233 | 534,546 → 534,546 |

Parser work is unchanged. NASA small reduces raster work by 1,124 units; NASA
large adds 86. Blur reduces work by 1,166 units and the observed peak by four
bytes. Other ledgers are unchanged. The mapping adds no buffer or owner; changed
edge events and PNG byte counts can still affect conversion allocation. NASA
small is 254×71, NASA large 1016×284, Nike 1000×357, Blur 800×429 and Ford 500×200,
with transparent backgrounds. Tiny, unit-cap and new cubic controls use Default.
The two cubic timing inputs retain their pre-admission CRLF recipe files; banked
SVG copies use canonical LF and have fresh identical reference triples.

| Recipe | Baseline ms | Current ms | Baseline allocated bytes | Current allocated bytes |
| --- | --- | --- | --- | --- |
| tiny | 0.083–0.143 | 0.082–0.153 | 24,696–24,698 | 24,696–24,698 |
| cap-unit | 0.147–0.187 | 0.150–0.177 | 35,322–35,322 | 35,322–35,322 |
| cubic-meet | 0.396–0.431 | 0.379–0.479 | 37,560–37,562 | 37,560–37,562 |
| cubic-slice | 0.591–1.300 | 0.716–1.009 | 42,058–42,058 | 42,058–42,058 |
| nasa-small | 4.939–9.444 | 8.089–9.357 | 214,320–214,470 | 214,440–214,595 |
| nasa-large | 25.655–47.362 | 20.144–46.331 | 1,401,908–1,402,145 | 1,401,908–1,401,977 |
| nike | 12.394–14.795 | 12.571–16.606 | 1,584,740–1,586,481 | 1,585,283–1,586,928 |
| blur | 18.573–24.877 | 17.787–22.020 | 1,720,886–1,721,944 | 1,720,894–1,721,470 |
| ford | 14.955–17.133 | 15.173–19.679 | 855,280–855,417 | 855,280–855,401 |

Every timing range overlaps, including the unchanged tiny/Default controls.
NASA small shows no proportional timing saving from its reduced charged work;
JIT/GC variation and PNG encoding remain part of these observations. No general
throughput or representative-performance improvement follows. The width-one cap
keeps its prior inexact output. Non-uniform curves, wider root/source operands,
stroke/clip fidelity, profiling and Linux retain separate obligations.


## Deferred authored fill mapping observations (2026-10-06)

Nine existing, identical recipes compare the saved native-root-mapping renderer
with deferred authored fill mapping. The copied declaration preceded this
measurement; eight warmups and three batches run in fresh baseline/current and
current/baseline process orders after rendering, tests, browser and package jobs
finished. Raw records stay under ignored artifacts/deferred-root-map-20261006/.
Git identifies implementation. Geometry values grow 248 → 256 bytes; rectangle,
stop, analytic-paint and quad-span sizes remain 112/16/24/32 bytes.

| Recipe | Parser work | Work through raster | Accounted peak bytes |
| --- | --- | --- | --- |
| tiny | 17 → 17 | 22 → 22 | 163,840 → 163,840 |
| cap-unit | 74 → 74 | 527 → 527 | 163,840 → 163,840 |
| cubic-meet | 91 → 97 | 2,696 → 2,713 | 237,156 → 237,676 |
| cubic-slice | 97 → 102 | 7,230 → 7,245 | 216,536 → 216,832 |
| nasa-small | 861 → 877 | 136,331 → 136,515 | 929,568 → 1,358,584 |
| nasa-large | 1,008 → 1,024 | 1,472,846 → 1,473,035 | 1,207,545 → 2,442,096 |
| nike | 297 → 305 | 1,229,354 → 1,229,390 | 1,454,293 → 2,036,496 |
| blur | 2,264 → 2,284 | 1,537,385 → 1,537,497 | 1,414,523 → 3,328,776 |
| ford | 4,657 → 4,683 | 1,301,233 → 1,301,440 | 534,546 → 2,588,992 |

Retained commands and replay increase work and accounted storage. Conservative
builder-growth reservations contribute to these peaks; peak accounting is
distinct from actual managed allocation. Tiny and unit-cap allocation also grow
because scene geometry values are larger. All 25 memory scenarios pass.

| Recipe | Baseline ms | Current ms | Baseline allocated bytes | Current allocated bytes |
| --- | --- | --- | --- | --- |
| tiny | 0.083–0.102 | 0.083–0.100 | 24,696–24,698 | 24,784–24,786 |
| cap-unit | 0.151–0.169 | 0.149–0.178 | 35,322–35,322 | 35,450–35,450 |
| cubic-meet | 0.381–0.402 | 0.401–0.464 | 37,560–37,562 | 43,690–43,690 |
| cubic-slice | 0.597–0.932 | 0.666–1.120 | 42,058–42,058 | 47,666–47,666 |
| nasa-small | 7.073–9.088 | 7.943–9.172 | 214,440–214,449 | 281,249–281,344 |
| nasa-large | 19.451–62.678 | 21.222–37.691 | 1,401,908–1,401,977 | 1,471,776–1,472,249 |
| nike | 12.463–14.199 | 13.444–17.279 | 1,585,347–1,586,513 | 1,605,404–1,606,536 |
| blur | 18.403–21.813 | 17.561–20.570 | 1,720,931–1,721,630 | 1,801,089–1,802,672 |
| ford | 15.327–17.548 | 15.440–19.930 | 855,280–855,417 | 1,048,040–1,048,193 |

Cubic meet and Nike skew slower in these local samples. Other ranges overlap;
the wide NASA-large baseline interval is retained. Every recipe allocates more,
especially Ford. This mapping repair establishes neither a throughput saving
nor the representative performance target. Remaining fidelity, profiling and
Linux require separate evidence.


## Authored directed-fill observations (2026-10-06)

Twelve identical recipes compare saved renderer `313ee775` with authored directed
fill replay. Nine recipes are retained from the previous checkpoint; SimpleX,
Omega and Google add intended-size workloads. Inputs/options were fixed before
measurement. Two fresh processes per renderer run eight warmups and three batches,
in baseline/current then current/baseline order after test, capture, memory and
package jobs finished. Raw records stay under ignored
`artifacts/authored-topology-20261006/`. No implementation or report hashes are kept.

Geometry/rectangle/stop/analytic-paint/quad-span sizes remain 256/112/16/24/32 bytes.
Multi-contour authored fills no longer need a raster admission graph scan. Scene
normalization and its existing work remain. Replay after topology normalization
also retains commands for paths that previously discarded them.

| Recipe | Parser work | Work through raster | Accounted peak bytes |
| --- | --- | --- | --- |
| simplex | 1,144 → 1,144 | 563,889 → 559,251 | 2,944,612 → 2,944,612 |
| omega | 3,894 → 3,896 | 166,890 → 103,979 | 1,307,758 → 2,272,808 |
| google | 1,787 → 1,788 | 160,375 → 121,427 | 887,056 → 888,240 |
| tiny | 17 → 17 | 22 → 22 | 163,840 → 163,840 |
| cap-unit | 74 → 74 | 527 → 527 | 163,840 → 163,840 |
| cubic-meet | 97 → 97 | 2,713 → 2,713 | 237,676 → 237,676 |
| cubic-slice | 102 → 102 | 7,245 → 7,245 | 216,832 → 216,832 |
| nasa-small | 877 → 877 | 136,515 → 136,515 | 1,358,584 → 1,358,584 |
| nasa-large | 1,024 → 1,024 | 1,473,035 → 1,473,035 | 2,442,096 → 2,442,096 |
| nike | 305 → 305 | 1,229,390 → 1,229,390 | 2,036,496 → 2,036,496 |
| blur | 2,284 → 2,284 | 1,537,497 → 1,537,497 | 3,328,776 → 3,328,776 |
| ford | 4,683 → 4,683 | 1,301,440 → 895,344 | 2,588,992 → 2,588,992 |

| Recipe | Baseline ms | Current ms | Baseline allocated bytes | Current allocated bytes |
| --- | --- | --- | --- | --- |
| simplex | 13.251–32.379 | 11.454–22.015 | 1,068,916–1,068,916 | 1,047,564–1,047,564 |
| omega | 12.585–16.281 | 14.150–18.847 | 504,954–504,980 | 568,722–568,748 |
| google | 4.874–9.263 | 4.967–16.462 | 364,842–365,786 | 367,416–367,442 |
| tiny | 0.083–0.097 | 0.104–0.202 | 24,784–24,786 | 24,784–24,944 |
| cap-unit | 0.137–0.178 | 0.168–0.356 | 35,450–35,450 | 35,450–35,607 |
| cubic-meet | 0.285–0.329 | 0.364–0.642 | 43,690–43,690 | 43,690–43,849 |
| cubic-slice | 0.398–0.603 | 0.417–0.686 | 47,666–47,666 | 47,666–47,826 |
| nasa-small | 3.237–4.571 | 3.498–5.222 | 280,648–281,409 | 280,817–281,406 |
| nasa-large | 20.626–33.588 | 20.620–38.339 | 1,471,012–1,472,617 | 1,471,108–1,472,841 |
| nike | 15.181–26.663 | 12.901–16.317 | 1,605,409–1,606,500 | 1,605,241–1,606,456 |
| blur | 20.028–33.349 | 18.490–24.400 | 1,801,313–1,802,835 | 1,801,244–1,802,624 |
| ford | 17.096–23.625 | 14.828–20.914 | 1,048,040–1,049,873 | 1,048,056–1,049,873 |

Omega uses more retained storage and allocates roughly 64 KiB more per conversion.
Its conservative accounted peak grows by 965,050 bytes despite less raster work.
SimpleX allocates less; Google allocates slightly more. Most timing ranges overlap,
and even unchanged tiny/cubic recipes show substantial process variation. These
local measurements establish no representative latency or throughput improvement.
All 25 memory scenarios pass. Wider fidelity, profiling and Linux remain open.

## Endpoint-arc observations (2026-10-06)

Compare `8e41031e` with endpoint-arc conics using the existing observation
harness. Fifteen identical recipes include intended SimpleLogin/Toyota and small
Pepsi. Each process warms eight times and retains three batches; process order
is baseline/current then current/baseline. Concurrent validation jobs and host
variation limit timing interpretation. Raw records stay under ignored
`artifacts/endpoint-arcs-20261006/`; Git identifies code.

| Recipe | Work through raster | Accounted peak bytes |
| --- | --- | --- |
| simplelogin | 88,688 → 91,064 | 2,229,760 → 2,319,520 |
| toyota | 2,722,033 → 2,715,549 | 3,766,640 → 3,768,784 |
| pepsi | 1,893,919 → 1,938,025 | 2,799,864 → 2,802,136 |
| ford | 895,344 → 895,496 | 2,588,992 → 2,588,992 |

Replay's capacity scan now charges each inspected command. Geometry and other
fixed record sizes stay unchanged. All twelve earlier recipes keep the same
accounted peak. SimpleLogin allocates 789,972 → 944,884 bytes per conversion;
Toyota remains around 2.85 million bytes, while Pepsi falls 1,053,292 → 1,023,020 bytes.
SimpleLogin batch times are 10.692–19.809 ms before and 15.154–25.701 ms after;
both process orders are slower. Toyota and Pepsi timings vary substantially,
and unchanged Ford varies 14.942–19.264 ms before versus 15.978–47.364 ms after.
These local measurements establish no representative performance improvement.
All 25 memory scenarios pass; throughput and Linux validation remain open.

## Convex cubic and incoming-edge observations (2026-10-06)

Compare `b770997a` with live convex cubics and mandatory incoming-edge checks.
Seventeen matched recipes add IKEA intended/large to the prior fifteen inputs.
Each process warms eight times and records three batches; process order is
baseline/current then current/baseline, after validation jobs finish. Input,
viewport, scale, background and limits match. Raw observations remain under
ignored `artifacts/convex-curve-rows-20261006/`; Git identifies implementation.

All seventeen parser-work totals and accounted peaks are unchanged. IKEA
intended raster work falls 48,672 → 43,756 and large falls 149,041 → 138,863;
SimpleLogin grows 91,064 → 91,088. All fourteen other raster-work totals remain
unchanged. The cubic scratch bound now reserves three live monotonic edges,
rather than 192 independent line steps; clipped cubic pieces reserve one edge.
The measured total-conversion peaks are dominated elsewhere. Geometry and other
fixed record sizes remain unchanged.

| Recipe | Before ms/conversion | After ms/conversion | Before allocated bytes | After allocated bytes |
| --- | --- | --- | --- | --- |
| ikea | 5.443–6.837 | 5.232–5.563 | 216,543–216,641 | 205,632–205,641 |
| ikea-large | 5.690–10.088 | 5.560–8.949 | 319,433–319,828 | 308,273–308,874 |
| simplelogin | 8.712–12.821 | 9.385–12.331 | 944,884–944,896 | 944,887–944,896 |
| toyota | 53.733–66.009 | 55.411–64.008 | 2,851,200–2,853,418 | 2,851,200–2,853,426 |
| pepsi | 20.958–23.719 | 21.677–24.029 | 1,022,860–1,022,940 | 1,022,860–1,022,908 |
| simplex | 8.759–10.198 | 9.152–9.516 | 1,046,124–1,046,124 | 1,046,124–1,046,124 |
| omega | 5.079–5.995 | 5.074–5.257 | 567,746–567,764 | 567,746–567,828 |
| google | 3.021–3.716 | 2.798–3.033 | 365,768–365,852 | 365,728–365,780 |
| tiny | 0.031–0.033 | 0.030–0.033 | 24,855–24,860 | 24,854–24,860 |
| cap-unit | 0.048–0.057 | 0.047–0.053 | 35,527–35,530 | 35,527–35,530 |
| cubic-meet | 0.133–0.164 | 0.130–0.139 | 43,750–43,763 | 43,750–43,763 |
| cubic-slice | 0.198–0.213 | 0.193–0.203 | 47,740–47,745 | 47,740–47,745 |
| nasa-small | 2.529–3.004 | 2.520–2.794 | 280,745–280,828 | 280,745–280,758 |
| nasa-large | 20.117–25.060 | 20.180–21.247 | 1,470,964–1,471,081 | 1,470,964–1,471,017 |
| nike | 13.028–13.758 | 13.100–13.476 | 1,606,284–1,607,529 | 1,605,356–1,607,488 |
| blur | 18.763–19.673 | 18.941–19.514 | 1,801,228–1,802,800 | 1,801,940–1,802,736 |
| ford | 15.099–16.654 | 15.645–16.149 | 1,048,152–1,048,305 | 1,048,120–1,048,241 |

IKEA allocation decreases with live edge preparation. Host/JIT variation remains
visible in unchanged recipes; these local ranges do not establish representative
throughput or latency. All 25 memory scenarios and installed package consumers
pass. Wider fidelity, representative profiling and Linux validation remain open.

## Straight-path hairline observations (2026-10-06)

Twenty-one matching input/options recipes compare `ddaabaab` with the straight
path hairline implementation after `83a5feee`, on Windows X64 / .NET 10.0.12.
Both orders run after builds, tests, captures and corpus jobs finish. Work/peaks
are deterministic; ten local timing/allocation batches per implementation include
both orders. Detailed ledgers and batches stay under ignored
`artifacts/hairline-implementation-20261006/`.

Five affected recipes reduce raster work. Parser work grows by one to seven
units there to retain centerlines. Other sixteen recipes keep their parser/raster
work. Accounted peaks grow by 128–2,704 bytes where the larger authored owner and
retained stroke commands are live; six recipes keep their peak. Geometry/paint
value sizes remain unchanged. These costs are explicitly reserved before owner
or command allocation.

| Recipe | Work through raster, before → after | Accounted peak bytes, before → after | Local ms, before / after | Allocated bytes, before / after |
| --- | --- | --- | --- | --- |
| eisenbahn-small | 60,309 → 16,594 | 163,840 → 163,840 | 2.263–8.061 / 3.096–5.683 | 64,528–64,536 / 65,528–65,536 |
| rede-small | 659,321 → 500,058 | 337,874 → 340,578 | 20.552–27.838 / 8.790–23.657 | 396,312–396,338 / 395,176–395,202 |
| rede-intended | 2,259,531 → 1,629,394 | 1,092,410 → 1,095,114 | 23.992–82.225 / 16.896–57.645 | 1,181,684–1,181,753 / 1,175,020–1,175,276 |
| hairline-half | 11,540 → 407 | 163,840 → 163,840 | 0.223–0.252 / 0.158–0.234 | 51,249–51,249 / 49,026–49,028 |
| cap-unit | 527 → 114 | 163,840 → 163,840 | 0.048–0.059 / 0.039–0.058 | 35,527–35,530 / 34,165–34,169 |
| ikea | 43,756 → 43,756 | 2,189,432 → 2,189,816 | 2.711–3.115 / 2.826–7.050 | 205,632–205,644 / 205,848–205,860 |
| ford | 895,496 → 895,496 | 2,588,992 → 2,589,248 | 15.790–22.929 / 14.710–20.211 | 1,050,360–1,050,526 / 1,048,408–1,048,529 |

Timing ranges vary on unchanged recipes and overlap on several affected cases;
these observations do not establish representative throughput or latency. All
25 memory scenarios and installed package consumers pass. Native curve/dash,
shape/paint and bitmap-context hairlines, representative profiling and Linux
validation remain open.

## Path hairline curve observations (2026-10-06)

Twenty-four matching input/options recipes compare `ae13bdc6` with retained
hairline curve replay on Windows X64 / .NET 10.0.12. The baseline snapshot
reproduces every previous recipe's complete PNG/work/peak record. Timing and
allocation runs follow completed builds, tests, captures, corpus and package
jobs, in both execution orders, with twenty batches per implementation/recipe.
Detailed ledgers and batches stay under ignored `artifacts/hairline-curves-20261006/`.

All recipes retain parser work, accounted memory peaks and geometry/paint value
layouts. Quadratic/cubic/arc hairline recipes sharply reduce raster work; four
prior line recipes save five to thirteen units after removing curve preflight.
The round-cap probe adds two units for independently charged tangent extension.
The other sixteen recipes retain raster work. New numerical scratch uses bounded
stack buffers; no new heap owner or retained field is introduced.

| Recipe | Work through raster, before → after | Unchanged accounted peak bytes | Local ms, before / after | Allocated bytes, before / after |
| --- | --- | ---: | --- | --- |
| curve-quad | 11,100 → 295 | 170,964 | 0.725–1.196 / 0.329–0.401 | 67,330–67,333 / 56,906–56,909 |
| curve-cubic | 20,779 → 593 | 181,220 | 0.635–1.235 / 0.443–0.628 | 83,845–84,006 / 67,698–67,701 |
| curve-arc | 11,326 → 516 | 170,964 | 0.379–0.624 / 0.350–0.526 | 67,770–67,933 / 57,066–57,228 |
| eisenbahn-small | 16,594 → 16,581 | 163,840 | 2.765–3.606 / 2.776–5.556 | 65,528–65,616 / 65,528–65,696 |
| rede-small | 500,058 → 500,053 | 340,578 | 5.777–10.737 / 6.272–24.852 | 395,248–395,298 / 395,200–395,298 |
| rede-intended | 1,629,394 → 1,629,389 | 1,095,114 | 18.806–26.744 / 16.838–23.151 | 1,175,084–1,175,169 / 1,175,084–1,175,169 |
| hairline-half | 407 → 402 | 163,840 | 0.087–0.145 / 0.087–0.100 | 49,103–49,109 / 49,103–49,109 |
| ikea | 43,756 → 43,756 | 2,189,816 | 2.115–3.357 / 2.060–3.240 | 205,750–205,932 / 205,750–205,932 |
| cap-unit | 114 → 116 | 163,840 | 0.033–0.039 / 0.035–0.048 | 34,166–34,170 / 34,166–34,170 |
| ford | 895,496 → 895,496 | 2,589,248 | 16.053–20.010 / 14.957–19.242 | 1,048,408–1,048,555 / 1,048,392–1,048,545 |

Local timing ranges remain variable, including unchanged recipes. These results
do not establish representative throughput or latency. All 25 memory scenarios
and installed package consumers pass. Dashes, shape/paint and bitmap contexts,
broader corporate fidelity, representative profiling and Linux remain open.

## Native dashed-path observations (2026-10-06)

Twenty-eight matching recipes compare `990f7ce7` with retained native curve
measurement/cutting before hairline dispatch, on Windows X64 / .NET 10.0.12.
The baseline reproduces every previous recipe's complete PNG/work/peak record.
Timing/allocation runs follow completed builds, tests, corpus, capture and
package jobs, in both execution orders, with twenty batches per implementation
and recipe. Full ledgers/batches stay under ignored
`artifacts/native-dash-curves-20261006/`.

All 24 previous PNG/work/peak records and geometry/paint value layouts are
unchanged. Four new dash recipes add parser work and 256 peak accounted bytes.
Temporary list/reservation owners and backing arrays are charged before
allocation; the owner allowance fits beneath the existing outline-preparation
peak on these inputs. Cut commands are retained for later scene placement.
Three thin recipes sharply reduce raster work. The width-1.5 cubic still uses its
outline fallback and pays retention cost without changing pixels.

| Recipe | Parser work, before → after | Work through raster, before → after | Accounted peak bytes, before → after | Local ms, before / after | Allocated bytes, before / after |
| --- | --- | --- | --- | --- | --- |
| dash-quad | 135 → 438 | 15,773 → 660 | 173,596 → 173,852 | 0.644–1.175 / 0.365–0.543 | 72,482–72,485 / 65,754–65,757 |
| dash-cubic | 333 → 816 | 94,820 → 1,687 | 205,220 → 205,476 | 0.834–1.639 / 0.527–0.653 | 134,695–134,858 / 108,773–108,775 |
| dash-arc | 166 → 722 | 24,775 → 1,993 | 192,396 → 192,652 | 0.447–0.601 / 0.399–0.647 | 81,586–81,749 / 73,933–74,093 |
| dash-wider | 333 → 816 | 98,042 → 98,526 | 205,220 → 205,476 | 0.787–1.018 / 1.310–2.625 | 139,983–140,063 / 151,207–151,369 |

Local timing ranges vary, including unchanged recipes. These measurements do
not establish representative throughput or latency. All 25 memory scenarios
and installed package consumers pass. Wider stroke construction, other hairline
shape/paint and bitmap contexts, transformed/pathLength browser coverage,
corporate fidelity, representative profiling and Linux remain open.

## Native conic oval-stroke observations (2026-10-06)

Thirty-three matched recipes compare `764593a4` with native conic normal-offset
oval outlines on Windows X64 / .NET 10.0.12. The baseline independently reproduces
the preceding checkpoint's 28 complete PNG/work/peak records; the current renderer
preserves all 28. Geometry/paint value layouts remain unchanged. Builds, tests,
bank/cohort, captures and package checks finish before measurement. Both execution
orders contribute twenty batches per implementation/recipe. Ledgers and batches
stay under ignored `artifacts/native-conic-strokes-20261006/`.

| Recipe | Parser work, before → after | Work through raster, before → after | Accounted peak bytes, before → after | Local ms, before / after | Allocated bytes, before / after |
| --- | --- | --- | --- | --- | --- |
| oval-circle-w10 | 218 → 440 | 43,502 → 47,918 | 266,120 → 201,556 | 0.998–2.567 / 1.066–2.181 | 291,634–291,639 / 160,981–160,986 |
| oval-ellipse-wide-w10 | 209 → 393 | 28,135 → 31,944 | 266,120 → 201,556 | 0.769–2.979 / 0.781–2.956 | 290,626–290,788 / 159,557–159,722 |
| oval-ellipse-collapse-w10 | 107 → 327 | 9,083 → 20,989 | 265,160 → 200,852 | 0.442–2.492 / 0.620–2.768 | 278,010–278,020 / 154,491–154,496 |
| sg-small | 6,554 → 7,194 | 591,540 → 651,254 | 1,595,944 → 1,599,080 | 11.152–14.400 / 11.025–14.470 | 1,486,644–1,486,857 / 1,367,428–1,367,785 |
| sg-intended | 6,554 → 7,194 | 1,784,393 → 1,819,967 | 2,075,104 → 2,078,240 | 24.640–29.146 / 22.756–30.639 | 2,021,529–2,022,798 / 1,902,257–1,903,521 |

Fitting and retained-edge setup add work. Exact fallback point counts replace
the old maximum sampled-builder reservation, reducing the small oval peaks;
SG adds 3,136 accounted bytes. Charged private list/owner storage, builder arrays
and copied instance spans are reserved before allocation. Measured allocation
decreases, but timings overlap and unchanged recipes show host/JIT variation.
These local observations do not establish representative throughput or latency.
All 25 memory scenarios and installed consumers pass. Generic wider path strokes,
corporate fidelity, representative profiling and Linux remain open.

## Native wider path-stroke observations (2026-10-06)

Thirty-nine matched recipes compare the frozen `a3acec6e` renderer with shared
native source-verb path/polyline construction on Windows X64 / .NET 10.0.12.
The preceding 33 recipes remain, with four wider controls and two Zeppelin
sizes added. Both execution orders contribute twenty batches per implementation/
recipe. Geometry/paint value layouts and 21 complete PNG/work/peak records are
preserved. Ledgers and timing batches remain under ignored
`artifacts/native-path-strokes-20261006/`.

| Recipe | Parser work, before → after | Work through raster, before → after | Accounted peak bytes, before → after | Local ms, before / after | Allocated bytes, before / after |
| --- | --- | --- | --- | --- | --- |
| path-quad-butt-w2-p0 | 121 → 200 | 11,497 → 6,425 | 171,988 → 171,652 | 0.710–3.285 / 0.660–1.803 | 70,184–70,189 / 59,976–59,981 |
| path-quad-butt-w8-p0 | 121 → 200 | 15,867 → 7,964 | 171,988 → 171,652 | 0.935–1.837 / 0.693–0.855 | 74,320–74,482 / 60,312–60,317 |
| path-cubic-round-w8-p0 | 134 → 398 | 27,549 → 19,097 | 178,532 → 176,644 | 0.744–3.418 / 0.896–1.032 | 83,845–84,006 / 69,416–69,579 |
| path-closed-dashes-w8-p0 | 950 → 2,229 | 80,944 → 76,674 | 232,348 → 246,164 | 1.292–2.875 / 1.329–3.428 | 122,861–123,026 / 130,549–130,706 |
| zeppelin-small | 1,791 → 2,475 | 191,789 → 112,104 | 1,324,568 → 1,324,088 | 3.394–5.447 / 3.055–8.328 | 454,330–454,364 / 396,830–396,890 |
| zeppelin-intended | 1,786 → 2,470 | 434,814 → 322,797 | 1,542,240 → 1,541,760 | 6.175–10.455 / 5.934–13.903 | 703,826–706,420 / 641,568–641,856 |
| dash-wider | 816 → 2,834 | 98,526 → 466,033 | 205,476 → 219,492 | 0.685–0.851 / 2.073–2.806 | 151,279–151,288 / 142,705–142,715 |

Source-curve fitting adds parser work, including for unchanged thin-stroke PNGs.
The new wider controls generally reduce raster work and small peaks; closed
dashes add 13,816 peak bytes. Zeppelin adds parser work but reduces raster work
and peak storage. The wider dashed cubic is a clear cost regression: work rises
98,526 → 466,033 and peak storage adds 14,016 bytes despite improved pixels.
The follow-up below identifies redundant contact classification in this
work increase. This checkpoint does
not meet representative performance gates. Timing ranges include host/JIT
variation and are exploratory.

All 25 memory scenarios and installed package consumers pass. Remaining stroke
scan/paint/clip fidelity, shape/hairline contexts, representative profiling and
Linux remain open.

## Directed multi-contour stroke preparation (2026-10-06)

The frozen `6d978201` renderer and the revised preparation reuse all 39 recipes
on Windows X64 / .NET 10.0.12, with twenty batches per implementation/recipe
in both execution orders. Inputs, parser work, value layouts, every complete PNG
and all accounted peaks are unchanged. Twenty-eight complete PNG/work/peak
records remain identical; eleven use less work.

| Recipe | Parser work, before → after | Work through raster, before → after | Accounted peak bytes, before → after | Local ms, before / after | Allocated bytes, before / after |
| --- | --- | --- | --- | --- | --- |
| path-quad-butt-w8-p0 | 200 → 200 | 7,964 → 7,964 | 171,652 → 171,652 | 0.701–1.226 / 0.785–1.311 | 60,312–60,317 / 60,312–60,317 |
| path-closed-dashes-w8-p0 | 2,229 → 2,229 | 76,674 → 46,745 | 246,164 → 246,164 | 1.383–4.021 / 1.283–4.421 | 130,541–130,706 / 130,589–130,706 |
| zeppelin-small | 2,475 → 2,475 | 112,104 → 95,204 | 1,324,088 → 1,324,088 | 3.124–4.796 / 3.181–5.368 | 396,864–396,970 / 396,864–397,050 |
| sg-small | 7,194 → 7,194 | 651,254 → 518,126 | 1,599,080 → 1,599,080 | 8.561–11.503 / 8.334–12.416 | 1,366,308–1,366,377 / 1,366,308–1,366,377 |
| dash-wider | 2,834 → 2,834 | 466,033 → 27,985 | 219,492 → 219,492 | 2.160–3.507 / 0.533–0.655 | 142,703–142,715 / 142,705–142,715 |
| rede-small | 1,005 → 1,005 | 486,444 → 431,994 | 335,810 → 335,810 | 5.197–7.690 / 4.856–7.976 | 393,648–393,706 / 393,648–393,706 |

The wider dashed cubic's 468 fallback points triggered two unused pairwise
contact scans, accounting for 438,048 work units. Removing those scans reduces
its total work 466,033 → 27,985, below the earlier pre-construction checkpoint's
98,526. Its measured timing ranges improve 2.160–3.507 → 0.533–0.655 ms.
The retained-outline peak remains 219,492 bytes; the earlier 14,016-byte peak
increase is still present. Most other timing ranges overlap, including the
unchanged quadratic control; these runs do not establish representative speed.

All 1,273 bank and 258 corporate reports are identical to the preceding
checkpoint. All 3,760 tests, 25 memory scenarios and installed consumers pass.
The unchanged pinned-browser smoke and new memory artifacts pass the Windows
artifact validator. Detailed ledgers/timings stay under ignored
`artifacts/native-stroke-preparation-20261006/`. General fidelity, representative
performance, Linux and S17 remain open.

## Directed authored-fill observations (2026-10-06)

The frozen `f7e59e0e` renderer and directed scene retention reuse all 39 recipes
on Windows X64 / .NET 10.0.12. Both execution orders contribute twenty batches
per implementation/recipe. The frozen baseline ledger matches the preceding
checkpoint exactly. All inputs, complete PNGs and geometry/paint value layouts
remain identical. Twenty complete PNG/work/peak records are unchanged; nineteen
use less parser and total work. Seven accounted peaks increase 24–3,308 bytes,
eleven decrease and twenty-one remain equal.

| Recipe | Parser work, before → after | Work through raster, before → after | Accounted peak bytes, before → after | Local ms, before / after | Allocated bytes, before / after |
| --- | --- | --- | --- | --- | --- |
| ikea | 2,915 → 737 | 43,756 → 41,578 | 2,189,816 → 2,191,988 | 2.179–2.679 / 0.878–1.235 | 205,747–205,766 / 182,048–182,073 |
| simplelogin | 18,505 → 4,771 | 91,088 → 77,354 | 2,320,544 → 2,322,956 | 4.603–9.326 / 2.494–4.415 | 944,410–944,541 / 900,746–900,757 |
| toyota | 166,041 → 1,244 | 2,715,549 → 2,550,752 | 3,768,912 → 3,770,248 | 54.557–64.613 / 35.542–41.802 | 2,851,448–2,854,066 / 2,741,536–2,743,762 |
| omega | 3,896 → 2,432 | 104,138 → 102,674 | 2,273,064 → 2,276,372 | 5.457–9.210 / 2.367–3.191 | 567,914–568,068 / 503,466–503,508 |
| google | 1,788 → 1,156 | 121,513 → 120,881 | 889,008 → 889,208 | 3.016–4.583 / 2.622–3.201 | 366,160–366,210 / 352,656–352,690 |
| ford | 4,683 → 4,343 | 895,496 → 895,156 | 2,589,248 → 2,589,168 | 15.727–20.618 / 13.748–17.310 | 1,048,272–1,049,024 / 1,042,752–1,042,921 |

Removing unused scene contact surgery cuts Toyota parser work 166,041 → 1,244,
with complete pixels preserved. Its selected timing ranges improve
54.557–64.613 → 35.542–41.802 ms; peak storage adds 1,336 bytes. Raw contours also
raise Omega's peak by 3,308 bytes while avoiding normalization allocations.
These exploratory local recipes do not establish representative speed or a
release performance gate. Native stroke-instance construction changes Nepal and
Kenya Default pixels; their independent oracle comparisons improve, separately
from this matched-recipe matrix.

All 3,780 tests, 25 memory scenarios and installed consumers pass. Windows
artifact validation reuses existing pinned-browser smoke evidence. Captures,
full regressions, ledgers and timing batches stay under ignored
`artifacts/authored-fill-admission-20261006/`. General fidelity, representative
performance, Linux and S17 remain open.

## Inactive presentation admission observations (2026-10-06)

The frozen `d112fc98` renderer and shared inactive presentation admission reuse
all 39 matched recipes on Windows X64 / .NET 10.0.12. Complete input/PNG identities,
parser work, work through raster, accounted peaks and geometry/paint value layouts
are identical. These checks isolate the syntax admission from numeric rendering;
no new timing or representative performance result is claimed.

Target now renders all nine original variants exactly. Six independently captured
controls are exact; all earlier bank outputs and corporate exact PNGs remain.
All 3,817 tests, 25 memory scenarios and installed consumers pass. Windows artifact
validation reuses existing pinned-browser smoke. Full reports/ledgers stay under
ignored `artifacts/inert-presentation-20261006/`; Git identifies implementation.
General fidelity, representative performance, Linux and S17 remain open.

## Directed integer-fill resource observations (2026-10-06)

The frozen `d146a6a8` renderer and directed integer-fill change reuse 39 resource
recipes on Windows X64 / .NET 10.0.12. Inputs and geometry/paint layouts are
identical. Only Eisenbahn's PNG changes, improving its one-pixel residual to exact.
Its parser work falls 518 → 364 and work through raster falls 16,432 → 6,662.
Rede Manchete parser/raster work adds 13/28 units and 716 accounted peak bytes in
each of two recipes. Other work records are unchanged.

Command-bounded replay cuts accounted peaks in fourteen recipes. Examples:
Zeppelin small 1,324,000 → 832,480 bytes; IKEA 2,191,988 → 1,572,884;
Toyota 3,770,248 → 3,311,496; Pepsi 2,802,800 → 1,526,992. Twenty-three peaks are
unchanged. These are accounted storage observations, not resident memory or timing
claims. The 25 fresh-process memory scenarios and installed package consumers pass;
representative performance and Linux remain open. Detailed ledgers stay under
ignored `artifacts/bitmap-corner-fill-20261006/`; Git identifies implementation.

## Bitmap-local curve resource observations (2026-10-06)

The frozen `014dd3b1` renderer and bitmap-local curve mapping/raw-control bounds
reuse all 39 recipes on Windows X64 / .NET 10.0.12. Input identities, parser work,
accounted peaks and value layouts remain unchanged: geometry 256 bytes, rectangle
112, stop 16, analytic paint 24 and quadratic span 32. The source contour tape
replaces its mapped predecessor; no extra contour owner is added.

Fifteen work-through-raster totals increase; twenty-four are unchanged. Examples:
cubic-meet 2,715 → 3,010; Nike 1,229,391 → 1,231,945; Blur 1,537,422 → 1,548,316;
Ford 895,156 → 917,007. Broader raw-control bounds, local remapping and translation
are charged explicitly. Ford and other previously exact outputs retain their
complete PNGs; cubic-meet, Nike and Blur become exact. SG/Toyota recipes improve
their measured pixels without becoming exact documents.

No timings were collected for this change, so it establishes no throughput or
representative performance result. All 25 memory scenarios and installed package
consumers pass. Windows artifact validation reuses earlier pinned-browser smoke;
Linux remains open. Ledgers stay under ignored
`artifacts/interval-curve-state-20261006/`; Git identifies implementation.

## Radial matrix-order resource observations (2026-10-07)

The 39 prior Windows X64 / .NET 10.0.12 recipes retain complete input, parser/raster
work, accounted peak, layout and PNG records. Three additional matched radial
parser recipes cover SDL large and the two independent controls. The radial
value grows 152 → 208 bytes; parser dictionary allowances use its actual size,
and scene factories reserve the paint value plus 64 bytes before allocation.

SDL scene storage rises 84,972 → 85,244 bytes and parser peak rises
1,622,500 → 1,623,924 bytes. The rectangle control's scene charge rises
456 → 728 bytes; the reflected path's rises 1,220 → 1,492. Their 163,840-byte XML
allowance still dominates peaks, which remain unchanged. Parser work is unchanged
and all owners release on success and tested denials. These are accounted storage
observations, not resident-memory or throughput measurements.

Full 3,842 tests, 25 memory scenarios, installed package consumers and Windows
artifact consistency pass. One concurrent memory run missed its background-loop
cancellation phase flag; the isolated retry passes all scenarios. Prior Chrome
smoke is reused. No timing, representative performance, Linux or remote delivery
claim is made. Detailed records remain under ignored
`artifacts/radial-matrix-sdl-20261007/`; Git identifies implementation.

## Quadratic start-snap resource observation (2026-10-07)

The 39 existing resource recipes retain input identity, parser work, peak storage
and value layouts. Fifteen raster-work records change by -218 through +449 units
with native quadratic start snapping and charged bitmap-local conic mapping;
their output identities change as expected. All 25 memory scenarios pass. These
are resource observations, with no new latency or allocation-rate claim. Full
before/after records stay under ignored `artifacts/quadratic-start-snap-20261007/`.

## Legacy conic subdivision resource observation (2026-10-07)

All 39 Windows X64 / .NET 10.0.12 resource recipes preserve input identity,
parser work, accounted peak storage and value layouts. The shared conic finite
check increases fifteen work-through-raster totals by four through 356 units;
two recipe PNGs change. Conversion remains allocation-free with the same caller
buffers. The maximum reserved conversion phase increases from 70 to 72 units
to cover bounded checking and repair passes. All 3,861 tests pass, including
finite-overflow vectors, work denial, cancellation and six-output regressions.

These are resource observations; no representative latency/allocation result
is claimed. Detailed ledgers stay under ignored
`artifacts/oval-residuals-20261007/`.

## Frozen corporate cohort baseline (2026-10-07)

`SvgCorporateCohortBenchmarks` measures all 27 frozen documents / 258 recipes,
including the four failed documents and eight fresh holdouts. Setup loads and
verifies the frozen SVG asset, constructs its declared viewport/scale/background
and checks output dimensions outside timing. Each measured operation converts
the input to owned PNG memory through the public synchronous API. The renderer
is `2fb6a7ff`; no production code changes accompany this measurement.

Reproduce from the repository with:

```powershell
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks -- --job Short --filter "*SvgCorporateCohortBenchmarks*" --exporters JSON --artifacts artifacts/cohort-performance
```

The run uses BenchmarkDotNet 0.14.0, SDK 10.0.401 / .NET 10.0.12 X64 RyuJIT,
concurrent workstation GC, Windows 10 22H2 (10.0.19045.6456), and an Intel Xeon
W-2223 at 3.60 GHz with four physical / eight logical cores. ShortRun has one
launch, three warmups and three measured iterations. All 258 cases complete;
exported timing/allocation rows match the complete per-case log identities.
Raw reports and a full variant-keyed summary remain under ignored
`artifacts/cohort-performance-20261007/`.

The table contains the 243 small/intended/large recipes: three backgrounds for
each document. The fifteen additional historical-size recipes are also measured
and retained in the complete report. Time columns summarize the distribution
of per-variant means; they are not request-latency percentiles.

| Stratum / size | Cases | Median mean (ms) | Maximum mean (ms) | Median allocated (MiB) | Maximum allocated (MiB) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Marks / small | 39 | 5.04 | 22.13 | 0.74 | 1.31 |
| Marks / intended | 39 | 16.37 | 93.23 | 1.46 | 4.93 |
| Marks / large | 39 | 53.20 | 290.46 | 4.37 | 19.31 |
| Wordmarks / small | 42 | 3.71 | 19.35 | 0.52 | 2.01 |
| Wordmarks / intended | 42 | 10.52 | 57.43 | 1.00 | 5.57 |
| Wordmarks / large | 42 | 28.66 | 160.88 | 2.55 | 19.41 |

Pepsi's 1600×1600 variants are the slowest at 196.84–290.46 ms mean, depending
on background. Larger outputs increase allocation traffic even when they are
pixel-exact. [MemoryDiagnoser](https://benchmarkdotnet.org/articles/configs/diagnosers.html)
reports cumulative managed allocation per operation, including owned output;
it does not report renderer-accounted live storage or OS peak working set.

This establishes a Windows baseline for the selected frozen cohort. Three
iterations leave broad confidence intervals: no speedup, regression threshold,
cold-start latency, concurrent-service capacity or broader SVG rate is inferred.
Fresh Windows Release, 25 memory scenarios, installed runtime/symbol-package
consumers, pinned-browser smoke, font tooling and artifact consistency pass.
The renderer's 3,861 tests now also pass on local WSL Linux with native platform
vectors and three independently captured Pepsi references. No Linux timing claim
is made. A complete fresh Linux oracle matrix and remote CI evidence remain open;
see [the platform record](RASTERIZATION_DESIGN.md#linux-delivery-and-platform-expectations-2026-10-07).

## Contained bitmap-edge construction cost (2026-10-07)

The shared contained-path repair maps borrowed source operands on demand and
translates constructed fixed state under the existing work limit. No point tape
or new owner is allocated. Thirty-nine matching Windows ledger recipes retain
their input identities, parser work, accounted peak and value-type layouts.
SimpleX adds 384 raster-work units; the other 38 complete records are identical.

Five additional matching recipes include the affected large SG Rotation case.
Parser work and accounted peaks remain equal in every case:

| Recipe | Raster work before → after | Accounted peak bytes, both |
| --- | ---: | ---: |
| 8×8 triangle | 238 → 238 | 163,840 |
| Ford, 500×200 | 917,007 → 917,007 | 2,589,168 |
| Dense inventory `inv-082`, Default | 15,721,321 → 15,721,491 | 4,073,248 |
| Gradient inventory `inv-050`, Default | 73,251,810 → 73,251,810 | 33,581,608 |
| SG Rotation, 792×806 | 5,415,275 → 5,421,494 | 3,640,716 |

The SG Rotation increase is 6,219 units (about 0.115%) and closes its one-pixel
large-variant difference. Accounted peak is live renderer storage, distinct from
managed allocation traffic and OS working set. Matching warmed stopwatch batches
also remain in the ignored study. A reverse-order follow-up showed wide timing
variation, so these batches establish neither a latency regression nor a speedup
and do not replace the full-cohort ShortRun. Raw ledgers and measurements stay under ignored
`artifacts/cohort-residuals-20261007/`.

## Native filled-path rectangle cost (2026-10-07)

Thirty-two matching Windows recipes compare the Git baseline `381d4abf` with the
native rectangle repair: nine variants each of SimpleLogin, Zeppelin and IBM,
plus the five new independent control documents. Parser work and accounted peak
remain equal in every recipe. Prepared contours add a 16-byte borrowed command
view, retaining no extra tape; their x64 owner fits the existing 128-byte allowance.
Eligible rectangles allocate no edge/row/event scratch.

| Recipes | Change in work through raster |
| --- | ---: |
| SimpleLogin, small/intended, each background | +35 |
| SimpleLogin, large, each background | −175 |
| Zeppelin and IBM, all eighteen recipes | 0 |
| Five independent control documents | −1,085 through −2,431 |

The added recognition charge precedes bounded mapping/clipping. Wider masks and
AA clips retain the analytic run route. Accounted peak is live renderer storage;
these ledgers measure neither allocation traffic nor elapsed time and establish
no throughput claim. The earlier 258-recipe ShortRun remains the performance
baseline. Raw ledgers stay under ignored `artifacts/simplelogin-20261007/`.

## Rounded-rectangle source commands (2026-10-07)

Twenty-six matching independent Windows recipes compare the validated
`3aeffaa5` renderer with retained rounded conics and native shape convexity.
Inputs/options stay unchanged; both process orders retain parser/raster work,
accounted peak storage and three owned-output allocation/timing batches.

| Measured quantity | Change per conversion |
| --- | ---: |
| Parser plus raster work | −5,245 to +1,265 units |
| Accounted peak live storage | zero to +75,978 bytes |
| Managed allocation traffic | −63,200 to +9,992 bytes |

Filled shapes add retained source storage and replay; strokes replace sampled
polyline expansion with source-native outlines. The largest accounted increase
belongs to the oval-stroke control. These are small synthetic recipes, not the
representative cohort performance baseline. Timing directions vary by recipe and
process order on a loaded host; no throughput improvement is claimed. Full
ledgers and batches stay under ignored `artifacts/rounded-rectangle-conics-20261007/`.

## Uniform group-mask composition (2026-10-07)

Forty-five matching admitted independent Windows recipes compare `70bdf463`
with isolated uniform group masks. Inputs and options stay unchanged, including
backward/forward references, overlaps, paints, mask values and three backgrounds.
Both process orders record total conversion work, accounted peak and three
owned-output allocation/timing batches.

| Measured quantity | Change per conversion |
| --- | ---: |
| Total accounted conversion work | zero to +1,590 units |
| Accounted peak live storage | zero to +6,508 bytes |
| Managed allocation traffic | −28 to +5,416 bytes |

Surface work includes parsing; total work counts it once. Backward masks now
allocate one existing isolated bitmap instead of stamping
each child's paint. Parse peak can dominate these small inputs even when raster
storage increases. These controls establish resource costs, not representative
throughput; exploratory timing on a loaded host carries no performance claim.
The earlier 258-recipe ShortRun retains its baseline scope. Full ledgers stay
under ignored `artifacts/uniform-mask-layers-20261007/`.

## Adjacent mask command ranges (2026-10-07)

Six matching admitted Windows recipes compare `52153a4a` with corrected sibling
range insertion, using the same inputs/options in both process orders. Parser
work and total conversion work each decrease by two units; surface work includes
parsing and is counted once. Accounted peak live storage is unchanged. Owned-output
allocation traffic increases by eight bytes on transparent backgrounds and 24
bytes on white/black; encoded output changes with the corrected composition.
Three newly admitted empty-group configurations have no baseline conversion and
are excluded. Exploratory timing batches establish no representative throughput
claim. Full ledgers stay under ignored `artifacts/leaf-mask-scopes-20261007/`.

## Leaf and instance mask composition (2026-10-07)

Seventy-two matching admitted Windows recipes compare `7b3f549d` with isolated
leaf/use-instance masks. Original input bytes and background recipes match in
both process orders. Six controls rejected by the baseline have no matching
conversion and are excluded. Three batches measure owned-output allocation
traffic; surface work includes parser work and is counted once.

| Measured quantity | Change per conversion |
| --- | ---: |
| Parser work | +0 to +72 units |
| Total accounted conversion work | -2,230 to +12,702 units |
| Accounted peak live storage | +0 to +7,868 bytes |
| Managed allocation traffic | +224 to +62,046 bytes |

Temporary group clones now have parser-owned storage and release on success,
work/memory denial and cancellation. Geometry values grow 256 → 264 bytes, including unmasked inputs and retained
pattern geometry; arrays charge the actual value size. Draw commands remain 72
bytes. New parser metadata and isolated surfaces have real costs; removed paint
stamping can reduce other allocations. This study does not measure a representative
workload or establish throughput. The preceding 258-recipe Windows ShortRun
retains its historical scope. Full recipes and both-order ledgers stay under
ignored `artifacts/leaf-mask-scopes-20261007/`.

## Recorded paint and opacity cost (2026-10-08)

Seventy-two matching admitted Windows recipes compare the `234cd638` renderer
with the shared recorded-paint repair. Original SVG bytes, background options
and owned-memory output match in both process orders. Each process warms up eight
conversions and records three batches of twenty conversions per recipe. The
allocation delta below compares medians across six batches per renderer; surface
work includes parsing and is counted once.

| Measured quantity | Change per conversion |
| --- | ---: |
| Parser work | +5 to +76 units |
| Total accounted conversion work | +12 to +4,263 units |
| Accounted peak live storage | zero to +568 bytes |
| Managed allocation traffic | +760 to +20,711 bytes |

Geometry values grow 264 → 272 bytes; draw commands remain 72 bytes. The scene
arena reserves a 192-byte owner and stores 68-byte paint values, including
separate optional object and source visual bounds. Copy maps and layer-bound
scans also consume accounted storage/work. The largest work/allocation increases
belong to the white-mask copy controls; the largest peak increase belongs to the
circle/ellipse alpha controls. Parse peak can dominate these small recipes even
when raster storage increases.

Summing each recipe's median time gives 67.973 → 74.474 ms when the baseline
runs first, and 70.226 → 70.525 ms when the repair runs first. These exploratory
timings on a loaded host do not establish representative throughput or a speed
improvement. The earlier 258-recipe Windows ShortRun retains its historical
scope. Full recipes, batches, resource ledgers and the production audit remain
under ignored `artifacts/opacity-folding-20261007/paint-cost-*` and
`paint-production-audit.json`; Git identifies the implementation.

## Hard mask-region and layer-bounds cost (2026-10-08)

Seventy-two matching admitted Windows recipes compare `ae95dbbb` with hard
mask-region rounding and the union of content/mask layer bounds. Original SVG
bytes, backgrounds and owned-memory output match in both process orders. Each
process warms up eight conversions and records three batches of twenty per recipe;
allocation deltas compare medians across six batches per renderer.

Parser work and accounted peak live storage are unchanged. Total accounted
conversion work adds **8–232 units** for the content bounds scan; surface work
includes parsing and is counted once. Geometry/command/paint-value sizes remain
**272 / 72 / 68 bytes**. Observed managed allocation deltas range from −112 to
+89 bytes per conversion; this is allocation traffic, not retained storage.

Summed per-recipe median timings read 93.696 → 76.010 ms when the baseline runs
first, and 75.120 → 80.522 ms when the repair runs first. Opposite directions
and the loaded host do not support a representative throughput claim. The earlier
258-recipe Windows ShortRun retains its historical scope. Full recipes, batches
and resource ledgers remain under ignored `artifacts/mask-region-rounding-20261008/`.

## Isolated layer curve construction cost (2026-10-08)

Seventy-two matching admitted Windows recipes compare `d6da414f` with isolated
bitmap curve construction and drawable shader-polygon layer bounds. Original SVG
bytes, backgrounds and owned-memory output match in both process orders. Each
process warms up eight conversions and records three batches of twenty per recipe;
allocation deltas compare medians across six batches per renderer.

Parser work and accounted peak live storage are unchanged. Total accounted
conversion work adds **zero to 1,944 units** for bitmap-local operand mapping and
drawable bounds traversal; surface work includes parsing and is counted once.
Geometry/command/paint-value sizes remain **272 / 72 / 68 bytes**. Observed managed
allocation deltas range from **−152 to +153.5 bytes** per conversion; this measures
allocation traffic, not retained storage.

Summed per-recipe median timings read **76.111 → 76.993 ms** when the baseline runs
first, and **78.307 → 77.747 ms** when the repair runs first. These exploratory
measurements do not establish representative throughput. The earlier 258-recipe
Windows ShortRun retains its historical scope. Recipes, batches and resource
ledgers remain under ignored `artifacts/layer-curve-construction-20261008/`.

## Direct-paint fringe storage cost (2026-10-08)

Seventy-two matching admitted Windows recipes compare `cac7d282` with direct-paint
fringe retention. Original SVG bytes, backgrounds and owned-memory output match
in both process orders. Each process warms up eight conversions and records
three batches of twenty per recipe; allocation deltas compare medians across six
batches per renderer.

Parser work, total accounted conversion work and accounted peak live storage are
unchanged for these recipes. They do not exercise direct-link growth. New fringe
controls separately test charged initialization/copying, expanded destination
traversal, old-plus-new live storage denial and caller-token cancellation.
Geometry/command/paint-value sizes remain **272 / 72 / 68 bytes**. Observed managed
allocation deltas range from **−80.5 to +158 bytes** per conversion.

Summed per-recipe median timings read **66.181 → 66.745 ms** when the baseline
runs first and **116.106 → 84.616 ms** when the repair runs first. The loaded host
and different process-order observations do not support a throughput claim.
The earlier 258-recipe Windows ShortRun retains its historical scope. Recipes,
batches and resource ledgers stay under ignored `artifacts/direct-paint-spill-20261008/`.

## SVG rounded-rectangle command-order cost (2026-10-08)

Seventy-two matching admitted Windows recipes compare `ab4f3ceb` with SVG
rounded-rectangle source order. Original SVG bytes, backgrounds and owned-memory
output match in both process orders. Each process warms up eight conversions and
records three batches of twenty per recipe; allocation deltas compare medians
across six batches per renderer.

Parser work, total accounted conversion work and accounted peak live storage are
unchanged. Geometry/command/paint-value sizes remain **272 / 72 / 68 bytes**.
Observed managed allocation deltas range from **−160 to +82.5 bytes** per
conversion; these measure allocation traffic rather than retained storage.
The independent shape controls separately preserve bounded construction/copying,
work/memory denial and caller-token cancellation.

Summed per-recipe median timings read **73.070 → 84.082 ms** when the baseline
runs first and **77.237 → 77.243 ms** when the repair runs first. Different
process-order observations on this host do not establish representative
throughput. The earlier 258-recipe Windows ShortRun retains its historical scope.
Recipes, batches and resource ledgers stay under ignored
`artifacts/hairline-color-20261008/`.

## Live convex polynomial quadratic cost (2026-10-08)

Fourteen matching Windows recipes compare the preceding production baseline and
the live convex-quadratic repair, including its paint-fill stroke cleanup. Twelve
independent controls cover solid and linear/radial outlines under integer,
fractional, clipped and scaled placement; two retained 16×16/64×64 solid arches
exercise the original defects. Inputs, options, backgrounds and output forms match
in both process orders. Each process warms up eight conversions and records three
batches of twenty per recipe. Allocation deltas compare medians across six batches
per renderer; timing sums add each recipe's batch median.

Parser work stays unchanged. Total conversion work falls by
**140–1,374 units**, with unchanged accounted peaks. Managed allocations
fall by **1,091–2,589 bytes** per conversion. Live edge reservations replace
the conservative pre-stepped expansion bound. The peak observations include
parser workspace and rasterization; allocation traffic and live ownership remain
different measurements. Geometry/command/paint-value sizes stay **272 / 72 / 68 bytes**.
Twelve fresh resource cases and six parser-work-denial regressions separately
verify reserve-before-allocation, owner release and cancellation behavior.

Summed per-recipe median timings read **13.186 → 16.076 ms**
when the baseline runs first and **20.295 → 14.932 ms**
when the repair runs first. These small, selected controls do not establish
representative throughput or Linux timing. The earlier corporate ShortRun retains
its dated scope. Recipes, batches and ledgers remain ignored under
`artifacts/convex-polynomial-quads-20261008/`.
