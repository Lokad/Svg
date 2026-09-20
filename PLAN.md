# Lokad.Svg implementation plan

Revised 2026-09-20 after reviewing Lokad.Svg at
`2bce6f5426b44bf98b572a0273175b9e16a463f4`. Font-pack interoperability targets
the publicly documented Lokad.OoxPdf 0.1.4 contract. The requirements below are
planned behavior, not implemented text support or application integration.

This is a standalone public-library plan. Keep documentation, examples, fixtures
and tests independent of private applications and repositories. Describe host
requirements through generic public contracts only; application-specific wiring,
configuration, deployment locations and internal source references belong in the
consuming application's repository, not here. Builds and tests must not require
access to private code or services.

The next product milestone is useful SVG labels: supplied-font `<text>` and
`<tspan>`, backed by bounded font parsing, layout and glyph rasterization.
Text moves ahead of gradients, images and filters. General outline rendering is
a dependency of that milestone; installing a font resolver alone will not enable it.

CI work is deferred at the user's request. A remote workflow URL or downloaded
CI artifacts are not prerequisites for local development, font research or this
milestone. Keep the existing workflow/tools; revisit remote platform evidence
when CI work resumes.

This revision replaces the historical progress diary with a current baseline,
an actionable review and dependency-ordered work. Follow-up implementation status
is recorded against the findings below; text is still unsupported. The earlier R1–R10
audit and detailed repair history remain available in Git history at the
Lokad.Svg revision above.

## 1. Current state and review evidence

Lokad.Svg is a usable renderer for controlled inputs within a narrow subset.
It is not yet suitable for arbitrary exported SVGs or diagrams containing text.

| Area | Current evidence |
| --- | --- |
| Implementation | Pure managed `net10.0` library, with no third-party runtime package dependencies or native rendering/font engine. |
| Rendering | Rectangles with integer/quarter-pixel edges, restricted axis-aligned lines and orthogonal paths/point lists, limited styles and positive integer axis transforms, narrow local `use`/`symbol` support, and one isolated opacity scope. |
| Output/API | Six conversion entry points: owned memory, caller memory and writable streams, each synchronous/asynchronous. Non-array-backed memory and independent concurrent conversions have tests. |
| Missing product capabilities | Text/fonts, general curves/affine geometry, gradients, clipping/masks, images and filters remain unsupported. |
| Exact corpus | All 43 manifest entries are rendered and compared against their retained Chrome references. 41 of these images contain at most 64 pixels; the other two are 20×10 and 300×150. Test count is not representative-asset coverage. |
| Research | 13 retained probes, including a path-heavy illustration, have browser reference evidence and explicit unsupported assertions. They are not supported fixtures. |
| Resource limits | One conversion budget covers parser/scene/geometry/surfaces/PNG storage, with structural/work limits and cancellation checks. XML/BCL allocation accounting remains conservative; this is not a process-memory ceiling. |
| Local verification rerun for this review | Locked restore, Release solution build with zero warnings/errors, 247 unit tests and 82 visual-project tests pass on Windows x64, SDK 10.0.401 / runtime 10.0.12. |
| F01–F03 / initial P1 implementation follow-up | Locked restore and Release solution build with zero warnings/errors; 290 unit tests and 82 visual-project tests pass. The 28 regression cases cover overlap and resolved output dimensions without changing admitted pixel output. The fresh-process memory suite and clean installed-package consumer also pass; the latter checks both repaired public failure contracts. Its opaque-background scenario allocates the 64 MiB surface, begins the loop, then records cancellation at a loop checkpoint with no output. The initial P1 catalog parses/resolves compatible metadata, and its bound caller-owned source verifies byte count/hash while preserving cancellation. |
| Evidence not rerun in this follow-up | Live Chrome captures and benchmarks. Earlier local evidence remains documented; no successful remote CI result is claimed. |

The previous audit's paint inheritance, stylesheet routing, EOF/numeric handling,
named-color and missing fixture-gate defects were repaired. Streaming PNG output,
shared budgeting, bounded CDP transport and package-consumer checks now exist.
Do not reopen these completed items based on the old baseline's descriptions.

Source anchors:
[public conversion](src/Lokad.Svg/SvgConverter.cs),
[options](src/Lokad.Svg/SvgRenderOptions.cs),
[traversal](src/Lokad.Svg/Parsing/SvgDocumentTraversal.cs),
[scene](src/Lokad.Svg/Parsing/SvgRoot.cs),
[PNG](src/Lokad.Svg/Encoding/PngEncoder.cs),
[budget](src/Lokad.Svg/Rendering/RenderBudget.cs),
[public tests](tests/Lokad.Svg.Tests/PublicContractTests.cs),
[fixture gate](tests/Lokad.Svg.VisualTests/FixtureManifestTests.cs),
[research inventory](tests/Research/manifest.json).
The [support matrix](docs/SUPPORT_MATRIX.md) remains the detailed current contract.

