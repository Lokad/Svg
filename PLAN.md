# Lokad.Svg review and implementation plan

Reviewed 2026-09-20 against `ebe9fa4586a0d619ccfd93ea037c562586f19275`.
This is a source/test/instrumentation review and plan refresh, not a runtime repair.
It supersedes the earlier `a6b6827` baseline and appended progress notes.
Finding IDs R01–R16 are retained for continuity; a repaired reproduction does not
close its broader feature or resource gate.

The next product milestone remains useful supplied-font SVG labels, before
gradients, images and filters. The public experimental direct-text path exists
and has positive regressions. It is not yet a bounded, semantically validated,
browser-calibrated text renderer. Some new findings also affect established shapes.

No missing permission, private dependency or remote service currently blocks local
work. Keep `external/` optional, ignored and unreferenced. Remote CI remains
deferred at the user's request; Linux oracle admission and publication are separate
gates, not prerequisites for the local repairs below.

## 1. Fresh verification and evidence boundaries

| Area | Result at the reviewed revision |
| --- | --- |
| Environment | Windows x64, SDK 10.0.401, .NET 10.0.12. Clean tracked worktree before this documentation edit. |
| Build | Locked restore and Release solution build pass: zero warnings/errors. |
| Tests | 387 unit cases plus 83 visual-project cases pass; zero skipped. Counts are not independent feature/corpus counts. |
| Pixels | The manifest test renders and exactly compares all 43 retained references. 41 fixtures contain at most 64 pixels; the others are 20×10 and 300×150. No fresh browser capture was performed. |
| Resources | All 16 child-process memory scenarios pass, including three repetitions per XML measurement shape and cancellation observed inside background initialization. These probes do not establish text-pipeline accounting or failure-time ledger release. |
| Packaging | Runtime/symbol package checks and an isolated installed-package consumer pass conversion, overlap, dimension-limit and font-catalog checks. No publication performed. |
| Fonts/research | No tracked TTF/OTF/TTC/WOFF assets; 13 separately inventoried research probes remain outside admitted support. |
| Not rerun | Live Chrome, Linux execution and remote CI. Historical results are not fresh verification. A fresh Windows BenchmarkDotNet `ShortRun` completed for the four rectangle output APIs at 1/64/512 pixels; its ignored raw reports are shape-only and do not measure fonts, repeated/distinct glyphs, or phase attribution. |

Fresh local diagnostics are in ignored `artifacts/review-20260920-refresh/`:
`Review.csproj`, `Program.cs`, `results.jsonl`, `memory/`, `package/` and
`package-smoke/`. The memory summary run ID is
`caa808ce2d2849b9a18b810e5333f489`; it retains runtime and source/binary/input hashes.
The review harness uses public APIs and, where explicitly identified, existing
internal access plus reflection into the private rasterizer. It does not alter
production code. These artifacts are local investigation aids, not portable tests:
the fresh benchmark raw CSV/HTML/Markdown reports are in ignored
`BenchmarkDotNet.Artifacts/results/` (Windows x64, .NET 10.0.12, ShortRun with
three warmups and three measurement iterations).
promote the decisive vectors/assertions into tracked tests with each repair.

Evidence labels below mean: **reproduced** in this review, **inspection** from
current code, or **retained regression** executed by the fresh suite. Allocation
traffic is not live memory; a ledger leak is not proof of permanent heap retention.

## 2. Reconcile completed work before extending the plan

