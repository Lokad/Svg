# Lokad.Svg — pixel-exact corporate-logo delivery plan

Reviewed 2026-09-28 against `8a9991e47ed8e897c3547bbf69fdf0fbeba6088f`.
This revision incorporates the previously uncommitted plan and supersedes its
stale queue. Detailed findings, fresh reproductions and verification are in
[the review](docs/REVIEW_2026_09_28.md). R01–R39 and S1–S17 retain their identities;
R40 records the calibration-evidence correction and R41 the degenerate-paint defect.

**Objective:** render most ordinary, static, self-contained corporate SVG logos,
including outlined wordmarks, pixel-exact against the pinned browser oracle.
“Most” means independently measured document coverage. It never permits pixel
tolerance. Preserve the pure-managed, dependency-free library contract.

The next work is implementation. There are enough existing assets and retained
counterexamples to proceed. The first product checkpoint is an independently
selected flat curved corporate mark and an outlined wordmark with counters,
each exact at every declared size/background; it is not the S17 release claim.

## 1. Current baseline and its limits

| Evidence at the reviewed revision | Assessment |
| --- | --- |
| Fresh locked restore / Release solution build | Nine projects, zero warnings/errors. |
| Fresh full tests | **2,602 passed**: 2,496 core, 100 visual-project, six FontProbe; zero failed/skipped. Test counts are not image counts. |
| Fresh memory and delivery checks | All 25 memory scenarios pass; runtime/symbol package and isolated installed-consumer checks pass. |
| Executed fixture manifest | **331 entries = 249 exact + 77 known-difference renders + five rejections**; 148 variants / 183 base entries. Synthetic coverage is not corporate-logo coverage. |
| Executed research manifest | **79 probes = 66 known-difference renders + 13 rejections**. Positive controls must be retained too; see R27. |
| Inventory | **92 independent files**. Fresh Default-options re-render: 46 render / 46 reject. Recorded amended-recipe dispositions: 27 labelled exact / 21 known-difference / 44 unsupported. These are different option sets. |
| All-variant evidence | Only inv-029, inv-034 and inv-053 have recorded completion of all declared small/intended/large × transparent/white/black combinations. The recorded 27/92 tally is not an all-variant rate. |
| Representativeness | 26 of the 27 exact-labelled entries are flags; the other is Bumps n01. No general corporate-logo or wordmark success rate is established. |
| Fresh independent probes | Mapped half-pixel polygon clip exact; overlapping stroke outlines overcount fringe coverage; degenerate objectBoundingBox gradient path stroke paints 24 opaque black pixels where Chrome paints none. Three stable captures each. |
| Not fresh evidence | Linux/remote CI, full inventory browser regeneration, BenchmarkDotNet throughput, private-font metrics and pixels. Do not imply those were rerun. |

Chrome remains **153.0.8010.52 win64, recipe 3**, with the existing executable hash
and Chromium revision field 1681091. Historical recipe-2 references stay identified
as such. The matching release-tag Skia pin is **f8b66b7597c4cc859d3ed190e9c6872241e6721c**;
the recent main-revision `8b8c3872` investigation does not supersede it (R40).

The tests deliberately lock known wrong pixels as well as successes. Returning
PNG, accepting a new spelling, a same-renderer twin, and matching a mismatch tuple
are not independent proof of exactness. The current API can return inexact output;
keep the preview's support/fidelity boundaries explicit without inventing a new
approximate tier or silently changing public behavior.

## 2. Execution decisions and ownership

1. **Use one implementation owner for the renderer queue.** The next agent taking
   implementation owns the active row through its tests and documentation. Work
   serially across `SvgConverter`, geometry lowering, reference resolution and
   shared style/mapping types. Do not split those files between concurrent writers.
   A second review may be read-only; delegation is not required by this plan.
