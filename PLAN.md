# Lokad.Svg implementation plan

Status: milestone 1 is complete as of 2026-09-19. Milestones 2 and 3 are in
progress: the independent PNG comparer, visual-diff CLI, original CC0 probes,
pinned Chrome-for-Testing capture and reviewed references, bounded root parsing,
managed PNG encoding, and a very narrow rectangle renderer exist. Stream output
and all broader SVG features remain incomplete.

## 1. Product contract

Build `Lokad.Svg`, an open-source, pure managed C# library targeting `net10.0`,
distributed as one NuGet package. Convert static SVG bytes into PNG bytes on
Windows and Linux. Use the repository and packaging conventions specified
in this plan.

- The shipped library has no dependencies beyond the .NET standard library:
  no third-party runtime packages, native assets, P/Invoke, graphics services,
  browser processes, or platform drawing APIs. In particular, no System.Drawing,
  SkiaSharp, ImageSharp, HarfBuzz binding, or dependency on another Lokad package.
  Standard .NET APIs, including their framework implementation details, are allowed.
- Memory is the primary input/output boundary. Rendering must work without files,
  installed fonts, a network connection, or a writable temporary directory.
- Provide synchronous and asynchronous entry points, with cooperative cancellation
  throughout parsing, geometry, resource decoding, rasterization, and PNG encoding.
- Use xUnit for tests, a small C# wrapper over headless Chrome for reference images,
  and BenchmarkDotNet for microbenchmarks. These are development facilities only.
  No Playwright, Puppeteer, Selenium, ChromeDriver, or browser automation package.
- Chrome rasterization is the visual ground truth. Aim for exact pixels, including
  antialiased boundaries and transparency. Do not redefine small differences as
  pixel perfection.
- Enforce a configurable per-conversion memory budget and structural/work limits.
  Input that cannot fit must fail predictably before the prohibited allocation.
- Keep a committed, reproducible corpus of public-domain/CC0 fixtures for fidelity,
  performance, and memory regression testing.

Static SVG is still a large undertaking: text shaping, image codecs, filters,
CSS, and browser-compatible antialiasing are substantial subsystems. Implement
in stages and publish a precise support matrix. An early preview is not a claim
to support every static SVG. Do not quietly drop difficult features to declare
the project complete.

## 2. Repository conventions and research findings

### Adopted conventions

Use a `.slnx` solution, separate `src/`, `tests/`, `tools/` and `benchmarks/`
directories, pinned SDK/packages, Release packaging and complete NuGet metadata
with symbols. Keep this `PLAN.md` versioned. Tests reference the actual production
assembly rather than compiling production sources into a second assembly.
Represent host-supplied resources and fonts as bytes, check image dimensions, and
account for memory through reservations before allocation.

Organize visual cases into manifests and capability families, with separate
dimension, diagnostic and visual expectations. Preserve reference/candidate/diff
artifacts. Use zero-threshold RGBA counts and exact hashes as the primary gate;
MAE, RMSE and similarity metrics support diagnosis. Reject dimension differences
rather than comparing only the common intersection. Similarity and human
assessments cannot override an exact-match failure. An image pixel limit alone
does not bound the simultaneous memory consumption of SVG groups and filters.

### External reference workspace

These shallow checkouts were created solely for inspection:

