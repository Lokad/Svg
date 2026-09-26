# Lokad.Svg — pixel-exact corporate-logo delivery plan

Reviewed 2026-09-26 against d7fddbf, reconciled through paused HEAD d561e6e and
the existing uncommitted dashed-ellipse use/symbol tests. This replaces the
accumulated status journal with current evidence and an ordered queue.
R01–R32 and S1–S17 retain their identities; R33–R39 record the new review defects.

**Objective:** render most ordinary, static, self-contained corporate SVG logos,
including outlined wordmarks, pixel-exact against the pinned browser oracle.
“Most” is document coverage, never permission for pixel tolerance. Do not drift
into an approximate tier, a native dependency, or a browser rendering service.

The immediate critical path is correct shared coverage and final device mapping.
General curves have been implemented experimentally, but “returns PNG” is not
exact admission. Live fonts, exhaustive stroke/paint combinations, patterns and
filters must not delay path-only logos.

## 1. Current assessment and verification

The agent has made substantial progress: typed contours, full-command path
parsing, affine/curve/arc kernels, winding/nesting, shared stroke outlines,
paint/reference combinations, retained oracle evidence and resource repairs.
However, **general corporate-logo usability remains unproven**. Independent
probes found scaled clipping, winding, flattening and CSS-stroke errors;
fractional curves and arbitrary public transforms still reject ordinary exports.

### Revision-scoped evidence

| Check | Result and boundary |
| --- | --- |
| d7fddbf isolated build | All nine Release projects build, zero warnings/errors, locked restore. |
| d7fddbf isolated tests | 2,285 pass / 1 stale dashed-circle rejection failure. Not current failure status. |
| 264099f isolated follow-up | All 2,288 pass; the agent repaired the stale test during review. |
| d561e6e + current tooling repair + two pre-existing uncommitted ellipse tests | Fresh locked Release solution build succeeds; full suite 2,202 core + 100 visual-project + 6 FontProbe = **2,308 pass**, zero failed/skipped. Includes the paused agent's two tests; runner counts are not image counts. |
| Current retained census | **193 entries: 117 exact, 76 labelled unsupported.** Of the latter, 71 return PNG with retained known differences, five reject. 14 variants / 179 base documents; 147 logo-tag entries. All are contributor-created CC0 inputs, no external source URL. |
| Independent representative inventory | docs/INVENTORY.md defines the framework; **zero documents selected**. No real-logo coverage percentage or defensible ETA exists. |
| Fresh independent oracle probes at d7fddbf | Nine rendered cases, each with three identical decoded fresh-process captures. Wrong scaled clips, disjoint contour cancellation, scale-dependent circles and ignored CSS stroke settings reproduced (R33–R36). Three further ordinary inputs rejected (R37). |
| Fresh allocation observations at d7fddbf | Tiny rectangle 13,312 bytes; circle 1,065,899; twenty circles 21,077,202; fractional triangle path 1,588,656 versus polygon 14,088 (R38). Warmed cumulative managed allocations, not live/ledger/process memory. |
| Other isolated d7fddbf checks | All 25 memory scenarios, artifact-validator negatives, font-host synthetic smoke, package creation and installed-consumer checks pass. They do not exercise the new curve-heavy hot path. |
| Windows Chrome repair | Hash-verified installer provisions Chrome-specific sandbox RX capabilities; fresh/cached/offline cases, launch stderr health/timing, cleanup and negative validation are exercised. See R31a and docs/ORACLE.md for the final repair evidence. |
| Not fresh evidence | Linux, remote CI, full corpus browser regeneration, BenchmarkDotNet throughput, private fonts and intended-font pixel comparisons were not rerun in this review. |

The browser remains Chrome 153.0.8010.52 win64, Chromium revision 1681091,
recipe 3. No pin, reference PNG, GPU/headless/security flag or pixel policy changed.
Earlier review results at cd0e352 are historical, not current status.

README.md still has blanket unsupported statements for gradients/images/general
paths despite their limited experimental implementation. Reconcile it with the
support matrix and exactness boundaries in the next relevant implementation slice;
do not replace stale negatives with unqualified positive support claims.

### Progress assessment

cd0e352..d7fddbf contained 221 commits and 415 changed files. New kernels are
real progress, not merely documentation. But the latest 20 commits ending at
d7fddbf contained three production changes, six test/proof additions, ten retained
fixture/reference additions and one inventory framework; all also edited PLAN.
They concentrated on paint-server round caps, circle/ellipse strokes and dashes.

Six subsequent commits through d561e6e added bevel ring declarations, ellipse
dashes/offsets and rounded-rectangle bevels. The paused agent also left two
uncommitted dashed-ellipse use/symbol twins and their support-matrix note:
they prove equivalence with direct rendering, not a new independent oracle result.
Those edits are preserved; the retained ellipse-dash differences still apply.

Since cd0e352, exact entries rose from 83 to 117 while known-difference renders
rose from four to 71. These changing synthetic denominators are not a success-rate
trend. Retaining mismatches is useful evidence, not completion of logo families.
Prioritize defects closed and independently selected documents made exact,
not commit/test/accepted-syntax counts.

## 2. Ordered execution queue

Take this queue before another peripheral shape/paint/definition combination.

