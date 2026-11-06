# Renderer benchmarks

Benchmarks measure the current checkout. They cover conversion costs separately
from the [correctness qualification](../tests/README.md) and approximate
[live-memory budget](SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).

BenchmarkDotNet `0.16.0-preview.2` is pinned for explicit .NET 11 runtime support.
The [document qualification](../tests/Research/release-baselines/benchmark-authored-documents.json)
checks discovery, frozen platform pixels and compilation of all 86 generated
workloads per runtime on Windows/Linux. It preserves the 80 workloads from the
[reference-document qualification](../tests/Research/release-baselines/benchmark-reference-documents.json).
Storage observations are untimed; scene rasterization excludes preparation and PNG
encoding. The [builder qualification](../tests/Research/release-baselines/benchmark-net11-qualification.json)
owns the pinned toolchain setup. Neither establishes measured renderer performance.

| Workload | Definition |
| --- | --- |
| Outlined curves, clipped gradients, opacity | [SvgCurveBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgCurveBenchmarks.cs) |
| Repeated geometry and styled instances, whole conversion and rasterization separately | [SvgSceneBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgSceneBenchmarks.cs) |
| Repeating tiles | [SvgPatternBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgPatternBenchmarks.cs) |
| Supplied-font labels | [SvgSuppliedLabelBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgTextBenchmarks.cs) |
| PNG encoding across entropy and destination types | [PngBufferBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/PngBufferBenchmarks.cs) |
| Reference documents and authored icon sheet, wordmark and chart; conversion and rasterization separately | [SvgReferenceDocumentBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgReferenceDocumentBenchmarks.cs) |

List parameterized jobs without executing benchmarks:

```powershell
dotnet run -c Release --framework net11.0 --project benchmarks/Lokad.Svg.Benchmarks -- --list json --runtimes net10.0 net11.0
```

Run timed measurements on a dedicated quiet host. A warmed conversion sample:

```powershell
dotnet run -c Release --framework net11.0 --project benchmarks/Lokad.Svg.Benchmarks -- --job Short --filter "*SvgCurveBenchmarks*" "*SvgSceneBenchmarks*" --exporters JSON --artifacts artifacts/benchmarks
```

Reports use the selected artifacts directory. BenchmarkDotNet keeps generated
projects in the benchmark project's ignored `.bdn/` directory.

Input construction, option setup and font acquisition belong outside warmed
conversion timing. Report the inputs/options, CPU/OS, .NET/runtime/GC, warmups,
sample count and variability with each measurement. Run hosts serially without
concurrent builds or captures. Short runs do not establish general SVG throughput.

BenchmarkDotNet allocation counts measure cumulative managed traffic per operation,
not peak live memory. Returning a byte count from a timed method does not turn it
into a reported memory metric. Print tracked encoder storage separately:

```powershell
dotnet run -c Release --framework net11.0 --project benchmarks/Lokad.Svg.Benchmarks -- --png-memory
```

This untimed JSON report includes source/output hashes, encoded lengths and owned/
stream reservation snapshots for both entropy inputs. It verifies matching PNGs,
unchanged inputs and scratch refunds. Caller inputs, stream capacity and process/
native memory are outside those reservations. Synthetic scene conversions retain
only input bytes in setup. Reference-document conversions additionally retain the
declared font snapshot; font-free cases acquire none. Raster setups prepare one
root, released before fonts in cleanup. The PNG cases use separate fixed 512×512 inputs.

Document workloads use nine sources from the
[upstream selection](../tests/Research/upstream-static-documents/README.md) and the
[authored workload bank](../tests/Research/benchmark-workloads/README.md): a 512×384
icon sheet, 768×192 wordmark and 1024×640 chart. Both banks retain their frozen
source hashes, viewport, scale, background and independent platform references,
sharing the supplied regular/real-italic font programs. Input I/O, catalog binding
and acquisition stay outside both timings.

The [installed/native preflight](../tests/Research/release-baselines/installed-version-comparison.json)
qualifies package/asset identities and frozen pixels on one fixed runtime. Follow
the [comparison procedures](../tools/README.md#installed-and-native-comparison-preflight)
before timing; record native fidelity differences alongside performance results.

For cold observations, use fresh MemoryProbe processes with
the [diagnostic procedure](../README.md#diagnostic-tools); report initialization
and input/output work separately. Verify output pixels when comparing changes.
