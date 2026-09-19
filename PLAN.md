# Lokad.Svg implementation plan

Revised 2026-09-19 after reviewing implementation revision
`4c0051197757ec2c414a6a9e69290fd08e30f47e`. This document supersedes the previous
milestone labels and implementation order. The review changed the plan, not the
renderer. The historical baseline below records why the priorities changed;
follow-through status is recorded separately so completed corrections do not get
mistaken for still-open defects.

Lokad.Svg is a working, narrow rendering prototype. Its committed examples render
correctly, but its advertised semantics, memory contract and regression coverage
are not yet reliable enough to support broader release claims. Repository
scaffolding is present; the earlier claim that the entire foundations milestone
was complete is withdrawn.

The next objective is to make the existing implementation trustworthy, then build
a general geometry pipeline. More isolated rectangle, selector or stroke variants
are secondary unless they fix a reported defect or unlock that pipeline.

## Follow-through since the review

| Package | Status as of 2026-09-19 | Verified outcome |
| --- | --- | --- |
| P0.1 corpus and provenance | Complete for the existing 43-fixture corpus | The manifest is the sole exact-fixture inventory: every row validates source/provenance metadata and renders against its reference. Fixture SPDX headers, original/materialized hashes and support documentation agree. |
| P0.2 accepted semantics, validation and colors | Complete for the documented narrow subset | The reviewed hidden-stroke, inherited/overridden `fill-opacity`, inherited `currentColor`, stylesheet-path (including opacity and generic classes on paths/point lists), stylesheet-definition, stylesheet-line, stylesheet-`use`, `darkgrey`, trailing-XML, overlapping root-unit suffix and extreme-line cases now match their stated expectations. A supported root stylesheet must precede rendered leaves and rectangle definitions, preventing streaming-order differences; selectors targeting the root or a symbol definition are explicit unsupported instead of silently failing to inherit. Each currently paintable leaf (`rect`, `line`, `path`, `polygon`, `polyline`, `use`) has a public conversion acceptance case and a nested-content rejection case. Rectangle content in instantiated `defs` and `symbol` definitions, other content in an instantiated symbol, stylesheet content outside the one direct root stylesheet, scripts, and each currently recognized animation element are explicit unsupported; definition-container attributes that would otherwise alter an instance are also explicit unsupported. Direct unimplemented visual children (circles, ellipses, text, images, foreign content and paint-server nodes) have conversion-level `UnsupportedFeature` coverage rather than being discarded. Public stylesheet/reference conversions cover groups, links, definitions, instances, lines, paths and point-list classes. Malformed XML, non-finite HSL hues, extreme supported transforms, full-range orthogonal geometry, underflowed output dimensions and contiguous RGBA surface sizes now produce categorized failures rather than framework overflows; the internal scene rectangle now requires every constructor argument. The finite public conversion-boundary matrix now covers the documented families, and no `System.Drawing` source reference remains; a new parser boundary must add a public positive or category assertion before it is claimed supported. |
| P0.3 one conversion budget | In progress | A budget is created before parsing; `SvgRootParser` reserves the greater of a conservative 16 KiB, four times input bytes or 512 bytes per configured XML-attribute limit before constructing `XmlReader`, then releases it when parsing completes. Local .NET 10 allocation probes observe roughly 98 KiB for 256 short attributes (the default 128 KiB reader allowance) and 2.13 MiB for 255 × 4 KiB values (within the 4.0 MiB input-derived allowance); these are pre-allocation renderer allowances, not exact BCL or process-RSS measures. The two main scene builders reserve before growth, then transfer exact retained arrays that remain live with the raster through owned-PNG allocation; parser-collection and parser-scene batch, growth and exact-transfer copies checkpoint shared work and cancellation every 4,096 items, releasing new exact-array reservations if a copy is interrupted. Group-stack, stylesheet-rule, definition-table and deferred-`use` collection growth now reserves conservatively before allocation. XML element nesting and per-element attribute count obey the per-conversion `MaximumXmlDepth` (128 default) and `MaximumAttributesPerElement` (256 default) limits before this parser consumes them; public conversion cases now verify node, nesting, attribute and cumulative path-command denials. Orthogonal-path/point-list points, scanline boundaries/intersections and generated bands use temporary geometry reservations until copied into the scene, with a post-handoff geometry-reservation test; span readers replace their former regex/split path, point-list, transform, viewBox, inline-style, stylesheet-selector-list, selector-normalization and fixed-arity functional-color token buffers. Identifiers/classes/local references and CSS identifier tokens cap at 1,024 characters before matching or retained storage; direct scalar presentation/layout values and inline-style property/value tokens cap at 256 characters before parsing, lookup or copying; transform values cap at 4,096 characters. Inline-style declaration readers, stylesheet rule/selector readers, path/point-list token readers and class selector matching scan source values without transient token arrays, and stylesheet comment removal reserves its one exact temporary copy before allocation rather than making one copy per comment, with shared-work/cancellation checkpoints while scanning and copying. The PNG encoder uses one charged scanline and a charged bounded growing compression buffer for owned/caller-memory forms, emits compressed data as at-most-64 KiB `IDAT` chunks, and includes every chunk header in its output cap; synchronous/asynchronous streams instead compress directly to charged at-most-64 KiB `IDAT` sinks, while caller memory plus writable streams do not incur an owned-output reservation. Stream output writes PNG payloads in cancellation-checked 64 KiB chunks, propagates destination write failures, and the async sink awaits each write. A 1×1 conversion is rejected at 131,071 accounted bytes and accepted at 131,072 because of the default XML reader allowance; direct high-entropy 512×512 PNG-encoder synchronous and asynchronous streams succeed within 70,000 charged bytes. Parsing and rasterization now share `MaximumWorkUnits` for stylesheet text/comment/rule/selector checkpoints, inline-style text/declaration checkpoints (including root and stylesheet rules), path/point-list token preflight/read checkpoints, rule matching, definition-table insertion and immediate/deferred local-reference resolution, cancellation-aware geometry-sort comparisons, parser-collection/scene-copy checkpoints, contour validation and orthogonal-band intersections. PNG scanline, compression-buffer and CRC checkpoints observe cancellation; stylesheet text/comment/rule/selector scanning, inline-style declaration scanning, path/point-list token preflight/read scanning, definition/reference resolution, geometry sorting, parser-collection/scene copies and contour validation also observe cancellation before or during their expansion, the path command cap is cumulative across supported path/point-list elements, and stylesheet rule expansion is capped by the configured structural limit. `MemoryProbe` is now an isolated, machine-readable worker, and its parent scenario script exercises success, budget denial, input-length/attribute XML-reader workspace denial and timed cancellation in separate bounded processes while retaining reports; it emits both conversion-time managed-allocation and OS peak-working-set observations, while Windows CI retains diagnostics on failure. Neither observation is a renderer guarantee. The benchmark suite resets caller streams per conversion and has a local ShortRun baseline with its environment and uncertainty recorded. Exact BCL XML allocation/attribute-string accounting and other structural/work limits remain incomplete. |
| P1.1 Chrome oracle transport | Started | CDP sends and receives are bounded at 16 MiB; receives assemble fragmented text frames with capped geometric growth, while oversized outgoing frames fail before allocation. The CLI admits at most 8 MiB of SVG input and capture dimensions at most 2 million pixels, keeping its nested data URLs and screenshot replies within that transport bound. Deterministic tests cover fragmentation, close and non-text frames, incoming/outgoing size limits, and protocol/evaluation failures. Fresh captures remain exact. The current provisioning pin now identifies recipe 2 while each retained reference preserves its actual recipe-1 or recipe-2 provenance; setup verifies the extracted executable hash and smoke validates its Windows file version when available. Windows and Linux CI now invoke platform-pinned exact smoke captures, emitting a runtime fingerprint and retaining their files on failure; the Linux pin remains separate from the retained Windows baseline inventory. Canonical Linux baseline admission remains. |
| P1.2 syntax/style/reference/scene separation | In progress | The bounded inline-style declaration reader and immutable specified-style value live in `SvgInlineStyleParser`; the constrained embedded stylesheet parser and cascade resolver live in `SvgStyleSheet`; `SvgDocumentStyleResolver` maps streaming ancestry to that cascade once for group, leaf, definition, symbol and instance routing; `SvgComputedStyleResolver` composes inline, stylesheet and presentation-attribute precedence into the immutable `SvgComputedStyle`; `SvgColorParser` owns bounded color/opacity token parsing while `SvgPaintResolver` resolves inherited paint and element alpha (the root retains the immutable supported-name table) and `SvgShapePresentationResolver` resolves paintable-leaf validation/visibility; an immutable `SvgPaintableElementContext` factory derives inherited paint/visibility/transform, matched stylesheet, color table and bounded work state uniformly for group, leaf, definition and instance lowering; `SvgGeometrySyntax` owns bounded coordinate, transform, root-size and viewBox syntax, `SvgRectangleSyntax` and `SvgLineSyntax` own validated bounded rectangle/line attributes, and `AxisTransform` owns applying that supported transform to retained geometry; `SvgOrthogonalShapeParser` connects bounded path/point-list attributes, tokenization and temporary lowering, while `SvgOrthogonalGeometryTokenizer` owns charged path/point-list tokenization and `SvgOrthogonalGeometryLowering` owns contour validation, charged temporary bands and typed retained-geometry handoff; `SvgElementAttributePolicy` owns the constrained root/container/leaf attribute subsets; `SvgRootAttributes` owns bounded root-style, paint, identifier, opacity and viewport interpretation; `SvgRootStyleSheetParser` owns the one ordered direct-root stylesheet; `SvgGroupParser` owns inherited group/link style, paint, visibility, transform and isolated-opacity interpretation; `SvgRenderedLeafParser` owns rendered-leaf routing; `SvgLeafParser` retains rectangles and axis-aligned lines as separate typed geometry; `SvgLocalReferenceParser` owns bounded local-`use` syntax, `SvgDefinitionParser` owns typed definition/symbol/local-instance routing, and `SvgReferenceResolver` owns immediate/forward typed local-reference resolution; `SvgSceneBuilder` accepts only typed geometry into one budgeted array, with opacity commands referencing geometry by index, deferred-reference placeholders and immutable transfer; the raster backend dispatches rectangle and axis-aligned-line stroke-band fast paths separately. `SvgDocumentState` owns per-document typed group, stylesheet, definition and deferred-reference state with its parser-reservation lifetime; `SvgDocumentTraversal` owns root validation, streaming node dispatch, EOF validation and scene finalization; `SvgRootParser` retains reader setup and the immutable supported-name table. |
| P1.3 geometry feasibility research | Started, calibration only | Eight original CC0 probes plus one classified external path-heavy illustration now retain three identical fresh-process Windows-pinned-Chrome captures, hashes, recipe metadata and public `UnsupportedFeature` assertions: circle coverage, a four-pixel object-bounding-box linear gradient, integer, grid-aligned fractional and half-grid diagonal coverage, a cubic curve, an even-odd hole, a round stroke join, and Tiger (49 paths/four groups, with original and SPDX-materialized hashes preserved). The Chrome tag's `DEPS` now pins the matching Skia source revision for inspection, without inferring a raster path from that pin. On the integral diagonal, pixel-centre, 16×16 uniform-supersampling, exact triangle–pixel-area, fixed-point-alpha, and sixteenth-snapped edge-strip baselines are quantitatively rejected (16/64 changed, max channel/alpha 255/127; then 14/64, max 7/7; then 7/64, max 3/3; then 6/64, max 3/3; then 2/64, max 1/1). Endpoint snapping does not change the fixed-alpha result, consistent with the matched source's small-mask path but not proof that Chrome selected it. The fixed-alpha and edge-strip baselines respectively change 22/64 and 23/64 grid-aligned-fractional-origin pixels (each max channel/alpha 255/32); at the half-grid tie phase they change 22/64 (255/23) and 20/64 (255/24). This proves the integral near-fit does not transfer across device phase. They are research inventory entries, not admitted product fixtures or a general-geometry/paint-server claim; calibrated scan-converter implementation and canonical Linux evidence remain. |