| Order | Deliverable | Required exit evidence |
| --- | --- | --- |
| 0 — completed enabling repair | R31a Windows Chrome setup/diagnostics repaired without weakening the sandbox. Preserve the gates; start implementation at rows 1–2. | Fresh/cached/offline permission checks, bounded failures, healthy exact smoke captures, retained diagnostics, no unrelated ACL/process changes. Keep the full suite green. |
| 1 — representative direction | Populate the first 5–10 independent legal exports, then the planned 20–30. Freeze declared size/scale/background variants; include failures unchanged. | Real source/provenance records and exact/known-difference/rejection/oracle-failure outcomes. Ask a specific asset question if needed; “sourcing pending” is not inventory completion. |
| 2 — repair shared geometry | R33 scaled clips, R34 spatial winding, R35 final-device flattening; consolidate the duplicated coverage loop while fixing R38/R39 allocation/work accounting. | Retained adversarial/browser cases across paint kinds, transforms and output scales; bounded memory/work/cancellation. No shape-specific offset patches. |
| 3 — critical path S10/S11 | Resolve exact non-orthogonal coverage, fractional/scientific curved paths, public affine mapping and compound contours. | Independent flat curved marks and outlined wordmarks with counters, exact across every declared variant. Parsing a command or preserving a known difference does not close this step. |
| 4 — practical exports | Fix R36 computed stroke style; complete shared strokes/caps/joins and required dashes, local references, arbitrary path clips and group opacity. | Independent exporter and mixed fill/stroke/clip/style cases, not only twins through the same renderer. |
| 5 — common paints/composition | Complete reusable linear/radial paint maps, transparent/multiple stops, nested layers and required masks. | Exact transformed/clipped/alpha families; no new per-paint geometry hierarchy. |
| 6 — coverage-driven completion | Add images/patterns/markers/effects and later live text only for measured inventory blockers. Run package/platform/resource checks throughout. | Agreed frozen exact-document coverage target; categorized residual gaps and fresh platform evidence. |

The first useful milestone is outlined wordmarks plus flat curved marks.
Do not complete every S9/S12/S13 permutation before attempting that milestone.
Do not trade the exactness criterion for quicker apparent coverage.

## 3. Active findings and concrete repairs

### R26 / P1 — Representative coverage is still unmeasured

The 193-entry synthetic corpus and its logo tags do not establish real-export
coverage. Keep docs/INVENTORY.md's selection/provenance/legal rules; populate it
now, beginning with a small independently selected set. Restricted inputs remain
private. Never redraw/simplify difficult logos or drop failing variants.
Count a document exact only when all declared variants are exact. Live text may
remain outside the first path-only denominator, with S7/S15 explicitly separate.

### R27 / P1 — Original manifest gate repaired; fidelity admission still needs discipline

The original missing unsupported branch is closed: FixtureManifestTests renders
every entry, asserts categorized rejection or measured mismatch, verifies variant
parents, compares exact pixels and requires graduation when a known difference
becomes exact. Preserve these tests.

Remaining work:

- Separate implemented/rejected behavior from exact/known-difference/oracle-failure
  outcomes in reporting. Default conversion still returns PNG for 71 inexact
  entries without a per-result fidelity status. Do not call these delivered exact
  support, or add an approximate tier without user direction.
- The known-difference tuple (changed pixels / max channel / max alpha) is not
  full candidate identity: a different error layout can retain the same maxima.
  Retain decoded candidate identity or complete diff evidence when ratcheting
  intentional failures; allow reviewed improvements and graduation.
- Validate reference browser/recipe/platform identity against the appropriate
  historical/current allowed pin. A PNG hash alone is not oracle provenance.
- The 14 variants have not grown with the new curve families. Add phase, size,
  fractional outputScale and transparent/white/dark background variants.
- Report all-variant outcomes per independent document, not only entry counts.

Known examples remain substantial: circle 145 wrong pixels / max alpha 63;
cubic 97 / 29; dashed circle 114 of 256 / 240. RGB error 255 on a faint sliver
can exaggerate appearance, but these alpha/extent errors are not all tiny slivers.
Retained known differences are not passing exactness gates.

### R28 / P1 — Original paint-map defects partly repaired, exactness incomplete

The original pattern/root-viewBox case now maps on its admitted integer tile grid
and has exact evidence. Preserve that fix. Nonuniform userSpace linear gradients
now use recorded inverse maps, but the original gradient case still has a retained
nine-pixel / max-error-one difference; its stroke companion has four.
Treat remaining paint/dither/alpha calibration as S11/S13, not an exact admission.
Do not reintroduce the original wrong coordinate mapping or mark all R28 closed.

### R30 / P1 design, P2 GC — Consolidate geometry, paint and ownership when fixing defects

SvgGeometry now has 13 kinds including Invalid and nine nullable reference
payload fields. Solid/linear/radial/pattern polygons repeat points, ends, signs,
mapping, clip, storage ownership and stamping. SvgConverter repeats four
mapping/bounds/clipping/coverage loops; reference and leaf lowering multiply
the same shape/paint cases. R33/R35 demonstrate real inconsistency.

Use one owned contour geometry, a typed paint binding/evaluator and one coverage
path. Preserve calibrated rectangle fast paths. Distinguish user/document/device
coordinates and geometry/stroke/paint bounds: “DevicePoint” currently precedes
the root/output map. Reconcile AxisTransform with the general affine kernel.
Retain computed stroke values instead of raw strings/boolean capability clusters;
avoid repeated dash parsing/array allocation. Encapsulate shared arrays and
reservation transfer instead of duplicating mutable ownership conventions.

This is a bounded consolidation around correctness/resource fixes, not a
repository-wide scene-graph or naming rewrite.