2. **Check actual state, not historical ownership.** Before edits, inspect HEAD,
   status and live coordination. Committed work from a previous session is not
   a file lock. Preserve genuine concurrent/uncommitted edits; use a separate
   worktree for an experiment if necessary and coordinate only the overlapping
   slice. At review start the only tracked modification was the PLAN draft, now
   incorporated here. No product-code conflict was observed.
3. **Start with row 1 below.** The miter and degenerate-gradient defects have
   concrete source explanations and witnesses. They need no new source assets,
   broader feature permission or chosen S17 percentage. Fix existing behavior
   before another shape/paint/reference permutation.
4. **Do not execute the old six-site floor256 switch.** It conflates coverage,
   byte-opacity multiplication and composition. Use the narrower row-3 work order.
   The release-tag source has been rechecked; extend that source trace only for
   the specific operation being implemented.
5. **Separate engineering from release decisions.** The numeric S17 coverage target
   and representative corporate cohort remain to be agreed before claiming “most”.
   They do not block defect repair, shared raster architecture, or the first
   outlined-wordmark checkpoint. No date or percentage is inferred from tests.

Make small, buildable commits with the regression, relevant support documentation
and actual validation. Update the current row/status in place. Detailed trials
belong in the existing design record; Git holds the implementation history.
Do not append another sequence of census-only or “all safe lanes exhausted”
entries. After two experiments without a mechanism-level improvement, record the
remaining uncertainty and choose the next architectural step. A real blocked slice
does not block independent repairs elsewhere in this queue.

## 3. Ordered implementation queue

Rows describe a sequence and dependencies, not one large rewrite. Finish each
bounded slice with a buildable commit. Re-evaluate inventory dispositions after
changes that affect them; do not start another broad sourcing campaign first.

| Order | Work and owner boundary | Exit evidence |
| --- | --- | --- |
| 1 | **Repair existing semantic defects:** S12 miter threshold; R41 zero-area objectBoundingBox gradient path strokes. Separate commits. Retain exact controls and failing SVGs as part of each fix. | Acute limit-1/default/limit-10 branch tests; degenerate path/line and userSpace controls; fresh retained oracle comparison, unchanged exact corpus, reviewed mismatch improvements. |
| 2 | **One owned contour/paint boundary:** R30/R38/R39. Extract common polygon mapping, bounds, clip and coverage traversal across four paints; reserve retained arrays before allocation and transfer ownership explicitly. | Behavior-preserving fixture/research results, six-output equality, denial/cancellation/disposal tests, measured tiny/many-curve and wordmark allocation behavior. |
| 3 | **Calibrate coverage-to-alpha narrowly:** S11/R40. Separate mask quantization from paint alpha and composition at the shared boundary. | Opaque quarter/half/full coverage, translucent 127/128 controls, both axes, all paints, overlaps and backgrounds; every existing exact case remains exact. No bulk six-site rounding replacement. |
| 4 | **Spatial fill plus matched straight-edge scan conversion:** R34/S11. Resolve winding/parity over spatial intervals, then coverage, including overlapping stroke outlines. Replace full per-pixel fan scans through the shared path. | Disjoint opposite contours, nested counters, crossings, touching/coincident edges, duplicate/overlapping strokes, held-out phases/slopes, charged work and during-phase cancellation. Both semantics and per-input oracle fidelity required. |
| 5 | **Final device mapping and retained curves:** R35/R37/S10. Wire a double affine context through containers, leaves, use/symbol and paints; lower curves with the complete map. Calibrate curves on the common raster path. | Equivalent direct/viewBox/instance encodings; fractional/scientific coordinates, rotations/skews/reflections/nonuniform scales; extrema/overshoot and scalable prepared scenes; flat curved mark and wordmark progress on existing independent exports. |
| 6 | **Shared stroke completion:** S12. Correct sampled circle/ellipse traversal and closed dash seams in separate slices; add bounded conic handling and preserve smooth-curve versus authored joins; then revisit gated odd/fractional widths. | Retained dash-offset, seam, round-cap/join and rounded-rectangle cases, acute limits, zero-length/hairline cases, pathLength and non-scaling controls; no width admission based only on parser readiness. |
| 7 | **Measured practical-export blockers:** S9/S13/S14. Finish needed computed style/references, diagonal/arbitrary clips, paint-alpha calibration and bounded layers/masks. Prioritize inv-050, inv-085, inv-094 and other retained first blockers. | Independent exporter semantics; mixed fill/stroke/paint/use/clip/opacity across sizes/backgrounds; source-order and per-object binding correct; temporary-surface bounds and cleanup measured. |
| 8 | **Independent milestone and delivery:** S8/S17. Reconcile frozen variants and corporate strata continuously; claim the milestone only after exact all-variant results and fresh platform/package/resource evidence. | Exact outlined wordmarks and flat curved marks, agreed document target, categorized residuals, portable reproducible reports and consumer checks. |