The browser review harness currently reports all 43 manifest fixtures exact. Its
accepted semantic probes match Chrome; `styled-use` now retains a definition's
typed geometry plus whether it has inherited fill, allowing a fill declared on a
`use` instance only for an otherwise unstyled visible rectangle definition.
Explicitly painted definitions and other use-paint combinations remain explicit
`UnsupportedFeature` cases pending fuller P1.2 instance-style semantics.

## 1. Product contract retained

- Ship one MIT-licensed, pure managed C# `net10.0` NuGet library for Windows and
  Linux. No third-party runtime packages, native assets, P/Invoke, platform drawing
  APIs, browser services or dependencies on other Lokad packages. Retain the
  original prohibition on `System.Drawing`, including the current color lookup;
  ordinary .NET framework APIs and their implementation details are allowed.
- Convert static SVG bytes to PNG through memory APIs, with independently owned
  results or caller-owned destinations. Accept non-array-backed memory. Caller
  buffers/resources remain alive and unchanged for the operation; reject detectable
  input/output overlap and document the rule for arbitrary providers. Owned results
  remain valid indefinitely and are never returned pool rentals; retained capacity
  counts toward the budget.
- Support synchronous and asynchronous stream output. Leave caller streams open,
  support nonseekable streams, bound buffering and propagate write failures.
  Destination/stream contents may be partial on failure; success requires a
  complete PNG. Do not promise compressed size before encoding.