| Finding | Current status and remaining boundary |
| --- | --- |
| R01: transparent triangle / missing positive text path | Original blocker closed. The corrected synthetic flag is `0x33`, not `0x13`. Tests retain decoded points, closed contours, scene presence, visible PNG pixels and equivalent results through all six APIs. A separate off-curve quadratic smoke test exists. Neither is an ordinary-font oracle. |
| R02: text skips its next sibling | Original cursor defect repaired with subtree consumption. Immediate rectangle adjacency through six APIs and CDATA adjacency pass. Complete the empty-text, group, comment, unsupported-sibling and malformed-tail matrix; do not describe the old bug as still reproduced. |
| R03: silent text attributes | Attribute policy now gates direct text; scalar dx/dy and start/middle/end anchors are implemented. Child elements, clipping, strokes, nonidentity transforms and evenodd text fill are rejected. Whitespace/chunk semantics and broader admission remain open. |
| R04: text/resource bypasses | Partial. Text extraction and exact positioned-glyph storage are reserved; 32,768 UTF-16 characters are capped across the document; inline scans are checkpointed. Nested contours, font work, stale record sizes and raster cleanup remain open below. |
| R05: malformed cmap | Format-12 short-length, selected-glyph overflow and full-group validation cases are repaired; format-4 segment and glyph-index payload checks are added. Broader validation and repeated-lookup work remain below. |
| R06: dedup/integrity/face index | Demonstrated defects repaired: conflicting same-hash sizes and selected nonzero standalone faces fail before provider access; legitimate face-0 aliases share acquisition; unused unsupported faces do not poison selection. Repeated hashing remains R14, not a reason to undo these checks. |
| R07: mutable public arrays / disposal | Read-only views, escaped-handle access checks and eight concurrent direct-text conversions are repaired and regression-tested. The public contract now states that disposal must not overlap a read/conversion; escaped prepared-font values fail after disposal. Borrowed bytes are intentionally not copied. |
| R08: discovery/painting disagreement | Quoting, case-insensitive requests, last inline declaration and root/group/link font inheritance have improved. Eligibility, caller limits and retained preparation state still differ. |
| R09: fractional text / fidelity | Finite fractional text syntax and finite coordinate-sum closure are admitted. Shapes retain their mapped quarter-pixel gate; independent curved-glyph fidelity remains open. |
| R10: font semantics | Format 12 handles BMP/supplementary scalars; Unicode-platform records are accepted and symbol records are not misclassified. Composite matrix semantics and broader font validation remain open. |
| R11: diagnostic failure masking | Long fields truncate instead of replacing the primary error; snapshot integrity/format stages are retained. Source location and consistent identity/stage coverage remain open. |
| R12–R16 | Instrumentation, meaningful corpus, repeated work, weak invariants and companion-document drift remain active work, detailed below. |

Preserve the overlap/dimension fixes, stream/backpressure behavior, offline shape
path and 43 exact fixtures. Do not replace independent assertions with current
implementation output or weaken positive assertions merely to restore green tests.

## 3. Active correctness and blocking findings

P0 means repair before extending/relying on the affected public path. P1 means
required for useful, supportable labels. P2 means targeted maintainability or
measured performance work. Each repair needs its own regression and exit evidence.

### R04a / P0 — Storage accounting is incomplete, including established shapes

**Reproduced sizes; inspected allocation sites.** On this x64 runtime:

| Storage | Charged element size | Measured value size | Consequence |
| --- | --- | --- | --- |
| Orthogonal lowering's `BudgetedParserList<SvgGeometry>` | 64 bytes | 128 bytes | Temporary bands for admitted paths/point lists are undercharged. |
| Document `BudgetedParserList<CssRule>` | 96 bytes | 120 bytes | The expanded inline-style payload outgrew its independent size constant. |
| Retained scene `SvgGeometry` | 128 bytes | 128 bytes | Outer record sizing is correct; referenced glyph arrays remain uncharged. |
| Group scope | 96 bytes | 72 bytes | Conservative today; do not replace every allowance with an unjustified estimate. |

Sources: [lowering](src/Lokad.Svg/Parsing/SvgOrthogonalGeometryLowerer.cs),
[document state](src/Lokad.Svg/Parsing/SvgDocumentState.cs),
[scene ownership](src/Lokad.Svg/Parsing/SvgRoot.cs),
[outline conversion](src/Lokad.Svg/Fonts/SvgGlyphOutline.cs).

OpenType point/flag/endpoint/component lists, flattened segment arrays, transformed
copies, contour arrays and per-contour copies are not reserved. Yielding one glyph
at a time removed whole-run temporary accumulation, but every glyph's contours
remain retained by the scene. Its disposal accounts only for outer arrays.

**Reproduced parser probe:** 1,000 triangle glyphs retain 3,000 segments while the
post-parse ledger reports 144,064 bytes, all Scene: exactly the outer geometry and
command arrays. Segment payload alone needs another 144,000 bytes before nested
array overhead. Allocation traffic was 2,970,160 bytes; ledger peak 406,624 bytes.
Those two figures measure different things and must not be subtracted as a leak.

Closure: couple value-array sizing to its element type and retain conservative
array/tracker allowances. Reserve every owned capacity before allocating, including
simultaneously live source/replacement/transformed/nested arrays. Introduce explicit
contour ownership with transfer through lowering → builder → retained scene, and
release on insertion/detach failure, cancellation and disposal. Test denial exactly
at growth/transfer boundaries, not only success under the default budget.