### Row 1: bounded repairs that unblock implementation

`SvgStrokeOutline.EmitJoin` computes a half-normalized miter ratio but compares it
to the full limit. Normalize displacement to half-width consistently (or use the
equivalent correctly scaled comparison). The matched Skia source and the retained
acute witnesses agree on the missing factor. Keep inner/outer joins, limits at the
threshold, and diagonal coverage separate. A fix to the branch choice need not
make every antialiased spike exact to be a valid existing-behavior repair.

R41's reproducer is a horizontal **path**, `M2,8 H14`, with width-2 gradient stroke
and default objectBoundingBox units. Chrome paints none; Lokad.Svg paints black.
The equivalent `<line>` currently rejects, so a line-only regression misses it.
Fix paint applicability using original geometric bounds, not expanded stroke
bounds. Cover vertical paths, linear/radial paints, instancing and nondegenerate
userSpace controls. Do not lose this case because it is “semantic, not calibration”.

Status 2026-09-28: row 1 is complete in two commits. The miter comparison now tests the full displacement-to-half-width ratio (new limit-2 regression alongside the retained limit-1/limit-4 branches; acute research ratchet improved from 50 pixels at max error 255 to 46 at 36 as the sole mover in a 66-probe sweep). Degenerate objectBoundingBox linear/radial path strokes now paint nothing through a shared rasterizer guard on the original paint box, with horizontal, vertical, radial, use-instance and userSpace-band regressions retained. Post-fix degenerate candidates are fully transparent against the pinned empty oracle, and the full gate holds 2502 core plus 100 visual plus 6 font green with no exact-corpus change. The line-spelling rejection for objectBoundingBox gradient strokes is unchanged.

### Rows 2–4: shared coverage and ownership

Retain one owned contour payload with ends/topology and a typed paint binding.
Use one mapping/coverage path for solid, linear, radial and pattern fills/stroke
outlines. Preserve exact rectangle/band fast paths. Consolidate incrementally
around the defects; a new general scene framework or file-renaming campaign is
unnecessary. Do not add another geometry kind for each fill/stroke combination.

Reserve before allocating points, ends, roles, transformed arrays and growth
copies; include simultaneous old/new storage and transfer reservations with
ownership. Existing raster mapping reservations, definition cleanup, reduced
curve scratch and work charging are repairs to preserve, not tasks to redo.
Eliminate spelling-dependent straight-path sizing and repeated dash parsing as
these paths are touched. Pooling is optional and must account retained storage.

The R34 winding-role and nonzero crossing-split repairs help separated contours.
They do not implement general spatial membership. `FinishStrokeFill` currently
bypasses validation and lifts zero roles; summing unsigned areas can double a
shared fringe. Fresh duplicate diagonal strokes differ from Chrome by 50 pixels
with maximum alpha error 108, versus eight for a single stroke. The browser itself
has a small single/duplicate delta, so compare each input to its own reference.
No fixture-specific deduplication or per-pixel correction is acceptable.