## 2. Findings from this review

These findings describe the reviewed baseline; completed corrections are marked
explicitly. "Reproduced" means a public-API probe was run during the review;
"inspection" identifies a source/design gap without claiming a measured failure
or latency.

| ID / priority | Finding and evidence | Planned correction / acceptance |
| --- | --- | --- |
| F01 / P0 — complete | The reviewed revision accepted detectable input/output aliasing. Reproduced: a 2×1 SVG in a 1,024-byte array was overwritten when the same array was used as destination. | Both caller-memory APIs now reject span-visible overlap with `ArgumentException` before parsing/writing. `DestinationOverlapTests` covers exact, containing/contained and partial overlaps, adjacent disjoint slices, arrays and distinct custom managers over shared storage. Caller responsibility remains for aliases the spans cannot expose. |
| F02 / P0 — complete | The reviewed revision returned `UnsupportedFeature` for a valid 2×1 SVG with `MaximumDimension=1`, combining dimension caps with unsupported output geometry. | Resolved integral dimensions exceeding a cap now return `LimitExceeded` before narrowing, allocation or output. `OutputDimensionTests` exercises all six API forms, both axes, viewport/scale, exact boundaries and integer overflow; fractional/non-finite/underflowed output remains explicitly unsupported. |
| F03 / P0 — complete | The reviewed revision had no cancellation checkpoint in nontransparent background initialization; precharged work did not make cancellation observable until later. | Opaque background filling now checks cancellation every at most 4,096 pixels. The fresh-process probe starts its timer only after the 4,096×4,096 background loop begins and records both start and loop-checkpoint observation. On the verified Windows x64/.NET 10.0.12 run it allocated 67,153,624 managed bytes, returned `cancelled` after 46 ms and wrote no PNG. This is during-work evidence, not a latency guarantee. |
| F04 / P1 | The oracle is not ready to establish font identity. `ChromeSvgCapture` creates a target with the SVG-containing page before enabling network blocking, then waits for `img.decode()`; it has no positive check that the intended face was used. Inspection establishes an ordering/readiness gap, not an observed network leak. | Start blank, configure blocking/emulation, then navigate. Add missing/corrupt/font-substitution controls and same-font image-context probes before admitting text goldens. |
| F05 / P1 | No text pipeline exists. Traversal does not retain text runs; rendered-leaf routing has no text case; computed style lacks font/text properties. `Lokad.Svg.Fonts.SvgFontPackCatalog` now independently parses and resolves compatible pack metadata, but is not yet connected to SVG parsing or rendering. | Add bounded text syntax, inherited text style, caller-provided program acquisition and typed glyph runs as separate responsibilities. Preserve text/CDATA order and whitespace across spans. |
| F06 / P2 | The scene/backend cannot draw ordinary glyphs. `SvgGeometryKind` has only rectangle, axis-aligned line and orthogonal band; `AxisTransform` is integer/axis-aligned, and device edges are restricted to quarter pixels. | Introduce general contour/curve and positioned-glyph representations with budgeted bounds and device-space coverage. Keep existing rectangle fast paths and exact fixtures unchanged. |
| F07 / P2 | General raster fidelity is unresolved. The retained diagonal/overlap/layer-opacity experiments reject the tested coverage models; identical font bytes will not resolve hinting or glyph-mask behavior. | Retain negative results and test new discriminating mechanisms. Separate font selection, metrics/layout and raster mismatches. A passing synthetic box glyph is insufficient for ordinary-font support. |
| F08 / cross-cutting | The memory promise needs an explicit boundary for fonts. Existing XML workspace is a conservative allowance, and future font bytes, parsed tables, composite outlines and caches are not yet represented. | Define borrowed versus owned storage, cap font inputs and expansion, charge parsed/glyph caches, and bound host-owned caches separately. Extend existing child-process probes as these allocations are introduced. |
| F09 / P3–P4 | Tests/measurements remain concentrated on tiny primitives. Benchmarks measure one solid rectangle at 1, 64 and 512 pixels per side; there is no working labeled chart, multi-font run or text-heavy retention case. | Add representative label/diagram fixtures and cold/warm font, many-glyph, repeated-call and concurrent measurements. Keep feature/fidelity inventories distinct. |
| F10 / P1–P3 | Unsupported-child reporting often reduces to a boolean and the generic "This SVG child element is not implemented yet." There is no bounded diagnostic record identifying a missing face, glyph or source text location. | Design bounded diagnostics alongside fonts: source location/element ID, requested/resolved family, face identity and failure stage. Never echo an unbounded text node or font data into an error. |
| F11 / planning | The old plan mixed historical failures, completed repairs and next work, and placed CI evidence ahead of product progress while leaving text behind filters. | This revision separates current findings from completed work, brings text forward, and records CI as deferred. |
| F12 / P1–P3 | Lokad.Svg accepts only memory input. A prepared-font snapshot alone would require an embedding application to parse SVG itself to discover the needed fonts, or to preload the entire pack. | Supply a bounded library preparation API that identifies and acquires only required fonts through caller-provided sources. Demonstrate bounded asynchronous stream intake and PNG output in a standalone test host. Keep SVG parsing and font/layout policy inside the library. |

