# Lokad.Svg review and implementation plan

Reviewed 2026-09-21 against `86fb21c`, using an isolated source archive for builds,
tests and diagnostic reproductions while another agent works in this checkout.
This is a plan-only review, not an implementation change. Finding IDs R01–R20 and
execution stages S1–S7 remain stable; this refresh adds R21–R23, sharpens R04/R15/R18,
and raises R20's reproduced stream-buffering defect above speculative optimization.
Preserve the completed R17 collection repair and the R18 sharing through `7f9d136`.
The other agent's R19 baseline commit `c7225d1` landed during this review; its
documented measurements are acknowledged below, not presented as our rerun.

## 1. Current assessment and verification

Lokad.Svg is a pure-managed, dependency-free .NET 10 SVG-to-PNG renderer with a
narrow, regression-tested shape subset and an experimental supplied-font text
pipeline. It is not yet a general SVG renderer or a validated ordinary-font label
renderer. The next milestone remains useful supplied-font labels before gradients,
images and filters.

No missing authorization or private dependency blocks local progress. The user has
authorized private testing with the existing OoxPdf font pack and unchanged
manifest. Initial access was verified on 2026-09-20; that is not Svg rendering or
browser-fidelity evidence. Keep all source addresses, assets and platform content
out of this repository; section 6 preserves the complete confidentiality contract.
Keep `external/` optional, ignored and unreferenced.

| Verification and provenance | Result and boundary |
| --- | --- |
| Environment / fresh build | Windows x64, .NET 10.0.12; locked restore and Release solution build pass at `86fb21c`, zero warnings/errors. |
| Fresh tests | 695 `Lokad.Svg.Tests` + 6 `Lokad.Svg.FontProbe.Tests` (= 701 unit) pass. The initial archive run passes 81/83 visual-project cases: two manifest tests fail because CRLF materialization changes SVG hashes. After normalizing only the isolated copy's 56 declared SVG assets to their manifest-matching LF bytes, all 83 pass, zero skipped (R22). These are runner counts, not feature or independent fixture counts. |
| Retained pixels | All 43 admitted references pass exact comparison. The corpus remains shape-only and mostly tiny: 41 fixtures have at most 64 pixels; the others are 20×10 and 300×150. No fresh browser captures. |
| Fresh resource probes | All 21 child-process scenarios pass at `86fb21c`: 16 conversion/XML-reader scenarios including three fresh-process measurements per XML shape, plus five font-discovery ledger scenarios (deep groups, denied budget, empty texts, distinct requests, cancellation). Run ID `07b55ddec5044ec4a8711876361e4500`. Pack programs stay synthetic non-font bytes; no ordinary-font scenario is present. |
| Fresh diagnostic harness | Confirms prepared-scene retention after disposal/conversion (R21), 3,508 versus 4 PNG IDAT chunks (R20), root-count disagreement (R18), mismatched generic scene sizing (R15), and discovery without the lowering pass's XML workspace allowance (R04). Diagnostic reproductions are not yet tracked regression tests. |
| Targeted S3/S4 tests | Synthetic tracked tests observe reservation after each simple/composite decoder phase, deny one byte before each observed temporary-array boundary, and prove cleanup. Repeated or post-disposal contour transfer now fails. Huge finite text, viewBox/device, coverage sample and winding-cross-product calculations fail as `LimitExceeded` at their owning boundary. |
| Historical package/installed-consumer check | Earlier 2026-09-21 Release package and symbols passed archive checks plus isolated restore/build/run of conversion, overlap, dimension-limit and font-pack catalog scenarios; not rerun here. |
| Benchmarks, not rerun here | Earlier ShortRun measured four rectangle output forms at 1/64/512 pixels. Concurrent commit `c7225d1` adds attribute-heavy conversion, styled-text discovery and isolated hash baselines; see R19 and `docs/BENCHMARKS.md`. Full font acquisition/layout/outline and repeated/distinct glyph attribution remain absent. |
| Historical Chrome smoke | Earlier cached pinned Chrome passed offline verification and three fresh-process exact captures of the fixed non-text fixture. This is not font fidelity evidence; not rerun here. |
| Not rerun | Package, benchmarks, browser captures, private font acquisition, OoxPdf tests, Linux and remote CI. Earlier successes are historical, not fresh evidence. |

Fresh review artifacts and the diagnostic `artifacts/review/Review.csproj` live
only in the isolated `%TEMP%/svg-review-e56d6b94fa7a4e7d8e12a08941f88b45/source/`
copy. Its source revision is fixed; the working checkout and another agent's
uncommitted changes are not covered by these results. The harness uses shape-only
inputs and deterministic synthetic pixels, no private assets.

Earlier ignored investigation files are under `artifacts/review-20260921/`.
Their historical memory summary run ID is `1fe470829cf94b049c68d125effa36eb`; it retains
runtime, source, binary and input fingerprints. `Review.csproj` / `Program.cs`
use the existing internal diagnostic access and a synthetic triangle font, not
private assets. These are review aids, not portable regression tests; promote
decisive vectors with each implementation repair.

Evidence labels below distinguish **fresh reproduction**, **source inspection** and
**retained regression**. Allocation traffic, live accounted storage and process
working set are different quantities; zero ledger bytes do not prove heap release.

### Actionable review queue

| Priority / finding | Next bounded change | Required proof |
| --- | --- | --- |
| P0 R04/R12 | Close remaining work charges and make XML workspace policy consistent across preparation/lowering. | During-phase cancellation, remaining-work denial, and allocation/ledger observations, not only pre-cancelled calls. |
| P1 R18 | Unify root/node counting and remaining eligibility transitions. | Same boundary outcome through raw, snapshot and prepared paths; preserve the existing 30-row matrix. |
| P1 R21 | Detach owned scene references when a prepared operation ends. | Retaining a disposed/consumed document no longer retains geometry/commands/contours; all six outputs and failure paths clean up. |
| P1 R20 | Repair first-write-dependent IDAT chunking before optimizing allocation. | High-entropy output, chunk counts, encoded limit, work denial and sync/async parity. |
| P1 R22 | Make fixture bytes and validation contracts portable. | Clean LF/CRLF materialization, independent host/child runtime fingerprints, pixel-exact PNG variants. |
| P2 R14/R15/R19 | Extend the new R19 baseline, measure duplicate work, and enforce scene sizing from `T`. | Phase-specific allocation/work baselines and tests making type/category mismatches impossible; reuse `c7225d1` rather than duplicate its benchmark work. |
| P2 R23 | Bound oracle connection cleanup and event retention. | Silent-peer, cancellation, event-flood and launch-failure tests; no orphan browser/profile. |

Useful ordinary-font labels remain gated by S7's intended-font metrics/pixel
evidence; passing these engineering repairs alone does not establish fidelity.

## 2. What the current implementation can and cannot do