Separate membership from the matched antialiasing rule. Exact continuous area is
not Chrome analytic-AA; more precision or supersampling does not close the known
diagonal residuals. Use the matched release's active-edge/fixed-point construction
as a scoped investigation route. Account edge setup, ordering/intersections,
contour-vertex scans, spans, painting and cancellation; a contour-pair count alone
does not cover inner vertex scans. Preserve held-out failures and exact controls.

For row 3, retain geometric coverage, mask alpha, paint alpha, premultiplied
intermediates and final straight RGBA as distinct operations. The opaque 0.75
observation (192 in Chrome versus 191) motivates a coverage change. It does not
authorize replacing byte-opacity/compositor rounding. Match translucent controls
(including alpha 127), backgrounds and isolated groups before widening it.
`SkScan_AAAPath` itself contains both shifts and rounded partial-alpha operations.
Never replace oracle references or automatically regenerate every mismatch tuple
to make an experimental rule green.

Status 2026-09-28: row 2 is complete in four commits. Contour preparation is shared through one payload, retention pre-reserves scene storage with ownership transfer on all four fill paths, denial and release are covered per paint plus crossing splits and stroke outlining, raster mapping/bounds/clip/coverage dispatch is shared through one owned payload with per-pixel paint evaluation left per loop, and six-output parity is pinned over a four-paint combo. Gates held green throughout with no exact-corpus change. Per-pixel paint unification is intentionally deferred: it would need hot-path polymorphism, and row 4 replaces the fan-scan traversal anyway.

### Rows 5–7: coordinates, curves, strokes and composition

Define user, object, viewport, instance, device and paint spaces explicitly.
`AxisTransform` is still the calibrated integer subset; `SvgDevicePoint` can still
precede the root map. A leaf-only double parser cannot fix container inheritance.
Introduce the common affine carrier/composition path with explicit integer fast
paths, updating consumers together. Preserve original geometry bounds for paint
and clip binding; stroke/clipped/layer bounds have separate purposes.

Keep verbs and controls until the complete device transform is available, or prove
a conservative tolerance under every remaining map. Current direct-use/symbol
magnification gates are sound restrictions, not completion of scalable instances.
Test root/nested viewBox, outputScale, use/symbol frames, collinear overshoot,
reflections, singular/nonfinite maps and nonuniform stroke transforms. Mathematical
flatness bounds and independent endpoint/tangent tests do not establish S11 pixels.

Round caps/joins need a bounded weighted-conic representation or justified
conversion: the current flattener supports only polynomial quadratics/cubics.
Preserve authored corners through curve subdivision so smooth rounded rectangles
do not gain joins at every tessellation vertex. Circle/ellipse dash traversal,
closed-run seam merging and cap/curve tessellation are separate defects; the
straight dash kernel has positive evidence worth retaining. Revisit odd/fractional
widths only after their quantum, overlap and geometry witnesses are resolved.

Continue from cascaded stroke values already present in `SvgComputedStyle`;
replace repeated raw-string interpretation with shared typed computed state.
Exercise attributes, inline/embedded CSS, important/default overrides, inheritance
and references with independent expected behavior. Required forward resources,
per-instance paint/clip binding, stylesheet location and harmless export metadata
belong to S9. Keep DTD/external entity/resource retrieval disabled.

R33 now maps clip boxes and applies fractional coverage on all four polygon paths;
the new half-pixel probe is exact. Preserve that behavior. Arbitrary clip paths,
clip-child union versus nested intersection, clip rules/units and use transforms
remain S14. inv-085's diagonal clip is not a rectangle-union shortcut. Keep group
opacity isolated, support nested layers, crop surfaces with correct origins and
bounds, then implement the mask regions/content evidenced by inv-094. Constant
mask opacity is not general alpha/luminance masking.