### R31 / P2 — Delivery-workflow gaps after the repaired CI hook

The validator negative suite is now wired into Windows/Linux CI with artifacts.
Preserve this repair and the new startup-health validation.

The capture-chrome.ps1 parent-deadline/stream-drain gap is repaired in this change:
concurrent drains, an overall capture deadline, owned-process cleanup, retained
stderr and atomic publication now preserve an existing output on failure.
Still open: one opt-in manifest-compatible corpus capture/compare/report workflow;
changing only the browser screenshot viewport does not scale a fixed-size SVG image.

Add curve-heavy logos, many-contour wordmarks, mixed paints/clips and isolated
opacity to existing benchmark/probe families. Existing rectangle/text/font/PNG
benchmarks and 25 resource scenarios do not characterize the new hot path.
Do not turn a small enabling tooling repair into another tooling campaign.

#### R31a / P1 — Windows Chrome sandbox startup repair (completed locally)

Implemented in this change:

- tools/setup-chrome.ps1 validates strict cache/install path containment and
  rejects reparse-point traversal before acting. Archive, executable and
  setup.exe are authenticated; the latter is compared with the verified archive.
- The pinned installer runs only --configure-browser-in-directory for the browser
  directory, with hidden/no-shell execution and a 30-second deadline. Chromium
  success status 78 is handled explicitly; other failure codes are actionable.
- Fresh, cached and offline installations receive the same idempotent setup.
  Verify both Chrome-specific capability RX rules throughout files/resources;
  reject missing/denied/protected-inheritance cases without broadening access.
  Preserve all pre-existing ACLs. No repo/home/cache-root grants, Everyone or
  all-application fallback, no sandbox-disabling flags.
- ChromeProcess drains stdout and stderr independently; retains at most 32 KiB
  of stderr, detects sandbox failures and repeated network-service crashes even
  beyond truncation, and emits redacted per-launch JSON on stderr, never PNG stdout.
  Reports include executable name, launch/ready timings and profile cleanup.
- Validate startup health through shutdown before returning screenshot bytes.
  Retry locked/partially written DevToolsActivePort inside the startup deadline.
  Existing cancellation and owned-process/profile cleanup remain bounded.
- The three-capture smoke retains hashed diagnostic files; Chrome fingerprint
  schema 4 and the artifact validator distinguish configured sandbox policy
  from observed startup health. Old schema-3 smoke artifacts do not prove health.
- Unit/setup/validator regressions cover chunk-split/late errors, bounded retention,
  benign stderr, repeated crashes, redaction, partial port files, cancellation,
  path escapes/junctions, denied permissions, exit codes and forged/missing health.
- capture-chrome.ps1 retains the same per-launch diagnostic beside its PNG, drains
  streams concurrently and applies a parent deadline; child-failure/forced-timeout
  smoke controls preserve an existing destination and remove only their partial file.

Fresh cached-install smoke: three exact, stable captures; zero sandbox failures,
zero network crashes; all profiles cleaned. Chrome lifetimes 1,243 / 1,342 /
1,390 ms; DevTools ready in 311 / 318 / 349 ms. These are local observations,
not a throughput promise or a matched before/after benchmark. The old trace
established an intermittent failure, not a controlled pre-repair timing baseline.
Fresh extraction/offline reuse and parent-ACL checks are separately exercised;
final results and reproduction commands belong in docs/ORACLE.md.

Do not infer that every stderr message is fatal. A successful PNG alone, or the
literal sandbox='enabled' policy field, is insufficient health evidence.

### R33 / P1 — Scaled polygon clips use the wrong coordinate space

SvgConverter.cs maps polygon vertices through root viewBox/outputScale but
intersects polygon.Clip directly with output pixel bounds. The shared defect
must be repaired across solid/linear/radial/pattern paths. Fractional clipping
also needs coverage intersection, not just rounded bounding extents.

Independent 32x32 repro, three identical browser captures:

~~~xml
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 16 16">
  <defs><clipPath id="c"><rect width="8" height="16"/></clipPath></defs>
  <polygon points="0,0 16,0 0,16" fill="red" clip-path="url(#c)"/>
</svg>
~~~

264/1,024 pixels differ, maximum alpha error 255. Replacing clipPath with a nested
8x16 SVG viewport gives the same failure. Map geometry and clips once into the
same final space; regress root offsets/scales, outputScale, nested viewports and paints. Status 2026-09-26: repaired by mapping clip boxes through the shared root/output transform in all four polygon-outline raster paths (single IntersectMappedClip helper; nested-viewport clipping flows through the same boxes); the 32x32 repro now renders byte-exact against the pinned oracle with viewBox/direct, nested-viewport/clip-path and gradient twins plus mapped-region pixel tests (4 tests); root offsets, outputScale and fractional clip coverage stay open.

### R34 / P1 — Signed area accumulation is not spatial nonzero fill

SvgPolygonCoverage.GetWindingCoverage sums signed contour/pixel areas before
SvgConverter takes abs/clamp. Disjoint shapes of opposite orientation in one
pixel can cancel, although both should paint.

In an 8x8 SVG, fill red:

~~~xml
<path d="M .1 .1 L .4 .1 L .1 .4 Z M .6 .6 L .6 .9 L .9 .6 Z"/>
~~~

Browser alpha (0,0)=37, candidate=0. Reversing the second contour to
M .6 .6 L .9 .6 L .6 .9 Z leaves the browser identical but candidate alpha=23.
Three stable captures per variant. This is semantic, not only one-step AA drift.