| Area | Implemented today | Boundary |
| --- | --- | --- |
| Output/API | PNG to owned memory, caller memory or a caller stream, each sync/async; overlap checks, non-array input memory, cancellation, output limits and stream backpressure/failure behavior. | Input is SVG bytes in memory; no public stream-input or reusable prepared-document API. Async memory conversion schedules CPU work; it is not a separate rendering engine. |
| Shapes | Rectangles with integral/quarter-pixel mapped edges; restricted even-width rectangular strokes and axis-aligned lines; closed orthogonal paths/polygons and filled orthogonal polylines. | No general curves/arcs, circles/ellipses, diagonal paths, arbitrary affine transforms or general stroke caps/joins/dashes. Shape admission remains deliberately narrow. |
| Structure/layout | Root viewport/dimensions, admitted absolute units, viewBox alignment/meet/slice/none, root/group/link inheritance, restricted integer transforms, local rectangle/line definitions and forward use references. | No general nested viewports, symbol styling/viewports or reference graphs. viewBox support does not bypass geometry admission after mapping. |
| Paint/style | Solid colors, alpha/source-over, background, fill/display/visibility inheritance, limited isolated opacity, inline styles and one ordered root stylesheet with restricted selectors and per-property precedence. | Not a browser CSS cascade. No gradients, patterns, images, clipping/masks, filters, general nested opacity or animation. |
| Fonts | OoxPdf-compatible manifest catalog, explicit host-provided sources, async selected-program preparation, length/hash checks, deduplication and immutable snapshot access contracts. | No bundled fonts, automatic installed-font lookup or implicit network/filesystem access. Snapshot caps are separate from renderer-owned accounting. Only the selected standalone TrueType face-0 subset is admitted. |
| Experimental text | Direct text/CDATA with XML whitespace modes, bounded CSS `white-space: normal`/`pre`, scalar `letter-spacing`, one validated version-zero horizontal format-zero legacy `kern` subtable, and one validated DFLT-script `kern` feature using an unfiltered GPOS PairPos format-1 first-glyph X-advance lookup: CSS properties can be supplied on direct text or inherited through `svg`/`g`/`a` from inline style or an admitted stylesheet rule, and direct CSS whitespace takes precedence over `xml:space`. Letter spacing and pair adjustments are added only between glyphs and participate in anchor width; GPOS takes precedence over legacy `kern`. Nested `tspan` concatenation inherits its parent whitespace mode and letter spacing, carries exact `xml:space="default"` or `xml:space="preserve"`, scalar `letter-spacing` and/or scalar presentation `dx`/`dy`, a style containing only the admitted whitespace and/or spacing properties, or an `id`/`class` selected through an admitted generic stylesheet rule containing only those style properties; inline span values win. Span `dx`/`dy` shifts the current position before the first retained glyph in that span or a descendant, with the nearest span that specifies each axis taking precedence; the shift persists for following glyphs after the enclosing text chunk is anchored. Span spacing applies after each span glyph, including at the following parent boundary, and contributes to the enclosing text anchor. Direct text itself admits a bounded `class` token for the same generic stylesheet selectors. Scalar x/y/dx/dy, start/middle/end anchors, inherited family/weight/style/size and paint, simple quadratic outlines and restricted composites remain admitted. Shared stylesheet font properties work through admitted selectors, including group inheritance. | Other CSS whitespace values, span paint/font styling, span x/y, positioning lists, transformed/stroked/clipped text, glyph-coverage fallback, other GPOS shaping, bidi/complex scripts, variable/color fonts, CFF or collection-face rendering. Ordinary-font metrics and browser pixels are not validated. |

The [support matrix](docs/SUPPORT_MATRIX.md) now records stylesheet font
inheritance/precedence through the admitted selector subset, matching retained
group-inheritance, inline-override, specificity and weight/style tests. Do not
describe experimental text as admitted browser-fidelity support, or infer that
every unsupported input is already rejected consistently.

### Repairs to preserve

| Findings | Current evidence |
| --- | --- |
| R01/R02 | Corrected triangle points/contours, visible public output through all six APIs, quadratic smoke, immediate siblings, CDATA/comments, empty text, group closure and malformed tails are regression-tested. |
| R03/R08 | Direct-text attributes/children are gated. Discovery shares root/container/text presentation, inherited text style, stylesheet precedence and caller limits; hidden, inert, empty, nonpainting or known render-unsupported paths do not acquire unnecessary fonts in the retained vectors. |
| R04 / S1–S2 | Generic parser storage uses type-coupled sizing; raster surface reservations clean up on failure; 4×4 glyph sampling is charged in both axes and preflighted. The one-pixel/four-segment vector costs 517 units, not the historical 37. |
| R04 / S3, partial S5 | Decoder endpoints, flags, points, simple-segment and contour-boundary growth/exact copies, composite component growth/exact copies, active recursion and retained contours now reserve before allocation. Focused tests observe every decoder phase, deny one byte below its observed boundary and verify cleanup; transfer rejects repeat/post-disposal use and releases after a failed scene build. Font metrics, cmap/layout, prepared-face lookup scans, catalog face-selection scans (including no-counter cancellation), snapshot metadata passes before provider acquisition, graph records, simple-glyph decoding, active-recursion cycle scans and contour copying consume shared work. Font discovery and stylesheet/opacity state track active ancestors without repeated scans; text extraction is chunked and bounded. Repeated copy work and broader traversal accounting remain S5 work. |
| R05/R10 / S4 | Full selected format-12 group validation, format-4 payload offsets, asymmetric matrix coefficients, nested composition and unsupported composite flags/instruction extents have independent regressions. The old transpose/cmap reproductions are repaired. |
| R06/R07/R11 | Conflicting same-hash sizes and selected nonzero standalone faces fail before provider access; valid aliases share acquisition. Read-only views, disposed-handle guards, eight concurrent conversions, bounded diagnostics and retained direct-text IDs are tested. Disposal must not race a read/conversion. |
| R09/R14 / S4 | Overflowing x+dx, transformed glyph coordinates, anchor adjustment and nonfinite viewBox/device/coverage coordinates are categorized as limits at their owning boundaries; rectangle mapped edges retain the quarter-pixel gate. Validated decoder contour ends now flow through simple and composite expansion into scene copying, so repeated coordinates cannot invent structural boundaries. All six output APIs share one owned parse/check/raster lifetime. |

Do not reopen these original defects merely because their broader workstream is
unfinished. Conversely, passing synthetic regressions do not close resource,
ordinary-font or fidelity gates.

## 3. Open correctness and blocking findings

P0 blocks reliance on the affected resource/safety contract. P1 blocks useful,
supportable labels or coherent documented behavior. P2 is targeted maintainability
or measured performance work, not a reason for a broad rewrite.

### R04b/R12 / P0 — Work accounting and cancellation still have holes

**Source inspection / retained regressions:** [outline construction](src/Lokad.Svg/Fonts/SvgGlyphOutline.cs)
copies validated decoder contour boundaries and every segment, with deterministic
cancellation after a contour-copy element. Decoder segment/component/boundary
accumulator growth and exact copies likewise charge every copied element and
release their reservations when cancellation follows exact allocation. The retained
triangle outline regression now uses 37 units. Active-recursion cycle scans now consume
the shared counter or check cancellation when no counter is present. Remaining
metadata work needs an explicit accounting policy.

[VerifiedSvgFontProgramSource](src/Lokad.Svg/Fonts/SvgFontProgramSource.cs) now
uses the same 4 KiB chunked SHA-256 helper as snapshot validation, including
deterministic cancellation between source-level hash chunks. SVG-driven snapshot
preparation passes its shared counter to that verification and reuses the resulting
verified bytes without a second hash for aliases; an 8 KiB regression observes two
hash chunks rather than four. Direct source calls remain unmetered because their
public contract has no work-counter argument. Metrics-backed cmap, advance,
glyph-range and empty-glyph paths now invoke their cancellation checkpoint even
when the nullable work counter is absent; retain this no-counter overload contract.