Paint work must share geometry. Keep corrected inverse paint maps and backward
cross-type gradient inheritance. Transparent-stop unpremultiplication, gradient
opacity/dither ordering, forward inheritance and combined paints need independent
evidence. Retain degenerate bounds/vector rules, stop ordering, spread and object
versus user-space semantics; replace arbitrary stop caps with configured bounds
only where measured inputs require it.

## 4. Findings and stage ledger

### Active findings

| ID | Current disposition and remaining closure |
| --- | --- |
| R26 | Inventory sourcing is substantially complete for prioritization. Reconcile all-variant records and corporate representativeness; 27/92 is not S17 evidence. Preserve failures and original assets. |
| R27 | Every fixture is executed. Still retain complete decoded candidate identity/diff, validate historical/current oracle identity, distinguish render/reject from fidelity, and graduate positive research controls instead of deleting them. |
| R28 | Original pattern/root-map repair and inverse gradient maps retained. Paint/alpha one-step residuals remain S11/S13. |
| R30 | Four contour/paint owners and raster loops, string-based computed values and repeated binding remain. Bounded consolidation is row 2, not a repository rewrite. |
| R31 | CI artifact-validator hooks, Windows sandbox setup/health, wrapper deadline/drain and atomic publication are repaired. One opt-in corpus capture/compare/report workflow and representative performance evidence remain; reuse existing tools. |
| R33 | Original scaled clip-coordinate fault repaired; fractional mapped coverage present and one fresh independent case exact. Wider phases/scales/paints and path clips remain validation/extension work. |
| R34 | Disjoint orientation roles, bounded nonzero crossing splits and stroke-role repair landed. General touching/coincident/complex evenodd geometry and spatial stroke unions remain open; row 4. |
| R35 | Root/output tolerance propagation, overshoot and unsafe magnification gates landed. Complete per-instance/device curve lowering is still architectural; row 5. |
| R36 | Cascaded cap/join/dash/width values are carried and many spelling twins pass. Finish shared typed state and practical inheritance without reopening repaired attribute/inline cases. |
| R37 | Fractional/scientific curved paths, fractional root sizing and both relative-moveto repairs landed. General public affine transforms, restricted numeric grids and layered exporter gates remain. |
| R38 | Maximum scratch allocations substantially reduced; continue actual-size storage, ownership transfer and measured many-curve/wordmark allocation work. |
| R39 | Raster-map reservation, vertex-weighted raster work and definition lifetime repairs landed. Retained arrays still allocate before scene reservation; nested scan cost and new spatial work require charging. |
| R40 — new | Correct source provenance and narrow the quantization work order. Release-tag pin reverified in this review; later design/inventory claims still need correction with the next calibration slice. |
| R41 — new | Accepted degenerate objectBoundingBox gradient path stroke paints black instead of nothing. Fresh retained reproduction in the review; row 1. |

### Completed original repairs: preserve their regressions

| IDs / stages | Disposition |
| --- | --- |
| R01/R02 | Glyph/contour visibility, sibling/CDATA/empty-text and XML-tail fixes retained. |
| R03/R08/R10 | Original shared style/admission and selected text/outline semantics repaired; wider text is S7/S15. |
| R04/R12; S1–S5 | Original sizing, cleanup, glyph ownership, numeric/aggregate work and XML workspace/copy/membership repairs retained. New geometry work is R38/R39. |
| R05/R06/R07/R09/R10/R11/R16 | Original matrix/cmap/face/identity/lifetime/diagnostic repairs retained. Ordinary-font fidelity is not thereby proved. |
| R13; S7 | Intended-font metrics/pixels remain open; synthetic glyphs and source access do not close them. |
| R14/R15/R19 | Sharing/cache/typed-size/allocation repairs retained; remaining consolidation is R30/R38. |
| R17/R18 | Discovery reservation and original eligibility/root-count parity repaired. |
| R20/R21; S6 | PNG first-write/output limit, common encoding kernels and disposable single-use prepared-scene lifetime repairs retained. |
| R22/R23/R31a | Original provenance, pixel comparison, CDP transport/cleanup and later sandbox startup/diagnostics/deadline repairs retained. |
| R24/R25 | Channel-wrap and definition-ancestor transform defects repaired. Residual alpha and broader inherited semantics have their active owners. |
| R27 original branch | Manifest now executes both renderable and rejected unsupported entries. Remaining fidelity reporting is above. |
| R29/R32 | Decoder reserve-before-copy, proportional image/tile work, shared gradient stops, corrupt-zlib categorization and critical-chunk policy repaired. Per-wrapper granularity and lenient late tRNS remain recorded limits. |

