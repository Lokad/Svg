# Lokad.Svg review and implementation plan

Reviewed 2026-09-20 at `a6b6827bad79c91f9fe103f9a56624cfb2421da5`.
This revision records source review and fresh diagnostics, not runtime repairs.
Findings remain open unless explicitly marked otherwise. Historical plans and
repair details remain in Git; their counts/status are not current evidence.

The next product milestone remains useful supplied-font SVG labels, before
gradients, images and filters. An experimental public `<text>`-to-PNG path now
exists, but its semantics, resource limits and pixels are not yet validated.
Repair its correctness contracts and retain an end-to-end regression first;
then admit ordinary curved glyphs through an independent font-aware oracle.

This is a standalone public-library plan: no private application, repository,
service or deployment is required. Keep `external/` optional, ignored and
unreferenced. CI remains deferred at the user's request; remote CI and Linux
oracle admission do not block local work. Publication is a separate action.

## 1. Verified baseline

| Area | Evidence at the reviewed revision |
| --- | --- |
| Build | Locked restore and Release solution build pass with zero warnings/errors; Windows x64, SDK 10.0.401, runtime 10.0.12. |
| Tests | 332 unit cases and 83 visual-project cases pass. Runner counts are not independent feature/corpus counts. |
| Admitted pixels | All 43 fixture-manifest entries compare exactly with retained Chrome references; 41 contain at most 64 pixels, the others are 20×10 and 300×150. |
| Research | 13 separately inventoried probes, including a path-heavy illustration, remain research/unsupported evidence, not supported fixtures. |
| Memory/cancellation | Fresh-process suite passes all 16 scenarios, including three repetitions per XML-reader measurement shape. Background cancellation observes the actual fill loop after allocating its 64 MiB surface. |
| Packaging | Runtime/symbol packages and isolated installed-package consumer pass content, conversion, overlap, dimension-limit and font-catalog checks. |
| Fonts | No tracked TTF/OTF/TTC/WOFF assets. Catalog, sources, snapshots, discovery, scalar layout, simple/composite outlines, scene contours and experimental coverage exist. |
| Not rerun | Live Chrome, BenchmarkDotNet, remote CI and Linux execution. Prior reports remain historical evidence. |

Ignored `artifacts/review-20260920/` retains the review harness, JSON-lines
diagnostics, memory reports, packages and installed consumer. These local artifacts
are not portable regression tests or build dependencies; promote decisive cases
into tracked tests. Probes used public rendering/preparation and selected internal
readers, as distinguished below.

The established subset remains constrained rectangles, axis-aligned lines,
orthogonal paths/point lists, limited styles/positive integer transforms/local
references and one isolated opacity scope. Preserve all six output APIs and the
43 exact fixtures. Previously repaired overlap rejection, dimension-limit error
categorization and opaque-background cancellation remain verified; do not reopen
them merely because an older plan described their original failures.

## 2. Correctness and blocking findings

P0: repair before extending or relying on the exposed text path. P1: required for
useful, supportable labels. P2: targeted design/performance improvement.
“Reproduced” means executed in this review; “inspection” does not claim a measured
failure, latency or memory peak. Each finding includes its closure evidence.

### R01 — Transparent-text blocker explained; regression still missing

The synthetic triangle's second-point flag was `0x13`, not `0x33`. Reproduction
decoded `(10,10), (20,2570), (10,2570)`, not the intended small triangle. At
units-per-em 1000, font-size 100 and baseline y=3, the current 4×4 sampler misses
the thin visible portion and produces transparent pixels. Correcting the flag
gives `(10,10), (20,10), (10,20)`; public conversion of the 5×5 SVG produces
alpha 96 at pixel (1,1). This proves a smoke path, not browser fidelity.

The transparent reproduction therefore does not establish a coverage/source-over
bug or external blocker. Earlier diagnostics without retained runnable assertions
are insufficient evidence. Closure: retain exact fixture bytes and independently
assert complete decoded points, contour closure, scene presence and nonzero PNG
alpha through all six APIs. Add an actual quadratic curve separately. Never change
a desired positive assertion to the observed failure, or remove it to regain green,
without recording the unresolved defect.

**2026-09-20 update — retained smoke chain.** The corrected three-point triangle
fixture is now asserted at each boundary: complete decoded point sequence, closed
outline, retained glyph geometry, nonzero output alpha, adjacent-shape preservation,
and byte-equivalent PNGs through all six public output forms. It remains a synthetic
straight-segment smoke fixture. A separate retained off-curve-point vector now
asserts decoded control/end points and nonzero public PNG output for a true quadratic
segment; a real curved-glyph oracle is still required.

### R02 / P0 — Text consumption skips the next XML sibling

