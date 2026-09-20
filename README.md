# Lokad.Svg

Lokad.Svg is a pure-managed .NET SVG-to-PNG renderer being built in staged,
explicitly supported subsets. The current subset renders explicitly sized empty
SVGs; integer or quarter-pixel `rect` layers; bounded axis-aligned `line`, closed
orthogonal `path`, `polygon` and filled `polyline` geometry; and direct local
`use` instances of supported definitions. It has bounded root/group/link
structure, inherited paint/visibility/display, limited element/group opacity,
source-over composition, caller-provided backgrounds, and managed PNG output to
owned memory, caller memory or a writable stream. Presentation attributes, inline
styles and a deliberately narrow ordered root stylesheet cover the documented
subset. Gradients, images, filters, text, general CSS and general path geometry
remain unsupported; see the support matrix for exact syntax and failure behavior.
The prototype also retains documented accepted-input contract gaps, so rejected
syntax must not be inferred solely from the absence of support claims.

See [PLAN.md](PLAN.md) for the product contract and implementation roadmap, and
[the support matrix](docs/SUPPORT_MATRIX.md) for the current status.

## Development

```powershell
dotnet restore --locked-mode
dotnet build -c Release
dotnet test -c Release
dotnet pack src/Lokad.Svg/Lokad.Svg.csproj -c Release -o artifacts/packages
```

The package will have no runtime package dependencies. Tests, Chrome tooling and
benchmarks are development-only projects.

Run the current-subset allocation and throughput benchmarks with:

```powershell
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks
```

The current machine-scoped baseline and its reproduction constraints are in
[docs/BENCHMARKS.md](docs/BENCHMARKS.md).

Run an isolated conversion probe, optionally with a conversion budget or a
timed cancellation request, with:

```powershell
dotnet run -c Release --project tools/Lokad.Svg.MemoryProbe -- tests/Fixtures/original/solid-red.svg $env:TEMP/lokad-svg-probe.png --maximum-accounted-bytes 1048576
```

The probe emits machine-readable elapsed-time, input-byte, cancellation-mode,
conversion-managed-allocation and OS peak-working-set fields. `--mode xml-reader`
instead emits a warmed direct BCL `XmlReader` traversal's managed allocation,
renderer workspace allowance and touched attribute characters without producing a
PNG. These fields are observations for bounded child-process scenarios, not a
renderer memory guarantee. `--cancel-after-ms` starts before input loading, while
`--cancel-after-conversion-start-ms` starts after the input is loaded; both are
timing dependent and are stress evidence rather than exact unit-test assertions.

Run the child-process success, budget-denial, XML-workspace-denial, direct
XML-reader workspace measurements (256 short attributes and 255 × 4 KiB values),
raster-cancellation and post-input parser-cancellation scenarios (which retain
their probe reports) with:

```powershell
tools/test-memory-probe.ps1 -ResultsDirectory artifacts/memory-probe
```

To inspect the packed package and smoke-test it from a clean consumer directory:

```powershell
tools/test-package.ps1 -PackageDirectory artifacts/packages -ResultsDirectory artifacts/package-smoke
```