### Stage completion criteria

| Stage | State / remaining exit |
| --- | --- |
| S1–S5 | Core foundations and original repairs exist; preserve API, parser, memory/work and output contracts throughout. |
| S6 | Disposable single-use preparation exists; preserve resource leases and six-output behavior. |
| S7 | Generic local/stdin/HTTP supplied-font host exists; actual-pack and same-font metrics/pixels remain separate work under section 6. |
| S8 | 92 selected independent exports exist. Freeze and measure variants/cohorts; retain corporate marks separately from flags/symbols and calibration probes. |
| S9 | Narrow styles/references/viewports implemented; complete the actual exporter blockers through shared semantics. |
| S10 | Full path-command/affine kernels and fractional curves exist. Public transforms, topology and final device lowering remain incomplete. |
| S11 | Critical path: general Chrome-exact coverage/curves/composition unproved. Require exact held-out phase/scale/paint/alpha families. |
| S12 | Caps/joins/dashes/stroke outlines implemented in narrow subsets with retained errors. Finish rows 1 and 6 using common coverage. |
| S13 | Limited gradients/patterns and paint maps exist. R41, transparent/opacity calibration and required inheritance/combinations remain. |
| S14 | Rectangle clips, limited masks and isolated opacity exist. General clips, mask regions/content and bounded nested composition remain. |
| S15 | Live-text wordmarks are experimental, not required for outlined wordmarks. Follow section 6 and shared glyph contours, not a separate renderer. |
| S16 | Uniform embedded PNGs and narrow patterns exist. General raster decoding/resampling, multi-element patterns, markers, filters/blending and additional static CSS only for measured blockers. |
| S17 | Not achieved. Requires a frozen representative corporate cohort, agreed exact-document target, every declared variant exact for counted documents and fresh delivery evidence. |

## 5. Product contract, evidence and completion

Keep one MIT pure-managed C# net10.0 library for Windows/Linux: no third-party
runtime packages, native assets/PInvoke, System.Drawing, installed-font engine,
browser rendering service or other Lokad runtime dependency. Rendering consumes
explicit options/resources; it does not discover filesystem/network resources.
Scripts/events, animation, mutable DOM, foreignObject HTML and arbitrary external
documents stay out of scope. Live fonts and effects must not delay outlined logos.

