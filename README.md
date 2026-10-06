# Lokad.Svg

Lokad.Svg is a pure-managed .NET SVG-to-PNG renderer being built in staged,
explicitly supported subsets. The current subset renders explicitly sized empty
SVGs; integer or quarter-pixel `rect` layers; bounded axis-aligned `line`, closed
orthogonal `path`, `polygon` and filled `polyline` geometry; and direct local
`use` instances of supported definitions, together with the matrix-documented diagonal,
curved-shape, gradient, pattern, image, clip and mask subsets. It has bounded root/group/link
structure, inherited paint/visibility/display, limited element/group opacity,
source-over composition, caller-provided backgrounds, and managed PNG output to
owned memory, caller memory or a writable stream. Presentation attributes, inline
styles and a deliberately narrow ordered root stylesheet cover the documented
subset. Gradients, images, filters, general CSS and general path geometry remain
supported only inside the narrow admitted subsets the support matrix documents;
everything outside those subsets stays explicitly unsupported. An experimental supplied-font direct-text path is deliberately not an
admitted text-support claim; see the support matrix for its exact narrow syntax and
failure behavior.
The prototype also retains documented accepted-input contract gaps, so rejected
syntax must not be inferred solely from the absence of support claims.

See [PLAN.md](PLAN.md) for the product contract and implementation roadmap, and
[the support matrix](docs/SUPPORT_MATRIX.md) for the current status.

Declaration-only DOCTYPEs are admitted with inert external identifiers. Internal
subsets and duplicate declarations remain malformed; DTD resources, entities and
default attributes are never loaded. Root parsing, font discovery and output-scale
probing use the same bounded policy.

The pinned-browser full-canvas solid-rectangle route preserves original
fill-opacity separately from ordinary byte composition. Its independent routing
controls are banked, while general layer composition and curved corporate-logo
fidelity remain open; the support matrix records those limits.

Antialiased rectangle paint clips now have independent native/browser regressions
and preserve the distinction between geometric clip edges and mask allocation
bounds. Ford and YouTube are each exact in all nine declared variants. Analytic
paints apply the rectangle's snapped byte mask to each ordered operation.
The frozen corporate matrix now has all 258 reference images: 240 rendered
comparisons (66 exact, 174 different) and eighteen retained renderer rejections.
Ford, ROC HCVS and YouTube are exact in every declared variant: 3/27 documents.
Straight dash cuts now preserve native float endpoints, and nonzero orthogonal
contact sets retain their one-byte fringes through signed-winding analytic
walking. All nine ROC variants and twelve fresh controls are exact. These
checkpoints do not establish general logo coverage.

Convex paths now round coverage for rectangular strips created by clipping a
slanted edge, while raw rectangles retain their separate calibration. This closes
YouTube's opaque triangle residuals at intended and large sizes. The subsequent
clip-byte repair closes the remaining boundary differences. Clipped orthogonal
paths, polygons and filled polylines now preserve their original contour and AA
paint clip, making eleven existing failures and eighteen new scaled controls
exact. Solid rectangles now retain their original edges and separate paint clip,
with native rounded coverage and premultiplication. Thirty fresh configurations
and two existing controls become exact; all nine IBM variants improve but remain
inexact. Shader quantization, clip-stack and layer parity remain open.

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
input it launched, then retains one schema-3 JSON summary per run containing every
child report, the fixture hash and their shared runtime/platform information.
Git and CI workflow revisions identify the code; reports do not hash solution
sources, scripts or assemblies.

Run an explicit local manifest/directory font probe with:

```powershell
dotnet run -c Release --project tools/Lokad.Svg.FontProbe -- --manifest <manifest.json> --pack-id <pack-id> --font-root <font-directory> --svg <input.svg> --output <output.png>
```

An explicit HTTP(S) source root can replace the local manifest/directory pair:

```powershell
dotnet run -c Release --project tools/Lokad.Svg.FontProbe -- --source-uri <https://source-root/> --pack-id <pack-id> --svg <input.svg> --output <output.png>
```

Pass `--svg -` to read an SVG byte stream from standard input. The probe preserves
the supplied bytes, including a BOM, and bounds reads as data arrives.

The probe has no built-in font source, pack identifier, credentials or network
configuration. Its [tool documentation](tools/Lokad.Svg.FontProbe/README.md)
describes its bounded local byte source and private-artifact handling. Its public
synthetic smoke check needs only a Release build; it covers local source laziness,
BOM-preserving standard input, loopback HTTP manifest acquisition and one transient
program-read retry, and writes caller-selected results (use the ignored `artifacts/`
tree). The focused test project also verifies HTTP-source concurrent
coalescing/cache reuse and recovery after cancellation:

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