SVG-driven preparation shares caller work limits across discovery and its
instrumented font phases. The lowered root carries the completed preparation
total into rasterization, and the resulting surface carries that cumulative total
into PNG encoding. PNG compression now separately charges the repeated CRC and
copy pass for each compressed IDAT payload. A one-pixel remaining-work regression
allows preprocessing and compression then denies serialization through owned,
caller-memory, synchronous-stream and asynchronous-stream encoder forms. A
retained public-contract test also proves that all six prepared output forms
enforce the same numeric operation limit even though they do not retain the
parser-counter object itself.

The tracked one-glyph direct-text lowerer now has a 64-unit exact parser vector,
63-unit denial, and deterministic contour-copy cancellation with zero retained
parser/scene budget. This covers XML traversal through layout, decoding, contour
ownership and scene construction; this parser-only vector is not an observation
of every phase in the complete prepared operation.

**Additional source inspection / fresh boundary reproduction (`86fb21c`):**

- [PNG compression-buffer growth](src/Lokad.Svg/Encoding/PngEncoder.cs),
  `PngCompressionBuffer.EnsureCapacity`, copies the old payload through a helper
  that checks cancellation but never charges the work counter for that additional
  copy. Existing compressed-input and serialization charges do not include it.
- [Catalog](src/Lokad.Svg/Fonts/SvgFontPackCatalog.cs) membership checks use
  `_faces.Contains` in binding/acquisition independently of the charged selection
  scan. Specify bounded identity lookup or cancellable/metered traversal; avoid
  assuming that charging selection also charged provenance validation.
- [Discovery](src/Lokad.Svg/Fonts/SvgFontRequestDiscovery.cs) creates `XmlReader`
  without the workspace reservation used by
  [root parsing](src/Lokad.Svg/Parsing/SvgRootParser.cs). A self-closing 1×1 SVG
  succeeds in discovery with `MaximumAccountedBytes = 1`, while raw conversion
  rejects it before reader creation. R17 correctly repaired control collections,
  not this BCL workspace policy. Reserve the same conservative allowance before
  either reader, or explicitly specify and instrument a justified different
  preparation contract. Keep discovery and later scene peaks distinguishable.

**Repair (`3fcb287`):** discovery reserves the same conservative allowance via
`SvgRootParser.GetXmlReaderWorkspaceBytes` before either reader is created. A
budget of one byte now rejects a self-closing 1x1 SVG identically through
discovery, raw and prepared paths with zero provider calls or retained ledger
bytes; the direct-text-chunk test is recalibrated to the exact workspace
boundary with a denied-workspace companion. The full suite (709 + 6 + 83) and
all 21 probe scenarios pass. Catalog membership charges plus the raw contour-inspection contract remain.

**Repair (`c8cb9a0`):** geometric-buffer growth now charges each
copied byte via `PngWorkCounter.ConsumeBufferedCopyBytes` before the copy,
with cleanup preserved on denial. A calibrated 64x64 high-entropy vector pins
exact total work (61,606 units), first-growth denial (20,484 units) and
byte-identical output. The full suite (710 + 6 + 83) and all 21 probe scenarios
pass. Remaining R04 work: catalog membership charges plus the raw
contour-inspection contract.
- The raw `OpenTypeFont.GetSimpleGlyphContours(..., workCounter, token)` inspection
  route drops both arguments when calling the unbudgeted `BuildQuadraticContours`
  overload. The production budgeted decoder does propagate them. Do not use raw
  inspection tests as proof of production phase cancellation, or promote that raw
  helper to rendering without repairing its contract.

Closure: define and charge the remaining actual phase work, including repeated
copies and integrity passes; use deterministic during-phase cancellation and
bounded remaining-work assertions. Prefer one verified program lease to repeated
unaccounted hashing. Keep host I/O/cache budgets distinct from library work.

### R03/R08/R10 / P1 — Useful text semantics are not closed

**Source inspection / retained regressions:** discovery and rendering now share
much more admission/style logic, and prepared documents now retain the admitted
scene. Default XML direct-text whitespace now collapses to an inter-word space; direct
`text` also admits `xml:space="preserve"`, converting tabs/line breaks while
retaining space runs. CSS `white-space: normal` and `white-space: pre` work on
direct text and inherit through admitted `svg`/`g`/`a` inline or stylesheet
styles; a direct CSS declaration wins over `xml:space`. Nested spans concatenate
into the same run, inherit their parent mode, carry exact `xml:space="default"`
or `xml:space="preserve"`, carry a `style` containing only one of those CSS
whitespace declarations, scalar `letter-spacing` and/or scalar presentation `dx`/`dy`, or carry an `id`/`class` selected
by an admitted generic stylesheet rule containing only those style properties; inline span
values win. Scalar `letter-spacing` works on direct text and inherits through admitted
`svg`/`g`/`a` styles, contributing only between glyphs to anchor width. A scalar span `dx`/`dy`
shifts the current position before its first retained glyph or that of a descendant; the nearest span that specifies each axis takes precedence, and the shift persists for following glyphs after anchoring. One validated legacy format-zero `kern` subtable or one DFLT-script `kern`
feature using an unfiltered GPOS PairPos format-1 first-glyph X-advance lookup adjusts
adjacent direct-run glyphs; GPOS takes precedence.
Other CSS whitespace values, span paint/font styling, span x/y and positioning lists,
glyph-coverage fallback and other GPOS pair positioning are absent. Catalog-level family fallback is not
per-glyph coverage fallback.

Selected cmap 4/12, metrics, loca/glyf and restricted simple/composite outlines exist.
Optional `name` table record/storage structures and version-specific `OS/2` table
bounds are validated, but their family/style data is not applied over manifest selection;
general GPOS semantics are not validated or applied. One
version-zero horizontal format-zero legacy `kern` subtable and one DFLT-script
`kern` feature using an unfiltered GPOS PairPos format-1 first-glyph X-advance lookup
are strictly validated and applied. Those limited pair adjustments do not establish faithful ordinary labels.
Many composite flags and instructions explicitly reject, which may exclude glyphs
in otherwise compatible fonts. Do not expand admission from table presence alone.

Closure: keep discovery/painting cross-tests, explicitly gate output-affecting
semantics, validate real selected programs privately and promote synthetic
regressions. Preserve the existing single-use prepared owner and shared accounting;
repair its end-of-operation scene detachment under R21 before adding retention.

### R09/R13 / P1 — Ordinary-font fidelity remains unproven

The glyph rasterizer uses fixed 4×4 samples and eight subdivisions per quadratic,
with no scale-dependent error bound or same-font browser oracle. A visible
triangle, quadratic smoke and square/hole coverage tests are not that oracle.
No ordinary-font SVG/browser comparison was performed in this review or the
2026-09-20 private source-access assessment.

Closure: section 6's intended-font controls, independent metrics and three stable
fresh-process captures; cover curves/counters/composites, real styles, pair
positioning, fractional size/origin, scale/viewBox, alpha/background and useful
labeled diagrams. Preserve exact references; record known differences without
tolerance inflation or silent regeneration.

### R07/R11/R16 / P1 — Lifetime, diagnostics and docs need final agreement

Prepared handles fail after disposal, but already-returned borrowed memory cannot
be revoked; repeated preparation/disposal/retention now has a regression without
claiming GC timing or allowing disposal to race conversion. Broaden diagnostic stage/identity/
location vectors. Snapshot preparation now identifies synchronous and asynchronous
caller provider/source failures by origin as `Source`, instead of inferring them
from an error code; retain distinct declared-size/hash `Integrity` diagnostics.
Keep primary failures bounded and do not log font/SVG payloads or private source
identities. SVG-driven preparation now validates root viewport and presentation
state, plus inherited container and direct-text paint/opacity admission, before it
acquires a selected program.