F01–F03 were finite repairs, not a new open-ended hardening phase. They are now
implemented and locally verified. Font contract and oracle work can proceed.
Other corrections below remain planned unless marked complete.

## 3. Font management: share ooxpdf's font packs and manifest

Compatibility with the existing ooxpdf font-pack format is a requirement.
Both libraries must be able to consume the same manifest bytes and font files
from one pack location, without a converted manifest, separate SVG font pool or
duplicate deployment of the font assets. This is data-format compatibility;
Lokad.Svg owns its implementation and public types under `Lokad.Svg.Fonts`.
It must not reference the Lokad.OoxPdf assembly, NuGet package or project.
Focused duplication/adaptation of ooxpdf font-management code is explicitly
allowed; a shared runtime library is not required.

The reviewed ooxpdf implementation uses:

```text
FontRequest -> IFontResolver -> FontFaceResolution
            -> IFontProgramSource -> OpenTypeFont -> embedded PDF font
```

The public font APIs and font-pack contract motivate the following design:

| Mechanism | Observed design | Adaptation for Lokad.Svg |
| --- | --- | --- |
| Selection | `IFontResolver`, `FontRequest`, `FontFaceResolution` and `FontStyleKey` separate requested family/style from the actual face, source, collection face index and fallback flag. | Keep that separation; SVG requests need ordered CSS family lists and explicit weight/style. Record substitutions rather than hiding them. |
| Storage | `IFontProgramSource` provides a stable ID and cancellable `GetBytesAsync`; memory/file sources implement it. | Keep storage-neutral identity and byte access, with documented lifetime, concurrency and ownership. No filesystem path in the text layout engine. |
| Host discovery | `WindowsFontResolver` scans Windows Fonts and Office CloudFonts, reads face metadata, and caches discovery. | Optional host integration only. The portable renderer must work with the same supplied bytes on Windows/Linux without scanning the machine. |
| Portable packs | `OoxPdfFontPackResolver` loads a manifest, resolves aliases/fallbacks, lazily fetches selected files, verifies byte size/SHA-256 and caches successful downloads. | Read that same manifest format unchanged and reuse the same font files. Implement selective acquisition independently, outside rendering; keep packs independent of the core package and offline rendering. |
| Coverage | `FontCoverageFallback` tests Unicode scalar coverage and splits simple text into runs. | Use a deterministic, bounded fallback order for the declared simple-text subset. Do not treat per-scalar fallback as complex-script shaping. |
| Font parsing/output | `OpenTypeFont` reads mapping/metrics/pair positioning; the PDF writer/subsetter embeds TrueType programs. It does not rasterize their outlines into PNG pixels. | Lokad.Svg additionally needs validated outline decoding, layout and glyph rasterization. PDF embedding/subsetting is not a text renderer to transplant. |

The host selects the pack and supplies its location or bytes. Compatibility does
not mandate a particular deployment, endpoint or bundled set of fonts. The
public package documentation's example source is not a distribution commitment.

Adopt the separation of responsibilities while preserving Lokad.Svg's contract:
no synchronous blocking on async acquisition, unconditional font copies,
unbounded shared caches or machine-dependent fallback. Font acquisition and
host discovery remain separate from deterministic, memory-only rendering.

### Manifest compatibility contract

Preserve the existing layout: `<source>/<packId>/manifest.json`, with font paths
relative to that pack root. A caller may supply the manifest and font bytes
directly; an explicit acquisition adapter may use the same HTTP(S) pack endpoint
as ooxpdf. Neither path requires the other library to be installed.