**Public rendering reproduction.**
[SvgRenderedLeafParser](src/Lokad.Svg/Parsing/SvgRenderedLeafParser.cs) uses
`ReadElementContentAsString()`, advancing past the text end;
[traversal](src/Lokad.Svg/Parsing/SvgDocumentTraversal.cs) calls `Read()` again.
Adjacent `</text><rect .../>` loses the rectangle; inserting whitespace renders it.
Adjacent text/group starts are also lost. An adjacent unsupported circle disappears
without an unsupported error. Capturing `textElement` before consumption fixed a
different state check, not this cursor contract.

Closure: explicit reader-position contract; regressions for adjacency/whitespace,
empty/nonempty text, groups, unsupported nodes, comments/CDATA, root/group closure
and trailing malformed input. Assert draw order, group state, node/work accounting
and failure category, not merely successful parsing.

**2026-09-20 update — CDATA sibling regression.** A public supplied-font test now
places CDATA-backed direct text immediately before a rectangle and verifies both the
glyph and sibling pixels. This covers the subtree-reader CDATA path without
weakening the existing all-output-form adjacency test; comments, empty text and
malformed trailing-content coverage remain separate closure work.

### R03 / P0 — Unsupported text attributes are silently ignored

**Reproduced.** The text arm bypasses
[attribute validation](src/Lokad.Svg/Parsing/SvgElementAttributePolicy.cs).
`text-anchor='end'`, `dx='2'` and `clip-path='url(#absent)'` all return unchanged
pixels. These are incorrect successful interpretations, not explicit exclusions.
Whole-element string reading also cannot model nested spans or SVG whitespace/chunks.

Initial corrections: attribute validation now gates the direct text path; `dx` and
`dy`, scalar `text-anchor` `start`, `middle` and `end` are admitted and position
the complete direct run; clipping remains an explicit unsupported failure. Subtree
reading preserves text boundaries but child elements remain unsupported. Direct text
also rejects `evenodd` rather than silently applying its fixed nonzero glyph fill.

Closure: validate text attributes before layout; implement or reject positioning,
clipping, stroke, events, whitespace and child semantics explicitly. Supplying a
snapshot must not weaken fail-closed rendering. Cover both unsupported errors and
implemented behavior.

### R04 / P0 — Text bypasses shared storage/work limits — initial discovery repair 2026-09-20

**Inspection plus allocation/counter probes.** Discovery accepts no render limits
or budget; `MaxCharactersInDocument = svg.Length` is not an independent input cap
or depth/node/attribute/work limit. A non-SVG `<not-svg>` root with a text descendant
is accepted (reproduced). Namespace/eligibility policies differ from traversal.

[Layout](src/Lokad.Svg/Fonts/SvgGlyphLayout.cs),
[outline conversion](src/Lokad.Svg/Fonts/SvgGlyphOutline.cs) and
[run expansion](src/Lokad.Svg/Parsing/SvgTextRunBuilder.cs) allocate lists, positioned
arrays and nested contour/segment arrays without reservations. All glyphs expand
before the first scene insertion. Scene accounting covers outer records, not
retained contour payloads; table dictionaries and outline copies are also uncharged.
Snapshot limits are separate, checked after providers return and verify whole bytes.

Parsing 1 versus 1,000 fixture `A`s consumed 7 versus 23 parser work units.
The 1,000-glyph case allocated 5,778,360 managed bytes with a 406,624-byte ledger
peak. Allocation traffic is **not** live memory: this signals churn, while source
establishes missing reservations. Geometry-record sizing itself is not a reproduced
undercount: x64 size is 128 bytes; a one-element reservation is 160 including the
32-byte array allowance. Referenced arrays are the missing part.

**2026-09-20 update — bounded inline font scans.** The shared direct-text and
discovery parser now scans the `style` span incrementally, charging parser work and
observing cancellation every 4 KiB. It keeps at most the four admitted font values
instead of creating a split-string array and one temporary string per declaration;
each retained property name/value is limited by `MaximumValueTokenCharacters`.
Coverage verifies last-declaration precedence, one normal scan charge and rejection
of oversized values. This narrows inline-style allocation and cancellation exposure,
but it does not budget generic paint-style parsing, glyph/contour storage, or raster
work.

**2026-09-20 update — eliminate direct-text rescan.** Paint resolution now retains
the admitted inline text-font declarations from its existing streaming style scan;
direct text receives that typed result instead of scanning `style` again. The generic
style reader is the sole work/cancellation checkpoint owner, including delimiter
boundaries. A focused integration test verifies that paint and all four supported
font declarations come from the same scan. Stylesheet font declarations and text
inheritance remain intentionally unsupported.

**2026-09-20 update — budget direct-text extraction.** Direct text now accumulates
in a parser-budgeted character buffer. Capacity growth reserves the replacement while
the old backing array remains live, checks cancellation during copies, and transfers
an exact final-string reservation to the glyph-layout scope before releasing the
buffer. Tests cover the overlapping growth denial and final-string release. This
closes the extraction-buffer hole, but positioned glyphs, contour arrays and raster
work still require their remaining aggregate limits.

