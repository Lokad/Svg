# Renderer benchmarks

Benchmarks measure the current checkout. They cover conversion costs separately
from the [correctness qualification](../tests/README.md) and approximate
[live-memory budget](SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).

BenchmarkDotNet `0.16.0-preview.2` is pinned for explicit .NET 11 runtime support.
The [component qualification](../tests/Research/release-baselines/benchmark-direct-mechanisms.json)
checks all 96 generated workloads per runtime on Windows/Linux: 62 timed methods,
including ten direct component workloads. It preserves the 86 workloads and frozen
platform pixels from the [document qualification](../tests/Research/release-baselines/benchmark-authored-documents.json).
Storage observations are untimed; scene rasterization excludes preparation and PNG
encoding. The [builder qualification](../tests/Research/release-baselines/benchmark-net11-qualification.json)
owns the pinned toolchain setup. Neither establishes measured renderer performance.

| Workload | Definition |
| --- | --- |
| Outlined curves, clipped gradients, opacity | [SvgCurveBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgCurveBenchmarks.cs) |
| Repeated geometry and styled instances, whole conversion and rasterization separately | [SvgSceneBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgSceneBenchmarks.cs) |
| Repeating tiles | [SvgPatternBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgPatternBenchmarks.cs) |
| XML/reference admission and direct CSS declaration/rule parsing | [SvgParseBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgParseBenchmarks.cs) |
| Polygon coverage, blending, gradient sampling, Gaussian blur and color operations | [SvgRasterKernelBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgRasterKernelBenchmarks.cs) |
| Supplied-font labels | [SvgSuppliedLabelBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgTextBenchmarks.cs) |
| PNG encoding across entropy and destination types | [PngBufferBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/PngBufferBenchmarks.cs) |
| Reference documents and authored icon sheet, wordmark and chart; conversion and rasterization separately | [SvgReferenceDocumentBenchmarks](../benchmarks/Lokad.Svg.Benchmarks/SvgReferenceDocumentBenchmarks.cs) |
| The 26 upstream documents and three authored workloads through an installed package's public conversion API | [InstalledDocumentBenchmarks](../benchmarks/Lokad.Svg.InstalledBenchmarks/Program.cs) |

List parameterized jobs without executing benchmarks:

```powershell
dotnet run -c Release --framework net11.0 --project benchmarks/Lokad.Svg.Benchmarks -- --list json --runtimes net10.0 net11.0
```

Run timed measurements on a dedicated quiet host. A warmed conversion sample:

```powershell
dotnet run -c Release --framework net11.0 --project benchmarks/Lokad.Svg.Benchmarks -- --job Short --memory --filter "*SvgCurveBenchmarks*" "*SvgSceneBenchmarks*" --exporters JSON --artifacts artifacts/benchmarks
```

Reports use the selected artifacts directory. BenchmarkDotNet keeps generated
projects in the benchmark project's ignored `.bdn/` directory.

Input construction, option setup and font acquisition belong outside warmed
conversion timing. Report the inputs/options, CPU/OS, .NET/runtime/GC, warmups,
sample count and variability with each measurement. Run hosts serially without
concurrent builds or captures. Short runs do not establish general SVG throughput.

Check measurements within each launch for warm-up drift. A fixed invocation count
makes the number of warmup calls explicit; retain the drifting samples and
predeclare a longer-warmup comparison before claiming warmed performance. Keep
cold-start and tier-transition observations separate.

The [installed host's process probe](../tools/README.md#installed-package-benchmarks)
separates input/font setup and the first conversion from later calls. Its process
resident/peak observations include the host, runtime, retained input/output and
diagnostics; managed heap estimates and allocation traffic remain distinct. External
launch-to-marker/exit timings include bootstrap and communication, with warm OS
file caches. They measure neither IPC alone nor a minimal consumer's startup.

The [thin installed consumer](../tools/README.md#installed-and-native-comparison-preflight)
has a single-document process mode for setup, ping/pong and first-call/write
observations. Its separate Linux compression observer measures attributed native
requests and lifetime; instrumented runs provide no latency result. Keep these
metrics separate from the managed heap, process RSS and governed reservations.
The [quiet-host record](../tests/Research/release-baselines/thin-consumer-quiet.json)
retains three launches per document/runtime in each arm, exact output checks and
all samples. These fresh processes use warm file caches; they do not measure
cold-disk startup or isolated IPC latency.

The [persistent resvg runner](../tools/README.md#installed-and-native-comparison-preflight)
can retain serial samples for full conversion, parsing, prepared-tree rasterization
and PNG encoding. Its input/font setup and output I/O are excluded; temporary
native allocation/destruction is included. Match those boundaries and workloads
before comparing results with managed benchmarks. Native pixels retain their
own differences from the browser references.

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

Direct CSS workloads exclude XML: one declaration block, or 32 rules with 64
selectors including their admitted string/rule storage and disposal. Raster
components exclude XML, scene lowering and PNG. Six scalar workloads normalize
256 samples per invocation; each Gaussian workload processes one 64×64 frame.
Inputs, output arrays and gradient coefficients are prepared outside timing.
Blur scratch allocation stays inside timing; blending includes resetting its
1,024-byte background. One-axis and two-axis blur retain their distinct scratch paths.

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

The installed benchmark is an isolated project, outside ordinary solution builds.
Select its package, asset and host explicitly, following the
[installed benchmark procedure](../tools/README.md#installed-package-benchmarks).
Use the same source and options for every variant. Compare released and candidate
net10.0 assets on each fixed runtime; compare candidate net10.0/net11.0 assets on
the net11.0 host separately. This separates renderer changes from runtime and
target-framework changes. Retain fidelity differences, including released behavior
that differs from a current frozen reference; image equality is no timing shortcut.
The measured public call includes parsing, rasterization and PNG encoding. Input
I/O and supplied-font acquisition stay outside; bare returned memory is distinct
from governed ownership and process/native peak memory.

The [first quiet-VM measurements](../tests/Research/release-baselines/quiet-vm-20261107.json)
retain [190 source measurements and two platform-route declines](../tests/Research/release-baselines/quiet-vm-current.csv),
plus [145 installed-package measurements](../tests/Research/release-baselines/quiet-vm-installed.csv).
Every normalized sample is retained. All candidate reference comparisons are exact;
the released package's two authored-workload differences stay attached to its rows.
One launch with eight samples establishes observations on this VM, with runtime
defaults and uncontrolled physical-host power. It does not establish general
throughput or cross-launch variance. Shared allocation growth, chart raster cost
and the icon-sheet runtime difference are the next profiling targets; native,
cold-process and broader memory comparisons remain separate work.

For cold observations, use fresh MemoryProbe processes with
the [diagnostic procedure](../README.md#diagnostic-tools); report initialization
and input/output work separately. Verify output pixels when comparing changes.
