# Renderer measurements

Measured **2026-10-17** at revision `c41a312e`, using the current renderer in a
clean checkout. Results describe these workloads on one Windows host; they do
not establish general SVG throughput or Linux performance.

Environment: Intel Xeon W-2223 3.60 GHz (4 cores / 8 threads), Windows 10 22H2,
.NET SDK 10.0.401 / runtime 10.0.13 x64 RyuJIT, workstation GC and BenchmarkDotNet
0.14.0. Measurements ran serially.

## Warmed conversion

BenchmarkDotNet ShortRun: one launch, three warmups and three measured iterations.
Each operation parses, rasterizes and encodes an owned PNG. Input construction,
option setup and supplied-font acquisition are outside timing. Standard deviation
is shown in milliseconds; three samples give limited timing precision.

| Method | Mean ms | Std dev ms | Allocated bytes/op |
| --- | ---: | ---: | ---: |
| `ConvertOutlinedWordmark` | 1.1093 | 0.0419 | 495,857 |
| `ConvertGradientClip` | 0.1277 | 0.0216 | 36,107 |
| `ConvertIsolatedOpacity` | 0.1017 | 0.0050 | 30,570 |
| `ConvertIdenticalInstances` | 1.5282 | 0.0248 | 275,734 |
| `ConvertPublicLabel` | 3.7963 | 0.1025 | 164,853 |

The set covers outlined geometry, clipped gradient paint, isolated opacity,
repeated patterns and supplied-font labels. Allocation is cumulative managed
traffic per conversion, not retained/live storage or process working set.

## Fresh-process conversion

The existing MemoryProbe ran three fresh processes per input at scale 1,
transparent background and default limits. All six PNGs match their unchanged
Windows references exactly. Elapsed time includes cold initialization and input/
output work; it is separate from the warmed conversion timings above.

| Input | Elapsed ms, range | Conversion allocated bytes, range |
| --- | ---: | ---: |
| `masked-source` | 492–551 | 593,392 |
| `image-resource-scope` | 361–368 | 470,016 |

Inputs: [masked filter source](../tests/Research/filter-source-exit-sheet/masked-source.svg)
and [image resource scope](../tests/Research/affine-image-maps/image-resource-scope.svg).
Memory observations follow the [approximate resource policy](SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).

## Reproduce

From a clean checkout, run the warmed set with:

```powershell
Push-Location benchmarks/Lokad.Svg.Benchmarks
dotnet run -c Release -- --job Short --filter "*SvgCurveBenchmarks*" "*SvgPatternBenchmarks.ConvertIdenticalInstances*" "*SvgSuppliedLabelBenchmarks.ConvertPublicLabel*" --exporters JSON --artifacts ../../artifacts/benchmarks
Pop-Location
```

For cold observations, build `tools/Lokad.Svg.MemoryProbe` in Release and run its
DLL in three fresh processes per linked input, using the command in [README](../README.md#diagnostic-tools).
Keep compilation outside measurement. Raw reports for this run remain under
[the ignored results directory](../artifacts/path-paint-strokes-20261017/benchmarks/).
