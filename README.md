# Lokad.Svg

Lokad.Svg is a pure-managed .NET renderer for a finite, documented subset of
static SVG. It converts geometry and local instances through composed affine
maps, solid/gradient/pattern paints, viewports, clips, alpha/luminance masks,
embedded PNGs, static CSS, markers and selected filters. Caller-supplied TrueType
programs support the documented label and text-path subset; font selection uses
an explicit catalog and fallback policy, without machine-font discovery.

`SvgConverter` writes PNGs to independently owned memory, caller memory or a
writable stream, with synchronous and asynchronous forms.
`SvgPreparedDocument` provides explicit preparation with single-conversion
ownership. Size/work limits, cancellation, cleanup and borrowed-source lifetimes
apply throughout; [API_CONTRACT](docs/API_CONTRACT.md) defines these guarantees.
Memory accounting uses conservative estimates for dominant allocations under
[PLAN's approximate engineering target](PLAN.md#maintenance-decisions-and-evidence),
with runtime/host overhead and borrowed storage identified separately.

[SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md) and [PLAN's finite boundaries](PLAN.md#required-support-boundaries-and-finite-exits)
define the useful subset and explicit unsupported outcomes. The six audit
corrections A1-A6 are complete. **Standalone delivery remains open for F1 and
final verification A7**: the [frozen upstream replacement collection](tests/Research/upstream-static-documents/README.md)
is **26/26 exact on Windows/Linux**, with fixture retirement and final
verification still required. Counts alone do not close release qualification.

The prior `e21f956c` qualification remains dated evidence for its declared cases:
**8,147 tests/platform**, package/installed-consumer checks and **27 documents /
258 variants** passed in [terminal green CI](https://github.com/Lokad/Svg/actions/runs/38952070864).
[INVENTORY](docs/INVENTORY.md) retains selection/evidence limits and historical
full-document results; F1 replaces their mandatory membership and count gates.

[Public regular/bold/real-italic/fallback qualification](tests/Research/public-font-qualification/README.md)
(T3-public), micro-tests, known-bug and resource contracts remain preserved.
Private deployment validation (T3-private) remains pending before production
adoption and does not block standalone qualification. [PLAN](PLAN.md#delivery-order)
owns delivery order and the release exit; [RASTERIZATION_DESIGN](docs/RASTERIZATION_DESIGN.md)
owns mechanisms/history and [BENCHMARKS](docs/BENCHMARKS.md) owns measurements.

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

Measure the complete frozen corporate cohort, including known differences, with:

```powershell
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks -- --job Short --filter "*SvgCorporateCohortBenchmarks*" --exporters JSON
```

Each case uses its declared viewport, scale and background. Input loading and
option construction occur outside the timed synchronous owned-memory conversion.

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

The unchanged `inv-143`/`inv-144` filter exports are exact at 512×341 transparent
through all twelve raw/prepared APIs on Windows/Linux. Shared primitive regions,
object-box coordinates, linearRGB transfer, bounded large-blur rescaling and
grouped geometric source capture complete the retained development queue at
**48/48**. The 24 frozen controls have 22 exact results and two measured numerical
residuals. Qualification passes 7,308 tests/platform, preserves all 2,856 prior
rendered PNGs and the corporate 27/27 documents /258/258 variants. Remaining finite
SVG support and release work stays in [PLAN.md](PLAN.md).

The additional authored [CSS/image card](tests/Research/static-css-images/README.md)
is complete against its unchanged Windows/Linux reference, with three original
controls and two focused bitmap phase/clip controls separately exact. Shared
bounded PNG decoding now retains varied pixels, with native fractional image
sampling and opacity. The selected development queue remains 50/50 and the
corporate cohort 27/27 documents/258/258 variants. Wider promised SVG behavior
remains explicit in PLAN.md; this document does not close all image/CSS support.