### R04b / P0 — Work limits miss sampling and expensive font traversal

**Reproduced through the internal rasterizer.**
[Coverage](src/Lokad.Svg/Rendering/SvgGlyphCoverage.cs) reports
segments × 8 operations **per sample**, but
[the caller](src/Lokad.Svg/SvgConverter.cs) charges
`bounds + pixels × (sampleWork + 1)` and requests 4×4 samples.
A one-pixel, four-segment square is charged 37 units instead of
`4 + 1 × (4 × 8 × 16 + 1) = 517` under this operation model.
It therefore passes a 40-unit raster limit. Bounds are charged only after their
traversal, and the counting passes themselves have no shared work accounting.

**Reproduced/inspection:** parsing 1 versus 1,000 triangle glyphs consumes 15 versus
31 parser units. Rune/layout/cmap/advance/outline work has no common work counter.
Format-12 group traversal has no cancellation token. Per-glyph depth/component/
segment caps do not bound aggregate composite visits: a branching graph ending in
empty glyphs can consume substantial work without growing the segment count.
Discovery uses default limits and unreserved collections, independently of render
options. Text extraction still materializes `subtree.Value` before its bounded
copy; the stylesheet reader already demonstrates chunked reading.

Closure: define units and checkpoint ownership for one operation context across
preparation, parse, layout, graph/table traversal, bounds, samples and encoding.
Charge before work, count samples in both axes, and reject arithmetic overflow as
`LimitExceeded`. Bound aggregate graph visits even when no geometry is produced.
Use incremental text reads and deterministic during-phase cancellation tests;
per-run caps, timer tests and cancellation checks alone are not work accounting.

### R04c / P0 — Raster failures bypass surface-reservation cleanup

**Reproduced through the internal rasterizer.** `Rasterize` reserves its main
surface, then allocates/fills/renders before returning the disposable owner. There
is no failure cleanup around that interval. The one-pixel case leaves 4 bytes
charged after either a glyph-work denial or cancellation triggered at the
background-start callback. The latter observes the actual background checkpoint.
Successful disposal returns the same ledger to zero.

The conversion-local ledger becomes unreachable after a public failure; this is
an accounting/ownership contract defect, not a demonstrated persistent 4-byte heap
leak. Closure: reserve/allocate under an exception-safe owner, transfer only on
success, and assert category/current bytes return to baseline for allocation,
raster, cancellation and downstream output failures. Existing reservation-unit
tests and RSS probes do not exercise this transfer boundary.

### R05 / P0 — cmap validation still depends on the requested scalar

**Repaired 2026-09-20:** [OpenTypeFont](src/Lokad.Svg/Fonts/OpenTypeFont.cs)
now validates every selected format-12 group before returning a lookup result,
and constrains nonzero format-4 offsets to the aligned `glyphIdArray` payload.
The two decisive malformed-reader vectors are tracked as unit regressions.

- Format 12: a valid first group maps U+0041 to glyph 1, followed by a reversed
  second range 100–99. Querying U+0041 succeeds; querying U+0042 produces
  `MalformedInput`. Early return skips validation of later groups.
- Format 4: a nonzero `idRangeOffset` pointing into the `idRangeOffset` array
  rather than `glyphIdArray` is accepted and returns glyph 0. The range check uses
  the start of the subtable, not the start of the glyph-index payload.

