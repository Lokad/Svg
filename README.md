# Lokad.Svg

Lokad.Svg is a pure-managed .NET 10 library that converts a documented subset of
static SVG to PNG on Windows and Linux, with no runtime package dependencies.
It supports affine geometry and local instances, gradients and patterns, clips
and masks, embedded PNGs, static CSS, markers, selected filters and supplied-font
labels/text paths. See [supported SVG](docs/SUPPORT_MATRIX.md#supported-svg-subset)
for the exact boundaries and unsupported outcomes.

Install [Lokad.Svg from NuGet](https://www.nuget.org/packages/Lokad.Svg):

```powershell
dotnet add package Lokad.Svg --version 0.3.0
```

[CHANGELOG](CHANGELOG.md) records release notes.

`SvgConverter` provides synchronous and asynchronous output to independently
owned memory, caller memory or a writable stream. `SvgPreparedDocument` supports
explicit preparation and one conversion. Fonts use an explicit catalog and
fallback policy, with ordered CSS family lists and caller-defined generic mappings;
the renderer does not discover machine fonts or fetch resources.
Font-selected fitting/scanning and pair kerning follow the [documented subset](docs/SUPPORT_MATRIX.md#text-and-fonts).
Size/work limits, cancellation and borrowed-storage lifetimes apply throughout.
Memory accounting follows the [approximate resource policy](docs/SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).

Qualification covers the [26 upstream documents](tests/Research/upstream-static-documents/README.md)
and focused regressions against frozen Windows/Linux references, plus
[package and installed-consumer checks](.github/workflows/ci.yml).
These results establish the declared outputs, not general SVG completeness.
See [qualification](tests/README.md#qualification) for font evidence and
host-specific deployment checks.

## Development

Use the SDK pinned in [global.json](global.json).

```powershell
dotnet restore --locked-mode
dotnet build -c Release
dotnet test -c Release
dotnet pack src/Lokad.Svg/Lokad.Svg.csproj -c Release
```

Packages are written to `artifacts/nuget/`. Non-Release packaging is rejected
unless explicitly enabled for troubleshooting.

Tests, browser tooling and benchmarks are used only for development.

Run allocation and throughput benchmarks with:

```powershell
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks
```

[BENCHMARKS](docs/BENCHMARKS.md) describes workloads and measurement conditions.
[RASTERIZATION_DESIGN](docs/RASTERIZATION_DESIGN.md) explains the renderer;
[Tests and provenance](tests/README.md) describes regression collections and admission.
The [documentation index](docs/README.md) maps the remaining guides.

## Diagnostic tools

After a Release build, run the resource, font-host and package smoke checks with:

```powershell
tools/test-memory-probe.ps1 -ResultsDirectory artifacts/memory-probe
tools/test-font-probe.ps1 -ResultsDirectory artifacts/font-probe-smoke
tools/test-package.ps1 -PackageDirectory artifacts/nuget -ResultsDirectory artifacts/package-smoke
```

For an isolated conversion and optional budget/cancellation controls:

```powershell
dotnet run -c Release --project tools/Lokad.Svg.MemoryProbe -- tests/Fixtures/original/solid-red.svg artifacts/probe.png --maximum-accounted-bytes 1048576
```

[FontProbe](tools/Lokad.Svg.FontProbe/README.md) documents explicit local/HTTP font
sources. Endpoints, credentials, caches and transport remain host-owned; ordinary
builds and CI require no private configuration. The [tools guide](tools/README.md)
documents reference capture, research probes and pixel comparison. Keep generated
diagnostic output under the ignored `artifacts/` tree.