Integrate spatial winding/parity membership before pixel coverage. Test disjoint
opposite contours, same-wound nesting, holes/small counters, contour reversal,
touching/self-intersections and overlapping stroke outlines. A comment requiring
separated boundaries is not an enforced public gate.

### R35 / P1 — Flattening happens before the final device transform

SvgOrthogonalGeometryLowerer's fixed flatness 0.25 and BuildDeviceMap omit the
root viewBox/outputScale subsequently applied by SvgConverter. A small user-space
curve can therefore magnify a nominal quarter-pixel error many times.

Equivalent 128x128 red circles: viewBox 0 0 8 8 with cx=cy=4,r=3 versus no viewBox
with cx=cy=64,r=48. Browser outputs are identical; candidates differ at 1,028
pixels. Against Chrome: scaled input 1,042 wrong pixels / max alpha 255; direct
input 372 / 74. Three stable captures each.

Flatten under the complete device map or derive a conservative tolerance from it.
The nonuniform-root guard currently checks only PolygonFill, not paint variants.
Regress fills, strokes, paints and prepared output. Correct subdivision alone
does not establish Chrome-exact AA. Also test collinear control overshoot/reversal:
the kernel's infinite-chord-line flatness test can miss an excursion.

### R36 / P1 — Validated CSS stroke declarations are discarded

SvgInlineStyleParser validates stroke-linecap/linejoin/dasharray/dashoffset but
does not retain them in SvgComputedStyle. Leaf lowering reads attributes instead.

Fresh 16x16 red-line cases, three stable captures each:

| Settings | Wrong pixels / max alpha error |
| --- | --- |
| x1=4,y1=y2=8,x2=12,width=4, square cap attribute + style stroke-linecap:butt | 16 / 255 |
| x1=2,y1=y2=8,x2=14,width=2, dasharray="4 4" + style stroke-dasharray:none | 8 / 255 |
| Same dashed line + style stroke-dashoffset:3 | 18 / 255 |

Retain typed specified/computed stroke values and use one cascade/inheritance path
for attributes, inline/embedded CSS, !important and references. Defaults cannot
be discarded when they override non-default attributes. Reject unsupported
semantics until honored; validation is not implementation.

### R37 / P1 — Kernel grammar/affine support is not public export support

SvgOrthogonalShapeParser rejects any decimal point/exponent in curved paths:
even numerically integral C 3.0 10.0 10.0 3.0 12.0 12.0 is rejected. Public
AxisTransform still uses integer scale/translation; rotate/skew and general
affine input are not wired through the new affine kernel. Touching/self-crossing
fills remain gated. A fill=none reversing cubic stroke is rejected by fill-first
lowering as needing a closed axis-aligned contour.

Prioritize finite decimal/exponent curves, transform composition and stroke-only
paths through the common representation. Add lexical/transform/equivalent-shape
tests backed by independent pixels. Do not advertise “full paths” from command
enumeration while ordinary exporter spelling fails.

### R38 / P1 scalability — Maximum scratch buffers for tiny geometry

Ellipse/circle/rounded lowerers allocate 65,536 SvgDevicePoint slots (~1 MiB)
per tiny shape. LowerDeviceFill/LowerStrokePath choose capacity from the configured
global command limit, not actual commands; even a fractional straight triangle
allocates about 1.5 MiB versus 14 KiB for the equivalent polygon.

Use budgeted actual-size/growing or bounded reusable scratch, count real
commands/contours, bypass curve buffers for straight segments, and avoid repeated
builder/list/retained/mapped copies. Pooling is not automatically a fix: account
retained pool storage. Add allocation checks for one curve, many curves and an
outlined wordmark. Keep cumulative allocated bytes distinct from peak memory.

### R39 / P1 resources — Polygon mapping/work bypasses the contract

Each polygon rasterizer allocates an unreserved mapped-point array. Lowering
creates retained/end/sign arrays before SvgPolygonFill.Create reserves scene
storage, missing transient overlap and allowing allocations before denial.

Raster work is charged per bounding-box pixel while each pixel scans all vertices
and clips fan triangles: actual work grows with pixels times geometry.
Contour classification is pairwise; ChargePairs amortizes a unit across a byte
checkpoint-sized group. Define and enforce the real unit across these algorithms.
Inner cancellation exists; this is not a claim it is wholly absent.

Reserve before allocation/copy, explicitly transfer ownership, charge mapping and
edge/coverage work, avoid full fan scans for every pixel, and test tight remaining
budgets plus cancellation during curve-heavy work. Add cases to the existing probe,
not a new resource-tool project.

## 4. Completed repairs and retained boundaries

Do not reopen a repaired original reproduction because its old paragraph said
“still missing”. Preserve regressions; new defects have their own IDs.