Glyph raster work now preflights its fixed contour-sampling operations (segments ×
eight subdivisions × samples × covered pixels) and checks cancellation while
traversing segments, including the bounds pass. Remaining raster loops still need
their own operation accounting. Composite depth/component/segment caps do not bound aggregate visits:
repeated empty descendants consume work without adding segments. A 32,768-glyph
per-run cap is not a document-wide cap.

Initial corrections: request discovery now rejects non-SVG roots before acquisition
and applies default input/depth/attribute/node limits; direct extraction caps text
at 32,768 UTF-16 characters across the document before layout, and charges each
subtree reader node to parser work while observing cancellation.
Direct glyph layout now pre-counts runes and reserves its exact positioned-glyph
array through the shared geometry budget, releasing it after direct lowering.
Discovery and direct inline-font-property scans now consume their applicable parser
work limits and observe their cancellation tokens.
Snapshot preparation now preflights distinct declared program sizes and conflicting
same-hash sizes before provider I/O. It remains unbudgeted and does not share
caller-provided render options. Remaining closure: one operation context through preparation, parsing, layout, outlines,
raster and output. Reserve owned capacities and overlapping old/new/nested storage
before allocation; preflight declared acquisition sizes. Bound total characters,
glyphs, graph/table visits, fallback and sample/edge work. Read text incrementally.
Test exact boundaries, early denial, during-phase cancellation and release on failure.

### R05 / P0 — Malformed cmap escapes validation/error categorization — initial repair 2026-09-20

**Internal-reader reproduction.**
[OpenTypeFont.GetFormat12GlyphIndex](src/Lokad.Svg/Fonts/OpenTypeFont.cs) omits
`length < 16`; unsigned `length - 16` underflows. A format-12 subtable declaring
length 0, one group and U+10000 → glyph 1 is accepted. A group U+10000–U+10001 with
`startGlyph = uint.MaxValue` throws raw `OverflowException`, not `SvgException`.
Managed memory safety does not establish table isolation or the failure contract.

Initial correction: format-12 lengths below 16 and group glyph-range overflow now
fail as `MalformedInput`; format-4 lookup now validates every segment's ordering,
sentinel and declared glyph-array range before scalar selection. Retained regression
vectors cover bypassed malformed segments. Remaining closure:
validated table-local ranges and overflow-safe mappings; lengths 0–15,
truncated/overlapping/unordered groups, invalid offsets/scalars/glyphs and arithmetic
boundaries. Malformed data must produce categorized failures, not incidental BCL
exceptions.

### R06 / P0 — Deduplication bypasses selected-program integrity — initial repair 2026-09-20

**Public snapshot reproduction.** Two manifest files with the same SHA-256 and
declared sizes 342 and 343 both enter the snapshot using 342 bytes.
[Snapshot creation](src/Lokad.Svg/Fonts/SvgFontSnapshot.cs) keys by hash and skips
the second binding's verification. Selection order determines which size is checked.
A standalone TTF with `faceIndex=7` is also accepted; parsing always uses face 0.

Initial correction: snapshots now revalidate every selected declaration against
reused bytes, reject nonzero face indices for standalone TrueType, and preflight
distinct declared sizes plus conflicting same-hash sizes before provider acquisition.
Selected nonzero standalone faces are also denied before provider access. Remaining
closure: separate program hash and face
identity; gate standalone face 0 and TTC explicitly. Test aliases, same-hash
different paths/sizes, order independence and unused unsupported faces (retained:
an unused nonzero face does not poison a selected standalone face). Replace
the current dedup test's “valid” standalone face-1 alias with legitimate face-0
aliases; keep unsupported face-index coverage separately.

### R07 / P1 — Public “immutable” containers expose backing arrays — initial repair 2026-09-20

**Reproduced/inspection.** `snapshot.Fonts is SvgPreparedFont[]` is true. Catalog
`Files`, `Faces` and `FallbackFamilies` also expose arrays as `IReadOnlyList`.
Callers can mutate resolution state despite immutable/concurrent-read promises.
Disposal only replaces the snapshot array; escaped prepared fonts retain byte
references, and subsequent lookup becomes missing-resource behavior.

Initial corrections: catalog and snapshot now expose read-only array views rather
than castable backing arrays, with mutation regressions; disposed snapshot access
now fails explicitly, including public prepared handles that escaped before disposal.
Remaining closure: concurrent-read/escaped-handle contracts. Distinguish immutable metadata from borrowed caller bytes and byte-lease
lifetime. Add mutation/disposal/concurrency tests without unconditional program copies.

### R08 / P1 — Preparation and painting implement different text languages — initial cascade repair 2026-09-20

**Reproduced/inspection.**
[Discovery](src/Lokad.Svg/Fonts/SvgFontRequestDiscovery.cs) supports inheritance,
quote trimming and inline font declarations;
[render syntax](src/Lokad.Svg/Parsing/SvgTextSyntax.cs) requires raw direct attributes.
Discovery selects `First` for `font-family: First; font-family: Last`; rendering
cannot find quoted `"Example"` although discovery strips the quotes. Computed style
has no font properties. CSS lists, generic mappings, stylesheet-driven selection and
coverage fallback are missing. Discovery deduplicates case-sensitively; lookup does not.

