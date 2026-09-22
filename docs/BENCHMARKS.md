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