The support matrix's stylesheet-font exclusion is already corrected. Keep it
aligned and avoid blanket deterministic-memory claims. Document the implemented experiment, admitted subset,
known defects and future requirements separately. This plan no longer presents
repaired raster leaks, stale element-size constants or composite transposition as
current blockers.

## 4. Instrumentation, tests, duplicated work and weak types

This audit covers public conversion/preparation, font acquisition/decoding/layout,
the two parser passes, scene ownership/accounting, PNG sinks, tests, benchmarks,
browser capture, package smoke and CI scripts. Findings explicitly distinguish
source inspection from fresh reproduction. No throughput or GC-pause improvement
is claimed without a benchmark; retained references, redundant copies and actual
output-size differences can be established independently of timing measurements.

### R12/R13 — Evidence that is present, and what is missing

| Instrument/evidence | Current strength | Next requirement |
| --- | --- | --- |
| RenderBudget + focused tests | Current/peak/category/denial snapshots; real cleanup and transfer assertions. | Full-conversion phase observations; owned versus borrowed storage; denial-stage assertions covering upstream allocations. |
| Deterministic seams | Background, coverage, expansion, cmap, SFNT metric-record and hash-chunk cancellation tests exist. The expansion seam also has a separate every-entry observer for bounded repeated-work measurements. The old claim that font tests are only pre-cancellation is stale. | Close uninstrumented hash/copy paths and prove interruption inside remaining layout/contour phases. Entry callbacks alone do not prove every loop is interruptible. |
| Fresh-process tool | 21 passing scenarios: 16 conversion/XML and five discovery; fingerprints, allocation attribution, ledger/provider assertions and background cancellation. | Supplied-font setup, repeated/distinct glyphs, cold/warm/repeated/concurrent lifetimes and font-phase denial/cancellation. The timed discovery-cancellation case does not itself establish an inside-phase checkpoint; preserve deterministic in-process seams. |
| Benchmarks | Four rectangle output paths plus `c7225d1` attribute-heavy conversion, styled-text discovery and 4-KiB/1-MiB hash baselines; managed allocation reporting. | Full font acquisition/layout/outline/raster/encode attribution; repeated versus distinct glyphs, retained storage and high-entropy PNGs before optimization. |
| Visual/reference tests | 43 exact admitted shape fixtures, 83 visual-project cases including tooling checks; 13 separate research probes. | Ordinary fonts and representative diagrams; font-aware browser input and substitution controls. Test totals must not be reported as visual corpus size. |

Prioritize independent failure-boundary vectors with exact category, phase,
draw-order and cleanup assertions. Extend malformed/truncated cmap/glyf,
off-curve/implied-point and contour-boundary coverage. Preserve six-output,
non-array memory, stream failure/backpressure, concurrency and package-boundary
tests; these layers are not redundant. Share fixture builders, not expected
answers derived from production decoding.

### R14 — Duplicated logic and repeated work

| Remaining duplication | Direction |
| --- | --- |
| Raw conversion independently parses after font discovery. A prepared document now lowers and retains the admitted scene during creation. | Retain only state with an explicit ownership/lifetime contract, and preserve discovery/scene eligibility cross-tests. |
| Discovery and lowering still separately orchestrate active ancestry, node counts, known/unsupported content and text/span transitions. Root/element admission, stylesheet timing and container inheritance are already shared; see R18's completed commits. Two XML reads are intentional because program acquisition precedes lowering. | Share the remaining policy transitions, not the full walkers or their different ownership. Extend provider/lowering cross-tests with numeric boundaries, especially the reproduced root-count disagreement. |
| A manifest-bound source still verifies its caller-owned bytes for each snapshot, but SVG preparation now charges that one hash and avoids a second snapshot/alias hash. | Retain a verified content/face lease across preparation and rendering; assert provider call counts, actual hashed bytes, cancellation and integrity. Preserve existing dedup/mismatched-size protections. |
| Public `SvgFontPackCatalog.AcquireAsync` fetches and verifies bytes, then returns only a resolution whose source fetches and verifies again when it is passed to snapshot creation. Its current contract says verification occurs, but it has no verified-byte handoff. | Either return a single-use verified program lease that snapshot creation can consume, or keep acquisition metadata-only under an explicitly revised contract. Do not add a global cache. Test provider calls, hash chunks and cancellation for the `AcquireAsync` → snapshot path. |
| cmap validation/lookup and glyph decoding repeat per character. | A fixed 16-entry stack-resident cache now reuses cmap and advance results within one layout operation; retained tests prove four repeated scalars enter cmap once, use exactly 18 work units and clean up on the 17-unit denial. A top-level decode carries its already-read header into expansion, while simple/composite metadata parsing reuses its already-charged glyph range and simple point decoding carries that range from its parsed structure. The text-run iterator retains and disposes one decoded outline for consecutive equal glyph indices; a four-glyph output regression observes one expansion and both first/second glyph pixels, while a separate early-iterator-disposal regression proves cache cleanup. Nonconsecutive and cross-run decoding still repeats; no global cache. |
| `SvgGlyphOutline.TransformDecodedContours` allocates a complete transformed segment array, then `CopyContours` allocates/copies per-contour arrays. | Topology preservation is complete, copy elimination is not. Measure direct transformation into final contour storage or flat segments plus validated offsets; retain reservation-before-allocation, finite-coordinate, cancellation and repeated-origin tests. |
| Raw and budgeted `OpenTypeFont.GetSimpleGlyphPoints` duplicate flag expansion and X/Y delta decoding. `GetSimpleGlyphContours` also has inspection-only and production accumulator routes. | Share the decoding kernel only after making scratch-buffer ownership and work/cancellation policy explicit; do not funnel budgeted rendering through the untracked helper. Cross-test independent malformed-font vectors on both paths. |
| Snapshot preparation creates selection/resolution arrays and validates metadata in several passes; layout separately counts, maps and positions glyphs. | Measure work and allocation by phase before merging passes. Preserve pre-provider failure timing and any validation needed across caller-controlled awaits. The 16-entry lookup cache and consecutive-glyph outline cache do not eliminate nonconsecutive/cross-run work. |
| Parser and scene budgeted lists repeat reserve/grow/copy/detach/release. | Consolidate only after ownership is explicit; preserve category/work policies and type-coupled sizes. |
| PNG sync/async compression, IDAT buffering and chunk serialization have three near-parallel implementations. They currently differ in sink type, buffer representation and async writes, while duplicating limits, cancellation and completion state. | Split common IDAT state/validation from the synchronous and asynchronous write adaptors. First pin identical bytes, limit category, cancellation point and partial-stream behavior across all output APIs. |
| Paint/font declaration consumers still have separate dispatch; alpha arithmetic is repeated. | The inline scanner, inherited text resolver and per-property cascade are already shared. Cross-test remaining semantics before further extraction. |

Completed simplifications: one parse/check/raster scope for six outputs, cached
SFNT table ranges, and reuse of decoded simple-glyph structure. Do not list those
as entirely unimplemented work.

### R15 — Weak invariants to strengthen alongside repairs

- Preserve validated contour boundaries through decoder expansion and scene
  copying. The repeated-origin regression prevents coordinate equality from
  reconstructing discarded topology.