Preserve the six output APIs, owned/caller memory, overlap rejection and non-array
memory; named null arguments, categorized errors and token-preserving cancellation.
Async memory schedules CPU work once; async streams await backpressure, propagate
failures and stay open. Hosts own transactional output. Prepared documents are
single-use owners; snapshots/borrowed bytes retain their documented lifetimes.
Read [the C# guidelines](docs/GUIDELINES_CSHARP.md) before C# changes.

| Default bound | Value / interpretation |
| --- | --- |
| Renderer-accounted live storage | 256 MiB; not process/host memory. R38/R39 exceptions must be closed. |
| SVG input / encoded PNG | 16 MiB / 128 MiB. |
| Output | 16,384 per axis; 16,777,216 pixels. |
| Nodes / cumulative path commands | 100,000 / 1,000,000. |
| Shared conversion work | 100,000,000 units through preparation, scene, raster and encoding. |
| XML depth / attributes per element | 128 / 256. |
| Direct text | 32,768 cumulative UTF-16 characters. |
| Snapshot caps | 256 faces / 32 MiB distinct borrowed programs, separate accounting. |

Root lowering and discovery share the conservative XML allowance
`max(16 KiB, 4 × input bytes, 512 × configured attribute limit)`. Distinguish that
allowance, ledger peaks, cumulative managed allocations, borrowed storage and
process working set. Charge curve/stroke/dash/reference expansion, pair/vertex
scans, coverage/paint, images and intermediate surfaces before denied work;
check cancellation within bounded chunks and release owners on every exit.

### Validation and evidence policy

- Each implementation slice includes locked restore/Release build/tests,
  independent semantic/numeric/negative vectors and relevant documentation.
  Current exact references must remain exact. Changes to known mismatches require
  full before/after comparison and explanation, not merely refreshed maxima.
- Preserve exact controls. This plan supersedes RESEARCH_PROBES.md's instruction
  to delete exact renders: graduate useful original cases into the exact fixture
  corpus with their oracle provenance. Keep semantic failures as well as AA probes.
  Extend the existing harness rather than creating another testing system.
- Retain decoded candidate identity or complete diff evidence; mismatch count,
  maxima and bounding box do not uniquely identify an output. Verify browser,
  executable hash, recipe, platform, dimensions/options, background and input/asset
  identity against the appropriate historical/current pin.
- New references require three stable fresh-process captures with existing
  sandboxed tooling. Compare decoded RGBA8 exactly, ignoring RGB only when both
  pixels are fully transparent. Browser/recipe migration is deliberate; a failed
  candidate never authorizes replacing its reference.
- Exercise during-phase cancellation, tight remaining memory/work denial,
  cleanup after partial construction and six-output parity where affected.
  Extend existing memory/benchmark scenarios for curves, many contours, paints,
  clips and layers; do not infer performance from ledger-only observations.
- Re-render affected inventory inputs after narrow repairs; broad coverage,
  transform or composition changes require the complete frozen variant set.
  Record missing/oracle-failure/unmeasured results explicitly. Ordinary unit tests
  and library consumers remain offline and independent of browsers/private assets.
- Package/API changes require the isolated installed consumer; release claims
  require fresh Windows/Linux build/test/package/resource and platform-appropriate
  oracle evidence. A Windows reference is not automatic Linux fidelity evidence.

### S8/S17 accounting and remaining user decision

Keep original files, hashes, source/license evidence and generator provenance when
known. Never redraw/simplify hard logos, select easier variants after failure, or
reuse retired inv-033/inv-036 IDs. Public-domain candidates are not contributor
CC0 originals; preserve their provenance and keep private assets untracked.

Turn inventory declarations into explicit options per variant: logical viewport,
outputScale, resulting dimensions and background, with input and materialized-input
hashes if capture requires materialization. Changing screenshot dimensions alone
crops a fixed-size SVG image; it does not implement scaling. The current browser
transport's two-million-pixel limit is an oracle-tool constraint, not permission
to drop a declared large variant or change the renderer's pixel limit.

Report render/reject separately from exact/known-difference/oracle-failure and
unmeasured. A document counts only when **all declared variants** are measured
and exact. Reconcile the current 27 exact labels without erasing their narrower
successes or their unmeasured sizes. Freeze a corporate-mark/outlined-wordmark
cohort and hold-outs; retain flags and other symbols as separate useful strata.
Use existing inv-019/inv-040/inv-066 and independently chosen curved marks to drive
the first checkpoint, not additions optimized for the current renderer.

The user still needs to choose the numeric S17 exact-document coverage target
against that frozen cohort before a release claim. Until then, report measured
results and gaps without a “most logos” claim or ETA. This decision does not hold
rows 1–7. S17 also requires categorized residuals, reproducible oracle evidence,
representative latency/allocations and the fresh delivery/platform gates above.

Maintain API_CONTRACT, README and SUPPORT_MATRIX as current contracts. In
particular, reconcile stale fractional-dimension rejection prose and synthetic
counts. PLAN is the execution queue; INVENTORY holds document evidence,
RASTERIZATION_DESIGN holds hypotheses/results and BENCHMARKS holds measurements.
Neither another status paragraph nor another source audit is an implementation exit.

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
| Case-insensitive requested/resolved family matching; italic match, closest 400/700 weight, then deterministic family/path/face tie-breaks; ordered configured fallback. | Existing Svg selection follows that order and accepts numeric SVG weights. Independent
conformance vectors now cover aliases, four real styles, fallback (`b0d4d5d`),
plus unused collection entries that parse and select without poisoning the catalog while
snapshots reject selected collection faces before acquisition. OoxPdf's bold request maps to 700; preserve SVG's numeric weight semantics. |
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

1. Retain the implemented opt-in generic local test host accepting a supplied manifest/byte source
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
single-use retained prepared document exists (R21's lifetime defect is repaired);
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

## 7. Optional reference sources

The local reference review in the earlier draft remains useful design guidance.
Keep `external/` optional, ignored and unreferenced: no submodule, build/test input,
runtime load, CI download, vendored/translated implementation or hidden Rust/C++/
Node dependency. Do not modify another worker's reference checkout to test its
absence. Ordinary build/test/package/consumer workflows need no sibling checkout.

| Inspected source / revision | Bounded use |
| --- | --- |
| resvg `75b6bbadd7999d0516dcd7153b4a321bfdf8670a` | `usvg` style/tree, use/clip/paint binding and renderer operation order; typed resolved state and distinct geometry/stroke/layer bounds. |
| tiny-skia `5d4754777746eef0828be166896eaf482c49f8f2` | Edge/span organization, compact paths, stroke/dash expansion and storage reuse. Its fourfold supersampling is not Chrome-exact analytic AA. |
| resvg-test-suite `d8e064337faf01bc5a9579187a56dbdbe3eacc72` | Semantic counterexample index. It overlaps heavily with resvg's bundled suite; neither is a separate real-logo cohort or our PNG oracle. |
| NanoSVG `239e102ec2c691f2902e20ace2ed36ee4a35cfe6` | Secondary small-parser/scanline comparison only; five-subsample output is not the exactness target. |
| Matched Skia `f8b66b7597c4cc859d3ed190e9c6872241e6721c` | Release-tag `SkScan_AAAPath`, blitter/draw routing, stroker and curve conversion for a specific S11/S12 hypothesis. Relevant AAA/stroker routines were freshly read in this review. |

The shelf's current Skia checkout `8b8c3872fbc03f025855db96ce683f34ec98a815`
is a main-revision comparison, not the browser release pin. Use explicit matched
source reads without changing it implicitly. resvg's registry tiny-skia dependency
also need not be byte-identical to the sibling checkout despite equal version text.

Upstream success criteria and defaults are not ours: resvg tests allow channel
tolerance and can regenerate references from themselves; the test-suite Chrome
helper disables the sandbox; upstream resolvers may read host files or keep a
malformed path prefix. Keep our pinned captures, explicit providers, categorized
failures, strict comparison and bounds. Keep upstream notices with optional
checkouts; bundled fonts/assets have separate licenses. Source access authorizes
study, not product vendoring or publication of private assets.

Use the existing [oracle tooling](docs/ORACLE.md),
[research record](docs/RASTERIZATION_DESIGN.md),
[comparison tool](docs/VISUAL_COMPARISON.md),
[inventory](docs/INVENTORY.md) and [benchmarks](docs/BENCHMARKS.md).
Do not build Chromium, fetch a renderer ecosystem or survey more reference
implementations as a prerequisite to the concrete queue.