- Keep options immutable. Distinguish CSS viewport from output scale, use 96 CSS
  pixels/inch, and resolve intrinsic dimensions, percentages and default object
  sizing explicitly. Preserve transparent and explicit caller backgrounds.
- Rendering needs no filesystem, network, installed fonts or temporary directory.
  Resources and fonts come from caller-supplied byte catalogs; local fragments and
  supported data URLs resolve internally. Define base-URI/key and missing-resource
  behavior before implementing external-reference mapping.
- Observe cancellation throughout expensive work. Memory-to-memory async APIs
  schedule the synchronous engine once; no task per shape or pixel. Async stream
  writes must provide backpressure and leave no producer running after completion.
  Synchronous entry points must not block on asynchronous implementations.
- Enforce per-conversion accounted memory, input/output, structural and work
  limits before prohibited allocations or work. This is not a process-RSS cap.
- Use SVG semantics and a pinned Chrome image-context oracle for exact canonical
  RGBA8 comparisons. Ignore RGB only where alpha is zero. Dimensions and every
  nonzero channel difference matter; no edge tolerance or automatic rebaselining.
- Unsupported rendering must fail explicitly with useful bounded diagnostics.
  Apply specified recovery for supported malformed CSS/path cases; distinguish
  inert metadata from unsupported visible content. Animated content is outside
  scope and must not silently become an arbitrary static frame.
- Preserve distinct failure categories for malformed input, unsupported rendering,
  unresolved resources/fonts, exceeded limits and insufficient destination space.
  Bound diagnostic count/size and include source location or element ID when
  available. Cancellation throws `OperationCanceledException` with the supplied
  token, never a successful empty or partial image.
- Keep a committed original/public-domain/CC0 corpus. xUnit, the BCL-only Chrome
  wrapper and BenchmarkDotNet remain development dependencies only.

The six existing `SvgConverter` memory and stream entry points remain the public
starting point; resource/font catalogs and diagnostics are additions to design.
Do not freeze incomplete preview behavior as a permanent contract.

The preview target remains geometry, styles, paints, composition and PNG images.
The initial stable target additionally includes the first filter set and basic
text with supplied TrueType fonts. Advanced static SVG work stays in the backlog;
a preview or a passing small corpus does not complete this plan. Scripts/events,
animation, interactive state, mutable DOM, `foreignObject` HTML layout, arbitrary
web content and full browser CSS remain outside the static product.

## 2. Verified baseline and limits of the evidence

The review inspected production sources, tests, tools, packaging, CI and the
support/design documents. The table and findings in this section preserve the
review-start baseline; the status summary above records subsequent repair work.
Checks ran on Windows x64, SDK 10.0.401 / runtime 10.0.12. Linux execution,
ARM64 equivalence and a clean installed-package consumer were not rerun in this
review.

### Current local verification (2026-09-19)

`dotnet test Lokad.Svg.slnx -c Release --no-restore -m:1` passes 216 unit tests
and 70 visual-project tests on the current working revision. The Windows pinned
Chrome smoke (`tools/test-chrome.ps1`) also reports an exact result with zero
changed pixels. This is local, sequential evidence only; it does not establish a
successful remote CI run, a canonical Linux baseline, ARM64 equivalence or
installed-package behavior.

Public P0.3 boundary tests now include structural denials and long stylesheet or
geometry-token scans that exhaust the one shared work limit before rendering.
The isolated memory probe additionally rejects metadata input one byte below its
four-times-input XML workspace allowance, then starts its parser-cancellation
timer after input is loaded and cancels a 4 MiB stylesheet scan without producing
output. Its Windows child-process suite now also warms and directly traverses
both a 256-short-attribute root and a root with 255 4 KiB attribute values through
the same BCL `XmlReader` settings and non-copying stream, records managed
allocation/touched characters, and rejects a measured allocation above the
renderer workspace allowance. The current Windows reports record 104,152 managed
bytes against the 131,072-byte allowance and 2,233,296 bytes against 4,190,904
bytes, respectively. That is regression evidence for the pinned runtime, not an
exact BCL-allocation or process-RSS guarantee.

Parser-owned stylesheet declaration/selector strings, definition identifiers and
copied local-reference identifiers now reserve both their exact string bytes and
conservative reservation tracker storage before copying, releasing that accounting
with document state. Reader-provided transient attribute/style strings remain
covered only by the conservative reader workspace; exact BCL string accounting is
still open.

Every supported root, group, definition, instance and rendered leaf now validates
its bounded attributes before constructing the context that evaluates stylesheet
selectors. Class-selector scans also consume shared-work checkpoints. This prevents
an oversized identifier/class value from being matched before its syntax limit is
enforced.

The definition table reserves twice its requested capacity before `Dictionary`
selects its prime backing capacity, then verifies the returned capacity stayed
within that pre-allocation allowance. This keeps the conservative table charge
ahead of the BCL allocation rather than treating the requested and actual
capacities as interchangeable.

Async PNG streaming now reserves one 64-byte PNG-workspace allowance before its
17-byte header/length scratch buffer is allocated, and reuses that buffer for all
chunk framing. It no longer creates uncharged managed header or length arrays per
`IDAT` chunk.

### P0.2 conversion-boundary matrix

Rows marked covered have public `SvgConverter` assertions; parser-only coverage
does not close a row. This makes the remaining P0.2 review finite and prevents a
new subset from being treated as supported merely because it parses.

