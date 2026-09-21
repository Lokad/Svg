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
subset. Gradients, images, filters, general CSS and general path geometry remain
unsupported. An experimental supplied-font direct-text path is deliberately not an
admitted text-support claim; see the support matrix for its exact narrow syntax and
failure behavior.
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

The probe emits machine-readable elapsed-time, input-byte/input-SHA-256, cancellation-mode,
conversion-managed-allocation, OS peak-working-set, operating-system,
process-architecture and runtime-framework fields. `--mode xml-reader` instead
emits a warmed direct BCL `XmlReader` traversal's managed allocation, renderer
workspace allowance and touched attribute characters without producing a PNG. Its
XML-reader report separately records construction, whole-traversal and
`Name`/`Value`-access allocation deltas, so the scope of each managed observation
can be inspected, and counts text/CDATA nodes without materializing their values.
These fields are observations for bounded child-process
scenarios, not a renderer memory guarantee. `--cancel-after-ms` starts before input loading, while
`--cancel-after-conversion-start-ms` starts after the input is loaded; both are
timing dependent and are stress evidence rather than exact unit-test assertions.
The scenario runner verifies every child's input bytes and SHA-256 against the
input it launched, then retains one schema-2 JSON summary per run containing every
child report, the newline-normalized scenario-script source hash, byte-exact
probe/renderer/fixture hashes, and their shared
runtime/platform fingerprint.

Run an explicit local manifest/directory font probe with:

```powershell
dotnet run -c Release --project tools/Lokad.Svg.FontProbe -- --manifest <manifest.json> --pack-id <pack-id> --font-root <font-directory> --svg <input.svg> --output <output.png>
```

An explicit HTTP(S) source root can replace the local manifest/directory pair:

```powershell
dotnet run -c Release --project tools/Lokad.Svg.FontProbe -- --source-uri <https://source-root/> --pack-id <pack-id> --svg <input.svg> --output <output.png>
```

The probe has no built-in font source, pack identifier, credentials or network
configuration. Its [tool documentation](tools/Lokad.Svg.FontProbe/README.md)
describes its bounded local byte source and private-artifact handling. Its public
synthetic smoke check needs only a Release build; it covers local source laziness,
loopback HTTP manifest acquisition and one transient program-read retry, and writes
caller-selected results (use the ignored `artifacts/` tree):

```powershell
tools/test-font-probe.ps1 -ResultsDirectory artifacts/font-probe-smoke
```

Run the child-process success, budget-denial, XML-workspace-denial, direct
XML-reader workspace measurements (a 64 KiB text node, 256 short attributes and
255 × 4 KiB values), raster-cancellation and post-input parser-cancellation
scenarios (which retain their probe reports) with:

```powershell
tools/test-memory-probe.ps1 -ResultsDirectory artifacts/memory-probe
```

To inspect the packed runtime and symbol packages and smoke-test the runtime
package from a clean consumer directory:

```powershell
tools/test-package.ps1 -PackageDirectory artifacts/packages -ResultsDirectory artifacts/package-smoke
```