| Stable IDs / stages | Disposition |
| --- | --- |
| R01/R02 | Visible glyph/contour, sibling/CDATA/empty-text and XML-tail fixes retained. |
| R03/R08/R10 | Shared style/admission and selected text/outline semantics repaired for the declared subset. Wider spans/fallback/GPOS remain S7/S15. |
| R04/R12; S1–S5 | Original type sizing, surface cleanup, glyph ownership, numeric/aggregate work and XML workspace/copy/membership repairs retained. New polygon gaps are R38/R39. |
| R05/R06/R07/R09/R10/R11/R16 | Original matrix/cmap/face/identity/lifetime/diagnostic repairs retained. Ordinary-font fidelity remains separate. |
| R13; S7 | Intended-font metrics/pixel evidence still open; synthetic glyphs/source access do not close it. |
| R14/R15/R19 | Several sharing/cache/typed-size/allocation repairs complete. Remaining bounded consolidation is R30/R38. |
| R17/R18 | Discovery reservation/probes and original root-count/eligibility parity repaired; retain parity without open-ended row enumeration. |
| R20/R21 | First-write IDAT/output-limit repair, common PNG kernels and prepared-scene detachment/consumption fixes retained. |
| R22/R23 | Original portable provenance/pixel comparison and CDP transport/cleanup repairs retained. Launch health and wrapper deadlines/publication are repaired here; the remaining corpus workflow is R31. |
| R24 | Impossible channel wrap repaired; remaining one-step coverage/composition is S11, not tolerance. |
| R25 | Definition-ancestor transform fault repaired; wider inherited presentation still bounded. |
| R27 original branch | Every manifest entry now executed and classified; remaining reporting/identity work above. |
| R29 original decoder/tile/stops | Reserve-before-copy decoder storage, proportional chunk/row/tile work, cancellation and scene-owned shared gradient stops repaired. Per-wrapper object granularity and lenient late tRNS remain explicitly accepted limits. Do not treat new polygon omissions as those original bugs. |
| R31 CI hook / R32 PNG errors | Validator negatives wired to both CI platforms; categorized corrupt-zlib errors and critical-chunk policy repaired. |
| Review stale dashed-circle test | Repaired by 264099f; full suite reverified. Keep full-suite checks on support-boundary changes. |

The fresh full-suite result is green but preserves known bad pixels intentionally.
A positive test asserting “stroke changes output” does not establish correct dash
placement. Twin renders can share a bug; combine semantics tests with oracle cases.

S6's disposable single-use prepared scene and S7's generic opt-in local/stdin/HTTP
font host exist. Keep private acquisition/metrics/pixel validation separate from
the path-only logo milestone. The user-authorized font/privacy contract below is
retained unchanged.

## 5. S10/S11 execution and decision discipline

1. Establish one final device map, contour representation and spatial fill rule.
   Retain R33–R35 and unseen variants as regressions with their fixes.
2. Calibrate a non-orthogonal solid-fill family across phases, slopes, scales,
   clipping, opacity and backgrounds; separate geometry from AA/composition.
3. Extend the same path to fractional Béziers/arcs, compound contours and complete
   affine input; share stroke outlines/paints instead of adding parallel engines.
4. Reach the outlined-wordmark checkpoint on independent exports with counters
   and many contours at small/intended/large sizes and every declared background.

Reuse the existing negative research: pixel-centre/uniform supersampling, exact
continuous area and partial snapping models have already failed. Record one
discriminating hypothesis before each experiment, then its complete outcome on
training and held-out cases. A pinned source revision is not proof of the runtime
raster route. Do not tune individual pixels or repeat eliminated hypotheses.

If two successive iterations make no cross-family progress, write the eliminated
hypotheses and make an explicit investigation/architecture decision. Preserve
pixel exactness and dependency restrictions; surface feasibility uncertainty
rather than diverting into easy pattern/cap/reference admissions indefinitely.

Every implementation slice includes tests, relevant support documentation and
actual verification. Update current status in place in the same small buildable
commit. Detailed history belongs in Git, docs/RASTERIZATION_DESIGN.md and
docs/BENCHMARKS.md, not another multi-page “Status” paragraph here.
Report newly exact independent documents, important defects closed, dominant
remaining blockers and evidence revision—not activity totals.

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

## 7. Core contract and validation discipline

Keep one MIT pure-managed C# net10.0 library for Windows/Linux: no third-party
runtime packages, native assets/PInvoke, System.Drawing, platform font engine,
browser service or other Lokad runtime dependency. Research does not authorize
copying/vendorizing third-party implementations. Keep external/ optional, ignored
and unreferenced. Rendering uses explicit supplied resources/options, never
implicit filesystem/network/installed-font access. Private/public asset permissions
and confidentiality remain exactly as specified in section 6.

Preserve the six output APIs, owned/caller-memory lifetime, overlap rejection and
non-array memory. Keep named null-argument contracts, categorized malformed/
unsupported/unresolved/limit/insufficient-destination errors and token-preserving
cancellation. Async memory schedules CPU work once; async streams await writes,
propagate failures and remain open. Hosts own partial-output cleanup and atomic
publication. Prepared documents remain single-use owners; raw snapshot concurrency
and borrowed-byte validity have their separate documented lifetimes.

| Existing default | Value / interpretation |
| --- | --- |
| Accounted renderer storage | 256 MiB; renderer-owned accounting, not process/host memory. Polygon gaps: R38/R39. |
| SVG input / encoded PNG | 16 MiB / 128 MiB. |
| Output | 16,384 per axis; 16,777,216 pixels. |
| Nodes / cumulative path commands | 100,000 / 1,000,000. |
| Shared conversion work | 100,000,000 units; preparation carries through scene/raster/PNG output. |
| XML depth / attributes per element | 128 / 256. |
| Text | 32,768 UTF-16 characters cumulatively in direct-text extraction. |
| Snapshot caps, separate accounting | 256 faces / 32 MiB borrowed distinct programs. |

Root lowering and discovery now share the conservative XML workspace allowance:
max(16 KiB, four times input bytes, 512 bytes per configured attribute limit).
The old claim that discovery lacked it is obsolete. This policy is not exact
BCL-allocation accounting. Keep allocated bytes, owned/borrowed/live-accounted
storage and working set distinct.