| Boundary | Current public evidence | Status / remaining work |
| --- | --- | --- |
| Paintable leaves | One positive PNG conversion for each of `rect`, `line`, `path`, `polygon`, `polyline` and local `use`; one nested active-content rejection for each. | Covered for the documented subset. |
| Styles and local references | Positive stylesheet conversions cover leaves, groups, links, definitions and instances; declaration/order/root/symbol/definition limitations produce `UnsupportedFeature`. | Covered for the documented subset. |
| Active and directly visible unsupported content | Styles outside the supported root position, scripts and every recognized animation tag reject; circles, ellipses, text, images, `foreignObject` and direct paint-server nodes reject. | Covered; unused inert definitions remain intentionally distinct. |
| Document structure and conversion bounds | Bad XML, wrong root, trailing non-whitespace, input/surface/output limits and output-sizing failures have public category checks. | Covered for the current probes. |
| P0.2 category coverage | Public assertions cover rejected presentation attributes, foreign-namespace lookalikes, malformed CSS/path/number forms, nested opacity and non-finite color syntax. | Complete for the current documented subset; a newly introduced parser boundary must add a public assertion. |

| Area | Evidence at the reviewed revision |
| --- | --- |
| Build/test | `dotnet test Lokad.Svg.slnx -c Release --no-restore`: 76 unit tests and 80 visual-project tests pass. Many visual-project tests are small pixel/comparer tests, not independent Chrome cases. |
| Corpus | The manifest contains 41 entries, all labelled `exact`. An independent review run rendered and compared all 41 against the stored references: all match. Only 30 have render-and-compare gates in `FixtureManifestTests`; the other 11 receive metadata/hash checks. |
| Representativeness | 39 of the 41 fixtures are at most 64 pixels. The other two are a 300×150 default-size case and a 20×10 sizing case. No representative illustration, supplied font, embedded image or filter corpus exists. |
| Current rendering | Solid and quarter-pixel rectangles, restricted orthogonal paths/polygons/polylines, narrow strokes/lines, positive integer axis transforms, limited styles, rectangle definitions/instances and one isolated opacity scope. All geometry ultimately becomes rectangles. |
| API | Four memory sync/async entry points work for the current subset. No stream output, resource/font maps or bounded diagnostic API. Current tests mostly cover small successful calls and cancellation before work. |
| Memory | `RenderBudget` has tested ledger arithmetic. `SvgConverter` uses it for surfaces only, creates it after parsing, and releases the main reservation before PNG encoding even though pixels remain live. |
| PNG | Managed RGBA8 output exists. Encoding allocates a complete filtered image, grows a compressed `MemoryStream`, calls `ToArray()`, and only then checks the PNG limit. Caller-destination output still allocates the compressed payload. |
| Oracle | A win64 Chrome 153.0.8010.52 archive pin and CDP capture wrapper exist. Reference metadata records both historical recipe 1 and current recipe 2; at the reviewed revision the provisioning pin still said recipe 1 despite the revision-2 wrapper. No canonical Linux oracle is provisioned. |
| CI | Windows/Linux locked restore, Release build/test and packing are configured. No live Chrome job, package-content audit, installed-consumer test, cancellation stress or memory/performance gate is configured. CI configuration is not evidence of a successful remote run. |
| Performance tools | `MemoryProbe` converts one file and prints PNG length; it measures neither memory nor cancellation. The benchmark entry point prints a placeholder and exits 1. |
| Package | Fresh Release `.nupkg`/`.snupkg` creation succeeds; inspection shows the net10.0 assembly/XML docs, README/license and an empty dependency group, with no bundled native assets. The assembly nevertheless references `System.Drawing.Primitives` through color lookup. |

Sources: [converter](src/Lokad.Svg/SvgConverter.cs),
[parser](src/Lokad.Svg/Parsing/SvgRootParser.cs),
[scene records](src/Lokad.Svg/Parsing/SvgRoot.cs),
[encoder](src/Lokad.Svg/Encoding/PngEncoder.cs),
[ledger](src/Lokad.Svg/Rendering/RenderBudget.cs),
[fixture gates](tests/Lokad.Svg.VisualTests/FixtureManifestTests.cs),
[manifest](tests/Fixtures/manifest.json), [CI](.github/workflows/ci.yml),
[memory tool](tools/Lokad.Svg.MemoryProbe/Program.cs),
[benchmark entry point](benchmarks/Lokad.Svg.Benchmarks/Program.cs).

The historical review found five rectangle/group stylesheet fixtures,
`rectangle-use`, `modern-functional-colors`, `rectangle-symbol`,
`compound-stylesheet-selectors`, `extended-named-colors`, and
`rectangle-use-opacity` missing from the render gates. This has been corrected:
the manifest-driven suite now executes every exact entry so adding metadata cannot
omit its actual comparison.

## 3. Findings that determine priority

These are defects or missing guarantees, not requests to expand SVG scope.

| ID | Finding and consequence | Evidence / required correction |
| --- | --- | --- |
| R1 | Accepted documents can silently render the wrong image. | Review probes confirm hidden strokes painting, broken inherited/overridden fill opacity and `currentColor`, and ignored styling on paths, definitions and `use`. Separate computed properties from resolved paint; make unsupported handling consistent across element and style forms. See the reproductions below. |
| R2 | The total memory limit is not enforced. | A 1×1 empty SVG succeeds with `MaximumAccountedBytes=4` and returns a 70-byte owned PNG. Parsing, scene arrays, filtered rows, compression storage and output are uncharged. One context must own reservations from parsing through output transfer. |
| R3 | Input/work/cancellation bounds do not cover expensive phases. | Path tokenization materializes strings before checking the path limit; contour intersection validation is quadratic; CSS matching scans every rule for each applicable node. These operations lack shared work accounting and cancellation. XML depth/token/CSS/reference limits are absent; path limits are per shape, not cumulative. Compression is one uncancellable write and CRC/output copying also lacks token checks. |
| R4 | Green tests overstate visual coverage. | 41 manifest declarations versus 30 automatic reference comparisons. The all-41 review run passes, but it does not repair the missing future regression gates. New review cases fall outside the corpus and fail against Chrome. |
| R5 | Validation is inconsistent at public boundaries. | Both an extra document element after an empty root and trailing text after a closed root are accepted because parsing returns before EOF. A nested `animate` under a root-level rectangle is ignored. Extreme line-coordinate subtraction wraps and produces an empty image. Several checked overflow/invalid-dimension paths can escape as framework exceptions or the wrong category. |
| R6 | Named-color support and the dependency rule are violated. | `darkgrey` is accepted but rendered black; Chrome returns `A9A9A9`. `Color.FromName` uses `System.Drawing.Primitives`, which is a framework assembly, not an extra NuGet/native renderer; it still violates this project's explicit prohibition and is not a CSS color table. |
| R7 | The parser is also the style engine, reference resolver and geometry converter. | `SvgRootParser` contains all those responsibilities, regex/split tokenization and shape-specific attribute policies. `SvgRoot` stores rectangles twice, as arrays and command payloads. Extending this shape-by-shape will multiply inconsistent semantics and accounting. |
| R8 | The oracle is insufficient for realistic assets and trustworthy baseline changes. | CDP receive logic rejects fragmented or larger messages, uses a fixed 64 KiB receive buffer, and does not centrally reject protocol/evaluation errors. Readiness failures can go unchecked. Executable version/backend, sRGB/locale/fonts, undeclared network use and bounded cleanup are not fully validated or recorded. |
| R9 | Performance and cancellation claims are unmeasured. | No working benchmark methods, process-memory measurements, retention reports or cancellation-during-work stress gates. Existing low-budget tests prove individual surface checks, not the advertised whole-conversion bound. |
| R10 | Documentation and provenance drift from the code. | Support docs claim every reference is gated and all other visible behavior fails. API/changelog/design prose still describes implemented pieces as future work. Fixture license text is a placeholder; several assets lack the SPDX declaration their manifest cites. Internal `SvgRectangle` has an optional parameter despite the C# rule; the API test only checks public converter methods. |

