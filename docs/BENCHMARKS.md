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
  returning the 64-character hex identity length so the hash is retained. Input
  setup occurs outside the timed method; no provider or snapshot work is
  included.

| Program bytes | Mean | Allocated |
| --- | ---: | ---: |
| 4096 | 19.93 μs | 344 B |
| 1048576 | 5,086.66 μs | 347 B |

Allocation is the fresh hex identity per verification plus hashing traffic;
elapsed time scales with program length. This does not measure acquisition,
snapshot field validation, cancellation latency or repeated/distinct program
behavior.
