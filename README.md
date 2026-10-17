# Lokad.Svg

Lokad.Svg is a pure-managed .NET 10 library that converts a documented subset of
static SVG to PNG on Windows and Linux, with no runtime package dependencies.
It supports affine geometry and local instances, gradients and patterns, clips
and masks, embedded PNGs, static CSS, markers, selected filters and supplied-font
labels/text paths. See [supported SVG](docs/SUPPORT_MATRIX.md#supported-svg-subset)
for the exact boundaries and unsupported outcomes.

`SvgConverter` provides synchronous and asynchronous output to independently
owned memory, caller memory or a writable stream. `SvgPreparedDocument` supports
explicit preparation and one conversion. Fonts use an explicit catalog and
fallback policy; the renderer does not discover machine fonts or fetch resources.
Size/work limits, cancellation and borrowed-storage lifetimes apply throughout.
Memory accounting follows the [approximate resource policy](docs/SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).

Qualification covers the [26 upstream documents](tests/Research/upstream-static-documents/README.md)
and focused regressions against frozen Windows/Linux references. [Qualification CI](https://github.com/Lokad/Svg/actions/runs/39021816411)
passes 8,088 tests/platform plus package and installed-consumer
checks. These results establish the declared outputs, not general SVG completeness.
Public regular, bold, real italic and fallback fonts are qualified; four private
deployed-manifest requests remain pending before production adoption.

## Development

```powershell
dotnet restore --locked-mode
dotnet build -c Release
dotnet test -c Release
dotnet pack src/Lokad.Svg/Lokad.Svg.csproj -c Release -o artifacts/packages
```

Normal build plus complete tests took about one minute on Windows and six on
Linux in the qualified local runs. Tests, browser tooling and benchmarks are
used only for development.

Run allocation and throughput benchmarks with:

```powershell
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks
```

[BENCHMARKS](docs/BENCHMARKS.md) records measured results and reproduction conditions.
[RASTERIZATION_DESIGN](docs/RASTERIZATION_DESIGN.md) explains the renderer;
[INVENTORY](docs/INVENTORY.md) describes the retained regression collection.

## Diagnostic tools

After a Release build, run the resource, font-host and package smoke checks with:

```powershell
tools/test-memory-probe.ps1 -ResultsDirectory artifacts/memory-probe
tools/test-font-probe.ps1 -ResultsDirectory artifacts/font-probe-smoke
tools/test-package.ps1 -PackageDirectory artifacts/packages -ResultsDirectory artifacts/package-smoke
```

For an isolated conversion and optional budget/cancellation controls:

```powershell
dotnet run -c Release --project tools/Lokad.Svg.MemoryProbe -- tests/Fixtures/original/solid-red.svg artifacts/probe.png --maximum-accounted-bytes 1048576
```

[FontProbe](tools/Lokad.Svg.FontProbe/README.md) documents explicit local/HTTP font
sources. Endpoints, credentials, caches and transport remain host-owned; ordinary
builds and CI require no private configuration. [Oracle tooling](docs/ORACLE.md)
and [visual comparison](docs/VISUAL_COMPARISON.md) describe reference capture and
pixel diagnostics. Keep local diagnostic output under the ignored `artifacts/` tree.