### Reproduced semantic failures

Each fragment below was placed inside
`<svg xmlns="http://www.w3.org/2000/svg" width="6" height="6">…</svg>` and compared
to a fresh capture from the existing pinned win64 Chrome executable. These are
review observations, not newly admitted goldens or three-capture stability claims.
Generated inputs/captures and the review harness are in ignored
`artifacts/plan-review/`; the fragments here preserve the reproducers independently
of that disposable directory.

| Fragment | Current result → Chrome result | Changed pixels |
| --- | --- | ---: |
| `<rect x="1" y="1" width="4" height="4" fill="none" stroke="red" stroke-width="2" display="none"/>` | Red stroke → transparent | 32 |
| `<g fill-opacity="0.5"><rect width="6" height="6" fill="red"/></g>` | Red alpha 255 → 128 | 36 |
| `<g fill="red" fill-opacity="0.5"><rect width="6" height="6" fill-opacity="1"/></g>` | Red alpha 128 → 255 | 36 |
| `<g color="red" fill="currentColor"><rect width="6" height="6" color="blue"/></g>` | Red → blue | 36 |
| `<style>#p { fill:red; }</style><path id="p" d="M 0 0 H 6 V 6 H 0 Z"/>` | Black → red | 36 |
| `<defs><rect id="r" width="6" height="6"/></defs><use href="#r" style="fill:red"/>` | Black → red | 36 |
| `<style>rect { fill:red; }</style><defs><rect id="r" width="6" height="6"/></defs><use href="#r"/>` | Black → red | 36 |
| `<rect width="6" height="6" fill="darkgrey"/>` | Black → `A9A9A9` | 36 |
| `<line x1="-2147483648" y1="2" x2="2147483647" y2="2" stroke="red" stroke-width="2"/>` | Transparent → red horizontal band | 12 |

The code-level causes include visibility applied only to `HasFill` while strokes
render independently, fill opacity baked into the inherited color, eagerly
resolved `currentColor`, stylesheet evaluation bypassed by some element paths,
and `int` subtraction before conversion to wider geometry. These combinations
deserve regression tests before accepting further support claims.

## 4. Prioritized execution order

P0 repairs guarantees already promised. P1 removes the bottlenecks to general SVG.
Later priorities retain the full target, with dependencies rather than arbitrary
fixture-count milestones. Start P1 oracle work alongside P0 where independent;
do not wait for all rendering breadth before investigating difficult raster/text
behavior or collecting measurements.

| Order | Status | Work package | Depends on | Exit evidence |
| --- | --- | --- | --- | --- |
| P0.1 | Complete | Make the corpus an executable inventory; correct support/provenance claims. | Existing tests/tools | Every manifest row executes the prescribed support and fidelity check. Missing references, missing cases and inconsistent statuses fail. All 41 existing comparisons remain exact; review regressions are registered visibly. |
| P0.2 | Complete for the documented narrow subset | Repair accepted semantics, validation and color lookup (R1, R5, R6). | P0.1 | Review reproductions pass their independent expectations; unsupported visible constructs fail rather than disappear. Add combinations and rejection tests, not only happy-path parser records. Remove `System.Drawing` usage. |
| P0.3 | In progress | Enforce one conversion budget and bounded output/work/cancellation (R2, R3). | Regression coverage; may proceed with P0.2 | Parser, scene, geometry, surfaces, PNG workspace and owned output share reservation lifetimes. Boundary tests deny allocations before they occur. Work/cancellation stress and owned/destination/stream equivalence pass. |
| P1.1 | Started | Repair oracle transport, readiness and metadata; establish the canonical Linux environment (R8). | Existing exploratory win64 harness | Fragmented and oversized CDP messages plus protocol failures are tested; identical fresh-process captures, complete runtime fingerprint, offline readiness and cleanup checks. Required live-Chrome CI works; baseline migration is reviewed explicitly. |
| P1.2 | In progress | Separate bounded syntax, computed style, reference resolution and scene construction (R7). | P0 semantics/context | Existing corpus remains exact through the new pipeline. Shared style rules apply consistently. References are typed, cycle/work bounded and do not clone expanding trees. Rectangle fast paths are backend optimizations, not the scene's geometry model. |
| P1.3 | Started, calibration only | Build the general path/coverage/stroke pipeline and run targeted raster/text feasibility research. | P1.1 for oracle evidence; P1.2 for integration | Relative/repeated commands, subpaths, curves/arcs, winding, affine transforms and stroke outlines have independent semantic tests. Device-space scan conversion matches a varied calibration family; unresolved differences have persisted reproducers and hypotheses. First path-heavy illustration is classified. |
| P2.1 | Not started | Add paint servers and complete composition/instancing. | General geometry/style/scene | Gradients, patterns, nested group/element opacity, clipping/masks, markers, `use`/`symbol` and nested viewports pass combination cases with shared budgets and bounded references. |
| P2.2 | Not started | Add host resources, PNG images and measured resampling. | P0.3, resource graph, composition | No implicit I/O; decoding/profile limits, mapped resources, nested SVG, alpha boundaries and scaling are exact or explicitly unsupported. First representative illustrations/diagrams work end to end. |
| P3.1 | Not started | Implement the first filter set with surface lifetime planning. | Images, composition, budgets | Each primitive, color space, region and dependency case has exact evidence; graph/resource bombs fail within limits. |
| P3.2 | Not started | Implement basic supplied-font text; then JPEG and broader static coverage. | Early text feasibility, geometry, resource budgets | TrueType parsing, layout and glyph rendering match the declared cases with identical font bytes. Unsupported scripts/formats fail explicitly. JPEG work has independent codec and memory gates. |
| P4 | Not started | Optimize measured bottlenecks, finish hardening and prepare release. | Verified feature combinations and measurements | Full fixed inventory, required platform/cancellation/package gates, numerical performance/retention reports and accurate release docs. Initial stable scope and unresolved advanced backlog remain explicit. |