Initial corrections: preparation now uses last-declaration-wins for repeated inline
font properties, matching the existing inline style parser; direct rendering also
normalizes quoted families. Empty, malformed and `!important` inline font
declarations now have the same categorized rejection in discovery and rendering.
Direct rendering now also retains the admitted root/group font properties (including
inline declarations) as typed inheritance state, matching discovery for the
single-font subset.
Remaining closure:
shared bounded declaration parsing, precedence and typed computed text
style. Retain admitted syntax/style in prepared documents; cross-test inheritance,
inline/stylesheet declarations, quoting/case, namespaces, hidden/inert nodes and
fallback boundaries. A prepared face list is not a prepared SVG document.

### R09 / P1 — Ordinary glyph coordinates and pixel fidelity remain unresolved

**Inspection/reproduction.** Text reuses quarter-pixel rectangle syntax:
font-size 102.4 and x=0.1 fail with rectangle-coordinate errors. Accepted syntax then
encounters `MapEdge`'s quarter-pixel restriction on glyph bounds. Fixed 4×4 sampling
and eight subdivisions per quadratic have neither an independent curved-glyph
reference nor a scale-dependent error bound; they do not prove Chrome fidelity.

**2026-09-20 update — direct text coordinates.** Direct text now uses finite
bare-number/`px` coordinates independently from the rectangle fast path's
quarter-pixel admission rule; output mapping also preserves finite fractional edges.
Syntax coverage includes fractional font size, origin and offsets. This is an
admission correction only, not a claim of reference-quality glyph rasterization.

Closure: general finite text/user/device coordinates alongside the unchanged
rectangle fast path; explicit contour topology, validated bounds and bounded
subdivision. Compare curves, counters, composites, fractional origins/sizes,
viewBox/scale and alpha against pinned same-font references. Preserve exact gates
and negative raster research; do not retune composition without new evidence.

### R10 / P1 — Font/text admission exceeds semantic validation — initial cmap repair 2026-09-20

**Reproduced/inspection.** A format-12-only font mapping BMP `A` fails: format 12 is
consulted only above U+FFFF. Unicode-platform cmap records are not selected; Windows
symbol encoding is treated as a format-4 candidate. Real `name`/`OS/2` face metadata
is not validated. Composite parsing admits XY placement and transforms, but other
flags, offset/metric semantics and instruction boundaries require explicit checks
or unsupported gates. Cycle/depth checks are not full composite conformance.

Layout maps scalars plus advances, without pair positioning, whitespace/chunk
normalization or script eligibility checks. Required `kern`/GPOS behavior can be
silently ignored; Unicode scalar iteration is not universal shaping support.

Initial correction: Unicode format-12 maps now handle BMP and supplementary
scalars; Unicode platform-0 records are accepted, Windows symbol records are not
misclassified, and surrogate values are rejected. Remaining closure: explicit
tested subset, deterministic supported cmap selection (including
BMP in format 12), metadata/composite validation and implemented-or-rejected required
positioning/shaping. Use independent binary vectors and licensed ordinary fonts,
not only synthetic programs constructed around this reader's assumptions.

### R11 / P1 — Diagnostics can replace the primary failure — initial repair 2026-09-20

**Reproduced/inspection.** Missing-family lookup with 257 characters throws
`ArgumentOutOfRangeException` while constructing
[SvgFontDiagnostic](src/Lokad.Svg/Fonts/SvgFontDiagnostic.cs), replacing the intended
`UnresolvedResource`. Accepted family/path lengths and diagnostic bounds disagree.
`Integrity`/`Format` stages exist but are not attached at those failures. Element
identity is empty; unsupported content is a boolean. Some face IDs use path:index
rather than hash:index.

Initial corrections: diagnostic fields truncate to their bounded maximum instead
of throwing, and a 257-character missing family remains `UnresolvedResource`;
snapshot format and integrity failures now retain the primary category with their
corresponding bounded stage. Remaining closure: consistent
admission limits. Include stage, bounded source location/ID and content/face identity,
never full text/font bytes. Test long names/paths/IDs and every failure stage.

## 3. Instrumentation and tests

### R12 / P1 — Measurements do not observe the new text pipeline

