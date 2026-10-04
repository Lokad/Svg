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