| Existing JSON location | Compatible fields / behavior |
| --- | --- |
| Root | `packId`, `files`, `families`, optional `fallbacks`. Preserve the requested pack-ID check. |
| `files[]` | `relativePath`, `byteSize`, `sha256`. Verify selected programs against their declared size and hash before use. |
| `families[]` | `requestedFamily`, `resolvedFamily`, `relativeFontFile`, `weight`, `italic`, `faceIndex`, `hasMathTable`. Preserve aliases and multiple faces pointing to one file. |
| Omitted face fields | Keep ooxpdf's defaults: weight 400, italic false, face index 0, `hasMathTable` false. Math metadata does not imply SVG math-layout support. |
| `fallbacks[]` | Ordered `{ "family": "..." }` entries; omitted fallbacks mean an empty configured list. Preserve case-insensitive family/alias matching and fallback order. |

The existing manifest has no required schema-version or license/source fields.
Do not make these new requirements for loading a compatible pack. Additional
provenance/version metadata may be optional or supplied in a sidecar; preserve
the existing reader's tolerance for unknown JSON properties and case-insensitive
property names. Keep resource bounds, safe relative-path validation and explicit
integrity failures; compatibility does not require accepting malformed data.

Keep SVG's ordered CSS family lists, generic-family mappings and missing-glyph
policy in its resolver, without rewriting the shared pack. Document any deliberate
selection-policy difference from ooxpdf, especially its final fallback to an
available face; do not promise identical layout or raster pixels merely because
the assets match.

Manifest acceptance and renderable-font support are separate. Retain entries for
TTC faces or other not-yet-supported formats without rejecting an otherwise valid
pack or downloading every file. Report an explicit format limitation when such a
face is selected for rendering. The initial standalone-TrueType subset must work
from the full shared manifest without requiring a pruned SVG-specific copy.

Pin conformance fixtures to the public Lokad.OoxPdf 0.1.4 font-pack contract
documented in its packaged README: unchanged manifest bytes, known font-file
hashes and expected family/style/face selections. Use synthetic manifests and
redistributable test fonts, never a private deployment's manifest or assets. Tests
must cover omitted defaults, aliases, case handling, fallback order, multiple
faces per file, unknown metadata, unused unsupported faces, size/hash failures,
unsafe paths, cancellation and selective loading. Keep those fixtures and
expected results in Lokad.Svg so ordinary build/test/package consumption needs
neither an ooxpdf checkout nor a project/package reference to it. Test font bytes
still need the asset provenance described below.

### Proposed Lokad.Svg contract

Use provisional `SvgFontRequest`, `SvgFontFaceResolution` and font-source/catalog
types; finalize names when implementing their tests. The design is:

```text
Host: explicit memory/file/blob/pack sources
  -> library preparation selects needed faces -> explicit async acquisition
  -> verified immutable font snapshot + prepared SVG
Renderer: prepared SVG + resolved font snapshot
  -> retained styles/metrics/outlines -> positioned glyph runs
  -> glyph coverage + existing paint/composition -> PNG
```

- Keep selection independent of storage. Resolve a CSS family list in order,
  with explicit host mappings for `serif`, `sans-serif` and `monospace`.
  Use real regular/bold/italic/bold-italic faces first. Synthetic styling and
  arbitrary machine-dependent fallback are outside the initial subset.
- Keep async acquisition explicit and separate from rasterization. Provide a
  bounded preparation API in Lokad.Svg that parses SVG text/style requests and
  awaits caller-provided font sources only as needed, including bounded coverage
  fallback. Hosts must not need to duplicate SVG parsing or preload the entire
  pack to create a snapshot. Hosts with prepared bytes can supply them directly;
  the six existing render APIs retain their memory-only font path.
- Preparation returns a disposable prepared document owning its retained parse
  state and font snapshot; define a single-conversion lifetime initially. Carry
  the same memory/work accounting through preparation, layout, rasterization and
  output rather than resetting limits between phases. Bound acquisition by
  declared/verified sizes, retain source-byte leases, and release conversion-owned
  resources on failure/cancellation. Host-owned shared caches remain separately
  bounded. Exact API names are provisional; the preparation capability is required.
- A default font snapshot is empty. Shapes require no fonts. Once text is
  supported, an unresolved required face/glyph fails with `UnresolvedResource`;
  unsupported font formats or shaping requirements fail explicitly. No silent
  disappearance or successful tofu-only result.
- Bind face identity to content SHA-256 plus collection face index, with
  requested/resolved names and style metadata kept separately. Stable IDs aid
  lookup but cannot authorize mixing different bytes under one cache key.
- Require caller bytes to remain alive and unchanged through the operation.
  Accept non-array-backed memory. Avoid an unconditional font copy. Keep parsed
  tables, glyph outlines and masks conversion-local initially; any later
  reusable cache must have an explicit owner, byte bound and eviction policy.
