# Renderer benchmarks

Benchmarks measure the current checkout. They cover conversion costs separately
from the [correctness qualification](../tests/README.md) and approximate
[live-memory budget](SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).

| Workload | Definition |
| --- | --- |
| Outlined curves, clipped gradients, opacity | [SvgCurveBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgCurveBenchmarks.cs) |
| Repeated geometry and styled instances | [SvgSceneBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgSceneBenchmarks.cs) |
| Repeating tiles | [SvgPatternBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgPatternBenchmarks.cs) |
| Supplied-font labels | [SvgSuppliedLabelBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgTextBenchmarks.cs) |

Run a warmed conversion sample from the repository root:

```powershell
dotnet run -c Release --framework net11.0 --project benchmarks/Lokad.Svg.Benchmarks -- --job Short --filter "*SvgCurveBenchmarks*" "*SvgSceneBenchmarks*" --exporters JSON --artifacts artifacts/benchmarks
```

Input construction, option setup and font acquisition belong outside warmed
conversion timing. Report the inputs/options, CPU/OS, .NET/runtime/GC, warmups,
sample count and variability with each measurement. Run hosts serially without
concurrent builds or captures. Short runs do not establish general SVG throughput.

BenchmarkDotNet allocation counts measure cumulative managed traffic per operation,
not peak live memory. For cold observations, use fresh MemoryProbe processes with
the [diagnostic procedure](../README.md#diagnostic-tools); report initialization
and input/output work separately. Verify output pixels when comparing changes.
