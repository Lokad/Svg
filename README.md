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
controls are banked, while general layer composition and broader curve
fidelity remain open; the support matrix records those limits.

Antialiased rectangle paint clips now have independent native/browser regressions
and preserve the distinction between geometric clip edges and mask allocation
bounds. Ford and YouTube are each exact in all nine declared variants. Analytic
paints apply the rectangle's snapped byte mask to each ordered operation.
The frozen corporate matrix has 258 rendered comparisons on Windows and Linux:
258 exact, zero known differences and zero rejections. Every declared variant is exact for
27/27 documents, including 13/13 marks and 14/14 outlined wordmarks. Local
Windows and Linux delivery checks and the complete fresh Linux browser matrix
pass; the [Windows/Linux CI run](https://github.com/Lokad/Svg/actions/runs/38285636122)
also passes build/test, sandboxed Chrome, resource,
font, artifact-validation and package checks. See [PLAN.md](PLAN.md) for the current
release assessment and cohort selection limits.

Generated native stroke outlines retain source operands until final mapping,
making all nine Zeppelin variants exact. Coincident stroke contours preserve
their native output classification, with independent browser regressions and
bounded ownership checks. Integer and fractional diagonal filled point lists
retain source commands through final mapping, preserving directed crossing events.
The retained integer bowtie is exact against its original browser PNG. Native float
rectangle endpoints close IBM's three large variants, completing the frozen-cohort
fidelity target on both platforms.

The frozen corporate matrix and completed original documents are exact;
measured numerical research residuals remain visible in PLAN.md. Native
stroke coverage keeps zero-signed-area contours carrying local winding, closing
the final contained-ring witness. [The support queue](PLAN.md#3-ordered-support-queue)
now targets remaining static SVG combinations, with four sourced
public-domain/CC0 failures retained in the development inventory.

Inert `enable-background` declarations now share validation across attributes and
admitted styles, including Illustrator's `new` rectangle hint. The retained heart
and complete 79-instance I Ching library match their frozen references on
Windows/Linux through all six outputs. Both retained patterns now join them: **all four gap documents are complete**
against unchanged references. Bounded vector tile scenes share source lowering,
native paint and premultiplied repeat sampling. Local forward pattern templates
retain independent attributes/content; fractional tiles keep their declared
period after integral texture rasterization. The two pattern banks retain 32
exact controls and three explicit boundary rejections. The original two-child
cubic-pattern fixture remains exact. The next retained correction is nested
affine clipping/opacity; finite remaining combinations stay in
[PLAN.md](PLAN.md#3-ordered-support-queue).

Direct-leaf symbols now preserve multiple children in document order, inherited
fill channels and separate child/symbol/use opacity. Magnified native-backed
instances replay original curve commands. Both retained families are exact on
three backgrounds, and sixteen independent controls match through all six
outputs. Symbols also work under the root and rendered groups, with optional IDs
and non-rendering descriptions; eleven new controls are exact. Nested symbol
groups/uses and wider style/viewport combinations remain in G2; see [the supported boundary](docs/SUPPORT_MATRIX.md#direct-leaf-symbols-and-instances-2026-10-08).

Circle and ellipse dashes now cut original conics through the shared native
path-dash constructor. All nine retained dash failures and twelve new independent
controls are exact, including thin widths, offsets, seams, scaling, pathLength
and clipping. Remaining admission restrictions are in the support matrix.
See [the dash repair](docs/RASTERIZATION_DESIGN.md#source-conic-ellipse-dashes-2026-10-08).

Rounded rectangles retain native conic corners and convex shape classification.
Fills and strokes share source commands through final mapping, with independent
browser regressions for fractional coordinates, clamped radii and reflected shapes.
See [the rectangle repair](docs/RASTERIZATION_DESIGN.md#native-rectangle-source-operands-2026-10-07).
See [the source-outline repair](docs/RASTERIZATION_DESIGN.md#source-native-stroke-outlines-2026-10-07).

Uniform masks apply after a group's children or a leaf's fill/stroke compose,
preserving overlap alpha and antialiased colors independently of definition order.
Copied use instances preserve source-child masks and their own mask boundaries;
adjacent and empty groups retain the validated command-range behavior. Independent
rounded fill/stroke, pattern, mask-value and instance controls match the browser
on three backgrounds. Other mask and layer combinations remain in the support matrix.
See [the mask repair](docs/RASTERIZATION_DESIGN.md#leaf-and-instance-mask-composition-2026-10-07).

Isolated opacity layers now use native image compositing, with masks applied
before outer opacity. This closes the original white-background group/root
controls and preserves the complete exact corporate matrix.
See [the layer repair](docs/RASTERIZATION_DESIGN.md#isolated-image-layer-composition-2026-10-07).

The following implementation checkpoints retain their historical results.
Authored fills retain directed winding and
curves after scene normalization, with root/group composition before float
rounding. Convex cubics retain live edges, and incoming intersections constrain
dense-path strips. Every prior exact image is preserved; six known controls
worsen slightly. Broader event fidelity remains open; see
[the design record](docs/RASTERIZATION_DESIGN.md#convex-cubic-and-incoming-edge-rows-2026-10-06).
Solid path strokes up to one device pixel use native hairline coverage for lines,
quadratics, cubics and endpoint arcs. Dashes measure and cut retained float curves
before stroking, preserving conic weights and closed seams. Twelve new thin dash
configurations are exact; all earlier exact images are preserved.

Wider paths and integer/fractional polylines now share native offsets, joins/caps
and closed seams, preserving fitted quadratics and rational round verbs.
Twenty-six existing comparisons become exact; all nine Zeppelin variants
improve, with every prior exact PNG preserved. Remaining stroke scan/clip/paint
fidelity, sampled shape routes, transformed/pathLength dash coverage and other
hairline contexts remain open.
Circle/ellipse strokes now retain native normal-offset quadratic outlines rather
than sampled concentric ellipses. Painted `use` instances retain those curves.
All twelve new oval controls and all nine SG Rotation variants improve but remain
inexact; every prior exact image is preserved. Remaining stroke scan/paint
residuals and sampled shape routes remain open.
Bitmap-local straight-edge clipping and float polygon mapping close Artsy's
remaining variants. Native straight open-stroke construction and winding make
six original miter/bevel probes and three viewBox configurations exact, and
improve every Rede Manchete variant. Bevel polylines share the path kernel.
Circle/ellipse fills and native straight solid open-stroke round caps, dots and
joins retain rational conics through final mapping. Signed native stroke contacts
preserve their directed fill. Four original round-stroke probes and three fresh
configurations become exact; every Rede Manchete variant improves but remains
inexact. The later hairline repair closes the width-one cap probe. Broader
fractional events, closed/curved/dashed strokes and clip fidelity remain open.
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