| Instrument | Strength | Gap / required extension |
| --- | --- | --- |
| Render budget | Current/peak/denial/category observations and reservation-release tests. | Full-conversion probes do not emit ledger snapshots; resource category is unused and font/run/outline/mask storage untracked. Add bounded per-phase/category/work summaries, including zero-after-release and failure paths. |
| Memory child process | Input hashes, runtime fingerprint, elapsed time, allocation traffic, working set, outcomes; script/tool/renderer fingerprints and timeouts checked by parent. | Add fonts, repeated/distinct glyphs, cold/warm/repeated/concurrent retention and during-font-work cancellation. Allocation traffic and RSS are not live-budget measurements. |
| XML-reader probe | Setup/traversal/attribute-access attribution; three fresh processes per shape. | Text scenario counts nodes without retrieving values; does not measure whole-text extraction/preparation/layout. Keep the conservative allowance and measure actual consumption. |
| Cancellation seam | Background callback starts after allocation and observes an actual loop checkpoint. | Timer/pre-cancel tests do not establish cancellation inside font parse, expansion, layout or coverage. Add phase-start/checkpoint/token evidence, bounded extra work, no wall-clock SLA. |
| BenchmarkDotNet | Four output paths, one rectangle, sizes 1/64/512; setup excluded and allocations reported. | Add parse/layout/outline/raster/encode attribution, real fonts, repeated/distinct glyphs and retention. Measure before optimizing; preserve host/runtime/raw reports. |
| Chrome / visual diff | Bounded transport, blocking before navigation, load/decode wait, exact RGBA and mismatch diagnostics, retained metadata. | No intended-font verification within the SVG image, embedded-font controls or independent metrics. Add missing/corrupt/substitution controls before admitting text. |

The review's background case reported 67,153,632 allocated bytes, cancellation
observed, no output, 56 ms elapsed: one fingerprinted observation, not a guarantee.
Diagnostics must remain bounded and operation-local, not global logging or
per-pixel events. Relevant sources: [budget](src/Lokad.Svg/Rendering/RenderBudget.cs),
[memory runner](tools/test-memory-probe.ps1),
[probe](tools/Lokad.Svg.MemoryProbe/Program.cs),
[benchmarks](benchmarks/Lokad.Svg.Benchmarks/Program.cs),
[capture](tools/Lokad.Svg.Chrome/ChromeSvgCapture.cs).

### R13 / P1 — Green tests do not gate useful text

Preserve strong existing API/PNG/shape tests: non-array memory, overlap/dimensions,
output equivalence, backpressure/write failures, partial output/cancellation,
budget release and exact fixture admission. Do not replace them with one smoke test.

Font tests concentrate on [OpenTypeFontTests](tests/Lokad.Svg.Tests/OpenTypeFontTests.cs),
discovery and syntax. Coverage has only two straight square/hole cases, no actual
curved glyph. The run-builder test expects an empty outline. There is no retained
positive public font-backed text-to-PNG regression, licensed ordinary font,
font-aware reference or labeled chart. Tests accepting standalone face 1 and
namespace-free discovery currently reinforce incorrect contracts.

Add independent expectations at each boundary:

1. Font vectors: complete coordinates, flags/repeats, implied points, contour
   boundaries, transforms, empty glyphs, cycles/expansion and malformed cmap.
2. Parser semantics: R02/R03/R08, empty/CDATA/spans/whitespace, aliases/case,
   real styles, missing glyphs and bounded diagnostics.
3. Public pipeline: corrected smoke fixture through six outputs; curves, holes,
   composites, shapes before/after text, concurrent reads/conversions and offline
   shape-only behavior.
4. Resources: exact aggregate limits, early denial, partial failure, during-phase
   cancellation and repeated-call retention.
5. Independent pixels/metrics: sizes/origins/backgrounds/alpha/viewports, mixed spans
   and a labeled diagram.

Share fixture builders without deriving expected values from production parsing.
Add deterministic mutation/truncation vectors. Counts/line coverage alone are not
completion criteria; retain failing inputs and negative research.

## 4. Duplication, repeated work and weak types

### R14 / P2 — Consolidate responsibilities that already diverge

Inspection findings below warrant targeted changes, not a wholesale wrapper rewrite.
R08 correctness precedes style refactoring; performance changes need measurement.

| Duplication/repeated work | Consequence | Direction |
| --- | --- | --- |
| Eight paint fields remain repeated in `InlineStyle`/`SvgComputedStyle`. Inline paint and text-font parsing now share one streaming declaration reader and checkpoint policy; the direct-text paint scan retains typed admitted font declarations for layout, while discovery and rendering share family/weight/style token normalization. | Wider paint inheritance remains distinct. | Shared bounded parser and computed semantics; retain genuine distinction between raw declarations and computed values. |
| Discovery reparses SVG later and discards structure/style. | Different languages and separate limits. | Prepared-document ownership retaining admitted state and one operation budget; preserve convenience APIs. |
| `AcquireAsync` verifies/discards bytes; snapshot fetches/verifies again; provider overload duplicates acquisition routing. | Repeated provider/hash work in acquire-then-snapshot composition; divergent dedup/diagnostics. | One verified-byte lease/handle path, explicit lifetime, lazy binding and host-owned cache policy. |
| `OpenTypeFont` still reparses directories in cmap, advances, glyph range/header/structure calls; repeated characters decode again. Prepared-font layout and outline decoding now reuse snapshot-validated core metrics. | Repeated dictionaries, scans and temporary arrays. | One validated budgeted program/table view and bounded needed-glyph reuse; benchmark repeated versus distinct glyphs. |
| Flatten contours, transform segments, split by endpoint equality and copy per contour. Direct text now yields one glyph's contours at a time rather than expanding a whole run to jagged storage. | Reconstructs lost topology and duplicates storage. | Explicit contour boundaries and incremental bounded lowering/ownership transfer. |
| `BudgetedSceneList<T>` / `BudgetedParserList<T>` repeat growth/reserve/copy/detach/release. | Lifecycle fixes can diverge; type sizing is supplied separately. | Shared backing-storage lifecycle, preserving specialized policies; test overlapping growth and failure release centrally. |
| Four conversion bodies repeat parse/check/raster lifetime; converter/paint/reference code repeat alpha multiplication. | Future setup/numeric-policy changes require several edits. | Small shared scope/alpha primitive where useful; preserve distinct output ownership, awaited writes and calibrated composition branches. |