- `SvgGeometry` now uses private typed constructors, inaccessible cross-variant
  payloads and an explicit invalid default. Preserve the unresolved-placeholder
  raster regression. **Fresh internal misuse reproduction:**
  `BudgetedSceneList<SvgGeometry>` still accepts `SvgSceneArrayKind.DrawCommand`;
  on x64 these are 120-byte versus 12-byte values, so capacity four reserves 80
  bytes instead of 512 (including its 32-byte header). Current builder call sites
  select the correct kind, so this is not a demonstrated public undercharge.
  Derive sizing from `Unsafe.SizeOf<T>()`, keeping memory category separate from
  physical type. The repaired parser-list generic already follows that approach.
- Request, selected face, verified program and lease are blurred by hash strings,
  path:index strings and `Resolution.Resolution`; introduce precise identity/
  lifetime boundaries where they prevent actual errors.
- `SvgDrawCommand` carries a geometry index, opacity and an enum in every variant;
  `-1` is a sentinel for group commands, and `SvgSceneSlot` exposes two unrelated
  raw `int` indexes. Use command-variant factories and non-negative scene-index
  value types before adding another retained command kind. Non-negative indexes
  alone cannot prove scene provenance or placeholder resolution: enforce those
  with owner-bound handles or validation at the scene-builder boundary.
- Copyable `SvgRoot`/`OwnedSceneArray<T>` values carry disposal authority over
  shared reference-backed storage. Idempotent reservations avoid double ledger
  release but do not make ownership unique or clear retained arrays (R21).
  Prefer a single explicit owner with borrowed views; preserve low-allocation
  geometry values rather than replacing every struct with an object.
- The committed `a4ad68b` change correctly distinguishes an omitted `tspan` axis
  from an explicit zero with nullable `SvgTextPositionOffset` axes, then resolves
  nearest specified axes into a concrete layout adjustment. Preserve that boundary:
  optional source syntax belongs only before resolution; positioned glyphs should
  retain finite concrete coordinates.
- Preserve the now-validated inherited-text reference type and required context
  constructor state. Audit remaining internal optional parameters when touching
  APIs; public reflection tests do not cover internal signatures.

### R17 / P0 — Font discovery control storage is budgeted and probed fresh-process (closed)

**Historical defect:** discovery's ordinary lists/set/stacks and heap candidates
were outside the preparation ledger. Do not describe them as still unbudgeted.

**Repair (`7b92422`):** discovery now reserves ancestor, whitespace, eligibility, group and distinct-request storage from its own preparation budget with type-coupled capacity accounting, value-type stacks and a value candidate, plus in-process denial/cleanup/cancellation regressions. The child-process scenarios in section 1 cover deep groups, denied budget, empty texts, distinct requests and cancellation with provider-call and no-retained-reservation assertions, closing R17.

Closure: complete for discovery control collections; retain the five child-process
scenarios, freshly rerun at `86fb21c`. Discovery's missing BCL reader-workspace
allowance remains separately recorded in R04. Neither zero current ledger bytes
nor these timed cancellation scenarios establish GC collection or every
during-phase cancellation boundary.

### R18 / P1 — Discovery and lowering need one eligibility contract

**Source inspection:** the discovery walker and the lowering walker both decide
whether text is rendered, whether style rules are active and whether an unsupported
subtree invalidates the operation. The former now uses budgeted value stacks and a value candidate (`7b92422`); the latter owns `SvgDocumentState`, typed group scopes and scene
commands. Existing examples cover selected cases, but there is no exhaustive
equivalence contract. Every new `text`/`tspan`/stylesheet feature therefore has two
places to update and can either acquire a font that will not be painted or omit a
font that lowering requires.

Closure: preserve the required pre-acquisition XML scan, but factor its shared
admission decisions into a pure state transition or analysis result consumed by
both passes. A compact input matrix (discovered requests, provider/source calls, lowering outcome and paint, now 30 cases) already covers opacity/visibility, root and group inheritance, definitions/symbols/references, foreign-namespace containers, unsupported siblings in nesting positions and both orders, empty/whitespace-only content, direct and nested spans, stylesheet precedence, transforms, malformed tails and cancellation; extend it as the factoring lands. It caught an unsupported-sibling incoherence (lowering surfaced `UnresolvedResource` for painted text the cleared discovery had omitted); discovery now retains earlier requests and lowering fails fast with the conversion-level `UnsupportedFeature`. Container names (group/link, group, inert, definitions) are single-sourced in `SvgStructuralVocabulary` with a truth-table regression consumed by both passes (`e1a709b`), which also gated the lowering inert branch on the SVG namespace to close a foreign-namespace divergence caught while extending the matrix. `827b930` extended the vocabulary with definition leaves and style/script/animation elements, aligned discovery stylesheet timing (definition-leaf styleable marking plus unconditional style/script invalidation) with four more matrix rows, and repaired a style-after-definitions wasted acquisition the matrix caught.
The root stylesheet timing gate (late/attributed rejection) is now one shared definition consumed by both passes (`6a0f962`) with direct gate regressions. Element-structure and root-identity gates are likewise single-sourced in `SvgStructuralAdmission` (`220baa1`) with direct admission regressions. Group/link container inheritance (presentation, text style, transform, opacity scope) is now one shared definition consumed by both passes (`7f9d136`) with direct resolution regressions. Finish the remaining factoring before broadening text semantics.

**Fresh boundary reproduction:** with `MaximumNodes = 1`, a root containing one
rectangle succeeds through raw conversion but discovery rejects it as
`LimitExceeded`. Both accept the self-closing root. Discovery increments its count
for the root; `SvgDocumentTraversal` starts counting after root parsing. Sharing
`ValidateElementStructure` did not unify this policy. Pin whether the root consumes
one node and test identical below/at/above boundaries through discovery, raw and
prepared APIs, including `tspan`, inert and stylesheet content. Keep remaining
known-content lists and unsupported-state transitions under the same matrix;
do not reopen already-shared root/style/container rules.

**Repair (7ca3e79):** the lowering traversal now counts the root element against
MaximumNodes, matching discovery's uniform per-element count. Eleven boundary
rows (Discovery_and_lowering_agree_on_root_node_boundaries and
Discovery_and_lowering_agree_on_text_node_boundaries) pin identical below/at/above
boundaries through discovery, raw and prepared APIs across rect, inert title,
stylesheet and nested-span/group text content, with zero provider/source calls on
rejection. Raw text without a snapshot still rejects as UnsupportedFeature once
node admission passes. The full suite (706 + 6 + 83) and all 21 probe scenarios
pass. Remaining factoring (known-content lists, unsupported-state transitions)
stays under the same matrix.

### R19 / P2 — Parse and identity transients have an initial allocation baseline

**Source inspection:** `SvgComputedStyle.Resolve` evaluates all eight
`reader.GetAttribute` calls before `Select` can choose an inline or stylesheet
value. `SvgInlineStyleParser` then materializes a separate string for every
recognized inline declaration even when it is immediately parsed and discarded.
At the font boundary, SHA-256 is converted to a fresh 64-character string for each
verification, diagnostic program identities are rebuilt as `path:index`, and
`VerifiedSvgFontProgramSource.StableId` concatenates a fresh string on every read.
These are small individually but occur in the style-heavy and repeated-acquisition
paths; source inspection alone does not establish their aggregate cost.

**Concurrent progress (`c7225d1`, not rerun here):**
[benchmark documentation](docs/BENCHMARKS.md) now records attribute-heavy conversion
(92.1 KB allocated), styled-text discovery (30.51 KB), and isolated repeated
4-KiB/1-MiB integrity hashing (344/347 B). Reuse those benchmarks; they do not yet
measure supplied-font text lowering or acquisition. Treat timings as local
ShortRun evidence, not cross-platform guarantees or proof of a particular
allocation's share.