- Extend immutable options and all consumers together. Specify cancellation,
  thread safety, failures and budget responsibility for every provider contract.
  Synchronous rendering must never call `GetAwaiter().GetResult()` on acquisition.

Implement the manifest contract above with bounded metadata and acquisition.
Validate entries, lengths/counts, ambiguous face mappings and path safety before
acquisition; preserve legitimate family/style aliases and multiple face entries.
One file may expose several faces, so count bytes by distinct program, not family
alias or face. Host-owned verified bytes may serve both libraries without a
duplicate font-file deployment; parsed tables and renderer caches remain separate
unless the host explicitly shares an appropriate storage abstraction.

Implementation status: `SvgFontPackCatalog` is the initial independent reader and
selector. It uses the same web-style JSON behavior, field defaults, safe relative
paths, case-insensitive family/alias matching, fallback order and final selection
tie-breaks as the public OoxPdf 0.1.4 contract. `ISvgFontProgramSource` then binds
a selected face to caller-provided asynchronous bytes and verifies the declared
byte count/SHA-256 without copying, caching or doing I/O. It has no OoxPdf
reference and does not parse font programs. The next P1 increment must connect
selected sources to bounded preparation and font parsing.

### Font footprint measured for this plan

The optional example below was measured from installed font files on 2026-09-20;
versions and sizes can differ across machines. MB means 1,000,000 bytes; MiB means
1,048,576 bytes. These are uncompressed file sizes, not package-compressed sizes
or parsed-font/glyph-cache memory estimates.

| Scope | Files | Bytes | MB | MiB |
| --- | ---: | ---: | ---: | ---: |
| Fonts currently bundled in Lokad.Svg | 0 | 0 | 0 | 0 |
| Fonts required to be bundled by the proposed core design | 0 | 0 | 0 | 0 |
| Example host set: Arial, Times New Roman, Courier New; four styles each | 12 | 10,506,328 | 10.506 | 10.020 |

Actual pack size depends on the caller's selection. Count distinct files, not
family aliases or collection faces. Sharing a pack between renderers does not
inherently double stored font-file bytes.

The 12-file example is explicitly:

| Family | Files under Windows Fonts | Bytes |
| --- | --- | ---: |
| Arial | `arial.ttf`, `arialbd.ttf`, `ariali.ttf`, `arialbi.ttf` | 3,455,912 |
| Times New Roman | `times.ttf`, `timesbd.ttf`, `timesi.ttf`, `timesbi.ttf` | 4,193,740 |
| Courier New | `cour.ttf`, `courbd.ttf`, `couri.ttf`, `courbi.ttf` | 2,856,676 |

Measure this example using the lengths of the twelve listed files under
`%WINDIR%/Fonts`. Check Lokad.Svg's tracked font inventory with
`git ls-files '*.ttf' '*.otf' '*.ttc' '*.woff' '*.woff2'`.

No fonts are copied or packaged by this plan change. The 12-file set is a
concrete deployment-sizing example, not a redistributable pack selection.
Public test assets need asset-level provenance and retained license terms;
installed Office/Windows fonts are not automatically admitted to the repository.
A portable public reference pack remains to be selected, pinned and measured in
P1. Original SVG artwork remains CC0; any separately licensed test fonts must be
identified explicitly rather than relabelled CC0 or MIT.

### Generic host integration contract

The public API must support embedding without knowing the consuming application's
architecture, configuration system or storage implementation. The host owns
resource locations, credentials, acquisition policy and cache lifetime; the
library owns SVG analysis, font selection, layout and rendering.

1. Stream intake: current SVG APIs require `ReadOnlyMemory<byte>`. Demonstrate a
   host adapter that reads asynchronously into bounded memory, preserves XML
   encoding/BOM bytes and enforces `MaximumInputBytes` during growth. It must
   support streams without `Length`, `Position` or seeking. Account for its input
   buffer separately from renderer-owned storage; no unbounded buffering or
   string round-trip. A new public stream-input overload is not required initially.
2. Font acquisition: load only selected programs and verify them before caching.
   Bound retained bytes and concurrent loads. Shared acquisition must isolate
   individual caller cancellation, have a host-controlled lifetime and permit
   retry after failure. Hosts may share verified raw bytes between independent
   renderers; parsed caches remain separately owned. Shape-only SVGs must work
   offline without triggering font acquisition.
3. Output and cancellation: await writes to nonseekable streams, propagate
   backpressure and write failures, and leave caller streams open. Pass the
   operation token through all phases; cancellation remains
   `OperationCanceledException`. Document that failures can leave partial output;
   hosts own destination cleanup or transactional publication.