Not all repeated tests are waste: dedicated overlap/dimension tests and installed
package smoke protect different boundaries. Share invocation/helpers where useful,
retain independent assertions and names. Do not delete negative raster experiments
or repeat a rejected model without a discriminating hypothesis.

### R15 / P2 — Strengthen invariants where invalid states already matter

| Representation | Risk / direction |
| --- | --- |
| `SvgGeometry`: enum plus all payloads and unrestricted internal positional constructor, unused payloads `default`. | Kind/payload mismatch, default null contour arrays and oversized records. Restrict valid variants/handles; choose compact storage from measured cost. |
| Array-bearing contour/run records; closure reconstructed by equality. | Mutable/unclosed/discontinuous/default records. Use owned/read-only storage, explicit contour ranges and validated topology. |
| Hashes and path:index strings still represent program/face identity. Discovery now uses a typed case-insensitive family/style key rather than string concatenation. | Different equality/normalization and confused request/program/face identity. Use validated request keys/comparers, program hashes and face keys; retain raw names only for bounded context. |
| `Resolution.Resolution` and loosely linked selected/bound/acquired/verified states. | Easy to bypass verification during reuse (R06). Name state/ownership boundaries; associate verified program view and validated face. |
| Text style includes x/y; doubles cross font/user/device units. | Rectangle rules leak into text; transformed finite bounds not guaranteed. Separate computed style, positioning and coordinate-space transforms without wrapping every scalar. |
| Unsupported-content boolean; mutable enum-indexed budget arrays and independent size integers. | Lost cause/location and sizing mismatch. Bounded structured failures, read-only named diagnostics and type-coupled conservative sizing. A cloned snapshot array is not deeply immutable. |

R07 is a concrete public contract defect, not a preference. Other representation
changes should accompany the behavior/ownership slice that needs them, not delay
the first retained end-to-end regression.

### R16 / P1 — Documentation and completion claims have drifted

The previous plan claimed text routing, composite decoding and scene contours were
absent despite current wiring. API/support docs and snapshot XML comments contain
stale/contradictory discovery, font-consumption and immutability statements.
Calling text unsupported in docs does not gate the publicly reachable experiment.

Closure: separate implemented experiments, admitted support, defects and future
requirements. Update API/support docs with corrective slices; record revision,
executed command/result and relevant artifact when closing findings. This review
updates PLAN.md only; companion-document repairs remain scheduled. Do not advertise
ordinary text before semantic/resource/oracle gates pass.

## 5. Actual blockers and execution order

No demonstrated missing permission, private dependency or remote service blocks
local progress. R01 is explained. The remaining blockers are internal engineering
dependencies, not reasons to wait or rerun the same failed diagnostic.

| Order | Bounded work package | Exit evidence |
| --- | --- | --- |
| M0 — honest vertical-slice gate — complete 2026-09-20 | Retained corrected synthetic fixture; repaired cursor and fail-closed text policy (R01–R03); synchronized experimental-status docs. | Public PNG assertion, sibling/attribute/span regressions and six outputs are retained; the full exact suite remains green. No ordinary-text/fidelity claim follows. |
| M1 — bounded trustworthy inputs | Repair cmap, dedup/face validation, mutability and diagnostics (R05–R07/R11); integrate preparation/layout/outline/coverage limits (R04). | Independent adversarial vectors, early denial and phase cancellation, categorized failures and ownership/ledger evidence, not merely more individual caps. |
| M2 — coherent preparation/style/font model | Shared retained syntax/style and validated font subset (R08/R10); reduce measured reparse/copy work alongside ownership changes (R14/R15). | Discovery/painting agree, selected programs acquired once, real styles, memory-only rendering and bounded standalone host demo. |
| M3 — ordinary glyphs/useful labels | Licensed probes, font-aware oracle, general glyph coordinates and coverage (R09/R12/R13). Oracle experiments can start alongside M1/M2. | Same-font metrics, three stable captures, curves/composites/holes/fractional origins, labeled chart and spans via public APIs. Classify mismatches honestly. |
| M4 — representative SVG breadth | General paths/transforms/strokes, paints/composition, then mapped PNG images; extend workloads. | Feature combinations, limits and independent pixels on useful diagrams. |
| Later | JPEG, bounded filter graphs, advanced shaping/scripts/font formats. | Separate codec/filter/shaping gates after useful labels/diagrams. |
| Deferred | Remote Windows/Linux CI, canonical Linux oracle admission and release. | Resume when requested; platform/package evidence and separate publication approval. Not a local M0–M3 prerequisite. |