Closure: extend the baseline to styled-text lowering/acquisition, then short-circuit
lower-precedence attribute reads, parse immediate-only inline values into typed
ephemeral fields where lifetime permits, cache stable diagnostics, and represent a
validated SHA-256 as a domain value rather than repeatedly formatting text. Any
hash comparison must retain the present validation, cancellation checkpoints and
diagnostic redaction. Do not introduce an intern table or process-wide pool merely
to hide allocation traffic.

### R20 / P1 correctness, P2 allocation — PNG buffering depends on the first write

**Fresh reproduction (`86fb21c`, .NET 10.0.12):** fill a 256×256×4 RGBA array with
`new Random(1729).NextBytes(rgba)`, then set every alpha byte to 255.
Owned memory emits 224,555 bytes in
four IDAT chunks. Both synchronous and asynchronous streams emit 266,603 bytes
in 3,508 chunks: 42,048 extra bytes (18.7%). A stream limit of 224,555 therefore
fails with `LimitExceeded` although owned output fits. This is observed encoded
size/limit behavior, not a throughput estimate.

In [PngEncoder](src/Lokad.Svg/Encoding/PngEncoder.cs), both stream sinks allocate
their buffer from the first compressed write and never grow it. The small zlib
header fixes it at 64 bytes; later sub-64-KiB writes repeatedly flush tiny chunks.
The duplicated sync/async implementation reproduces the same defect. Repair the
bounded chunk-aggregation policy and its accounting before performance redesign.
Preserve partial-write/backpressure behavior and establish the intended cross-sink
encoded-size contract explicitly; equal decoded pixels alone miss this defect.

**Source inspection:** owned-memory and caller-memory encoding first grows
`PngCompressionBuffer` geometrically, copying its previous array on each growth,
then copies/CRCs the compressed payload again. Only owned-memory output allocates
the final PNG; caller-memory output writes the caller's buffer. Large,
incompressible output can create several temporary large arrays. Growth copies
are also the concrete work-accounting omission recorded in R04.

Closure: benchmark low- and high-entropy images near the encoded-output limit for
owned memory, caller memory and both stream forms, reporting allocation traffic,
peak accounted storage, time and cancellation latency separately. If it is a real
hotspot, use a budgeted segmented accumulator or another explicitly owned staging
strategy that removes geometric copies while retaining exact byte output and limit
semantics. `ArrayPool.Shared` is not a free fix: its retained global memory must
not bypass the renderer budget or introduce an undocumented cache. Keep byte-exact
PNG, CRC, output-limit, partial-write and one-byte work-limit regressions for every
sink.

### R21 / P1 — Disposed/consumed prepared documents retain their owned scene

**Fresh reproduction / source inspection:** create a shape-only prepared document
with 1,000 rectangles, retain the document, and dispose it or finish its one
conversion. A weak reference to its geometry array remains live after forced GC
in both cases, while its budget reports zero current bytes. This is a deterministic
strong-reference problem, not a claim about normal GC scheduling.

[SvgPreparedDocument](src/Lokad.Svg/SvgPreparedDocument.cs) keeps a readonly `_root`
containing arrays; `Release` only disposes it and the snapshot.
[SvgSceneStorage](src/Lokad.Svg/Parsing/SvgRoot.cs) releases reservations without
clearing `Geometries`/`Commands`; `SvgGlyphGeometry` similarly retains `Contours`.
Thus a retained completed operation can keep its scene on the heap after that
storage disappears from the ledger. The observed probe is shape-only; contour
retention follows from the reference chain and needs its own regression. This is
distinct from callers intentionally retaining already-returned borrowed font memory.

Closure: detach the owning root/storage when release is safe, without racing an
in-flight async conversion. Test dispose-before-use, all six successful outputs,
cancellation, encode/stream failure, repeated disposal and attempted reuse.
Assert cleared owner references as well as zero ledger bytes; use a no-inline
weak-reference probe as supplementary retained-heap evidence. Keep raw snapshot
concurrency and caller-owned output lifetimes unchanged.

### R22 / P1 — Validation tooling conflates bytes, pixels and runtime provenance

**Fresh reproduction:** `git archive HEAD` under the local `core.autocrlf=true`
configuration materializes CRLF SVGs. Both visual manifest tests fail: all 43
admitted fixtures and 13 research SVGs hash differently. Normalizing only those
isolated files to LF restores every declared hash and all 83 visual-project tests.
There is no tracked `.gitattributes` pinning these byte-sensitive inputs. The
current working copy happens to use LF; its passing tests do not establish clean
checkout/archive reproducibility. This is not a reproduced Linux failure.

**Source inspection of [artifact validation](tools/validate-ci-artifacts.ps1):**

- `Test-ChromeArtifact` requires each candidate PNG hash to equal the reference
  PNG hash, although the visual contract is exact decoded RGBA. Byte-distinct,
  pixel-identical encodings are valid under that contract. Verify each file against
  its recorded hash, then compare decoded pixels; define within-run byte stability
  separately from cross-platform/reference fidelity.
- `Assert-PlatformFingerprint` compares the child memory probe's runtime to the
  validator's PowerShell-host runtime. They are independent processes; installing
  the pinned SDK does not pin PowerShell's runtime. Both happen to report .NET
  10.0.12 locally, so no local mismatch is claimed. Record host and child identities
  separately and validate the child against its selected runtime/build provenance.
  Chrome's `runtimeFramework` currently describes PowerShell, not the capture tool.
- Scenario names/outcomes/input paths are duplicated between the memory producer
  and validator; reports are string-keyed `key=value` bags, with schema/version and
  policy prose repeated in scripts. The validator fixes XML runs/timeouts that
  producer parameters can vary. Define one bounded/versioned report contract,
  reject duplicate/missing keys, and test deliberate parameter/schema mismatches.
- The synthetic local/stdin/HTTP/lazy/retry
  [font-host smoke](tools/test-font-probe.ps1) is not invoked by
  [CI](.github/workflows/ci.yml). Its six unit tests do not replace that integration
  coverage. Add the offline/local synthetic gate without any private assets.

Closure: pin materialized SVG bytes (and maintain explicit source/materialized
hash semantics), test clean LF/CRLF environments without silently regenerating
references, and add validator negative/positive fixtures for each independent
contract. Retain actual CI run/revision evidence before claiming platform parity.

### R23 / P2 — Oracle transport cleanup is not fully bounded

**Source inspection:** [DevToolsConnection](tools/Lokad.Svg.Chrome/DevToolsConnection.cs)
limits a single message to 16 MiB, but retains all unrelated events in an unbounded
`Queue<string>` and allocates a 64-KiB receive buffer plus message staging on every
receive. `DisposeAsync` awaits `CloseAsync(..., CancellationToken.None)` before
disposing the socket: a silent peer can bypass the capture's cancellation deadline
and prevent the outer browser owner from reaching cleanup. The smoke script's
process-tree timeout mitigates that entry point, not the connection lifetime itself.
[ChromeProcess](tools/Lokad.Svg.Chrome/ChromeProcess.cs) creates its temporary
profile and calls `Process.Start` before entering the cleanup `try`; a launch
exception can leave the profile. Discovery also assumes `DevToolsActivePort` is
complete as soon as the file exists.