4. Diagnostics and limits: provide bounded, machine-readable resource, integrity,
   feature and limit failures without application-specific DTOs or error codes.
   Hosts choose input/output/concurrency caps appropriate for their consumers.
   Count host input buffers and shared caches separately from conversion storage.

Exercise these requirements in self-contained tests using synthetic manifests,
redistributable font assets, fake acquisition responses and nonseekable streams.
Cover selective loading, retry, concurrent cancellation, output limits, partial
output and a shape-only offline case. P3 must also demonstrate a real supported
font-backed label through preparation and async PNG output; transport tests alone
do not prove text rendering. Application-specific adapters and their wiring tests
belong in their respective consumer repositories.

## 4. Execution order and completion criteria

The priority numbers below supersede the previous P0–P4 work packages. Completed
repair details stay in Git history; unresolved raster evidence stays in
[the rasterization notes](docs/RASTERIZATION_DESIGN.md) and the research manifest.

| Order | Work | Dependencies and exit evidence |
| --- | --- | --- |
| P0 — current contract | F01–F03 complete: overlap rejection, dimension-limit categorization and bounded background cancellation are verified. Preserve the shared budget and current exact corpus. | Complete; no platform-CI prerequisite. Reopen only for a newly reproduced public-contract defect. |
| P1 — fonts and oracle | `SvgFontPackCatalog` and `ISvgFontProgramSource` complete metadata selection plus caller-owned byte-size/hash verification. Implement bounded async preparation and host-neutral diagnostics; select/pin public font probes and repair F04. | Manifest metadata conformance, aliases, defaults, path safety, fallback selection, memory-backed sources, integrity failures and cancellation preservation have tests. Remaining evidence: selective acquisition, missing faces, real styles and no-I/O rasterization. No dependency on another Lokad package or a consuming application is introduced. |
| P2 — outlines and coverage | Add bounded TrueType outline parsing, typed general contours/glyph runs and the coverage/composition needed by real glyphs. Run discriminating font/path calibration early. | P1 supplies exact font identity. Existing 43 cases remain exact. Curved/simple/composite glyphs, holes, fractional origins, size changes and alpha interactions have independent references and classified outcomes. |
| P3 — useful text | Integrate basic `text`/`tspan` parsing, inherited style, positioning, metrics, kerning and painting through the shared scene. | P1/P2. Labeled bars/axes, multiple spans/styles, Unicode scalar handling and missing-glyph cases work through every public output form. Demonstrate the preparation/async-stream path with a standalone test host, without an external application or private service. |
| P4 — useful SVG breadth | Extend general paths/transforms/strokes, then paints/composition and mapped PNG images, guided by representative diagrams. Expand workload measurements. | Reuse the outline/style/resource infrastructure. Each feature needs combinations, limits and independent pixel evidence. |
| P5 — later scope | First filters, JPEG, advanced text/shaping and further static SVG families. Optimize only measured bottlenecks. | After useful text and diagrams. Independent acceptance gates for codecs, filters and script/font-format additions. |
| Deferred — CI/release | Inspect remote Windows/Linux jobs, admit a canonical Linux oracle deliberately, then finish release/platform/package evidence. | Resume when requested. Local development does not wait for this row. Publication remains a separate operation. |

The next implementation sequence is P1's compatible manifest reader/providers and
font-aware oracle, then one end-to-end curved-glyph vertical slice through P2/P3.
P1's first deliverable is
manifest/asset compatibility with
independent tests, before claiming glyph rendering or full text support.
Do not complete every SVG path command, paint server or filter before attempting
that slice. Conversely, an Ahem-style rectangular-glyph demonstration alone must
not be called general text support.

### First text milestone

The first useful target is horizontal, left-to-right Latin labels (ASCII and
admitted precomposed Latin letters) with supplied static TrueType outlines.
Work in these increments:

1. Font data and metrics: validate SFNT table directory/ranges, `head`, `maxp`,
   `hhea`/`hmtx`, `cmap` formats 4/12 and `loca`/`glyf`; validate face metadata
   from `name`/`OS/2`. Decode simple
   contours, implied points and quadratic curves; support bounded composite
   transforms/placement and detect recursive or explosive expansion. Start with
   standalone TTF/face 0; keep face index in identity and gate TTC separately.
2. Layout and painting: preserve text/CDATA and span order, normalize supported
   whitespace rules across element boundaries, and apply inherited font family,
   size, style/weight, fill, visibility and the admitted opacity behavior.
   Implement scalar x/y/dx/dy, alphabetic baseline and start/middle/end anchors
   with font advances and required pair positioning for the admitted fonts
   (`kern` and a bounded, explicitly supported GPOS pair-adjustment subset).
   Normal font features must not be silently ignored: implement the required
   behavior or reject/classify the affected text/font combination.