Make coherent, buildable commits with regressions and relevant docs. New helpers,
wrappers and records are groundwork, not completed milestones. Close findings with
acceptance evidence and update dependencies when a reproduction changes diagnosis.
Do not finish broad geometry/filters before attempting the ordinary-glyph slice.

## 6. Font, text and host requirements retained

### Shared public font-pack contract

Target public Lokad.OoxPdf 0.1.4 manifest compatibility without referencing its
assembly/package/project. Both renderers must use unchanged manifest/font files
from one location: no converted/pruned SVG manifest, duplicated deployment or
required shared runtime. Focused independent font-management adaptation is allowed;
PDF embedding is not glyph rasterization. This is a retained requirement, not a new
upstream audit.

| JSON location | Compatibility |
| --- | --- |
| Root | `packId`, `files`, `families`, optional `fallbacks`; requested ID validation. Convention `<source>/<packId>/manifest.json`, font paths relative to root. |
| Files | `relativePath`, `byteSize`, `sha256`; safe paths, verified selected declarations. |
| Families | `requestedFamily`, `resolvedFamily`, `relativeFontFile`, `weight`, `italic`, `faceIndex`, `hasMathTable`; aliases/multiple faces per file. |
| Omitted fields | Weight 400, italic false, face index 0, math flag false; math metadata implies no SVG math layout. |
| Fallbacks | Ordered `{ "family": "..." }`; omission means empty list; case-insensitive family/alias matching and deterministic style/selection tie-breaks. |

Preserve web-style case-insensitive properties and unknown-property tolerance;
require no new schema/license/source fields. Provenance may be a sidecar. Bound
metadata without rejecting unused unsupported-format entries; reject explicitly
when selected. Pin self-contained conformance fixtures to the public contract:
defaults/aliases/fallback order, unknown metadata, paths, unused TTC and selective
loading. No other renderer checkout is required.

Hosts own locations, credentials, I/O/retry/concurrency and bounded shared caches;
the library owns SVG analysis, ordered family/generic mapping, face selection,
coverage fallback, layout and painting. Prefer real styles; no implicit synthetic
styles or machine fallback. Record substitutions and deliberate policy differences.
Scalar fallback is not complex shaping.

Bounded async preparation acquires only required resources and retains admitted
parse/style state plus byte leases in a disposable prepared document, initially
with explicit single-conversion ownership and shared limits through output.
Raw snapshots may have a separately specified concurrent-read lifetime. Never
synchronously block on acquisition. Avoid unconditional font copies/unbounded
global caches. Identity is hash plus face index; caller bytes stay valid/unchanged.
Shape-only input works offline; required missing faces/glyphs cannot disappear.

Core bundled/required font bytes remain zero. The earlier, not remeasured deployment
example was 12 Windows files (four styles per family): Arial 3,455,912 bytes,
Times New Roman 4,193,740, Courier New 2,856,676; total 10,506,328 bytes
(10.506 MB / 10.020 MiB). Count files, not aliases/faces. This is neither a public
redistributable pack nor parsed-memory estimate. Select/license/hash/measure public
test fonts independently, retaining asset licenses instead of relabeling them CC0.

### First useful text milestone

Horizontal LTR Latin labels, initially ASCII/tested precomposed Latin, using supplied
static standalone TrueType face 0. Validate SFNT/ranges, `head`, `maxp`,
`hhea`/`hmtx`, `cmap` 4/12, `loca`/`glyf`, `name`/`OS/2` metadata.
Include simple curves, implied points and bounded composite placement; gate
unsupported formats/flags.

Preserve text/CDATA/span order and admitted whitespace across boundaries. Support
inherited font/paint/visibility, scalar x/y/dx/dy, alphabetic baseline,
start/middle/end anchors, advances and required pair positioning (`kern` and
explicit bounded GPOS pairs). Test AV, spaces/punctuation, accents/composites,
supplementary scalars, mixed faces/sizes/colors and fallback boundaries.
Implement or reject features affecting output. Add nested `tspan`, spacing and
explicit line positioning for labels.

Position/rotate lists, `textLength`/`lengthAdjust`, more baselines, transformed/
stroked text and decorations need later gates. Initially exclude automatic wrapping,
bidi/joining/complex scripts, unadmitted mark positioning, general GSUB/GPOS, text
paths/vertical text, variable/color/bitmap fonts, CFF/CFF2 and WOFF/WOFF2.