The [OpenType cmap definition](https://learn.microsoft.com/en-us/typography/opentype/spec/cmap)
places nonzero format-4 offsets in the glyph-index array and defines ordered
format-12 ranges. Validation must not become weaker when a lookup hits early.

Remaining closure: retain lengths 0–15, truncation, offsets after payload and
boundary scalars as independent vectors. Require categorized failures and bounded
work/cancellation. Avoid reparsing and revalidating the whole table for every
repeated character.

### R10 / P0 — Composite transforms can produce wrong geometry

**Reproduced internal-reader vector.** A component with file matrix values
`(1, 0.5, 0, 1)` maps point (10,10) to (15,10); it should map to (10,15).
`ReadCompositeTransform` places file-order off-diagonal values directly into
`XY/YX`, but `OpenTypeOutlineTransform.Apply` uses the opposite convention.
The current translation/uniform-scale regression cannot detect this transpose.
The independent convention is in the
[OpenType glyf definition](https://learn.microsoft.com/en-us/typography/opentype/spec/glyf).

**Inspection:** component flags are discarded after parsing. Scaled offsets,
grid rounding, conflicting transform flags, component metrics and the trailing
instruction-length boundary are not fully implemented or explicitly gated.
`name`/`OS/2` metadata and required pair positioning are not validated; scalar
mapping plus advances is not universal text shaping.

Closure: add asymmetric shear/rotation and nested-composition vectors with
hand-computed points; preserve relevant flags in a validated component type.
Implement or reject output-affecting semantics, validate instruction extents even
when hinting is not executed, and explicitly state the static TrueType face-0
subset. Aggregate expansion limits belong to R04b. Ordinary-font acceptance
requires separate metrics and pixels, not just synthetic decode success.

### R09 / P0 for numeric/admission regressions; P1 for fidelity

**Repaired 2026-09-20:** finite attributes `x='1e308' dx='1e308'` now fail
at the text-run boundary with categorized `LimitExceeded`, rather than reaching
layout as an infinite sum and throwing raw `ArgumentOutOfRangeException`.
`checked` does not make floating-point addition overflow-safe; the finite result
of each admitted coordinate sum must be checked explicitly.
[Layout](src/Lokad.Svg/Fonts/SvgGlyphLayout.cs) checks some advances, but text-origin,
anchor, outline-transform and device-mapping arithmetic need one finite-result
policy and categorized failure at the SVG boundary.

**Repaired 2026-09-20:** a 1×1 viewport with `viewBox='0 0 3 3'` and a 1×1
rectangle now returns `UnsupportedFeature`: rectangle edges are checked after
mapping and must remain on the documented quarter-pixel grid. General finite
glyph mapping stays separate from this shape gate.

Remaining closure: preserve all 43 exact cases and check nonfinite intermediate
results before bounds/raster loops. Additional shape admission requires fresh
independent references and matching docs.

Fixed 4×4 sampling and eight subdivisions per quadratic still have no
scale-dependent error bound or same-font curved-glyph oracle. Keep fidelity open
for curves, counters, composites, fractional origins/sizes, viewBox/scale, alpha
and backgrounds; do not declare ordinary text supported from nonzero alpha.

### R08 / P1 — Preparation is not the same document model as rendering

**Repaired 2026-09-20:** `<defs><text font-family='Unused'>A</text></defs>`
does not contribute an `Unused` request when only a rectangle is rendered outside
the definitions. Discovery no longer treats standalone `tspan` as eligible and
inherits direct/inline `display` and `visibility`, matching those renderer boundaries.
Other eligibility rules can still
request resources not needed for painting. Its default input/depth/node/attribute/work
limits formerly could not express caller render limits; the new preparation overload
now accepts and applies caller limits. It still discards the parse and style state.

**Repaired 2026-09-20:** discovery now uses `SvgTextInheritance.Resolve`, the
same direct/inline family, weight, style and size precedence path as rendering.
The declaration scanner and token normalization are shared, while direct text
still parses its final scalar positioning. Stylesheet font properties, ordered
family/generic mapping and coverage fallback remain absent.
Direct text/CDATA is concatenated without SVG whitespace/chunk normalization.

Closure: cross-test preparation and painting for root/g/a inheritance, precedence,
quotes/case, namespaces, defs/metadata, hidden/inert text, empty text and unsupported
children. Specify eligibility before acquisition. Share bounded computed semantics
and retain admitted parse/style/resource state in a disposable prepared document.
Keep preparation async, rendering memory-only, and shape-only input offline.

### R07, R11, R16 / P1 — Lifetime, diagnostics and documentation closure

Read-only collection views and lease access guards now work. Escaped
`SvgPreparedFont` objects still hold readonly byte/source references after lease
invalidation; already-returned `ReadOnlyMemory<byte>` cannot be revoked. Define
what disposal releases versus what caller-retained handles keep alive, and whether
disposal may overlap a read. Test supported parallel reads/conversions and repeated
lifetime/retention; do not add unconditional byte copies or promise GC timing.

Keep bounded primary errors plus phase, content/face identity and source location.
Snapshot and provider diagnostics now both use bounded manifest `path:index` face
identity. Direct text retains its bounded `id` into glyph-stage and render-time
missing-prepared-face diagnostics; source and integrity/format failures during
preparation occur before a direct-text run is materialized, so they intentionally
retain no element ID. Do not include whole
SVG/text/font payloads.
Test long names and each failure
stage, not only source/format/integrity examples.

Companion docs had remained inconsistent: the support matrix's text row required direct
font declarations while its added note described inheritance, and its viewport/
fractional rows disagreed. The inheritance and quarter-pixel viewport wording now
match `MapEdge`. The API contract and snapshot comments now describe the implemented
direct-text discovery subset and caller-limit overload rather than calling discovery a
future phase. Blanket deterministic-reservation claims exceed R04's evidence. Keep
the documentation aligned with the corresponding repairs, distinguishing
implemented experiment, admitted subset, known defect and future requirement.

## 4. Instrumentation, tests, duplication and types

### R12 / P1 — Extend observation to the work now being developed

| Instrument | Preserve | Missing evidence / action |
| --- | --- | --- |
| RenderBudget | Current/peak/category/denial arithmetic and idempotent reservations. | Observe a full conversion at phase boundaries and after cleanup on success/failure; include work consumed/denied and owned vs borrowed storage. Snapshot arrays are mutable copies, not deeply immutable observations. |
| Fresh-process memory tool | Input/hash and runtime fingerprints, timeout, actual background-loop cancellation. | It accepts no supplied-font setup and emits no full-conversion budget ledger. Add repeated/distinct glyphs, cold/warm/repeated/concurrent retention and font-phase cancellation. Distinguish allocated bytes, live accounted bytes and working set. |
| XML-reader probes | Three fresh measurements per text/attribute shape, separate setup/traversal/property-access deltas. | Text nodes are counted without reading their values; this does not measure direct extraction, layout or preparation. Keep the conservative allowance and probe the actual consumption path. |
| Cancellation seams | Background callback observes actual loop entry and cancellation. | Font tests mostly establish pre-cancellation, not interruption during cmap/expansion/layout/coverage. Add deterministic phase/checkpoint evidence and bounded remaining work; no timing SLA. |
| Benchmarks | Four output paths at rectangle sizes 1/64/512, setup excluded, allocation reporting. | No fonts, repeated-vs-distinct text or phase attribution. Measure lookup, hash, parse, layout, outline, raster and encode before optimizing. Retain host/runtime/raw reports. |
| Chrome/diff | Bounded transport, resource blocking, image decode wait, exact RGBA, retained metadata. | No proof that the intended font is used inside the SVG image. Add embedded same-font assets and missing/corrupt/substitution controls before admitting text references. Outer-page font readiness is insufficient. |

The fresh background probe allocated 67,153,672 managed bytes and observed
cancellation with no output in 33 ms. This is one fingerprinted run, not a latency
guarantee, memory-budget proof or font-pipeline result. Keep diagnostics bounded
and operation-local; no global logs or per-pixel events.

### R13 / P1 — Test behavior and failure boundaries, not scaffolding counts

Preserve non-array memory, overlap/dimensions, all six outputs, stream backpressure/
write failure/partial output, PNG and shape tests. Positive text and true-quadratic
smokes now exist; the old “no positive public regression” statement is obsolete.
The run-builder empty-outline fixture still tests an empty glyph, not painting.
Coverage-unit tests remain a per-sample work check and straight square/hole cases.

Prioritize tracked independent regressions for:

1. R04: real record sizes, nested ownership, exact work boundaries (including
   all 16 samples), empty composite expansion, and zero ledger after phase failures.
2. R05/R10: malformed cmap independent of query order; asymmetric and nested
   transforms, off-curve/implied points, contour boundaries, flags and truncation.
3. R02/R03/R08/R09: empty/nonempty text, adjacent shapes/text/groups/unsupported
   nodes, comments/CDATA, group closure and malformed tails; failure category,
   scene/draw order, whitespace, finite arithmetic and shape mapping admission.
4. Public supplied-font conversion: curves/holes/composites, inheritance/styles,
   missing resources/glyphs, backgrounds, anchors, six outputs and concurrent reads.
5. A licensed ordinary-font corpus with independent metrics and a labeled diagram;
   then representative sizes, origins, spans, styles and fallback boundaries.

Share binary fixture builders, not expected values derived from production
decoding. Add deterministic mutation/truncation cases. Keep negative research and
known differences separate from admitted fixtures; no tolerance or silent reference
regeneration. More helpers or passing runner cases do not close a feature gate.

### R14 / P2 — Duplicated logic and repeated work

| Current duplication / repeated work | Bounded direction and evidence needed |
| --- | --- |
| Three font-inheritance paths; paint and font readers repeat declaration validation/dispatch even after sharing the scanner. | Share precedence/admission with table-driven cross-tests; retain the distinction between raw declarations and computed values. Direct text's former second style scan is already removed. |
| Discovery reparses SVG during conversion and throws away style/structure. | Prepared-document ownership under shared limits (R08); avoid a second incompatible language. |
| `AcquireAsync` verifies bytes then returns a resolution; snapshot can fetch again. The bound source hashes on fetch, then snapshot hashes again for every alias. | One verified byte/program lease and validated declaration binding per operation; count provider calls and hash bytes, preserve R06 checks and caller-owned caching. |
| Core metrics now retain validated cmap/hmtx/loca/glyf table ranges, so direct layout and glyph expansion no longer rescan the SFNT directory; cmap validation and glyph decoding still repeat per character. | Extend bounded needed-glyph reuse where evidence warrants it; benchmark repeated versus distinct glyphs, never an unbounded global cache. |
| Flatten → transform → split-by-endpoint-equality → copy per contour. | Carry explicit contour ranges/topology through decoding and transfer bounded storage; avoid reconstructing lost boundaries from coordinate equality. |
| `BudgetedSceneList<T>` / `BudgetedParserList<T>` repeat reserve/grow/copy/detach/release. | Centralize tested backing-storage lifecycle once ownership is explicit; retain distinct work/category policies. Type sizing must not be a separately guessed integer. |
| Four converter bodies repeated parse/check/raster lifetime; alpha multiplication remains repeated across parsing/rendering. | `RasterizedConversion` now owns the shared budget, parsed scene and surface lifetime for all output paths, while preserving each API's validation order and awaited stream encoding. Keep composition branches calibrated before extracting them. |

Repeated tests at API, renderer and installed-package boundaries are not redundant.
Keep independent assertions; share only setup/invocation where it improves clarity.
Do not repeat rejected raster experiments without a discriminating hypothesis.

### R15 / P2 — Strengthen types where invariants already fail

- `SvgGeometry` is an enum plus every payload, with unrestricted construction and
  default/null array states. Prefer validated variants or compact typed handles
  when changing scene ownership; measure record/storage cost.
- Contours/runs expose mutable arrays and reconstruct closure by point equality.
  Carry explicit validated ranges and ownership. Validate continuity and finite
  transformed points before admitting a glyph.
- `SvgInheritedTextStyle` is now a validated reference type, and root, group and
  paintable-element contexts require it in their constructors; zero weight or size
  cannot enter inherited text state. Continue replacing other init-only context
  properties where they conceal required state.
- Font hash strings, path:index diagnostic strings and
  `Resolution.Resolution` blur request, selected face, verified program and lease.
  Use validated identities/comparers at those boundaries; avoid wrapping every
  scalar or forcing an unrelated public-API rewrite.
- Separate computed text style from positioning, and state font/user/device matrix
  conventions explicitly. Bare doubles plus `checked` did not enforce numeric or
  matrix semantics (R09/R10).
- Replace unsupported-content booleans with bounded cause/location when introducing
  diagnostic context. Couple budget categories/sizes to typed owned storage.
- `SvgInlineStyleParser.ParseCore` now requires explicit parser-context arguments;
  audit remaining internal optional parameters and budgeted collections, because the
  public reflection test does not check internal code. Fix touched APIs/callers
  together.

These changes belong with the behavior/ownership repairs that justify them.
They are not a prerequisite for every small regression or an excuse for a broad
wrapper rewrite.

## 5. Execution order and actual blockers

All rows below are **pending** unless the status table above says a specific
reproduction is repaired. Start with S1; do not extend text admission while the
P0 resource and geometry defects remain. Each row is a work package to split into
small buildable commits, each including its tests and relevant documentation.

| Order / dependency | Work package | Required exit evidence |
| --- | --- | --- |
| S1 — complete 2026-09-20 | Correct type-coupled storage sizing and main-surface failure ownership (R04a/c). `BudgetedParserList<T>` now derives each value-array charge from `Unsafe.SizeOf<T>()`, removing stale point/geometry/group/rule/pending-use constants; dictionary allowances remain separately conservative. `Rasterize` transfers its surface reservation only on success and releases it on any intervening exception. | 390 unit plus 83 visual tests pass. New tracked checks cover geometry and CSS-rule array charges and verify zero surface/current ledger bytes after actual background-loop cancellation and malformed-scene failure. The general path and point-list lowerers use the same generic list. Nested glyph contours remain S3. |
| S2 — complete 2026-09-20 | Make current glyph bounds/sample charging exact and preflighted (R04b). Bounds segment work is preflighted and charged before traversal; coverage charges segments × eight subdivisions × 4×4 samples plus one pixel-dispatch unit. `GlyphCoverageProbe` is an execution-context-local entry seam used only for deterministic tests. | 393 unit plus 83 visual tests pass. A one-pixel/four-segment contour charges exactly 517 work units; limits 3 and 516 deny before bounds and coverage respectively; coverage-entry cancellation is observed and returns the surface ledger to zero. Aggregate font/table/graph work remains S5. |
| S3 — complete 2026-09-20 | Budget decoded/transformed/nested contours and define transfer ownership (R04a). Composite expansion now grows its decoded-segment accumulator under reservation, before materializing the exact decoded array; decoded and transformed segment arrays then overlap through contour splitting and retained scene ownership. Retained contours reserve their outer and segment arrays, transfer with scene geometry, and release on scene disposal. Later parser failure, partial scene transfer (geometry detached, command transfer denied), and capacity growth preserve ownership and overlap invariants. | 403 unit plus 83 visual tests pass. The 350-byte regression crosses accumulator growth and decoded materialization before transformed-buffer denial, then proves ledger cleanup. No new public prepared-document API was added. |
| S4 — complete 2026-09-20 | Correct cmap validation, asymmetric composite matrices and numeric/shape admission (R05/R10/R09). Selected format-12 groups are fully validated before lookup returns; nonzero format-4 offsets must be aligned and point into `glyphIdArray`. The OpenType scale01/scale10 file-order mismatch is repaired: an asymmetric scale01 vector maps (10,3) to (10,8), rather than transposing it to (11.5,3). Finite text-coordinate sums report `LimitExceeded` rather than a CLR range exception, while mapped rectangle edges retain the documented quarter-pixel gate. | 398 unit plus 83 visual tests pass. Tracked vectors cover malformed cmap, matrix order, overflowing coordinates and non-quarter shape mapping. All 43 shape references pass. |
| S5 — in progress | Aggregate font/table/graph work and phase observations (R04b/R12). Every visited glyph in decoded expansion consumes the shared conversion counter before header/empty/composite handling; simple-glyph contour endpoints, flag runs, coordinates and contour lowering plus composite component records now consume that counter too. Both a direct empty child and the second empty leaf of a branching composite graph are denied at their actual visits. The branching vector records exactly seven visits/records and zero retained geometry bytes after both success and denial. Direct-text layout charges cmap encoding records and format-4/12 groups or segments, lookup scans and advance reads to the shared counter; snapshot metrics retain validated cmap/hmtx/loca/glyf table ranges, removing repeated SFNT directory scans from layout and glyph expansion. The one-glyph format-4 path now needs exactly 6 units and releases positioned-glyph storage when denied at 5. SVG-driven preparation shares its caller work limit from discovery through SFNT metric parsing and chunked integrity hashing. That hashing now runs in the same 4 KiB checkpoint size as byte work, making cancellation observable between actual appended hash chunks through the execution-context-local `FontIntegrityProbe`; a padded valid-font regression cancels after its first chunk and before the second. `GlyphExpansionProbe` and `CmapLookupProbe` likewise prove cancellation at actual expansion and lookup checkpoints. Direct text is consumed through a budgeted 4 KiB `ReadValueChunk` buffer and span append, rather than materializing a whole XML node value. | 441 unit plus 83 visual tests pass. Next: remaining font/table phase observations. |
| S6 — in progress | Coherent discovery/style/preparation and verified program lifetime (R07/R08/R11/R14). SVG-driven preparation has a caller-limits overload; discovery applies those structural/work limits and follows renderer eligibility: only direct-root or `g`/`a`-descendant text can be considered. It therefore excludes nested `svg`, inert `defs`/`title`/`desc`/`metadata` (retaining an outer inert boundary through nested inert elements), standalone `tspan`, `symbol` and arbitrary unsupported containers, text subtrees with unsupported child elements, text under nonidentity transforms, and text hidden by inherited direct/inline `display` or `visibility`. Hidden and direct/inherited nonpainting text now complete text syntax/size validation and bounded extraction but exit before snapshot lookup/layout, so their empty preparation result can be converted. Unsupported render-path siblings clear and suppress requests, so they cannot trigger a provider before their eventual conversion failure. Discovery now retains and resolves the renderer's bounded root stylesheet rules using the same parser, selector cascade and leaf-ordering gate, so stylesheet-hidden/nonpainting text is skipped and late stylesheets reject before acquisition; it also performs root/group/link attribute validation and rejects their inline or stylesheet fill-rule presentation before acquisition. Direct untransformed text reuses the renderer's attribute and default-fill-rule policies before provider acquisition, and shares renderer font inheritance including invalid ancestor font-size handling. Root, group and paintable contexts now require validated inherited text state rather than an init-only default-prone struct. A bounded text `id` now reaches glyph-stage and render-time missing-prepared-face diagnostics. The full preparation path proves inert definitions, unsupported text children, transformed text, stylesheet-hidden/direct-or-root/group/stylesheet-inherited-nonpainting text and unsupported sibling content trigger zero provider acquisition, while unsupported text attributes, nondefault fill rules and inline or stylesheet unsupported group presentation are rejected before acquisition; one immutable supplied snapshot is proven safe across eight concurrent direct-text conversions. All six output APIs now use one owned parse/check/raster scope, eliminating duplicated lifetime paths without changing their validation order or stream-await behavior. | 450 unit plus 83 visual tests pass. Next: remaining eligibility, preparation/painting agreement and bounded diagnostic context. |
| S7 — blocked on an approved font asset; admission after S4–S6 | Licensed ordinary fonts, explicit font semantics and font-aware oracle (R09/R10/R12/R13). The repository has Chrome/PNG oracle tooling and shape references, but no tracked font program, font-pack manifest, or font-bearing fixture with license/provenance; system/browser fonts cannot establish intended-font controls. | Supply an approved font program plus compatible manifest and provenance. Then create three fresh-process intended-font captures; compare metrics and pixels separately for curves/holes/composites/fractional origins; then real styles, pair positioning, spans/fallback and a useful labeled diagram via public APIs. |
| Each slice | Synchronize API/support docs and update this plan (R16). The direct-text inheritance and caller-limit wording is synchronized with the implemented subset. | Record revision, command/result and regression/artifact; replace stale status instead of appending contradictory history. |

Milestone interpretation:

- M0, experimental vertical slice: original triangle/cursor/attribute defects have
  concrete repairs. This is not closure of all parser semantics or documentation.
- M1, bounded trustworthy inputs: S1–S5, plus explicit lifetime/failure contracts.
- M2, coherent supplied-font preparation/style: S6 and the admitted semantic subset.
- M3, useful ordinary labels: S7's metric, pixel, resource and public-host gates.
- M4, wider useful SVGs: general geometry/transforms/strokes, paints/composition,
  then mapped PNG images; JPEG, filters and advanced shaping follow separate gates.
- Deferred: remote CI, canonical Linux oracle and publication. No current private
  dependency or external permission prevents S1–S6 or local S7 investigation.

The missing licensed-font oracle is an acceptance dependency, not evidence that
contour accounting or validation must wait. Likewise, a clean build is a baseline,
not closure of reproduced defects. If a probe changes the diagnosis, update the
specific finding and dependency; retain its input and do not restart the review.

## 6. Font, text and host requirements retained

### Shared public font-pack contract

Target public Lokad.OoxPdf 0.1.4 manifest compatibility without referencing its
assembly/package/project. Both renderers must use unchanged manifest/font files
from one location: no converted/pruned SVG manifest, duplicated deployment or
required shared runtime. Focused independent font-management adaptation is allowed;
PDF embedding is not glyph rasterization. This is a retained requirement, not a new
upstream audit. This review did not re-audit Lokad.OoxPdf or alter that contract.

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

Commands executed for this review (results in section 1):

```powershell
dotnet restore --locked-mode
dotnet build Lokad.Svg.slnx -c Release --no-restore -m:1
dotnet test Lokad.Svg.slnx -c Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"
./tools/test-memory-probe.ps1 -ResultsDirectory artifacts/review-20260920-refresh/memory
dotnet pack src/Lokad.Svg/Lokad.Svg.csproj -c Release --no-build --no-restore -o artifacts/review-20260920-refresh/package
./tools/test-package.ps1 -PackageDirectory artifacts/review-20260920-refresh/package -ResultsDirectory artifacts/review-20260920-refresh/package-smoke
```

The ignored diagnostic harness was also executed with
`dotnet run --project artifacts/review-20260920-refresh/Review.csproj -c Release`;
its final repeat used `--no-restore`, with output retained in `results.jsonl`.
This harness is not required for a clean checkout: S1–S4 must promote its decisive
vectors into tracked regressions. Markdown local links and `git diff --check`
were checked after the plan rewrite.

Use fresh output/package directories for subsequent evidence runs. Run oracle checks for pixels,
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