3. Useful labels: add nested `tspan`, mixed real faces/sizes/colors, spacing and
   explicit line positioning. Validate `AV`, spaces, punctuation, precomposed
   accents/composite glyphs, supplementary scalars, fallback boundaries and
   fractional positions across several sizes and transparent backgrounds.
4. Extend the text contract deliberately: x/y/dx/dy/rotate lists, `textLength`/
   `lengthAdjust`, additional baseline rules, transformed/stroked text and
   decorations each need their own semantic and raster cases.

Initial exclusions include automatic wrapping, bidi/joining/complex-script
shaping, combining-mark positioning beyond the admitted subset, general
GSUB/GPOS, text on paths, vertical text, variable/color/bitmap fonts, CFF/CFF2 and
WOFF/WOFF2. Unsupported constructs must be diagnosed, not approximated silently.
Do not mistake UTF-16 code-unit iteration for Unicode scalar handling, or one
scalar-to-glyph mapping for a complete shaping engine.

The [SVG text specification](https://www.w3.org/TR/SVG2/text.html) is the semantic
reference for chunks, positioning, whitespace and text painting. The
[OpenType glyf specification](https://learn.microsoft.com/en-us/typography/opentype/spec/glyf)
is the reference for simple/composite outlines. Browser evidence is still needed
for the actual font, metrics, hinting and raster behavior.

### Text-specific limits and tests

Choose measured defaults during P1/P2 for font-program bytes, distinct faces,
text length, glyph count, fallback attempts, table records, composite depth/
components, outline points and subdivision. Enforce their aggregate memory/work
cost as well as individual caps before allocation or expansion. Do not expand a
whole font's pair-positioning classes or glyph collection for a short label.

Borrowed font bytes remain caller storage but obey total font-input limits.
Owned copies, parsed tables, runs, outlines and masks stay charged for their
actual overlapping lifetimes. Acquisition/cache budgets outside conversion must
be explicit; the existing 256 MiB default is not a budget for an entire host.

Add malformed/truncated font, cyclic composite, invalid table range, missing
glyph, many-face/many-glyph, cancellation-during-parse/raster, concurrent snapshot,
output equivalence and repeated-call retention cases. Test parser logic with
independent vectors and visual output with an independent oracle.

## 5. Fidelity and corpus strategy

Keep exact decoded RGBA8 comparison for admitted fixtures, ignoring RGB only
where alpha is zero. Do not introduce tolerances or silently regenerate references
to make text pass. Track support status separately from `exact`,
`known-difference` and `oracle-failure`; extend the current exact-only fixture
gate or add a separate explicit text-research inventory.

The earlier coverage experiments are useful negative evidence. Pixel-centre,
supersampling, analytic-area and tested fixed-edge/alpha models failed across
different phases. Disjoint semi-opaque probes locate a residual before
source-over, so another compositing-rounding adjustment is not a sufficient new
hypothesis. Glyph hinting, mask generation and ordinary path rasterization may
take different routes; reusing contour geometry does not prove pixel equivalence.

For each new font probe:

- Retain the original SVG and font hashes, face index, settings and materialized
  oracle-input hash. Supply exactly the same font bytes to both engines.
- Use a self-contained image-context reference with explicit embedded font data
  where supported. Verify that the intended face loaded; include a visibly
  different fallback/missing-font control. Outer-page `document.fonts.ready`
  alone does not establish readiness inside an SVG image.
- Record browser/platform/version, capture recipe, scale, locale, background and
  font/raster policy. Require three stable fresh-process captures for admission.
- Compare layout/advances separately from final pixels to isolate selection,
  shaping, baseline, coverage, hinting and compositing failures. Keep candidate,
  reference, diff, mismatch bounds and quantitative findings.
- Prove ordinary curved glyphs and a labeled diagram, not just a box-glyph font
  or a single successful font size. An unresolved experiment stays classified
  and does not expand the advertised support matrix.

Local Windows oracle work can proceed now. Keep its platform identity; do not
call it canonical Linux evidence. If exact text coverage remains unresolved,
record the concrete blocker and continue independent font/parser/layout work
rather than repeatedly tuning a rejected model or waiting for remote CI.

Grow toward the retained coverage goals of 200 targeted cases and 10–20
representative illustrations/diagrams. These are coverage prompts, not release
criteria. Fonts, resources, license evidence and option/limit settings belong in
the inventory; a high count of tiny passing images does not establish breadth.

## 6. Contract and wider scope retained

Ship one MIT-licensed, pure managed C# `net10.0` library for Windows/Linux.
No third-party runtime packages, native assets, P/Invoke, platform drawing APIs,
browser service or dependency on another Lokad package. BCL implementations are
allowed; `System.Drawing` must not return.

Rendering consumes static SVG and explicit resources/options from memory, with
no implicit filesystem, network, installed-font or temporary-directory access.
Host acquisition is separate. Preserve owned-result lifetime, non-array memory,
caller ownership and detectable-overlap rejection. Streams remain open and
support backpressure/nonseekable destinations; failure may leave partial output.
Memory async APIs schedule once; async streams await writes; no task per glyph.

Preserve categorized malformed-input, unsupported-feature, unresolved-resource,
limit-exceeded and insufficient-destination failures. Cancellation uses the
supplied token. Keep diagnostic size/count bounded.

| Existing default | Value |
| --- | --- |
| Accounted renderer storage | 256 MiB |
| SVG input / encoded PNG | 16 MiB / 128 MiB |
| Output | 16,384 per axis; 16,777,216 pixels |
| Nodes / cumulative path commands | 100,000 / 1,000,000 |
| Shared work | 100,000,000 units |
| XML nesting / attributes per element | 128 / 256 |

The XML-reader allowance is the maximum of 16 KiB, four times input bytes and
512 bytes per configured attribute limit. Retain its existing fresh-process
checks and documented limits; do not call it exact BCL accounting. Check
cancellation throughout long phases and distinguish live renderer storage,
managed allocation traffic and process working set. Account for capacity and
old/new buffers during growth; a reservation release is not garbage collection.

The wider static-SVG backlog remains:

| Family | Subsequent work |
| --- | --- |
| Structure/style | Nested viewports, sizing/units, broader cascade/selectors, typed reference graph, forward references/cycles and shared instances. |
| Geometry/strokes | Relative/repeated path grammar, subpaths, curves/arcs, winding, circles/ellipses/rounded rectangles, full affine transforms, caps/joins/dashes and non-scaling strokes. |
| Paint/composition | Linear/radial gradients, patterns, clipping/masks, nested opacity, markers, paint order, effect bounds and reference limits. |
| Images | Explicit host maps/data URLs, PNG decoding/profiles/resampling and nested SVG; JPEG as a separate codec milestone. |
| Filters | First blur/offset/flood/color-matrix/component-transfer/composite/blend/merge/shadow set, then further primitives; explicit graph/surface lifetimes and color spaces. |
| Advanced text | OpenType shaping, bidi/complex scripts, text paths/vertical layout and additional font formats, each separately bounded and verified. |

Scripts/events, animation, interactive state, mutable DOM, `foreignObject`
HTML layout and general web content remain outside scope. Keep `external/`
optional, ignored and unreferenced; third-party implementations are research
references, not code to copy/translate/vendor. Asset rights are separate from
source-code licenses.

## 7. Validation, deferred CI and working discipline

The local baseline remains:

```powershell
dotnet restore --locked-mode
dotnet build Lokad.Svg.slnx -c Release --no-restore -m:1
dotnet test Lokad.Svg.slnx -c Release --no-restore -m:1 --logger "console;verbosity=minimal"
```

Run targeted oracle checks for visual changes, the existing child-process memory
suite for budget/cancellation changes, and installed-package checks for public
API/packaging changes. Preserve [benchmark methodology](docs/BENCHMARKS.md);
extend it to parse/layout/raster/encode and font cold/warm/retention workloads.
Investigate repeatable regressions on stable hosts; do not promote the current
rectangle ShortRun into a broad performance guarantee.

CI provisioning, remote run inspection and Linux baseline admission are deferred.
Existing `test-chrome.ps1`, `test-memory-probe.ps1`, `test-package.ps1` and
`validate-ci-artifacts.ps1` remain available. When resumed, retain the successful
run URL/commit/conclusions and matching `chrome-smoke-*` / `memory-probe-*`
artifacts. The validator checks content consistency with the matching revision
and built tools; it does not authenticate a remote run. Detailed commands remain
in [the oracle documentation](docs/ORACLE.md).

Release still needs verified platform/package behavior, representative exact
cases, documented limitations and no hidden runtime/font dependencies. Defer
publication until that evidence exists, without making it a prerequisite for
implementing the next local feature.

Follow [the C# guidelines](docs/GUIDELINES_CSHARP.md). Make coherent, buildable
changes with meaningful tests and support documentation. Refactor only when a
font/text/geometry responsibility requires its own behavior and ownership; avoid
more wrappers around the unchanged rectangle subset. Update this plan when a
finding is resolved or a dependency changes, and update the support matrix only
when behavior is implemented and verified.