### Host/oracle/corpus acceptance

Demonstrate a standalone host with async bounded nonseekable stream intake,
preserved XML/BOM bytes and growth-time input limits; a public stream-input overload
is not initially required. Host buffers/shared caches are separate budgets. Bound
concurrent loads, isolate caller cancellation and allow retry. Output awaits
backpressure, propagates failures and leaves streams open; hosts own partial-output
cleanup/transactional publication. Include real labels, not only transport tests.

Retain SVG and font hashes, face index, materialized oracle-input hash, browser/
platform/version, recipe, scale, locale/background and font/raster policy. Verify
font use inside the SVG image with missing/corrupt/substitution controls; outer-page
font readiness is insufficient. Require three stable fresh-process captures.
Compare metrics/layout separately from pixels and keep candidates/references/diffs,
bounds and classified findings. A box glyph or single size is insufficient.

Keep exact decoded RGBA8 comparison, ignoring RGB only for transparent pixels.
No tolerance or silent reference regeneration. Separate support and fidelity
(`exact`, `known-difference`, `oracle-failure`) in explicit inventories.
Preserve rejected diagonal/overlap/opacity models as negative evidence; Windows
oracle results are not canonical Linux evidence. Work toward 200 targeted cases and
10–20 representative diagrams/illustrations as coverage prompts, not release quotas.

## 7. Core contract, wider scope and validation

One MIT pure-managed C# `net10.0` library for Windows/Linux: no third-party runtime
packages, native assets/PInvoke, System.Drawing/platform font engine, browser service
or other Lokad runtime dependency. Rendering uses explicit memory resources/options,
no implicit filesystem/network/installed-font access. Asset rights are distinct
from code licenses; third-party implementations are research references, not code
to copy/translate/vendor through `external/`.

Preserve owned-result/caller-memory lifetime, detectable overlap rejection,
non-array memory and all six APIs. Keep categorized malformed/unsupported/
unresolved/limit/insufficient-destination failures and token-preserving cancellation.
Async memory schedules once; async streams await writes, no per-glyph tasks.
Preserve nonseekable streams, output limits and partial-failure behavior.

| Existing default | Value |
| --- | --- |
| Accounted renderer storage | 256 MiB, not process/host memory |
| SVG input / encoded PNG | 16 MiB / 128 MiB |
| Output | 16,384 per axis; 16,777,216 pixels |
| Nodes / cumulative path commands | 100,000 / 1,000,000 |
| Shared work | 100,000,000 units |
| XML depth / attributes per element | 128 / 256 |
| Current snapshot caps, not shared accounting | 256 faces / 32 MiB borrowed distinct programs |

XML allowance remains max(16 KiB, four times input bytes, 512 bytes per configured
attribute limit): conservative BCL allowance, not exact accounting. Distinguish live
owned storage, borrowed bytes, allocation traffic and working set; release is not
GC. Choose measured aggregate font/text/graph/subdivision defaults during M1.

Retain wider scope: nested viewports/units/cascade/reference graphs; general paths/
arcs/circles/ellipses/affine transforms and stroke caps/joins/dashes; gradients/
patterns/clipping/masks/nested opacity/markers; mapped PNG/profiles/resampling/nested
SVG, later JPEG; bounded blur/offset/flood/color-matrix/component-transfer/composite/
blend/merge/shadow filters; advanced text as separately admitted families.
Scripts/events, animation, mutable DOM, foreignObject HTML and general interactive
web content remain outside scope.

Local verification:

```powershell
dotnet restore --locked-mode
dotnet build Lokad.Svg.slnx -c Release --no-restore -m:1
dotnet test Lokad.Svg.slnx -c Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"
./tools/test-memory-probe.ps1 -ResultsDirectory artifacts/memory-review
dotnet pack src/Lokad.Svg/Lokad.Svg.csproj -c Release --no-build --no-restore -o artifacts/package-review
./tools/test-package.ps1 -PackageDirectory artifacts/package-review -ResultsDirectory artifacts/package-smoke-review
```

Use fresh output/package directories per evidence run. Run oracle checks for pixels,
phase/resource probes for limits/cancellation and package consumers for public API
changes. Preserve [benchmarks](docs/BENCHMARKS.md),
[oracle procedures](docs/ORACLE.md) and [raster research](docs/RASTERIZATION_DESIGN.md).
When CI resumes, retain run URL/revision/conclusions and matching artifacts;
`validate-ci-artifacts.ps1` checks consistency, not remote authenticity.
Release requires honest limitations, representative exact cases and platform/package
evidence without hidden font/runtime dependencies.

Follow [C# guidelines](docs/GUIDELINES_CSHARP.md). Retain each failing input/regression,
update relevant API/support documentation and record actual verification on closure.
Prefer complete small behavior changes over more scaffolding without observed output.