### Next implementation changes

1. Close the remaining P0.3 accounting evidence before adding scope: retain and
   test the conservative `XmlReader` allowance unless a portable, stricter
   accounting mechanism replaces it; make exact BCL XML/attribute-string
   accounting explicitly measurable or retain it as a documented conservative
   gap; and repeat child-process budget/cancellation probes on stable hosts before
   turning the current local benchmark into a threshold.
2. Obtain remote evidence for the configured Windows and Linux jobs, then admit a
   canonical Linux baseline only through an explicit capture/provenance review.
   A configured workflow, a Windows-exact capture, or a freshly generated image is
   not admission evidence.
3. Continue P1.2 from the extracted bounded style syntax/cascade, precedence,
   local reference resolver and scene builder: split bounded XML element syntax,
   computed paint values and shape lowering behind characterization tests. Preserve
   the P0 subset exactly while moving responsibilities; do not add another
   shape-specific path to `SvgRootParser`.
4. Continue P1.3 from the retained diagonal, curve, winding, join and
   path-heavy-illustration references. Test device-space edge quantization and
   compositing hypotheses against the measured non-exact coverage baselines before
   adding a general geometry feature; keep every unsuccessful probe classified
   rather than silently relaxing the fidelity bar.

These steps need no further prioritization permission within authorized
implementation work. Split large items into reviewable commits and advance to
the next dependency when evidence permits. A clean worktree, an unexecuted plan,
or repeating a feasibility capture without retaining its outcome is not progress.

## 5. Architecture and execution requirements

### Shared context and data flow

Use one lifetime-aware context through:

```text
memory input + immutable options/resources
  -> bounded XML, CSS and number/path tokenization
  -> typed nodes, properties and ID tables
  -> computed styles, references, units/viewports and affine geometry
  -> compact scene with bounds and a budgeted render plan
  -> device-space edges/coverage, shading and composition
  -> bounded PNG rows/chunks and owned, caller-memory or stream output
```

Keep the non-copying `XmlReader` input adapter and disable external resolution.
Consume and validate the entire document. Define safe legacy-DOCTYPE handling
without permitting custom entity expansion. Enforce depth, token and aggregate
storage limits, including a conservative allowance for bounded BCL parser state.
Use span-based number/path/CSS readers in place of regex/split materialization.

Represent specified and computed style separately: color, paint, fill/stroke
opacity, element opacity, display and visibility have different inheritance and
application rules. Resolve `currentColor` at the proper stage. Apply presentation,
inline and embedded-sheet cascade uniformly to renderable nodes and references.
Do not treat parsing a recognized declaration as proof that it was applied.

Separate geometry, stroke, paint and effect bounds. Store paths and typed node/
resource handles in charged arrays or slabs. Use shared immutable definitions and
instance contexts, explicit traversal/reference stacks and cumulative expansion
limits. Resolve forward references without eager subtree duplication. Preserve
shape opacity boundaries when decomposing into multiple draws.

### Raster and compatibility research

Retain calibrated rectangle fast paths. Add deterministic scalar device-space
edge/coverage processing for general paths, nonzero/evenodd winding and strokes;
bound subdivision, active edges and scan work. Specify premultiplied versus
straight-alpha storage, rounding, clipping and unpremultiplication at each stage.
Test low-alpha colors, fractional overlap, clipping and background combinations.

Existing [circle and gradient notes](docs/RASTERIZATION_DESIGN.md) establish that
the attempted algorithms failed their probes. They do not establish impossibility
or prove a particular Chrome implementation: one-unit gradient differences could
involve quantization, dithering or pipeline choice, not necessarily fixed-point
interpolation alone. Preserve those cases in the inventory, verify backend/version,
inspect the matching Skia revision, vary dimensions/origins/colors, and test each
hypothesis. Supersampling is not accepted merely because it looks close.

Start a text feasibility family early with Ahem and a rights-cleared curved-glyph
font. Identical font bytes alone do not settle hinting, shaping or glyph coverage.
Keep experimental code/probes reviewable and retain findings; unsupported or
known-difference entries are useful evidence, not a reason to hide the family.

### Limits and cancellation

| Limit | Current default / intended extension |
| --- | --- |
| Accounted renderer storage | 256 MiB; scene, parser collections, surfaces and PNG workspace/output are covered with conservative XML-reader workspace, while exact BCL XML/attribute-string allocation remains incomplete |
| SVG input / encoded PNG | 16 MiB / 128 MiB; PNG cap must apply during generation |
| Output | At most 16,384 per axis and 16,777,216 pixels; validate products with wide checked arithmetic before allocation |
| Nodes / path commands | 100,000 / 1,000,000; require cumulative accounting and checks before growth |
| Rendering work | 100,000,000 current units across parsing/matching/reference resolution, collection transfers, geometry and raster work; calibrate on a declared stress host |
| XML structure / reference depth | XML parsing reserves a conservative 16 KiB-or-four-times-input-byte workspace allowance. Nesting and attributes per element have configurable defaults of 128 and 256; the `XmlReader` still allocates its current element before this parser can enforce the attribute limit, and reference depth 64 remains planned |
| Decoded resources / filter graph | Planned 64 MiB total / 256 primitives, both also constrained by overall memory/work |
| Additional bounds | CSS rules/matching steps, token/name sizes, gradient stops, contours/edges/subdivision, glyph processing and resource recursion |