| Checkout | Revision inspected | Relevant findings |
| --- | --- | --- |
| [resvg/usvg](https://github.com/linebender/resvg), `external/resvg/` | `75b6bbadd7999d0516dcd7153b4a321bfdf8670a` | Separate parsing/normalization from rendering; inspect `crates/usvg/src/parser/`, `text/`, and `crates/resvg/src/{render,clip,mask,image}.rs` and `filter/`. Group isolation and filter dependency regions need explicit representation. |
| [tiny-skia](https://github.com/linebender/tiny-skia), `external/tiny-skia/` | `5d4754777746eef0828be166896eaf482c49f8f2` | Study `src/edge*`, `scan/`, `pipeline/`, `shaders/`, and `path/src/{stroker,dash}.rs`. Useful raster architecture, but its `scan/path_aa.rs` still has a four-sample-per-axis path and an analytic-AA TODO. Similar ancestry does not establish equality with current Chrome. |
| [resvg test suite](https://github.com/linebender/resvg-test-suite), `external/resvg-test-suite/` | `d8e064337faf01bc5a9579187a56dbdbe3eacc72` | Coverage inventory: 397 filter, 93 masking, 149 paint-server, 304 painting, 133 shape, 247 structure, and 356 text SVGs in this checkout. Existing expected PNGs are not our Chrome oracle. |
| [NanoSVG](https://github.com/memononen/nanosvg), `external/nanosvg/` | `239e102ec2c691f2902e20ace2ed36ee4a35cfe6` | A compact example of parsing, path normalization, and rasterization. Its intentionally limited rasterizer is not a completeness or accuracy target. Example artwork needs its own provenance. |

All four directories are currently ignored through this repository's local
`.git/info/exclude`. This preserves the planning-only scope: `PLAN.md` is the only
file intended for review/versioning in this task. During scaffolding, add
`/external/` to the tracked `.gitignore` for everyone.

Rules for `external/`:

- Optional, disposable research material. No submodules, project references,
  build steps, test discovery, generators, benchmark runs, or runtime loading
  may depend on these checkouts. A fresh clone must work with the directory absent.
- Record source URLs and revisions when research informs an engineering decision.
  Do not copy, translate, or vendor their implementations into this project.
- Use their feature inventories to design original probes. Their licenses do not
  automatically apply to embedded fonts or artwork. The resvg suite is MIT, and
  its font directory also contains OFL/Apache notices; it is not a public-domain
  corpus. Keep it as research, not a test dependency.
- Inspect the Skia version corresponding to the pinned Chrome build for coverage,
  stroking, compositing, and sampling behavior when a reduced probe requires it.
  The current [Skia analytic scan converter](https://github.com/google/skia/blob/main/src/core/SkScan_AAAPath.cpp)
  is a useful research entry point; a moving branch is not a reproducibility pin.

### Planned layout

```text
Lokad.Svg.slnx
global.json
Directory.Build.props
Directory.Packages.props
PLAN.md
README.md / LICENSE.txt / CHANGELOG.md / SECURITY.md
src/Lokad.Svg/
tests/Lokad.Svg.Tests/           # unit, public API, codec, limits, cancellation
tests/Lokad.Svg.VisualTests/     # xUnit cases using the Chrome oracle
tests/Fixtures/                 # SVGs, resource bytes, provenance, manifests
tests/References/               # reviewed PNGs and oracle metadata
tools/Lokad.Svg.Chrome/         # small BCL-only CDP wrapper
tools/Lokad.Svg.VisualDiff/     # exact comparison and report generation
tools/Lokad.Svg.MemoryProbe/    # isolated-process memory/cancellation measurements
benchmarks/Lokad.Svg.Benchmarks/
docs/                          # support matrix, design, corpus and benchmark notes
.github/workflows/
external/                      # ignored inspection checkouts
artifacts/                     # ignored renders, reports, browser cache, packages
```

Use MIT for project code/documentation; original fixture assets receive a separate
CC0 dedication. Enable nullable,
deterministic builds, warnings as errors, SDK pinning, and locked test/tool package
versions. Keep the public library free of `PackageReference` entries initially;
use SDK-provided repository/source metadata where sufficient. Test and benchmark
packages must not appear as dependencies in the `.nuspec`.

### C# engineering guidelines

The rules below are the project's C# engineering guidelines and apply to production
code, tests, tools and benchmarks. They are fully specified here. During scaffolding,
publish them in this repository's `docs/GUIDELINES_CSHARP.md` and reference that
document from `AGENTS.md`.

- **No default parameter values.** Every method and constructor parameter is
  mandatory, including options and cancellation tokens. Where frequent simpler
  usage warrants it, add an explicitly named convenience method or an overload
  forwarding common values to the complete method. Do not add an overload merely
  to conceal call sites missed when a parameter is introduced.
- **Update constructors and interfaces together with their consumers.** For
  repository-internal changes, update every call site and implementation in the
  same change. Avoid compatibility shims and parallel interfaces for hypothetical
  implementers. A published NuGet API has external consumers, so preserve its
  published contract or make an intentional, documented versioned breaking change.
  Keep the initial public
  extension surface small rather than creating speculative compatibility layers.
- **Prefer non-nullable designs.** Nullable types represent genuine absence, not
  a shortcut for requesting defaults. No null-forgiving operator. Trust internal
  non-nullable contracts instead of adding repetitive defensive null guards;
  validate nullable, external and deserialized inputs at their boundaries. In
  particular, public package inputs are an external boundary.
- **Use precise domain types.** Prefer enums, typed identifiers and validated
  value objects over loosely related strings, integers, booleans or tuples.
  Examples include node/resource handles, dimensions, byte budgets, blend modes
  and coordinate spaces. Search for an existing suitable type before creating
  one; convert at parsing/API boundaries and keep the core strongly typed. Use
  compact value types so this discipline does not create per-node heap objects.
- **Keep single-caller helpers local.** A helper with one caller belongs as a
  local function inside that caller. Avoid unnecessary closure allocation in hot
  paths; use static local functions and explicit parameters when appropriate.
- **Document implementor contracts.** Every abstract/virtual method must describe
  the semantics required of overrides. For this renderer, relevant contracts
  include buffer ownership/lifetime, cancellation, budget accounting and thread
  safety. Document the same concerns for any public extension interface.
- **No `InternalsVisibleTo` entries in `.csproj` files.** Prefer testing public
  behavior. If subsystem tests require friend access, declare the narrowly scoped
  assembly attribute explicitly in a source file and document the need; do not
  widen the NuGet API merely for tests.

Enforce applicable rules through compiler settings, API/metadata checks and review;
do not add a runtime dependency for coding conventions. Default values on immutable
option properties, such as `SvgRenderOptions.Default`, remain compatible with the
rule against optional method/constructor parameters.

## 3. Public API and observable behavior

Proposed API shape, to validate before freezing names:

```csharp
public static class SvgConverter
{
    public static ReadOnlyMemory<byte> ConvertToPng(
        ReadOnlyMemory<byte> svg,
        SvgRenderOptions options,
        CancellationToken cancellationToken);

    public static Task<ReadOnlyMemory<byte>> ConvertToPngAsync(
        ReadOnlyMemory<byte> svg,
        SvgRenderOptions options,
        CancellationToken cancellationToken);

    public static int ConvertToPng(
        ReadOnlyMemory<byte> svg,
        Memory<byte> destination,
        SvgRenderOptions options,
        CancellationToken cancellationToken);

    public static Task<int> ConvertToPngAsync(
        ReadOnlyMemory<byte> svg,
        Memory<byte> destination,
        SvgRenderOptions options,
        CancellationToken cancellationToken);
}
```

Callers explicitly pass `SvgRenderOptions.Default` and `CancellationToken.None`
when those choices are intended. Any convenience overloads must follow the C#
guidelines above; the complete operations retain all mandatory parameters.

`Memory<byte>` inputs convert naturally to `ReadOnlyMemory<byte>`. Do not require
array-backed memory. The caller keeps input, destination, fonts, and resource
memory alive and unchanged until the operation completes. Reject overlapping
input/output where it can be established, and document non-overlap as a caller
requirement for arbitrary memory providers. Keep all spans within synchronous
scopes; no borrowed spans across awaits.

The convenience result is independently owned managed memory, valid indefinitely,
and never a returned pool rental. Return a slice of the final owned buffer if this
avoids a final copy; retained capacity still counts against the budget. The
destination overload returns the written length and allocates no second complete
PNG buffer. A too-small destination produces a specific failure; its contents may
be partially written and are not a PNG result. Do not promise the exact required
compressed size without actually encoding. Cancellation/failure has the same
partial-destination rule. No result is returned as successful before PNG completion.

Also plan synchronous/asynchronous writable-`Stream` output overloads for hosts
that want bounded output buffering. Leave caller streams open, support nonseekable
streams, and document partial writes on failure. Filesystem convenience APIs and
a separately packaged CLI are unnecessary for the initial package.

### Options and resources

- Distinguish the logical CSS viewport from output density: optional logical
  viewport width/height, a positive output scale (default 1), and transparent or
  explicit background color. CSS absolute units use 96 CSS pixels/inch.
- Specify and test intrinsic size, one missing dimension, percentage root sizes,
  `viewBox`, and default object sizing against Chrome. Use the CSS default object
  size of 300 x 150 only through the applicable sizing algorithm, not as a blanket
  replacement for every missing size. Document integer pixel rounding.
- Options are immutable or snapshotted at invocation. Include limits and a bounded
  diagnostic mechanism. Reject invalid dimensions, scales, and incompatible options.
- Font faces and referenced resources are supplied as bytes in explicit immutable
  catalogs/maps. No implicit system font search, filesystem access, HTTP fetching,
  or global fallback font. The host can load its bytes asynchronously beforehand.
- Resolve local fragment references and data URLs internally. Resolve other allowed
  references only through the caller's resource map, with explicit base-URI/key
  semantics. Missing resources/fonts produce actionable failures by default.
- Keep caches within the conversion initially. A reusable compiled document or
  bounded shared font cache is a later API decision, backed by benchmarks and
  explicit ownership/budget rules.

### Async and cancellation contract

CPU work does not become I/O merely by adding `async`. The proposed memory-to-memory
async methods schedule the synchronous engine once onto the thread pool; they do
not run the entire conversion inline before returning a completed task. Account
for that scheduling/allocation overhead in benchmarks. Do not create a task per
shape, pixel, or filter, and do not add implicit parallel rendering initially.

For async stream output, await the destination's `WriteAsync` with backpressure.
If CPU encoding and async writes require a producer/consumer bridge, bound it to a
small fixed number of charged chunks and propagate either side's failure to the
other. The sync API must never block on an async API. No async resource callbacks
are needed for the first release because resource inputs are already memory-backed.

Check cancellation before work and at bounded intervals in XML/CSS/path scanning,
reference expansion, curve subdivision, glyph processing, edge processing,
scanlines/tiles, filter loops, image decompression, and compression chunk writes.
Passing a token only to `Task.Run` is insufficient. Throw
`OperationCanceledException` with the supplied token, release all owned resources,
and leave no worker running after the operation has completed. Target sub-50 ms
cancellation observation under the defined stress workload on the benchmark host;
this is a measured responsiveness target, not a hard real-time guarantee.

### Failure policy

Define stable categories for malformed input, unsupported visible behavior,
unresolved resource/font, resource/work limit, and insufficient destination.
Include source location/element ID where available; cap diagnostic size/count.
Do not catch cancellation and return an empty or partial image.

Default to explicit failure when an unsupported construct would change rendering.
Apply specified CSS/SVG recovery for invalid declarations and paths where supported,
and tolerate inert metadata/editor namespaces. Avoid treating harmless extension
attributes as fatal. An optional best-effort mode may report every dropped visible
feature, but must not silently become the default or qualify for fidelity gates.

## 4. Static feature scope

Use SVG 1.1 static rendering plus common SVG 2 behavior observed in the pinned
Chrome build. The [SVG 2 processing modes](https://www.w3.org/TR/SVG2/conform.html)
provide terminology; this project defines its own explicit supported subset.
Use specifications for semantics and Chrome for the requested pixel compatibility.
Record disagreements as named compatibility decisions with minimal fixtures.

| Area | Planned work and staging |
| --- | --- |
| XML and structure | Namespaces, encodings/BOMs, `svg`, `g`, `defs`, nested viewports, `symbol`, `use`, `href`/`xlink:href`, forward references, `switch`, inert link containers, visibility/display and overflow. Cycles and reference expansion are bounded. |
| Geometry | All path commands, repeated/relative commands, exponent numbers, elliptical arcs, rectangles/rounded corners, circles, ellipses, lines, polylines/polygons; affine transforms, units, percentages, `viewBox`, and `preserveAspectRatio`. |
| Style | Presentation attributes, inline declarations, embedded stylesheets; inheritance, initial/unset/inherit, specificity, source order, `!important`, named/hex/RGB/HSL colors, alpha and `currentColor`. Start with type/ID/class, selector lists, descendant/child and attribute selectors; track unsupported selectors, custom properties, CSS transforms and other advanced CSS explicitly. |
| Painting | Nonzero/evenodd fills, all basic stroke caps/joins/miter limits, dash arrays/offsets, zero-length strokes, nonuniform transforms, `pathLength`, non-scaling strokes, paint order, and shape-rendering hints. Test subpixel and hairline cases separately. |
| Paint servers | Linear/radial gradients, focal points, stop inheritance/order/opacity, spread methods, transforms and both coordinate-unit modes; patterns, nested pattern content, and reference cycles. |
| Composition | Isolated group opacity, source-over, clipping, alpha/luminance masks, markers including orientation, nested clipping and proper application order; blend/isolation modes tracked individually. |
| Filters | First: blur, offset, flood, color matrix, component transfer, composite, blend, merge and drop-shadow behavior. Then convolution, morphology, displacement, tile, turbulence, diffuse/specular lighting and `feImage`. Cover primitive/filter regions, named inputs, units, edge modes, and sRGB versus linearRGB. Each primitive needs its own support and fidelity status. |
| Embedded images | Managed PNG decoding first, including transparency, palette and interlace coverage; nested SVG images using the same budgets. Add JPEG baseline/progressive decoding as a separate substantial stage. Explicitly defer GIF/WebP/AVIF and unsupported color profiles rather than borrowing a native decoder. Define handling of animated image formats as unsupported. |
| Text | Start with supplied TrueType `glyf` fonts, cmap/metrics/kerning, composite glyphs, `text`/`tspan`, whitespace, x/y/dx/dy/rotate lists, anchors, baseline/spacing and `textLength`; then text paths and OpenType substitution/positioning, bidi and script shaping. CFF/CFF2, variable/color fonts, vertical text and WOFF/WOFF2 need separate milestones before being claimed. |

Out of the initial static product: SMIL/CSS animation, scripts/events, interactive
state, a mutable DOM, HTML/CSS layout inside `foreignObject`, arbitrary external
web content, and full browser CSS. Detect animated inputs and fail rather than
choose an undocumented frame. Link containers may render their static children.

The first preview can cover geometry, styles, paints, composition and PNG images.
The initial stable release should also cover the first filter set and basic text
with supplied TrueType fonts. Advanced static features remain visible backlog
items, with representative fixtures kept in the full inventory. A preview is not
the completion of this plan; changes to the stable release scope must be explicit.

Text fidelity requires a dedicated early investigation: equal font bytes do not
guarantee equal hinting, shaping, fallback or glyph coverage. Ahem is useful for
layout, but cannot prove ordinary-font fidelity. Do not label basic cmap/kerning
support as complete Unicode shaping or silently render complex scripts incorrectly.

## 5. Rendering architecture

```text
SVG memory + options + resource/font bytes
  -> bounded XML/CSS parsing and ID tables
  -> computed styles, viewport/reference resolution
  -> compact typed scene, geometry and conservative bounds
  -> budgeted render plan (groups, clips, masks, filter graph)
  -> scan conversion, shading, compositing
  -> PNG scanlines, compression and bounded output
```

### Parsing and scene representation

Use BCL `XmlReader` over a non-copying memory-backed stream initially. Do not load
`XDocument`, create a full input string, and then build another DOM. Disable XML
resolvers and external entity access. A legacy DOCTYPE may be skipped safely;
custom entity expansion is unsupported. Limit document characters, depth, names,
attribute values and decoded resources before their materialization can escape
the budget. The XML parser's internal allocations need a conservative reservation
based on accepted input limits, not just accounting after `Read()` returns.

Parse numeric/path/CSS tokens over spans with invariant culture. Bound token size
and matching work. Avoid regular expressions for general CSS parsing. Store typed
nodes and path commands in charged arrays/slabs with compact typed index handles
and bounded tables. Use explicit traversal stacks. Preserve separate geometric, stroke, paint,
and effect bounds, since percentages and clipping depend on different boxes.

Resolve forward references without repeatedly cloning subtrees. Represent `use`
instances with their own context/transform/style and shared immutable definitions.
Track active references for cycles and cumulative instance work for expansion
bombs. Never eagerly flatten an exponentially expanded scene.

### Geometry and rasterization

Keep a replaceable internal raster backend behind the scene model. Start with a
deterministic scalar implementation. Research Chrome/Skia coverage and rounding
before committing to a scan converter: arbitrary 4x/8x supersampling cannot be
assumed to match Chrome and increases memory/work substantially.

Use robust affine math, bounded curve subdivision in device space, stroke expansion,
and sorted active edges/coverage spans. Bound numeric ranges, reject nonfinite
values where required, and handle degenerate paths and singular transforms.
Define premultiplied-alpha arithmetic and unpremultiplication exactly. Test
coverage, clipping, interpolation and blending independently. Introduce SIMD only
after proving equivalence to the scalar path, including rounding/FMA differences.

Render ordinary shapes directly into the destination surface. Allocate isolated
surfaces only when group opacity, masks, filters, or blending require them. Use
conservative effect regions; offscreen input may still contribute to an onscreen
blur or displacement result. A filter cannot be clipped to the visible bounds
before its input dependencies have been computed.

Build filters as a graph with input lifetimes, reusable buffers and explicit
color/alpha representations. Last-use analysis reuses surfaces. Start with a
fully budgeted full-surface implementation; add band/tile execution only when its
boundary behavior matches the untiled version exactly. Derive filter halos from
the actual kernel/dependency rules. Some effects need larger/full intermediates;
fail at the budget rather than shrink a region, lower quality, or approximate it.

### PNG and other binary formats

Write a small PNG encoder: signature, IHDR, row filtering, IDAT chunks, CRC-32 and
IEND; begin with noninterlaced 8-bit RGBA in a defined sRGB output space. Convert
premultiplied working pixels to straight-alpha PNG correctly. Canonicalize RGB to
zero at alpha zero. Use BCL [ZLibStream](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibstream?view=net-10.0)
for bounded-chunk compression, following the [PNG specification](https://www.w3.org/TR/png-3/).
No full uncompressed PNG copy and no unconditional `MemoryStream.ToArray()`.

PNG input decoding validates chunk sizes/CRCs, bit depths, palette/transparency,
scanline sizes, interlace passes, and decompression limits before allocating.
Define image resampling and profile handling against Chrome. Start with explicitly
supported sRGB inputs; fail on unsupported profiles rather than claim exact color.
JPEG decoding and font parsing each get independent bounds/cancellation reviews.

## 6. Memory and work budgets

The enforceable promise is a bound on accounted renderer-owned storage and work.
It is not a hard bound on total process RSS: the CLR, GC heap reservation, JIT,
thread stacks, framework internals, caller memory and other concurrent work also
consume memory. Publish measurements of these separately. A host requiring a hard
process cap must enforce it outside the library.

Create a render context with a reservation ledger from the first implementation.
Reserve before allocating and release/reuse through explicit lifetimes. Charge
actual capacities and conservative object/table overhead, including:

- parser state, strings/interned names, nodes, ID/style tables and path buffers;
- traversal stacks, edge/coverage buffers, stroke and curve temporaries;
- decoded images, font structures, glyph caches and resource copies;
- color surfaces, clip/mask planes, filter inputs/results and halos;
- PNG rows, compression workspace allowance, chunk queues and output capacity;
- overlapping old/new capacity during growth or consolidation, and any retained pool.

Borrowed input/resources and caller-owned destinations are reported separately,
with their own input/decoded-size limits. Library-created output counts while
rendering; ownership transfers to the caller on success. Retaining many completed
outputs is the caller's responsibility. Per-call limits multiply with concurrency;
document a host concurrency bound rather than hide an unlimited internal queue/cache.

Use a private bounded allocator/arena or fixed-capacity buckets, not unrestricted
`ArrayPool.Shared` as proof of a cap. A rental's physical capacity may exceed the
request. Avoid per-pixel/per-edge objects, unbounded dictionaries, and retaining
exceptional buffers globally. Reuse transient storage within the call instead of
repeatedly allocating and pretending that releasing a ledger entry forces a GC.
Track total allocation traffic as well as live/retained capacity. Do not call
`GC.Collect()` as a production memory strategy.

Provisional defaults, to calibrate against the baseline corpus:

| Limit | Initial proposal |
| --- | --- |
| Renderer-owned accounted storage | 256 MiB per conversion |
| SVG input | 16 MiB |
| Output dimensions | Each axis <= 16,384; total <= 16,777,216 pixels |
| Encoded PNG | 128 MiB, also subject to the total simultaneous budget |
| Nodes / parsed path commands | 100,000 / 1,000,000 |
| XML nesting / active reference nesting | 128 / 64 |
| Decoded resource bytes | 64 MiB total, within the overall budget |
| Filter graph | 256 primitives; bounded surface dependencies |

Also cap CSS rules/matching steps, token sizes, gradient stops, glyph work,
subdivision/edge counts, resource recursion and total rendering work. Calibrate
work-unit limits during the first stress runs. Use checked wide arithmetic for
every size/product/offset. Memory, dimensions and work are independent constraints.

A 4096 x 4096 RGBA surface already costs 64 MiB. Two full-size group/filter
surfaces, encoded output growth and parsing can exhaust a 256 MiB budget. Record
this explicitly in tests; do not infer success from the pixel limit alone. A
1024 x 1024 surface costs 4 MiB. Favor bounding boxes and reuse before tiling.

Instrument current/peak reserved and retained bytes, component totals and denied
reservations. For each allocation family test just-under/at/over limits and ensure
failure/cancellation releases the context. Audit ungoverned BCL operations and
conservatively reserve their bounded workspace; do not claim an exact byte cap on
unobservable CLR internals.

## 7. Chrome oracle and visual feedback loop

### Establish a reproducible reference

Use an exact [Chrome for Testing](https://developer.chrome.com/docs/automation-and-testing/chrome-for-testing)
version, downloaded by an explicit setup step into an ignored cache. Lock the
version, archive hash, OS/container image, architecture, fonts and launch flags.
Include the harness/recipe revision and Chromium revision in each reference's
fingerprint; cache keys also include all input/resource hashes and render options.
Use unified [headless Chrome](https://developer.chrome.com/docs/automation-and-testing/headless).
Do not select whatever auto-updating Chrome happens to be installed.

Initially designate one pinned Linux x64 environment as the canonical pixel
oracle. Run library portability tests on Windows and Linux and compare library
output across architectures. Local Chrome on another OS is useful for exploration,
but it does not silently replace the canonical baseline, especially for text.

Implement a small BCL-only wrapper using `Process`, `HttpClient`,
`ClientWebSocket` and `System.Text.Json` over the
[Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/).
Only implement launch/target lifecycle, command IDs/responses/events, navigation,
readiness, metrics/background settings and screenshot capture. Bind debugging to
loopback on an ephemeral port, use a unique temporary profile, drain logs, and
apply startup/command/case timeouts. Dispose targets and kill the process tree on
timeout/cancellation; test that cleanup. Keep browser sandboxing enabled.

Define the rendering recipe before creating goldens:

1. Render the SVG in image context using an `<img>` in an otherwise empty harness
   page. Use fixed viewport/output dimensions, zero margins, block layout, no
   scrollbars, known zoom/device scale, and a transparent default background.
2. Supply self-contained fixture bytes. For mapped fonts/images, generate a
   deterministic fixture variant with embedded bytes and pass that same variant
   to both Chrome and Lokad.Svg. Record original and materialized input hashes;
   never rewrite paths, outline text, or simplify geometry to aid the candidate.
3. Pin sRGB, locale, preferred color scheme, rendering backend/GPU policy and font
   environment. Verify the selected backend with startup metadata and probe images;
   a launch flag alone does not prove deterministic rasterization.
4. Await image decoding and font/resource readiness, then a defined compositor
   readiness barrier. No arbitrary sleep as the correctness mechanism. Block
   undeclared network requests and report missing resources. Harness scripting is
   controlled test code; input SVG scripts never execute in image context.
   Verify embedded-font readiness with dedicated image-context probes; the outer
   HTML document's `document.fonts.ready` alone does not establish it.
5. Capture precisely the image rectangle through
   [Page.captureScreenshot](https://chromedevtools.github.io/devtools-protocol/tot/Page/#method-captureScreenshot),
   with the recorded transparency/device-metrics settings. Compare multiple fresh
   process captures first; byte-different PNG compression is acceptable only when
   decoded pixels are identical.

Calibrate alpha, gradients, fractional coordinates, strokes, text, and image
sampling before declaring the oracle stable. Repeated instability is an oracle
failure, not a reason to increase candidate tolerances. Chrome/OS upgrades are
explicit baseline changes with old/new reports.

### Exact comparison and useful diagnostics

Compare decoded, same-sized RGBA8 pixels in the declared color space; PNG byte
equality is not required. Canonicalize only invisible RGB when alpha is zero.
Partial-alpha differences remain failures. Keep alpha deltas separate and also
composite over black/white/checkerboard for diagnosis; composited agreement cannot
replace RGBA equality.

For every case report exact-match status, changed-pixel count/ratio at any nonzero
delta, maximum per-channel error, MAE/RMSE, alpha error, mismatch bounds and error
histogram. Optional perceptual scores are explanatory only. Produce reference,
candidate, magnified nearest-neighbor views, alpha view, amplified heatmap and an
HTML overlay/blink report. No alignment, rescaling, blurring or best-fit registration
in the acceptance comparison.

Keep the comparison decoder independent of production image-codec logic where
practical; it only needs the pinned oracle's PNG formats plus generated RGBA PNGs.
Cross-check it against browser canvas pixel readback and tiny independently specified
PNG vectors. Test the comparer itself with one-pixel/channel/alpha changes,
dimension differences, transparent images and deliberately truncated files. A
production encoder/decoder round trip alone cannot establish correctness.

Every fixture records separate semantic support and fidelity status:

- `exact`: zero mismatching canonical pixels; any regression fails.
- `known-difference`: bounded recorded mismatch plus an issue/reduced reproducer;
  remains visibly failing the pixel-perfect objective, even if unchanged.
- `unsupported`: expected explicit feature error, retained in the coverage inventory.
- `oracle-failure`: missing/unstable reference; never counted as a renderer pass.

Report counts against the entire fixed inventory, by feature family and image
area. Do not inflate percentages by excluding failures or large cases. No blanket
edge tolerance, automatic golden acceptance, silent skips, or replacing Chrome
references with candidate images. A changed reference requires a reviewable report
and provenance. Track both new regressions and progress in the known-difference set.

## 8. Fixtures and provenance

The default tests and benchmark baseline must run from committed assets without
downloads or `external/`. Keep compact reviewed goldens in Git; larger diagnostic
outputs belong in ignored artifacts. Store a manifest with stable ID, source URL,
creator, retrieval date, original SHA-256, license evidence, modifications/generator
seed, resource/font hashes, feature tags, dimensions/scale/background, required
limits, oracle identity and expected support/fidelity status.

Use original CC0 probes for precise semantics and known seeds for scalability.
Use selected public-domain/CC0 illustrations for realistic combinations. The
[Openclipart FAQ](https://openclipart.org/share) identifies its CC0 policy, but
inspect each selected file's provenance and any upstream source/remix chain.
Do not treat an arbitrary SVG download or a renderer's bundled `tiger.svg` as
proof of public-domain status. Do not substitute MIT/CC-BY/OFL for the requested
rights-free fixture baseline.

### Concrete illustration candidates inspected

Downloaded into memory for inspection only; no assets have been added to the
project. Counts below are element inventory, not claims that every element is used.
Recheck hashes and preserve original bytes when importing during implementation.

| Candidate | Observed bytes/features | Intended role |
| --- | --- | --- |
| [World map, molumen, Openclipart 1733](https://openclipart.org/detail/1733) | 108,359 bytes; one large path; public-domain metadata | Long path parsing, tessellation/edge load, geographically detailed boundaries |
| [Tiger, Telemachos, Openclipart 16551](https://openclipart.org/detail/16551/tiger) | 20,037 bytes; 49 paths; public-domain metadata | Layered illustration, curves, fills and composition |
| [Peaceful landscape, hjkl, Openclipart 315037](https://openclipart.org/detail/315037/peaceful-landscape) | 17,797 bytes; 12 paths, one circle, seven linear and one radial gradient definitions | Gradient/curve composition; confirm asset-level provenance under the site policy before admission |
| [Certificate Border 4, Arvin61r58, Openclipart 302664](https://openclipart.org/detail/302664/certificate-border-4-us-size) | 556,128 bytes; eight paths, two linear gradients; CC0 metadata | Large authored input, long decorative paths, physical units and gradients |

Observed SHA-256 values, in the same order:

```text
1733    896e5bc94c9cbca7985880942351e6afdb65486f87a8f0dc7e05a4e2a0af802c
16551   a010007a1654f0ccfd3eb49ddce9ea9be030ebef0f3261907791097faf6cc0ac
315037  95db37bfd005b34846076fb483ea4ab6ceeb891dd5156d8ae955a14c460c867f
302664  c6f6acd24d317b5afa1e836d6475b21a3b67b63b39e13bf8ac059bde9d699b89
```

### Corpus construction

Begin with 30-50 small probes covering the highest-risk geometry/alpha/sizing
questions, then grow to at least 200 targeted cases across the feature matrix and
10-20 representative illustrations/diagrams. Counts are planning targets, not a
substitute for feature coverage. Add interacting features and regression cases,
not merely many variants of easy opaque shapes.

Use [Ahem](https://www.w3.org/Style/CSS/Test/Fonts/Ahem/), whose provider identifies
it as public domain with a CC0 fallback, for predictable text layout. Create small
original CC0 font fixtures with curved/composite glyphs and controlled layout
tables for font-parser/rasterizer tests. Ahem alone is not representative text.
If ordinary-font coverage requires another asset, establish suitable rights before
admitting it; separately licensed research fonts do not become baseline fixtures.

Plan these original fixture families:

- paths/arcs/fill rules, subpixel edges, caps/joins/dashes and degenerate geometry;
- viewports/transforms/percentages, stylesheet conflicts, forward references and `use`;
- gradients/patterns, overlapping translucent groups, clips, masks and markers;
- each filter primitive, filter color spaces, large radii and offscreen dependencies;
- chart/dashboard and diagram scenes with many nodes and controlled text;
- embedded PNG/JPEG variants, scaling, nested SVG and transparency boundaries;
- bounded stress cases: long paths, repeated instances, dense crossings, many stops,
  nested opacity, filter graphs, output entropy and very large requested dimensions;
- adversarial inputs: malformed tokens, reference cycles, deep XML, decompression
  bombs, overflowing dimensions, CSS matching explosions and pathological curves.

The [W3C SVG suite](https://www.w3.org/Graphics/SVG/Test/Overview.html) and resvg
suite inform missing cases. W3C suite licensing is not a blanket public-domain
dedication; its [license documentation](https://www.w3.org/copyright/test-suites-licenses/)
also distinguishes historical terms. Design original reproducers without copying
their expressive artwork, fonts, harnesses, or expected outputs.

## 9. Tests, performance and CI

### xUnit suites

Keep fast deterministic tests independent of Chrome. Cover parser/geometry math,
style/viewport semantics, composition, codecs, public API ownership and equivalence,
options, errors, memory accounting, concurrency and cancellation. Reference the
actual production assembly; expose selected internals to tests only where needed.
Follow the friend-assembly guideline above: no `.csproj` declarations or public
API expansion for test access. Include API checks rejecting optional parameters
and confirming the documented non-nullable contracts.

Add deterministic, bounded property/fuzz loops without another test dependency:
valid/invalid path grammar, affine identities, crop equivalence, equivalent SVG
forms, PNG chunk corruption, and resource graphs. Keep seeds and minimized failures.
Use independent expected results where possible rather than tests that mirror code.

Test cancellation during each expensive phase, not just before conversion starts.
Exercise custom memory providers, sliced buffers, disposed caller resources,
destination exhaustion, streams that fail or stall, and repeated/concurrent calls.
Verify no outstanding rentals/workers after failure. Stress runaway cases in a
child process with a timeout so a broken guard cannot wedge the test runner.

Separate Chrome integration traits/projects. Local users may explicitly choose the
unit suite, but a required visual CI job must fail if Chrome or reference metadata
is unavailable. Validate case catalogs for duplicate IDs, missing resources,
uncovered feature families, invalid hashes and empty/incomplete reports.

### Performance baseline

Use BenchmarkDotNet's [MemoryDiagnoser](https://benchmarkdotnet.org/articles/configs/diagnosers.html)
for allocation/GC data and a separate probe for peak memory. Allocated bytes per
operation are not peak live bytes; current-thread allocation counters also cannot
alone account for work scheduled by async entry points.

| Benchmark group | Inputs and measurements |
| --- | --- |
| Parsing/normalization | Short icon, long map path, large authored input, stylesheet-heavy scene; bytes/s, allocations, bounded reference expansion |
| Geometry | Curve/arc subdivision, edge generation, dashed and transformed strokes; small and large coordinate ranges |
| Raster/shaders | Solid/gradient/pattern spans, translucent overlap, clipping, masks and image resampling at 24/128/512/1024/4096 pixel scales where appropriate |
| Filters/text/codecs | Blur radius and graph depth, font load versus warm glyph work, PNG filters/compression and PNG/JPEG decode |
| End-to-end | The same CC0 fixtures used visually, through owned-memory, caller-memory and stream APIs; sync/async scheduling cost, latency and output size |
| Budget behavior | 32/64/128/256 MiB configurations, fits/fails-at-boundary, repeated calls and bounded concurrency; high-water and retained bytes |

Separate parse-only/raster-only/encode-only benchmarks from end-to-end conversion.
Preparing input bytes, starting Chrome and generating goldens happen outside timed
iterations. If a prepared scene is reused, label that result explicitly. Include
cold startup/font costs separately and test both low/high entropy PNG output.

Record runtime/SDK, OS, CPU/architecture, GC mode, source revision, fixture hashes,
options and benchmark configuration. Measure latency distributions in the scenario
runner, throughput, allocation bytes, GC counts, charged peak, process peak and
post-run retention. Warm Chrome timings may be informative but are not the package's
performance gate and must not be confused with browser startup costs.

Establish numerical latency/allocation baselines after the first correct renderer.
Use stable dedicated-host runs for speed regression decisions; initially flag
repeatable >10% regressions for investigation. Enforce deterministic budget and
allocation-growth failures in ordinary CI. Do not invent unsupported absolute
throughput promises or optimize by reducing fidelity. Small hot loops should reach
zero steady-state allocation; end-to-end output necessarily owns storage.

### CI and package validation

- PRs: locked restore, Release build/test on Windows and Linux (add ARM64
  coverage as runners permit), corpus integrity, package dependency audit and a
  bounded canonical Chrome smoke/fidelity suite. Required visual cases cannot skip.
- Scheduled/manual: full corpus, broader scales, stress/cancellation/memory runs,
  architecture equivalence and stable-host BenchmarkDotNet comparisons. Keep full
  suite counts visible even when PR execution uses a selected subset.
- Failures: upload reference/candidate/diff images, report, diagnostics and oracle
  metadata. Never upload arbitrary user inputs through public CI.
- Packaging: `dotnet pack -c Release`, NuGet metadata/README/MIT license, portable
  symbols, repository links, explicit version/release notes, and no runtime package
  dependencies or native assets. Inspect the actual `.nupkg`/`.nuspec` contents.
- Smoke-test the packed package in a clean consumer with memory-only inputs and
  no `external/`, Chrome, test packages, installed fonts, network or temporary files
  involved in conversion. Building/testing still needs the normal SDK/tool restore.
- Document reproducible developer commands for unit tests, oracle setup, one visual
  family, full corpus, memory probes, benchmarks and package creation. GitHub/NuGet
  publication is a later release operation, not part of this planning task.

## 10. Implementation sequence and exit criteria

All implementation milestones below remain planned. Order work by dependency
and risk, not by the ease of producing a recognisable image.

1. **Repository and contracts.** *(Complete 2026-09-19: project/configuration/
   documentation, API contract, package audit, clean consumer build, support
   inventory, and a tested pre-allocation budget ledger are in place.)*
   Create the planned projects/configuration/docs;
   establish MIT/CC0 notices and `/external/` ignore; define API ownership, sizing,
   errors, limits and async policy; publish the adopted C# guidelines locally and
   align the API/review checks with them. Add the budget ledger and test seams before
   renderer allocations. Exit: dependency audit and an API-only package consumer
   build pass, with the feature/support inventory created.

2. **Oracle and feasibility probes.** *(In progress: independent comparer/CLI,
   original CC0 probes, a hash-pinned Chrome-for-Testing capture path, and thirty
   reviewed stable references exist; broader feasibility probing remains.)* Build the thin Chrome wrapper, independent
   pixel comparison and provenance format. Create the first original probes plus
   a licensed small corpus. Investigate antialiasing, group alpha, gradients,
   filter rounding and real glyph behavior before choosing core algorithms.
   Exit: repeated canonical Chrome runs have identical decoded pixels; the comparer
   catches injected one-channel errors; findings identify the raster strategy and
   text limits. If exactness is not yet attainable, record the blockers and continue
   investigation without changing the meaning of the fidelity target.

3. **Minimal complete conversion.** *(In progress: bounded root parsing, RGBA PNG
   encoding through owned and caller buffers, caller backgrounds, document-order
   integer rectangle layers, and calibrated analytic fractional rectangle coverage
   with source-over composition are implemented; this is
   not yet the milestone's complete conversion scope.)* Implement bounded parsing, basic scene/viewport
   resolution, solid shapes/paths, alpha composition and PNG output through both
   memory API forms. Exercise cancellation end to end. Exit: simple integer and
   fractional geometry probes match exactly, PNGs decode independently in Chrome,
   ownership tests pass, and a low-budget failure occurs before allocation.

4. **Geometry, style and painting breadth.** Complete arcs/strokes/dashes,
   transforms, supported CSS, nested SVG/`use`, gradients and markers. Fix the
   first real illustrations against Chrome. Exit: supported feature families have
   exact probes plus combination cases, and all residual corpus differences are
   classified with reduced reproducers. Record the first performance baseline.

5. **Composition and resources.** Add patterns, clips/masks, isolated groups,
   image resampling/PNG decoding, resource maps and nested SVG. Exit: group opacity
   and alpha edge cases are exact; cycles/resource bombs fail within limits; no
   filesystem or network access occurs in rendering.

6. **Filters and bounded execution.** Implement the first filter set with graph
   lifetime analysis and color spaces; verify every reservation family. Add band/tile
   execution only where it preserves exact pixels, along with async stream output.
   Exit: filter probes, full-surface/tiled equivalence, fixed-budget stress, chunk
   backpressure and cancellation cleanup pass. Document cases rejected for memory.

7. **Text and remaining static breadth.** Implement supplied-font basic text and
   glyph rasterization using findings from milestone 2; add JPEG and then broader
   filters/layout according to the feature inventory. Keep advanced shaping/font
   formats separately visible. Exit for the initial stable subset: basic text,
   selected filters and image combinations have explicit exact/known-difference
   results; unsupported scripts/formats cannot silently produce a successful image.

8. **Optimization and hardening.** Profile actual CC0 cases; reduce allocation
   traffic and repeated work, then introduce justified SIMD/fast paths. Exercise
   adversarial/fuzz/concurrency cases and architecture differences. Exit: previously
   exact cases remain exact, measured memory stays within the defined contract,
   cancellation targets hold, and performance/retention reports are reproducible.

9. **Release preparation.** Complete support/limits/API documentation, fixture
   provenance, changelog, CI, clean-package consumer checks and reviewed visual
   reports. Exit: no regression in exact cases, no unclassified failure, no missing
   oracle result, no hidden dependencies, and published performance/memory data.
   Pixel-perfect claims require zero differing pixels for every case in the stated
   supported release corpus; an explicitly labelled preview may disclose gaps but
   does not satisfy that claim. Keep remaining static-feature work in the roadmap.

### Coding-agent commit workflow

Once implementation is authorized, the coding agent must progress through small,
well-scoped Git commits. Complete and validate a coherent change, commit it, then
continue to the next increment. Split the milestones above into independently
reviewable commits rather than accumulating an entire milestone's work uncommitted.

- Give each commit one clear purpose: a feature increment, bug fix, refactoring,
  test improvement or documentation change. Keep unrelated changes separate.
- Include the relevant tests, fixtures and documentation with the behavior they
  establish. Each visible-feature change needs its minimal Chrome-grounded fixture,
  an appropriate semantic/API test, and a performance or budget check when affected.
- Before committing, review the staged diff and run the checks appropriate to the
  change. Keep commits buildable and preserve existing passing gates. Stage only
  the agent's intended changes; preserve unrelated user work.
- Use a concise commit subject describing the change. Explain material rationale,
  validation and remaining limitations in the body when needed. Update this plan
  and the support matrix when scope or milestone status changes.

This workflow governs subsequent implementation; the current task remains limited
to preparing `PLAN.md` and inspecting reference material.
