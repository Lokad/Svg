# Renderer measurements

## Leaf declaration reuse (2026-10-20)

Compared `596d05f6` with `9b9828fe` on the same i7-14700KF/Windows/.NET host
described below. Two alternating fresh-process pairs ran serially, with tiered
compilation disabled and affinity to logical processor 2. Each workload had 500
warmups and seven samples of 500 conversions, excluding input setup. Times are
the range of the two process medians.

| Workload | Allocated bytes/op, before → after | Median ms, before | Median ms, after |
| --- | ---: | ---: | ---: |
| 256 stylesheet rectangles | ≈762,748 → ≈762,748 | 2.7197–2.9599 | 2.1022–2.1430 |
| 256 inline-style rectangles | 787,092 → ≈746,132 | 2.7233–2.7879 | 2.0392–2.0872 |

Both inputs are defined in [SvgSceneBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgSceneBenchmarks.cs).
Inline declarations parse 256 times instead of 1,536 times, saving about 40,960
allocated bytes per conversion (5.2%). The outlined-wordmark, gradient-clip and
isolated-opacity controls retain their allocation counts. All five output PNG
hashes are unchanged. Timing varies between processes; these short Windows runs
do not establish general throughput or Linux performance. Measurements use the
same direct `Stopwatch`/allocation-counter method described below.

## Simplification comparison (2026-10-20)

Compared `ee993567` with `6c2def9b` on an Intel Core i7-14700KF, Windows build
26200, .NET SDK 10.0.401/runtime 10.0.12 x64 and workstation GC. Two alternating
fresh-process pairs ran serially with tiered compilation disabled. Each workload
had 200 warmups and seven samples of 200 conversions; input setup was excluded.
Times below are the range of the two process medians. This short comparison
shows allocation savings; small timing differences are inconclusive.

| Workload | Allocated bytes/op, before → after | Median ms, before | Median ms, after |
| --- | ---: | ---: | ---: |
| Outlined wordmark | 496,216 → 482,968 | 0.6676–0.6679 | 0.6654–0.6697 |
| Gradient clip | 36,512 → 35,504 | 0.0595–0.0607 | 0.0570–0.0634 |
| Isolated opacity | 30,952 → 29,512 | 0.0451–0.0468 | 0.0443 |
| 256 styled rectangles | 936,430 → 762,748 | 2.9128–3.0221 | 2.7569–2.8972 |

Inputs are defined in [SvgCurveBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgCurveBenchmarks.cs)
and [SvgSceneBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgSceneBenchmarks.cs).
Direct measurements use `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread`
around those conversion methods after `SetUp`. All four output PNG hashes are
unchanged. On x64, retained `SvgGeometry` values shrink from 280 to 208 bytes.
These cases do not establish general SVG throughput or Linux performance.

## Earlier benchmark run (2026-10-17)

Measured **2026-10-17** at revision `397c8ea2` in a clean checkout.
Results describe these workloads on one Windows host; they do
not establish general SVG throughput or Linux performance.

Environment: Intel Xeon W-2223 3.60 GHz (4 cores / 8 threads), Windows 10 22H2,
.NET SDK 10.0.401 / runtime 10.0.13 x64 RyuJIT, workstation GC and BenchmarkDotNet
0.14.0. Measurements ran serially.

### Warmed conversion

BenchmarkDotNet ShortRun: one launch, three warmups and three measured iterations.
Each operation parses, rasterizes and encodes an owned PNG. Input construction,
option setup and supplied-font acquisition are outside timing. Standard deviation
is shown in milliseconds; three samples give limited timing precision.

| Method | Mean ms | Std dev ms | Allocated bytes/op |
| --- | ---: | ---: | ---: |
| `ConvertOutlinedWordmark` | 0.9699 | 0.0029 | 495,856 |
| `ConvertGradientClip` | 0.1307 | 0.0293 | 36,131 |
| `ConvertIsolatedOpacity` | 0.0979 | 0.0046 | 30,570 |
| `ConvertIdenticalInstances` | 1.3559 | 0.0045 | 275,730 |
| `ConvertPublicLabel` | 3.8016 | 0.0139 | 164,854 |

The set covers outlined geometry, clipped gradient paint, isolated opacity,
repeated patterns and supplied-font labels. Allocation is cumulative managed
traffic per conversion, not retained/live storage or process working set.

### Fresh-process conversion

The existing MemoryProbe ran three fresh processes per input at scale 1,
transparent background and default limits. All six PNGs match their unchanged
Windows references exactly. Elapsed time includes cold initialization and input/
output work; it is separate from the warmed conversion timings above.

| Input | Elapsed ms, range | Conversion allocated bytes, range |
| --- | ---: | ---: |
| `masked-source` | 478–527 | 581,056–593,392 |
| `image-resource-scope` | 351–355 | 470,016 |

Inputs: [masked filter source](../tests/Research/filter-source-exit-sheet/masked-source.svg)
and [image resource scope](../tests/Research/affine-image-maps/image-resource-scope.svg).
Memory observations follow the [approximate resource policy](SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).

## Reproduce with BenchmarkDotNet

From a clean checkout, run the warmed set with:

```powershell
Push-Location benchmarks/Lokad.Svg.Benchmarks
dotnet run -c Release -- --job Short --filter "*SvgCurveBenchmarks*" "*SvgSceneBenchmarks*" "*SvgPatternBenchmarks.ConvertIdenticalInstances*" "*SvgSuppliedLabelBenchmarks.ConvertPublicLabel*" --exporters JSON --artifacts ../../artifacts/benchmarks
Pop-Location
```

For cold observations, build `tools/Lokad.Svg.MemoryProbe` in Release and run its
DLL in three fresh processes per linked input, using the command in [README](../README.md#diagnostic-tools).
Keep compilation outside measurement. The command above generates local JSON
reports; the tables here retain the measured results.