Charge actual capacities, conservative metadata/framework workspace and overlapping
old/new buffers during growth. Keep reservations live while arrays remain owned,
including pixels during encoding and output until ownership transfer. Do not
equate a ledger release with garbage collection. Report borrowed caller storage
separately; avoid unbounded shared pools/caches and production `GC.Collect()`.
Limits multiply across calls; document host concurrency control.

Bound long loops and cancellation observation in tokenization, CSS matching,
reference resolution, subdivision, scan conversion, resource decoding, compression
and chunk writes. Measure the original sub-50 ms observation target on a declared
stress host; do not claim it from pre-cancelled-task tests. Exercise partial
destination writes, failing/stalled streams and worker/resource cleanup in child
processes with timeouts. Limit failures must be categorized consistently.

### PNG and output

Use BCL `ZLibStream`, row filtering, bounded IDAT chunks and incremental CRC work.
Do not create another full uncompressed image or unconditional compressed copy.
Owned output counts toward the shared budget; caller-buffer output must avoid a
second complete encoded payload. Compression output sinks must stop at their cap.
Async streaming writes directly to the caller sink in cancellation-checked 64 KiB
chunks; no producer/consumer bridge is used.

Keep the comparison decoder independent from production codecs. Harden its PNG
validation, chunk ordering, limits and truncation handling; cross-check filter
variants and browser pixel readback with independent vectors. Production PNG/JPEG
decoders require their own resource, profile and decompression-bomb reviews.

## 6. Remaining static-feature scope

This inventory preserves the original destination; the execution order above
determines when each family is implemented and accepted.

| Family | Required work |
| --- | --- |
| XML/structure/sizing | Encodings/BOMs, namespaces, `svg`/`g`/`defs`, `switch`, inert links/metadata, nested viewports/overflow, `symbol`/`use`, href/xlink, forward references and cycles; intrinsic/percentage/absolute sizing, `viewBox`, meet/slice/none and alignment. |
| Geometry | Complete path number grammar, absolute/relative/repeated commands, multiple subpaths, implicit closure, lines and quadratic/cubic curves, elliptical arcs; rectangles/rounded corners, circles/ellipses, polygons/polylines, affine transforms, degeneracies and singular matrices. |
| Styles | Presentation/inline/embedded cascade, inheritance/initial/unset/inherit, specificity/source order/`!important`, type/ID/class/list/descendant/child/attribute selectors, named/hex/RGB/HSL colors and alpha. Track unsupported selectors, custom properties and CSS transforms explicitly. |
| Fills/strokes | Winding rules, all basic caps/joins/miter limits, dashes/offsets, odd/fractional widths, zero-length strokes, transforms/non-scaling strokes, `pathLength`, paint order, shape-rendering hints and subpixel/hairline cases. |
| Paint servers | Linear/radial gradients, focal points, stops/order/opacity/inheritance, spreads, transforms and both units modes; patterns/nested content and reference cycles. |
| Composition | Element/group isolation, nested opacity, source-over, clip paths, alpha/luminance masks, marker orientation, effect application order and individually tracked blend/isolation modes. |
| Images/resources | Host maps/data URLs, PNG palette/transparency/interlace, supported sRGB/profile policy, sampling and nested SVG. JPEG baseline/progressive is a separate stage; GIF/WebP/AVIF, animation and unsupported profiles remain explicitly deferred. |
| First filter set | Blur, offset, flood, color matrix, component transfer, composite, blend, merge and drop shadows; named inputs, primitive/filter regions, units, edge modes and sRGB/linearRGB. |
| Further filters | Convolution, morphology, displacement, tile, turbulence, diffuse/specular lighting and `feImage`, each with its own support/fidelity status. |
| Basic text | Supplied TrueType `glyf`, cmap/metrics/kerning/composite glyphs; `text`/`tspan`, whitespace, x/y/dx/dy/rotate lists, anchors, baselines, spacing and `textLength`. Do not equate this with complete Unicode shaping. |
| Advanced text | Text paths, OpenType substitution/positioning, bidi/script shaping; separate milestones for CFF/CFF2, variable/color fonts, vertical text and WOFF/WOFF2. |

Filters use explicit input lifetimes, conservative effect regions and color/alpha
representations. Begin with fully budgeted surfaces and last-use reuse. Tiles,
bands, SIMD and parallel execution follow profiling and exact equivalence tests;
offscreen filter dependencies and kernel halos must not be clipped away to fit.

## 7. Oracle, corpus and measurements

### Oracle and acceptance

Retain current win64 references as exploratory historical evidence. The original
canonical target remains pinned Linux x64, requiring its own archive hash and
fixed environment. Provision it through P1.1, compare old/new captures explicitly
and preserve platform identity. Do not relabel Windows results as Linux or
silently regenerate baselines. Library portability tests remain Windows/Linux,
with ARM64 equivalence added when infrastructure is available.

The BCL-only CDP wrapper needs bounded multi-frame receive, protocol/evaluation
error propagation, startup/command/case/cleanup deadlines, process-tree ownership,
log drainage and failure-path tests. Keep browser sandboxing enabled and debugging
on ephemeral loopback. Verify the executable/runtime version; record OS, backend,
GPU policy, sRGB, locale, fonts, scale, viewport/background and recipe revision.
Reject missing/undeclared resources and failed image/font readiness. Check
readiness inside image context; outer-document font readiness is insufficient.

Generate self-contained fixture variants only to embed the same resource/font
bytes for both engines; record original/materialized hashes. Do not rewrite paths,
outline text or simplify geometry to help the candidate. Require three fresh
captures with identical decoded pixels before admitting a new golden.

Use a fixed inventory with independent semantic support and fidelity statuses:
record implemented/unsupported behavior separately from fidelity outcomes such as
`exact`, `known-difference` and `oracle-failure`. Exact regressions fail;
known differences retain mismatch bounds and reproducers without qualifying as
pixel-perfect. Missing/unstable oracles are failures, never skips. Support errors
must match their expected category. Report counts by family, status and image area.

Produce JSON metrics plus reference/candidate/diff images, alpha diagnostics and a
small HTML overlay/blink report. Preserve dimension, maximum/mean/RMS/channel/alpha
errors and mismatch bounds; add error histograms. Compositing over black/white or
checkerboard is diagnostic only. No alignment, scaling, blur or perceptual score
may change acceptance.

### Corpus and provenance

The current passing examples are a seed, not a complete denominator. Persist
failed circle/gradient probes and the review cases, then add interactions and
representative authored inputs as early as the harness permits. Retain the
original planning targets of at least 200 targeted cases and 10–20 illustrations/
diagrams; those counts are coverage prompts, not release criteria by themselves.