Closure: bound the close handshake and always dispose/abort in `finally`, retain
only needed events under an explicit cap, and make the profile owner cover launch
and partial-port-file failures. Test a silent peer, event flood, cancelled capture
and failed executable launch. Only then measure transport buffer reuse; oracle
reliability is the purpose, not optimizing this tool as if it were the renderer.

Execution consequence: retain R17 closure; finish R04/R18 and the reproduced
R20/R21 resource/lifetime defects before broadening text. Repair R22 so passing
validation is reproducible. Continue R19's coordinated baseline work; R19/R20
allocation proposals do not authorize a speculative pooling rewrite.

## 5. Execution order and actual blockers

| Stage | Status | Next concrete deliverable / exit evidence |
| --- | --- | --- |
| S1 — typed sizing and surface failure cleanup | Original stale-size/cleanup defects repaired; retained tests pass. | Preserve parser sizing from `T` and correct scene call sites; close R15's remaining independently supplied scene-kind invariant. |
| S2 — glyph sampling preflight | Complete for the current sampling model. | Preserve exact 517-unit vector and denial before bounds/coverage. This is not fidelity acceptance. |
| S3 — complete decoder/storage ownership | Complete; focused phase observations, one-byte denial boundaries and cleanup tests pass. | Preserve reservations for simple/composite temporary and exact arrays, active recursion and retained contour transfer. Keep raw inspection helpers off budgeted rendering paths. |
| S4 — malformed reader, matrices and numeric admission | Complete; malformed readers, matrices and numeric outline/device/coverage arithmetic have categorized regressions. | Preserve asymmetric/nested and malformed-cmap vectors, finite coordinate checks and exact admitted pixels. |
| S5 — aggregate work and phase observations | Direct-text 64/63 vectors, cumulative preparation-through-output work, one-hash preparation and budgeted discovery collections/probes are retained. | Close R04's remaining copy/membership work and discovery XML workspace policy; repair R20's high-entropy streaming behavior. Preserve deterministic denial/cancellation and phase-specific peaks. |
| S6 — coherent preparation, lifetime and documentation | Single-use prepared scene/snapshot ownership exists across six outputs; implemented text semantics are listed in section 2. Shared root/style/container rules and the 30-row equivalence matrix are retained. | Repair R18's root-count boundary and finish remaining eligibility transitions; clear R21's retained scene after disposal/completion. Do not weaken raw snapshot concurrency or broaden text without the relevant evidence. |
| S7 — ordinary fonts and intended-font oracle | Asset permission/access resolved; an opt-in local manifest/directory or explicit HTTP source host has public synthetic local/stdin/HTTP/lazy/retry smoke and focused nonseekable-byte/growth-limit/concurrent-cache/cache-cap/cancellation-recovery tests, while inventory and oracle acceptance remain pending. | Inventory selected faces and semantics, then compare the same verified programs with browser metrics/pixels and promote reproducible defects into public synthetic regressions. Follow section 6 exactly. |

R17 stays closed. R04/R18/R20/R21 provide concrete, independently testable repairs;
R22 must close before relying on checkout/platform validation. Reuse the R19
baseline commit rather than assigning duplicate benchmark work. Private
inventory/oracle investigation can start now, but text admission requires the
relevant S5–S6 repairs and S7 evidence. No new source permission is needed to
start it.

Milestones: M0 experimental vertical slice exists; M1 bounded trustworthy inputs
is incomplete; M2 coherent prepared/style state is incomplete; M3 useful ordinary
labels is unvalidated; M4 wider SVG features remains later work. Remote CI,
canonical Linux oracle and publication remain deferred, not blockers to these
local repairs. Public asset redistribution is a separate permission gate.

Each implementation step must include its discriminating regression, relevant
docs and actual verification in a small buildable change. Update this status
rather than append contradictory progress paragraphs or repeatedly mark the same
partial step complete.

## 6. Font, text and host requirements retained

### Shared public font-pack contract

Preserve the public Lokad.OoxPdf 0.1.4 manifest contract; the 2026-09-20 source review
of the local OoxPdf checkout at `8e4b0333` (declared version 0.1.5, not a verified
published release) confirms the same fields and resolution order. The reviewed
public files are `Fonts/OoxPdfFontPackResolver.cs`, `Fonts/FontProgramLoader.cs`,
`FontTests.cs`, `README.md` and `docs/PrivateValidation.md`. Both renderers must use
unchanged manifest/font files from one location, without a converted/pruned SVG
manifest, duplicated deployment or OoxPdf assembly/package/project dependency.
Focused independent font-management adaptation is allowed; PDF embedding and SVG
glyph rasterization retain distinct responsibilities.

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

The focused comparison establishes these alignment targets; source inspection of
upstream tests is not a claim that those tests were executed here:

| OoxPdf behavior inspected | Lokad.Svg alignment and remaining work |
| --- | --- |
| Manifest first; font bytes fetched lazily for selected files and checked by length/SHA-256. | Keep `SvgFontPackCatalog` metadata-only and acquire through caller-owned sources during async preparation. Verify the unchanged private manifest with Svg itself before claiming end-to-end compatibility. |
| Case-insensitive requested/resolved family matching; italic match, closest 400/700 weight, then deterministic family/path/face tie-breaks; ordered configured fallback. | Existing Svg selection follows that order and accepts numeric SVG weights. Add independent conformance vectors for aliases, four real styles, fallback and unused unsupported entries. OoxPdf's bold request maps to 700; preserve SVG's numeric weight semantics. |
| Shared per-file sources coalesce concurrent downloads, cache successful bytes and use at most three attempts for transient transport failures. Manifest reads cap at 1 MiB; font reads cap at the smaller of the declared size and 64 MiB. | Put transport, bounded caching and retry in an opt-in test host/provider. Test concurrent reuse, cancellation and recovery with synthetic transport responses. Keep Svg's existing 32 MiB aggregate borrowed-program and 256-face caps explicit; the source's per-file cap does not replace them. |
| Font loading can select collection faces; the inspected loader synchronously waits for bytes, copies them and can return no font for certain failures. | Retain Svg's async acquisition, immutable borrowed bytes and categorized required-font failures. Current face-0 standalone TrueType admission remains explicit; packs may contain unused TTC or other unsupported entries without rejecting the whole catalog. |
| Private validation keeps manifests, inputs and reports ignored and commits anonymized findings. Download exceptions can contain source URIs. | Apply the private workflow below and redact transport errors before they reach repository reports or shared logs. Do not propagate private source identities through diagnostic `StableId` values. |

### Authorized private font testing

User authorization covers testing with the existing deployment's blob storage and
manifest. It does not authorize committing or publishing any platform content.
Never commit the storage address, account/container identifiers, credentials or
SAS values, deployment pack identifier, actual manifest, font binaries, platform
configuration/code, or derived artifacts that disclose them. This includes
embedded-font SVG/data URLs, request/response traces, exception text and reports.
Any retained private inputs, font caches and captures belong outside this repo or
under its already-ignored `artifacts/private-fonts/`; check that they are untracked
before use. Follow OoxPdf's private-validation approach with Svg's existing ignore
layout. Ordinary builds/tests and public CI must remain offline and independent of
the private source and sibling checkouts; do not upload private test artifacts.