Validation per implementation slice:

- Locked restore and Release build/test; all retained exact references remain
  exact. A regression against current source is separate from an old recorded run.
- Independent geometry/semantics tests, retained browser references for changed
  pixel behavior, and negative grammar/degenerate/numeric-boundary tests.
- Deterministic during-phase cancellation, remaining-work/storage denial,
  cleanup after partial construction, and six-output equivalence where touched.
- Resource probes for new owning/work-heavy paths; package plus isolated installed
  consumer for public API/package changes; fresh platform evidence for release.
- Three stable fresh-process captures per new reference with SVG/assets, browser,
  platform, recipe, output mapping and background identity. Compare decoded RGBA,
  not PNG byte identity; ignore RGB only when both pixels are fully transparent.
- A changed browser/recipe is a reviewed migration, never automatic replacement
  of failing references. Preserve known-difference and failed research evidence.

Use existing [oracle tooling](docs/ORACLE.md), [benchmarks](docs/BENCHMARKS.md),
[raster research](docs/RASTERIZATION_DESIGN.md) and
[C# guidelines](docs/GUIDELINES_CSHARP.md). Local artifact consistency is not proof
of remote CI authenticity; retain run/revision/conclusions for remote evidence.
No release claim depends on private fonts, a sibling checkout or a browser runtime.

## 8. Pixel-exact corporate-logo requirements and stage status

| Stage | Current state | Exit |
| --- | --- | --- |
| S8 — inventory/oracle | Honest synthetic exact/known-difference gate; real inventory empty. | Frozen independent inventory, variants and agreed exact-document target. |
| S9 — static exports/style/references | Limited cascade, defs/use/symbol and forward references; CSS stroke defect R36. | Practical exporter semantics and bounded reusable resolution. |
| S10 — geometry/coordinates | Curves/basic shapes and contour/affine kernels implemented experimentally; fractional curves/general public affine/complex contours still incomplete. | Complete required geometry/maps with independent semantics and S11 pixels. |
| S11 — coverage/composition | Shared analytic polygon work exists; general Chrome-exact solution missing, with R33–R35 defects and known alpha residuals. | Exact held-out cross-phase/scale/alpha families. Critical path. |
| S12 — strokes | Outline kernels, caps/joins/dashes and limited shape/paint instancing exist; many curved outputs inexact. | Correct shared fractional/transformed strokes and inventory-required variants. |
| S13 — paints | Limited linear/radial stops, alpha, spread and inverse maps implemented; exactness/interaction gaps persist. | Reusable correct paint maps and exact mixed-feature evidence. |
| S14 — clips/masks/layers | Restricted rectangles, constant masks, opacity and forward resolution; R33 device clipping defect. | Arbitrary path clips and corpus-required nested composition. |
| S7/S15 — live text | Supplied-font preparation/host exists; ordinary-font fidelity unproven. | Same-font metrics/pixels under section 6, separate from outlined logos. |
| S16 — extensions | Uniform PNG and narrow patterns exist; other features inventory-driven. | Only measured blockers, no detours. |
| S17 — logo validation | Component tests green; representative milestone not demonstrated. | Agreed all-variant exact coverage with fresh platform/package/resource evidence. |

The requirements below are targets, not blanket current-support claims.

### S8 — Inventory and measurable completion

Initially select 20–30 representative documents for prioritization, then 50–100
distinct logo documents for the agreed milestone. Curves, outlined wordmarks with
counters, thin strokes, compound/self-intersecting shapes, rotated/transformed
groups, gradients, clipped marks, translucent overlap and live text must be visible,
whether supported or not. Include ordinary design-tool and minified exports;
record actual generator provenance, not guesses. Synthetic microprobes stay a
separate calibration/regression suite, never substitutes for representative logos.

Record original/materialized hashes, permission/license evidence, feature
combinations, output dimensions, required supplied assets and pinned oracle
identity. Do not assume brand assets are redistributable or simplify them to
fit current admission. Restricted inputs/captures remain private; derive public
original reproductions without private content.

Capture intended size plus representative small/large sizes (e.g. 24/64/256 on a
reference axis), selected non-integral OutputScale, and transparent/white/dark
backgrounds where applicable. Preserve aspect ratio and distinguish resizing SVG
input from same-input viewport/output-scale options. A smaller browser screenshot
viewport alone clips a fixed-size img; it does not scale it.

Freeze the inventory and agree a numeric exact-document coverage target before
claiming “most”. A document counts exact only if **every declared variant** passes.
Report exact, known-difference, unsupported and oracle-failure counts with feature
intersections and current revision; count duplicates/variants separately.
Do not infer coverage or a date from unit-test totals.

### S9 — Practical static exports, style and references

- Accept ordinary namespaces, metadata and non-rendering export wrappers under a
  documented policy; do not reject harmless metadata or silently ignore unfamiliar
  output-affecting attributes. Keep DTD/external entity/resource resolution off.
- Resolve presentation attributes, inline style and ordered embedded stylesheets
  with correct per-property cascade, specificity, inheritance, important priority,
  currentColor, opacity and needed initial/inherit/unset forms. Preserve group
  opacity versus inherited fill/stroke opacity and display versus visibility.
  Add selectors/variables/declarations only for actual static-export blockers.
- Parse dimensions, units and percentages against the proper viewport/bounding
  box; root/nested svg/viewBox/preserveAspectRatio, overflow and intrinsic size
  must use one mapping contract. Unsupported unknown-base semantics remain explicit.
- Support local defs/use/symbol references, href/xlink:href, forward references,
  nested containers and reusable groups as demanded by the inventory. Correct
  instance presentation/inheritance, element-local transforms, symbol viewport,
  clipping and object bounds; no accidental definition-ancestor transform reuse.
  Bound resolution/expansion and detect cycles/duplicate-ID ambiguity explicitly.
- Separate unresolved source syntax from computed style/typed paint and geometry.
  Keep font discovery and lowering eligibility coherent, without duplicating
  whole traversals or blocking shape-only rendering on font lookup.

Exit: independently expected cascade/reference/viewport results, shared numeric
and eligibility boundaries, plus representative exporter documents. General CSS,
scripting, events, external-document retrieval and animation are not prerequisites.

### S10 — General filled geometry and transforms

- Parse absolute/relative M/L/H/V/C/S/Q/T/A/Z, repeated parameters, implicit lines
  after moveto, reflected curve controls, compact signed decimal/exponent syntax
  and strict arc flags. Preserve contour starts and closure. Handle radii
  correction, arc rotation, degeneracies and malformed tails explicitly.
- Preserve multiple contours/holes, either orientation, touching/coincident and
  self-intersecting contours under nonzero/evenodd. Distinguish implicit fill
  closure from explicit stroke closure.

- Normalize circles, ellipses, rounded rectangles, lines, polygons and polylines
  to shared geometry; retain independent equivalent-shape tests.
- Support fractional coordinates, relevant units/percentages and all viewBox
  alignments without quarter-pixel-only admission. Compose matrix, translate,
  nonuniform/negative scale, rotate about optional center, and skew in order,
  including ancestors/use/output scale. Define singular/nonfinite/overflow policy.
- Keep object/user/viewport/device/paint maps and geometric/stroked/clipped bounds
  distinct. Use compact typed commands/contours and explicitly owned arrays,
  not a heap object per segment or reconstruction of topology from repeated points.

Geometry exit: independent endpoints/tangents/extrema/bounds, winding and transform
order; malformed/truncated command vectors and exact command/expansion limits.
Subdivisions need a bounded device-space error/work policy. Mathematical closeness
alone is not S11 fidelity; parsing the grammar alone is not rendering support.

### S11 — Calibrated antialiasing, alpha and composition

Build one bounded device-space coverage subsystem for fills, shared stroke outlines
and eventually glyphs. Keep normalization and coverage independently testable.
Preserve rectangle fast paths only where they match the same admitted output.

Reuse the [retained calibration record](docs/RASTERIZATION_DESIGN.md): pixel-centre,
uniform supersampling, exact continuous area and partial fixed-point/edge-snapping
models have already failed. The pinned Skia source revision identifies a candidate
source, not proof of the Chrome runtime raster route. Do not repeat rejected
experiments or tune a handful of pixels into an asserted general rule.

Calibrate edge inclusion, curve subdivision, scan conversion, phase, coverage
quantization, coverage×opacity, premultiplied intermediates and final straight
RGBA conversion. Test cusp/tangent/coincident/shared edges, slivers, winding
cancellation, clipping, small/large/reflected/off-canvas geometry, transparent
overlaps and opaque backgrounds. Investigate shape-rendering modes explicitly.

Preserve the earlier full-canvas versus partial-rectangle alpha finding: identical
local half-red/half-blue stacks can give alpha 191 versus 192 under the pinned
oracle. This rejects a single context-free rounding explanation; it does **not**
justify a fixture allowlist or prove that a general mechanism cannot be identified.
Likewise, the current transposed-Bayer gradient model has recorded one-step
residuals and must not be described as universally exact.

Exit: zero decoded differences across the complete admitted and hold-out families;
stable references, six-output equivalence and deterministic work/cancellation/
cleanup tests. Known mismatches stay visible under R27 and unadmitted until fixed.
Outlined wordmarks exercise this path, not the experimental font sampler.

### S12 — Strokes used in normal exports

Implement arbitrary positive fractional width, butt/round/square caps,
miter/round/bevel joins and miter limit, proper closed joins, zero-length segments
and curve offsets. Then corpus-required dash arrays/offsets, odd-array repetition,
degenerate dashes and pathLength normalization. Support fill plus stroke with
separate paints/opacities, paint-order, affine/nonuniform transforms and
vector-effect=non-scaling-stroke without a scalar-width approximation.

Generate a bounded stroke outline for the shared coverage/compositor; avoid
independently composited overlapping rectangle bands at fractional corners.
Retain the prior failed odd-width/fractional-line experiments: orientation-dependent
half-coverage differences and double-composited band corners are unresolved, not
permission to admit an approximation. Test acute miters, round tangencies, hairlines,
dash seams, open/closed/degenerate paths, self-overlap and transformed strokes.
Bounds/expansion/work denial tests are independent of pixel success.

### S13 — Paint servers

Use one typed paint resource and explicit paint-space transform for solid,
linear/radial gradient and later pattern paints. Preserve original object bounds
through clipping and instancing. Resolve objectBoundingBox/userSpaceOnUse,
gradientTransform and href inheritance with bounded cycle-aware references.

Complete stop ordering/clamping, duplicate/edge stops, opacity/transparency,
spread pad/repeat/reflect, degenerate vectors, radial center/focal/radius behavior
and specified color-interpolation policy. The present eight-stop limit is a narrow
admission limit, not an SVG semantic rule; replace arbitrary caps with measured
configured resource bounds as corpus cases require. Fill and stroke share paint
evaluation; both gradients together should not require a new geometry variant.

Exit: independent coordinate/stop expectations and exact size/phase/color/alpha
oracle families, including diagonal ramps under nonuniform/rotated transforms,
clipped/instanced shapes and combined gradient fills/strokes. Validate dithering/
interpolation across unseen colors/slopes, not just a four-pixel red-blue ramp.

### S14 — Clips, masks, groups and operation order

Apply arbitrary paths/basic shapes and multiple children as clip geometry using
the shared contour/coverage path; support clip-rule, clipPathUnits/transforms,
nested/intersecting clips, referencing object bounds and local references.
Retain viewport/symbol clipping order and antialiased clipped stroke boundaries.

Preserve isolated group opacity: render overlapping children once, then composite
the group, not opacity on every child. Support nested layers and object/group
opacity interactions. Bound and crop intermediate surfaces without changing edge
coverage or painting order; test empty/offscreen groups and allocation denial.

Add alpha/luminance masks, maskUnits/maskContentUnits, region/defaults/transforms
and painted mask content according to actual inventory needs. Mask and clip are
different operations; a single byte opacity/rectangle is not general masking.
Keep related definition state coherent rather than separate nullable lookups.

Exit: mixed fill/stroke/gradient, transform/use, nested viewport, clip/mask and
opacity cases on transparent/white/dark backgrounds with independent expected
operation order, exact pixels and bounded intermediate lifetime/work.

### S15 — Live-text wordmarks (not a prerequisite for outlined text)

Use section 6/S7's unchanged supplied-font manifest and same-font oracle controls.
Prioritize actual corpus-required family/style selection, metrics, kerning,
whitespace, span paint/positioning, letter/word spacing and baseline behavior.
Retain missing/corrupt/substitution controls; catalog family fallback is not
per-glyph fallback or complex shaping. Required missing glyphs must not disappear.

Feed validated glyph contours into the common geometry/paint/clip/compositor;
do not duplicate the general rasterizer or claim exactness from a triangle font.
Text-path placement, position/rotate lists, textLength/lengthAdjust, complex
shaping/bidi, variable/color fonts and new formats each need separate measured
scope and admission. Private success does not replace redistributable public
regressions or authorize publication of private fonts/assets.

### S16 — Measured extensions; avoid detours

| Feature | Only pursue when it blocks the representative target |
| --- | --- |
| Embedded raster images | Bounded real multi-color decoding, data/reference policy, intrinsic sizing, transforms, resampling, alpha and color/orientation metadata. The current uniform-PNG-to-rectangle feature does not cover this. Preserve R29/R32's repairs; broaden formats only for measured blockers. |
| Patterns | Reusable multi-element tile scenes, patternUnits/contentUnits, viewBox/aspect, transform, clipping/overflow and alpha through shared scene/paint evaluation. Preserve corrected tile work/ownership; resolve remaining paint-map fidelity without a pattern-only renderer. |
| Markers | Start/mid/end placement, tangents/orientation, units, local viewports/context paint, degeneracies and bounded expansion. |
| Effects/blending | Only corpus-required shadows/blur/offset/flood/composite/merge or feDropShadow first; filter regions/units, color space, edge extension, graph/surface/kernel limits and isolation/blending explicitly tested. |
| Remaining export syntax | Static selectors/variables/conditional content and geometry/transform declarations evidenced in S8; add semantics before accepting formerly rejected spellings. |

Scripts/events, animation, mutable DOM, foreignObject HTML, arbitrary external
document fetching, installed-font discovery and unbounded global caches remain
out of scope. No native backend or renderer dependency is introduced to bypass
a difficult pixel-matching problem.

### Cross-cutting resource and maintainability gates

Charge commands, reference/curve/stroke/dash expansion, edge intersections/active
edges, coverage, paint evaluation, clips/masks/layers and image/filter work.
Reserve before allocation and before expensive denied work. Check cancellation
inside bounded chunks; pre-cancelled entry tests are not phase evidence.
Sparse/off-canvas geometry, tiny dash periods, cycles, near-degenerate curves and
huge transformed bounds need adversarial tests.

Every owned paint/contour/surface releases on success, failure, cancellation and
disposal, with ownership transfer rather than bookkeeping-only release. Share
immutable geometry/paint resources without hiding memory or double-accounting it.
Keep six-output bytes/errors consistent, caller buffers/resources borrowed and
async streams backpressure-aware. Benchmark repeated/distinct logos and cold/warm
paths; do not conflate lower ledger peaks with fewer GC allocations.

### S17 — Definition of usable for most corporate logos

The milestone is reached only when the frozen S8 inventory meets the agreed exact
document-coverage target, all declared variants of admitted documents are exact,
and the remaining unsupported/known-difference/oracle failures are classified.
No tolerance tier, reference rewriting, easier replacement assets or inflated
variant counts. Publish implemented, oracle-validated and unsupported boundaries
separately.

Retain stable SVG/asset hashes and three-capture references with browser/platform/
recipe, output mapping, background and any font/media assumptions. Exercise
feature intersections, not only individual capabilities. Report representative
latency, allocations/live peaks and cancellation/denial behavior.

Require fresh Windows/Linux build/test/package/installed-consumer and resource
evidence for claimed platforms. Windows exactness is not automatically canonical
Linux evidence. A normal library consumer needs no browser, network, private font
pack or sibling checkout. Use the census to choose work and the independent
oracle to decide exact admission; do not confuse either with implementation volume.