Replace the placeholder fixture license with an actual original-asset dedication;
ensure each manifest's evidence exists. Record stable IDs, source/creator/date,
rights evidence, original/materialized hashes, modifications/seeds, resource/font
hashes, feature tags, options/limits and oracle identity. License repairs affecting
SVG bytes require deliberate hash updates and reference association checks.

Use original CC0 cases for paths, fractional geometry, caps/joins/dashes,
viewports/styles/references, gradients/groups/masks, filters, text and images.
Add large paths, dense edges, deep XML, CSS matching explosions, reference bombs,
many stops/glyphs, numeric extremes, malformed codecs and output entropy cases.
Run dangerous stress workloads in bounded child processes. Admit Ahem and other
font/illustration bytes only with asset-level provenance; broad site policy or a
code repository license is not sufficient evidence for every embedded asset.

### Measurements, CI and package gates

Replace measurement scaffolds during P0/P1. Benchmark parsing, normalization,
geometry, raster/composition, encoding and end-to-end separately, with input setup
outside timed work. Cover owned/caller/stream sync/async APIs and scheduling cost,
small/large inputs, low/high PNG entropy and 32/64/128/256 MiB budget boundaries.
Add filters/text/codecs as implemented. Report allocation traffic, charged peak,
process peak, retention, latency distributions, GC counts, throughput and output
size; allocation bytes are not peak live storage.

Record source/fixture hashes, runtime, OS/CPU/architecture, GC mode and options.
Use BenchmarkDotNet for microbenchmarks and a child-process scenario runner for
memory/cancellation. Start with measured baselines; investigate repeatable >10%
regressions on stable hosts, while deterministic memory/work failures belong in
ordinary CI. Optimize only after locating a measured bottleneck.

Required CI should add full manifest integrity/render gates, dependency and actual
package-content audits, a clean installed-package consumer exercising memory-only
conversion, and a pinned live-Chrome smoke job. Scheduled/manual jobs exercise
full captures, larger scales, fuzz/stress/cancellation and architectures. Retain
failure artifacts for committed cases; do not upload arbitrary user SVGs.

Release requires accurate support/limits/API docs and changelog, reproducible
commands and reports, no hidden runtime dependencies, no missing oracle results
or unclassified failures, and exact pixels across the stated supported corpus.
Validate stream backpressure, ownership, custom memory, concurrent calls and
cleanup. GitHub/NuGet publication remains a separate release operation.

## 8. Engineering and working discipline

Keep the current `.slnx`, `src/`, `tests/`, `tools/`, `benchmarks/`, `docs/` layout,
pinned SDK/locked packages, nullable analysis, deterministic builds and warnings
as errors. Tests reference the production assembly. Restore/Release-test/pack
commands in README remain the baseline developer workflow; add documented oracle,
family/full-corpus, measurement and installed-consumer commands as tools mature.

Follow [the C# guidelines](docs/GUIDELINES_CSHARP.md): mandatory parameters, updated
internal consumers, non-nullable designs without null-forgiving operators, precise
domain types, local single-caller helpers, documented implementor contracts and
source-level narrowly justified friend assemblies. Remove current violations as
their subsystems change and make checks cover internal contracts too.

Use small coherent commits with the behavior, meaningful tests and support
documentation together. Before committing, inspect the staged diff and run checks
appropriate to the change. Each visible family needs independent Chrome evidence
and combination cases; budget-affecting changes need boundary/work checks. Keep
experimental differences in the inventory and retain the findings. Avoid repeated
implement/revert cycles for already-calibrated trivial variants.

Update this plan when dependencies, evidence or product scope change, not merely
to increment a fixture counter. The support matrix must describe actual validated
behavior. Separate scaffold availability, implementation, verification and release
acceptance; none implies the next automatically.

### Historical research leads

The following pins and asset hashes were recorded in the original plan. They were
not fetched or revalidated during this review; they remain research leads, not
build dependencies, newly verified evidence or automatic import permission.

| Research checkout | Recorded revision | Use |
| --- | --- | --- |
| [resvg/usvg](https://github.com/linebender/resvg) | `75b6bbadd7999d0516dcd7153b4a321bfdf8670a` | Parsing/normalization, resources, composition and text architecture |
| [tiny-skia](https://github.com/linebender/tiny-skia) | `5d4754777746eef0828be166896eaf482c49f8f2` | Coverage/stroke/shader architecture; ancestry is not Chrome equivalence |
| [resvg test suite](https://github.com/linebender/resvg-test-suite) | `d8e064337faf01bc5a9579187a56dbdbe3eacc72` | Feature inventory, not our CC0 corpus or Chrome goldens |
| [NanoSVG](https://github.com/memononen/nanosvg) | `239e102ec2c691f2902e20ace2ed36ee4a35cfe6` | Compact parser/geometry architecture, not a completeness target |

Keep `external/` ignored, disposable and unnecessary for build/test/package/runtime.
Inspect source at recorded revisions; do not copy, translate or vendor those
implementations. Pin the matching Skia revision before using it to explain Chrome
behavior. Source-code licenses do not establish artwork/font rights.

| Illustration lead | Intended role | Previously observed SHA-256 |
| --- | --- | --- |
| [World map, molumen, Openclipart 1733](https://openclipart.org/detail/1733) | Long path and edge load | `896e5bc94c9cbca7985880942351e6afdb65486f87a8f0dc7e05a4e2a0af802c` |
| [Tiger, Telemachos, Openclipart 16551](https://openclipart.org/detail/16551/tiger) | Curves, layered fills and composition | `a010007a1654f0ccfd3eb49ddce9ea9be030ebef0f3261907791097faf6cc0ac` |
| [Peaceful landscape, hjkl, Openclipart 315037](https://openclipart.org/detail/315037/peaceful-landscape) | Curves with linear/radial gradients | `95db37bfd005b34846076fb483ea4ab6ceeb891dd5156d8ae955a14c460c867f` |
| [Certificate Border 4, Arvin61r58, Openclipart 302664](https://openclipart.org/detail/302664/certificate-border-4-us-size) | Large authored paths, physical units and gradients | `c6f6acd24d317b5afa1e836d6475b21a3b67b63b39e13bf8ac059bde9d699b89` |

Recheck source/remix provenance and original bytes before admission. Research
fonts or test suites with other licenses must not silently replace the requested
public-domain/CC0 baseline.