Historical assessment (2026-09-20; not repeated in this review): read the
configured source without printing its location; streamed the unchanged manifest
under 1 MiB and checked the requested
identifier; fetched one selected regular face-0 program under its declared size
and Svg's aggregate limit; verified exact length and SHA-256. It has the standalone
TrueType signature and `head/maxp/hhea/hmtx/cmap/loca/glyf` table entries, plus
`kern` and `GPOS`, and no `fvar` entry. These are initial format observations, not
table validation, shaping support or a whole-pack inventory. All downloaded bytes
stayed in memory. No Svg catalog/snapshot conversion, OoxPdf test run, browser
capture or pixel comparison was performed in that assessment. No new private source access was needed for the 2026-09-21
review.

Execute S7 through the following concrete steps:

1. Add an opt-in generic local test host accepting a supplied manifest/byte source
   or externally configured endpoint and pack identifier, with no real defaults
   or platform configuration discovery in tracked code. Reuse unchanged assets;
   retain private hashes/provenance locally. Share selection and source behavior
   with the public OoxPdf contract without importing platform implementation.
2. Parse the actual manifest with Svg, prepare only requested programs, and build
   a private inventory of formats, faces/styles, coverage, composite flags and
   positioning tables. Compare catalog selection with OoxPdf for representative
   requests. Report unsupported semantics explicitly; do not rewrite the pack or
   silently substitute a machine font. Turn reproducible defects into synthetic
   tracked tests and fix them under S3–S6 before admitting wider text behavior.
3. Generate small synthetic label inputs covering curves, counters, composites,
   AV pairs, spaces, real styles and fractional origins/sizes. Feed identical
   verified font bytes to Svg and an SVG-image-local browser font embedding.
   Browser captures remain network-blocked; use missing/corrupt/substitution
   controls to prove intended-font use. Retain three fresh-process captures,
   independent layout metrics and exact RGBA comparisons in private artifacts.
4. Record sanitized failure categories, feature gaps and measured outcomes here.
   A private mismatch must remain visible even if public tests pass. Private
   success may support local investigation, but public corpus/package admission
   needs independently redistributable fixtures and reproducible public evidence.

The requirements below mix implemented foundations and remaining targets. A
single-use retained prepared document exists (with R21's lifetime defect);
general family/coverage fallback and shaping are not thereby implemented.

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

Core bundled/required font bytes remain zero. Count distinct programs rather than
aliases/faces and measure acquired bytes separately from parsed storage. Keep
deployment inventories and private program identities out of this plan. Select
redistributable public test fonts separately, retaining their asset licenses.

### First useful text milestone

Horizontal LTR Latin labels, initially ASCII/tested precomposed Latin, using supplied
static standalone TrueType face 0. Validate SFNT/ranges, `head`, `maxp`,
`hhea`/`hmtx`, `cmap` 4/12, `loca`/`glyf`, plus optional `name`/`OS/2` metadata
structures.
Include simple curves, implied points and bounded composite placement; gate
unsupported formats/flags.

Preserve text/CDATA/span order and admitted whitespace across boundaries. Support
inherited font/paint/visibility, scalar x/y/dx/dy, alphabetic baseline,
start/middle/end anchors, advances and required pair positioning (`kern` and
explicit bounded GPOS pairs). Test AV, spaces/punctuation, accents/composites,
supplementary scalars, mixed faces/sizes/colors and fallback boundaries.
Implement or reject features affecting output. Add styled/positioned `tspan`, spacing and
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
bounds and classified findings. Private evidence stays in the authorized local
workflow above and must not enter public reference manifests or CI artifacts.
A box glyph or single size is insufficient.

Keep exact decoded RGBA8 comparison, ignoring RGB only for transparent pixels.
No tolerance or silent reference regeneration. Separate support and fidelity
(`exact`, `known-difference`, `oracle-failure`) in explicit inventories.
Preserve rejected diagonal/overlap/opacity models as negative evidence; Windows
oracle results are not canonical Linux evidence. Work toward 200 targeted cases and
10–20 representative diagrams/illustrations as coverage prompts, not release quotas.

## 7. Core contract, wider scope and validation

Keep one MIT pure-managed C# `net10.0` library for Windows/Linux, with no third-party
runtime packages, native assets/PInvoke, System.Drawing, platform font engine,
browser service or other Lokad runtime dependency. Windows was tested here;
Linux support still needs fresh platform evidence. Rendering uses explicit
resources/options, never implicit filesystem/network/installed-font access.
Third-party research is not permission to copy/vendor implementations.

Preserve owned-result/caller-memory lifetimes, overlap rejection, non-array
memory and six output APIs; categorized malformed/unsupported/unresolved/limit/
insufficient-destination errors and token-preserving cancellation. Async memory
schedules once; async streams await writes, propagate failures and remain open.
Hosts own partial-output cleanup and transactional publication.

| Existing default | Value |
| --- | --- |
| Accounted renderer storage | 256 MiB, not process/host memory; R04 gaps remain. |
| SVG input / encoded PNG | 16 MiB / 128 MiB |
| Output | 16,384 per axis; 16,777,216 pixels |
| Nodes / cumulative path commands | 100,000 / 1,000,000 |
| Shared conversion work | 100,000,000 units; a prepared document carries its preparation work through parsing, rasterization and PNG compression |
| XML depth / attributes per element | 128 / 256 |
| Text | 32,768 UTF-16 characters cumulatively in direct-text extraction |
| Snapshot caps, separate accounting | 256 faces / 32 MiB borrowed distinct programs |

Root lowering reserves XML workspace as max(16 KiB, four times input bytes,
512 bytes per configured attribute limit); discovery does not yet reserve the
same allowance (R04). This is a conservative BCL policy, not exact allocation
accounting. Distinguish owned, borrowed, live-accounted, allocated and working-set
measurements. Choose measured aggregate font/text/graph defaults during M1.

Retain wider scope after useful labels: nested viewports/units/cascade/references;
general paths/arcs/circles/ellipses/transforms/strokes; gradients/patterns/clipping/
masks/nested opacity/markers; mapped PNG/profiles/resampling/nested SVG, later JPEG;
bounded filters and separately admitted advanced text. Scripts/events, animation,
mutable DOM, foreignObject HTML and general interactive web content remain out of
scope.

Commands executed against the isolated `86fb21c` review copy:

```powershell
dotnet restore Lokad.Svg.slnx --locked-mode
dotnet build Lokad.Svg.slnx -c Release --no-restore --verbosity minimal
dotnet test Lokad.Svg.slnx -c Release --no-restore --verbosity minimal
./tools/test-memory-probe.ps1 -ResultsDirectory artifacts/review/memory
dotnet run --project artifacts/review/Review.csproj -c Release
```

The test run was first executed on the archive's CRLF assets, then repeated after
normalizing only its 56 manifest-declared SVG assets to their expected LF bytes;
both outcomes are recorded in section 1. The diagnostic harness is temporary and
not required by a clean checkout. This review changes only `PLAN.md` in the shared
repository; production fixes and promotion of reproductions into tracked tests
are future implementation work. The other agent's commits remain untouched.

For implementation changes, also use locked restore; run package/isolated-consumer
checks for public API changes, phase/resource probes for limits/cancellation, and
fresh independent oracle checks for admitted pixels. Use new result directories;
do not label earlier reports as fresh. Preserve [benchmarks](docs/BENCHMARKS.md),
[oracle procedures](docs/ORACLE.md) and [raster research](docs/RASTERIZATION_DESIGN.md).
When CI resumes, retain run URL/revision/conclusions and matching artifacts;
`validate-ci-artifacts.ps1` checks consistency, not remote authenticity.

Follow [C# guidelines](docs/GUIDELINES_CSHARP.md). Release requires honest
limitations, representative exact cases, platform/package evidence and no hidden
font/runtime dependencies.
