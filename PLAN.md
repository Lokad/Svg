# Lokad.Svg — review and corporate-logo delivery plan

Reviewed 2026-09-23 against **cd0e352**. This replaces the accumulated status
journal with one current assessment and an ordered implementation queue.
R01–R25 and S1–S17 retain their identities; R26–R32 are new review findings.
Historical reproductions/commit-by-commit notes remain in Git history at cd0e352,
[benchmarks](docs/BENCHMARKS.md) and [raster research](docs/RASTERIZATION_DESIGN.md).

**Objective:** render most ordinary, static, self-contained corporate SVG logos,
including outlined wordmarks, pixel-exact against the pinned browser oracle.
“Most” is document coverage, not permission for pixel tolerance. General filled
paths and calibrated coverage are the critical path. Live-font support, pattern
elaboration and filters must not hold up path-only logos.

Only PLAN.md is changed by this review. Verification used an isolated LF source
archive, not the other agent's uncommitted work. At review time that work touches
mask/clip context in traversal, groups, rendered leaves and references, with tests.
Finish/reconcile that in-flight slice before taking the queue; do not overwrite it.
The shared HEAD remained cd0e352 during verification.

## 1. Current assessment and verification

There is a useful engineering foundation and genuine recent feature progress,
but **not yet a general corporate-logo renderer**. A curved or rotated flat mark
can still be rejected, and admitted pattern/gradient combinations can silently
render incorrectly. Passing tiny orthogonal examples is not a coverage estimate.

### Fresh evidence

| Check at cd0e352, Windows x64 / .NET 10.0.12 | Result and boundary |
| --- | --- |
| Locked Release solution build | Pass, zero warnings/errors; all nine projects build. |
| Full test run | 1,318 core + 88 visual-project + 6 FontProbe = **1,412 passed**, zero failed/skipped. Runner counts are not independent image counts. |
| Retained exact corpus | **83 exact entries** pass their retained decoded-RGBA comparisons. Browser regeneration of the full corpus was not run. |
| Census | 92 entries; 83 labelled exact, 9 labelled unsupported; 14 variants, hence 78 base documents. All 92 are contributor-created CC0 inputs with no external source URL. Only 49 entries carry the logo tag: 35 base logo documents + 14 variants. The other 43 are microprobes. |
| Unsupported-entry diagnostic | Four of the nine “unsupported” entries actually return PNG successfully; five reject. The four are already documented as pixel mismatches, not missing syntax (R27). |
| Fresh pinned browser checks | Chrome 153.0.8010.52 win64, cached pin verified offline, existing SVG-image capture implementation/background/scale-one recipe. Three fresh-process captures per new case were decoded-pixel identical. Pattern/viewBox case: **2,048/4,096** pixels differ, max channel error 255. Nonuniform gradient case: **512/512**, max error 73 (R28). |
| Fresh embedded-PNG diagnostics | CRC-valid damaged zlib escapes as raw InvalidDataException; unknown chunk criticality is reversed for two discriminating vectors (R32). One-row solid images at widths 1 and 16,384 both consume **5 decoder work units** (R29). |
| Fresh resource tool | All **25** child-process scenarios pass, including three runs per XML shape, five discovery-ledger scenarios, two embedded-image conversion scenarios and two pattern tile scenarios. Run ID ef8d7d3b28a448b0bc433a157509df26. |
| Fresh validator negatives | test-validate-ci-artifacts.ps1 passes; run ID 6836595ddad44945a2606e8b14f9ab7d. This is local validation-tool evidence, not proof of a remote CI run. |
| Not rerun | Linux, remote CI, package/installed-consumer smoke, throughput/GC benchmarks, private font acquisition and intended-font browser comparisons. Historical results are not fresh evidence. |

Reproduction in an isolated archive (git archive with core.autocrlf=false):

```powershell
dotnet test Lokad.Svg.slnx -c Release -p:RestoreLockedMode=true --verbosity minimal
dotnet build Lokad.Svg.slnx -c Release -p:RestoreLockedMode=true --verbosity minimal
./tools/census-report.ps1
./tools/test-memory-probe.ps1 -ResultsDirectory artifacts/review/memory
./tools/test-validate-ci-artifacts.ps1 -ResultsDirectory artifacts/review/validator
dotnet run --project artifacts/review/Review.csproj -c Release
```

The last command is a temporary diagnostic, not a tracked checkout prerequisite.
Its source and generated images remain under
%TEMP%/svg-logo-review-1d47bd48fb204266866f5f60a85e7d4c/source/artifacts/review/.
Promote the decisive inputs into tracked regressions with the fixes; do not depend
on temporary artifacts for ongoing validation. No private assets were used.

### Is the other agent progressing reasonably?

The latest 20 commits contain **10 production/test changes and 10 PLAN-only
updates**, unlike the earlier low-yield matrix-row expansion. They add image
aspect/composition, viewport opacity, translucent lines, pattern transforms/band
fills, gradient line strokes, multiple stops, use paint servers and the R25 repair.
This is real progress, with small buildable changes and useful reuse of existing
viewBox/style/placement code.

The priority balance is nevertheless wrong for general logos: most of these
extend rectangle/band special cases while S10/S11 remain the central gap.
Several changes have parse-level tests and scratch browser comparisons but no
retained visual regression for the newly admitted interaction. “Exact by
construction” is insufficient when transforms, paint units, clipping or alpha
change. R28 demonstrates why. Pairing every implementation with a separate
journal commit also leaves stale summary tables; update the current status in
the same tested change instead.

Do not extrapolate an ETA or “90% of logos” from these totals. Pixel-exact
non-orthogonal coverage remains a material technical uncertainty, not a short
list of parser cases. Measure progress as new document families rendered exactly,
reproduced defects closed, and decisive calibration hypotheses resolved.

## 2. What is actually implemented

[SUPPORT_MATRIX.md](docs/SUPPORT_MATRIX.md) remains the detailed grammar contract;
its positive claims need the R27/R28 qualifications below.

| Area | Present foundation | Important remaining boundary |
| --- | --- | --- |
| API/output | Pure-managed dependency-free net10.0 SVG-to-PNG; owned/caller memory and stream, sync/async; prepared documents and supplied-font snapshots; cancellation, limits, overlap and lifetime tests. | Resource accounting is not a process-memory limit; new image/paint paths have gaps. Async does not supply another rendering backend. |
| Geometry | Rectangles, axis-aligned even-width lines/strokes, orthogonal paths/polygons/polylines lowered to rectangle bands, relative m/l/h/v/z and repeats, multiple contours/fill rules, selected orthogonal crossings, and coincident vertical edges (abutting/spike/touching-hole regressions 2026-09-24; degenerate pairs still gate). | No general diagonals, C/S/Q/T/A curves, circles/ellipses, rounded corners, arbitrary fractional geometry or rotate/skew/full affine mapping. Degenerate coincident pairs still gate. |
| Coordinates/structure | Root and restricted nested viewports, viewBox/aspect mapping, selected percentage dimensions, integer axis transforms/reflections, groups/links, local use and restricted symbols/definitions. | Per-feature viewport/transform restrictions, limited definition/reference graph and style inheritance. R25 fixed definition ancestor transforms, not all definition presentation semantics. |
| Paint/composition | Solid colors/opacity; restricted isolated groups, rectangular clips and simple masks; two-to-eight opaque-stop linear gradients, selected paint-server strokes, single-rectangle pattern tiles and solid-color PNG images. | Not general clipping/masking/images/patterns. No radial/transparent-stop gradients; paint-coordinate defects and alpha/dither residuals remain. An accepted fill is not automatically exact under another transform/background. |
| Export/CSS | Shared inheritance and per-property precedence; one early root stylesheet with restricted selectors, inline/presentation styles, selected no-op defaults and local references. | Not a general static-export cascade; group paint-server inheritance, reference combinations, stylesheets/units and useful spellings remain restricted. Prioritize observed exporter blockers, not arbitrary CSS completeness. |
| Text | Manifest/catalog/async acquisition, bounded snapshot and prepared lifetime, selected TrueType face-0 outlines, whitespace/spans/spacing, limited kern/GPOS pair positioning and synthetic-font tests/benchmarks. | Ordinary supplied-font pixel fidelity unvalidated; fixed 4×4/eight-subdivision glyph rasterizer is not the general-path solution. No implicit machine fallback. Outlined wordmarks need no fonts. |

## 3. Open review findings

P0 means an existing resource/error contract cannot be relied on; P1 blocks
trustworthy rendering or delivery prioritization; P2 is targeted tooling/design/
performance work. Delivery order is in section 5: do not turn every P2 observation
into a prerequisite to general paths.

### R26 / P1 — The census does not establish representative logo coverage

**Fresh census + source inspection:** census-report.ps1 counts manifest labels;
it does not render. Its 78 “distinct documents” combine 43 microprobes with 35
synthetic logo bases. All inputs are original contributor work, mostly constructed
around the admitted orthogonal subset. Separate research includes curves and the
path-heavy Tiger illustration, but those are not representative brand coverage.

The former claim that 67 documents already satisfied S8 breadth was incorrect.
Also, base documents can have unsupported size/background variants while their
other entries count exact. Entry counts must not stand in for all-variant success.

**Next:** establish a separate, versioned representative inventory, initially
20–30 independently selected real export documents to steer implementation,
growing to the planned 50–100 without delaying the first path slice. Track legal
provenance; restricted assets stay private. Include curves/outlined wordmarks,
holes, rotations, thin strokes, gradients and clipped/translucent combinations,
including failures. Keep synthetic probes in their own count. Validate variant
parents/duplicates/cycles and aggregate exactness over every declared variant.
Freeze the inventory/target before a “most logos” claim; never simplify logos or
change the denominator to improve the score. Selection can be local/offline.

### R27 / P1 — “Unsupported” is not enforced, and known differences escape the gate

**Fresh reproduction + source inspection:**
FixtureManifestTests validates hashes/metadata for every entry, but only invokes
the renderer and compares pixels for expectedSupport=exact. The unsupported branch
only validates labels/reasons and forbids oracleIdentity/referenceSha256.

The current public renderer successfully returns PNG for these four entries:

| Fixture | Existing manifest's mismatch evidence (historical, not recaptured here) |
| --- | --- |
| original-logo-translucent-mark | 376 differing pixels, maximum error 1 after R24's channel-wrap fix. |
| original-logo-viewbox-mark-24 | 28 edge pixels differ by 1 at 0.75× viewBox mapping. |
| original-logo-use-fade-mark | 256 pixels differ by 1. |
| original-logo-group-fade-mark-white | 1,792 pixels differ by 1 RGB step, alpha exact; other backgrounds pass. |

**Next:** separate syntax/runtime support, fidelity (exact / known-difference /
oracle-failure), and release admission. A metadata label is not a runtime guard.
Exercise every inventory entry: asserted rejection/category when rejected;
retained reference/diff and measured mismatch when currently renderable-but-wrong.
Repair or explicitly gate the affected semantic domain before claiming it exact;
do not implement a filename allowlist. Keep known differences visible without
tolerance admission or silently blessing current output as expected pixels.

Promote decisive scratch captures into the appropriate exact or research suite.
Add CI failures for an exact entry drifting, an unsupported case starting to
render without review, a missing known-difference capture, or a variant omitted
from the coverage report. Existing research-rejection tests are useful but do not
cover these nine census entries. R24's wrap defect stays closed; its residual
rounding is an S11 blocker, not evidence that overlaps are generally admitted.

### R28 / P1 — Paint coordinate systems are wrong under admitted transforms

**Fresh three-capture browser reproductions**, ordinary valid inputs:

1. A 64×64 SVG with viewBox="0 0 32 32", an 8×8 userSpaceOnUse pattern containing
   a red 4×8 rectangle, and a 32×32 filled rectangle: half the output differs.
   SvgConverter.StampPatternFill indexes device x/y modulo user-space tile width/
   height/origin. Root viewBox/output scale and referencing placement are not
   carried into tile sampling. At scale 2 Chrome stripes are eight pixels wide;
   the renderer still paints four-pixel stripes.
2. A 32×16 SVG, diagonal userSpaceOnUse linear gradient (0,0)→(16,16), opaque
   red/blue stops, and a 16×16 rectangle in g transform="scale(2 1)": every pixel
   differs, maximum error 73. SvgGradientSyntax.MapUserSpaceVector transforms
   endpoints, then the renderer projects in the transformed Euclidean metric.
   Nonuniform scaling requires evaluation through the inverse paint map; endpoint
   transformation alone does not preserve that gradient field.

**Next:** retain these two self-contained cases immediately. Introduce explicit
object/user/device/paint maps and keep unclipped object bounds distinct from
coverage bounds. Evaluate paint in its own coordinates; share composition with
viewBox, gradientTransform, patternTransform, referencing transforms and use.
Test direct versus referenced geometry, diagonal ramps, anisotropic scale,
translation, reflection, nonzero viewBox origin, output scale and clipping.
Prefer a bounded explicit rejection until fixed over a silently wrong admitted
combination. Do not solve this with per-fixture offsets or another shape kind. Status 2026-09-24: R28 cases retained as rejects-pinned fixtures (original-logo-pattern-viewbox-mark, original-logo-gradient-anisotropic-mark; three identical pinned-Chrome captures each, candidate gates from b1c968f); exact paint maps remain S13 work. Status 2026-09-25: pattern tiles map through the root viewBox and output device transform with integer-grid gating (uniform, non-uniform and offset probes each renderer byte-exact against three identical pinned-Chrome captures; fractional tile maps and stroked tile content under scaling stay explicitly gated; 1 converted admission plus fractional, content-stroke and output-scale equivalence tests, 4 end-to-end tests); the R28 pattern-viewbox fixture promotes to an exact oracle entry while the gradient-anisotropic case remains S13 work. Status 2026-09-25: non-uniform userSpaceOnUse linear-gradient fields evaluate through the recorded inverse referencing map (per-copy reference scales composed from gradientTransform, referencing and symbol viewBox maps with uniform rendering bit-identical on the legacy path; converted gate plus scale-record, use and band equivalences, 4 end-to-end tests; R28 fill probe renders with measured oracle delta of 9 pixels at maximum channel error 1 and a stroke-composition probe with 4 pixels at maximum channel error 1 across three identical pinned-Chrome captures per probe, the same one-step family as the radial entries); the R28 gradient case graduates to a measured known-difference entry with a stroke-composition companion entry, and radial non-uniformity stays explicitly gated.

### R29 / P0 — New paint/image paths bypass storage/work obligations

**Source inspection, plus a fresh work-counter reproduction:**

- SvgLeafParser.DecodeImage allocates a base64 substring and decoded byte array
  before reserving the decoded storage. PngImageDecoder copies IDAT data into
  an unbudgeted growing MemoryStream and makes palette/transparency copies.
  Transparency length validation follows its allocation. XML workspace allowance
  does not replace ownership/accounting for these additional buffers.
- PNG CRC loops, unfiltering and per-pixel uniformity scans have no byte/pixel
  work charges or inside-row cancellation checkpoints. A 1×1 and a 16,384×1
  all-white PNG each consume five decoder units; accounted decode peaks are
  13 and 196,609 bytes respectively. Pixel/dimension limits bound absolute size,
  but MaximumWorkUnits does not bound the proportional decode work.
- RasterizePatternFill allocates/renders the whole tile separately for each
  geometry/band/use. Its tile storage is reserved, but BlitPatternBox's content
  pixel loop is not charged; only destination stamping charges coverage.
- SvgPatternGeometry, SvgGradientGeometry and SvgGradientStrokeGeometry allocate
  per-instance/per-band wrapper objects without reservations. Their IDisposable
  methods are empty. Gradient stop arrays are covered by a parser-dictionary
  allowance while parsing, then remain reachable from scene geometry after the
  parser dictionary releases that allowance; no reservation transfers with them.

**Next bounded repairs:** reserve before decode/copy/growth, validate lengths first,
charge/check cancellation in bounded chunks, and transfer one explicitly owned
paint resource/reservation into the scene. Define shared-resource accounting so
multiple bands do not either hide storage or count the same stops repeatedly.
Test one-byte/one-work-unit denial, cancellation during CRC/unfilter/tile work,
cleanup after partial construction and retained prepared scenes. Add these workloads
to the existing probe instead of creating another resource tool. Explicitly gate
an affected feature if a safe short repair is unavailable; do not expand its syntax
while its resource contract is false. This is new code, not reopening the repaired
R04/R17 glyph/discovery defects.
Status 2026-09-24: retained gradient stop arrays transfer one scene-owned reservation per distinct array at scene-add time (shared href and band references counted once, empty arrays skipped), with scene-builder accounting, retained-scene, and budget-denial regressions. Per-wrapper object bytes remain unreserved, matching the existing glyph-wrapper granularity.

### R30 / P1 design, P2 GC — Geometry, paint and ownership are becoming entangled

**Source inspection; performance impact not yet benchmarked:**

| Duplication / weak invariant | Targeted direction |
| --- | --- |
| Geometry kind encodes GradientFill, PatternFill and GradientStroke; rectangle/line/band lowering, clipping, alpha stamping and stroke-band logic recur for each paint. Each added combination needs more branches. | Shared contour/shape geometry + typed paint + transform + clip/layer state. Introduce this through the first general-path vertical slice, preserving exact rectangle fast paths; no big-bang scene rewrite. |
| Per-band paint wrapper allocations; ClipTo/Stamped allocate more wrappers; every pattern band rerenders the same tile. | Scene-owned immutable paint resources referenced by typed IDs, per-conversion bounded tile reuse or direct solid-tile evaluation where equivalent. Benchmark many bands/uses and repeated/distinct paints before choosing a cache. |
| AxisTransform and bare doubles/rectangles mix original boxes, mapped geometry and device/paint coordinates; paint declarations stay strings across repeated resolution; masks spread related state over parallel dictionaries. | Validated affine value and explicit coordinate/bounds roles; cohesive typed mask/clip definitions. R28 is a concrete consequence, not just naming preference. Reconcile the concurrent mask work first. |
| Copyable scene views coexist with disposal authority; draw commands still carry sentinel indexes and variant fields. | One owner with borrowed views, variant factories and builder-validated handles. Preserve the R21 release repair and type-derived array sizes; avoid per-edge heap objects. |
| Discovery/lowering separately traverse XML and orchestrate some eligibility; property/paint parsing can repeat during lowering. | Share policy and resolved state where it prevents disagreement. Two passes for async font acquisition are intentional; do not merge the entire walkers or create one walker per feature. |
| Raw/budgeted glyph inspection paths and PNG sync/async sinks retain related decoding/framing logic. | Preserve already shared kernels and cross-path error/byte tests. Only extract further when the touched slice benefits; do not route production through unbudgeted inspection helpers. |

Measured earlier, not rerun: transformed glyph contour copies cost 688 B for the
triangle benchmark; snapshot setup about 30 µs/32 KB versus about 300 µs end to end.
These were reasonably deferred. The 16-entry cmap/advance cache, consecutive-glyph
outline reuse, cached SFNT ranges, single preparation hash and shared output scope
already exist; do not schedule their implementation again. AcquireAsync has a
documented point-in-time verification contract, with snapshot re-verification and
mutation regression; this is not an accidental missing global cache.

Also inspect full-viewport opacity surfaces/scans and the orthogonal band builder's
repeated all-edge intersection sorting on actual logo workloads. Limit layer
bounds/tile work only after exact clipping/composition tests. Report allocated
bytes/GC, live accounted peaks and working set separately. Avoid speculative
pooling or process-wide caches to make the ledger look better.

### R31 / P2 — Oracle/tooling tests need to cover the delivery workflow

**Source inspection; existing validator tests pass freshly:**

- capture-chrome.ps1 writes stdin, drains stdout, then stderr synchronously and
  waits without a parent timeout/tree kill. The CLI has its own cancellation,
  but that does not bound every pipe/parent failure. Use concurrent draining,
  bounded termination and failure cleanup; test a silent/flooding child.
- Capture width/height currently change the screenshot viewport, not the img's
  displayed size or device scale. There is no OutputScale-equivalent argument.
  Size-specific SVG variants are valid evidence for those changed inputs, but
  do not prove SvgRenderOptions.OutputScale equivalence on unchanged input.
  Pin explicit sizing/scale behavior and verify actual image bounds; capture
  same-input fractional-scale variants with complete provenance.
- Windows/Linux CI already builds/tests, runs memory/Chrome/font smoke, validates
  artifacts and tests packaging. Do not list these as absent. It does not invoke
  test-validate-ci-artifacts.ps1's negative suite; wire the existing suite in. Status 2026-09-24: repaired stale validator tables for the fresh-process image and pattern probe workloads (25-report outcomes, LimitExceeded codes, input hashes, success-PNG allowance) with updated self-test counts, verified by a full local 25-scenario run, and wired the suite into CI on both platforms with artifact upload.
  The browser smoke covers a fixed tiny fixture, not representative corpus
  regeneration. Add a reproducible opt-in corpus validation command/report.
- Many recent tests inspect lowered values or compare two paths through the same
  renderer. Those are useful semantics tests, not independent pixel truth.
  For each new output-affecting combination retain one discriminating oracle
  case; replace repetitive eligibility rows with a missing boundary/mechanism.
- No benchmark family yet characterizes curve-heavy logos, many bands/paints,
  clipped opacity layers or embedded image work. Add a small representative
  family with the relevant feature, not a benchmark project rewrite.

R22's portable hashes/provenance and R23's CDP cleanup/event repairs stay closed
for their original scope. This finding concerns the remaining wrapper/workflow.

#### R31a / P1 — Repair Windows Chrome sandbox setup before further capture batches

**User trace and read-only diagnosis, 2026-09-23:** repeated Chrome messages
`Sandbox cannot access executable` followed by network-service restart messages
point to restricted child-process startup, not SVG rasterization. The cached
chrome.exe and its directory lack explicit AppContainer access grants;
tools/setup-chrome.ps1 extracts/verifies Chrome but only configures executable
permissions on non-Windows platforms. This matches the documented
[Windows Chrome sandbox permission problem](https://pptr.dev/troubleshooting#chrome-reports-sandbox-errors-on-windows).
The likely diagnosis is permissions, not yet a demonstrated before/after repair.
A separate fresh capture completed in 10.93 seconds, exited zero and matched the
2x2 reference exactly, without these messages in captured stderr. Thus the trace
does not itself prove a hung agent or invalid pixels. Different browser PIDs can
be successive intentional fresh-process captures; DevTools readiness alone does
not establish successful capture or healthy child-process startup.

**Next concrete tooling change (bounded; do not interrupt another agent's process):**

1. In tools/setup-chrome.ps1, add an idempotent Windows permission setup/check
   for both newly extracted and already cached installations, including -Offline.
   Resolve the installation from the pin; validate its absolute path is inside
   the explicitly selected Chrome cache before changing any ACL. Verify pinned
   hashes first. Inspect the executable, required DLL/resources and relevant
   directory access under the actual account/environment launching captures.
2. Use the pinned Chrome installer's documented sandbox ACL mechanism or its
   specific application/capability identity where available. If using the
   documented ALL APPLICATION PACKAGES fallback (S-1-15-2-1), grant only inherited
   read/execute (OI)(CI)(RX) on that resolved Chrome installation, not write/full
   control. Preserve existing ACLs; check command exit codes and effective access.
   Do not reset permissions, modify the repo/home/TEMP roots, grant Everyone access,
   or add --no-sandbox / disable the network-service sandbox. If the required
   narrow ACL cannot be set, stop with an actionable error rather than retrying
   captures indefinitely. Keep Linux behavior and setup's executable-path-only
   stdout contract unchanged.
3. In ChromeProcess and tools/test-chrome.ps1, retain bounded per-launch Chrome
   stderr (not merely the outer dotnet wrapper), drained asynchronously and kept
   separate from binary PNG stdout. Surface sandbox access failures and repeated
   child-service crashes with the executable identity and elapsed time; redact
   private paths/input data where applicable. Do not treat every benign stderr
   message as failure. A successful PNG or the hard-coded sandbox='enabled'
   fingerprint must not conceal these startup errors. Keep the existing timeout,
   owned-process-tree cleanup and profile cleanup; never kill unrelated Chrome.
4. Test fresh extraction, cache reuse and offline reuse, denied ACL changes,
   bounded failure, and stderr/stdout separation. From the agent's actual launch
   environment, run three fresh-process pinned smoke captures: no sandbox access
   failures/restart loop, exact retained RGBA, normal completion and no orphan
   process/profile. Recheck representative captures without regenerating their
   references. Preserve hashes/recipe/security flags; report observed startup
   health separately from configured sandbox policy. Record before/after timings
   and sanitized diagnostics in docs/ORACLE.md and update this finding in place.

This is one setup/observability repair, not a browser upgrade or raster-recipe
change. No ACLs, launcher code or other agent processes were modified by this review.

### R32 / P1 — Embedded PNG decoding violates categorized errors and chunk policy

**Fresh public conversion reproductions:** a PNG with a damaged zlib header but
recomputed correct chunk CRC throws InvalidDataException outside SvgException.
Inserting an unknown CRC-valid critical ABCa chunk succeeds; inserting ancillary
abCD is rejected as an unrecognized critical chunk.

PngImageDecoder.IsCritical tests type[3]; the ancillary/critical bit belongs to
the first byte, while the fourth is safe-to-copy
([PNG chunk naming](https://www.w3.org/TR/png-3/#5Chunk-naming-conventions)).
This is not a reason to implement general image formats now.

**Next:** fix the bit, categorize compression/structure failures at the decoder
boundary without swallowing cancellation/limit exceptions, and add independent
CRC-valid corrupt-compression/chunk-order/truncation cases across public output
forms. Keep destinations untouched for parse failures. Pair with R29's decoder
repair; keep multi-color image/resampling expansion deferred to measured demand. Status 2026-09-24: bit fix and pixel categorization already landed (26ba8fa); this turn adds a programmatic PNG builder with CRC self-check plus order/truncation negatives (IDAT-before-PLTE, missing/trailing/second-IHDR, truncated header, overlong length, empty stream, post-IEND guard) and a four-input six-forms failure matrix with destination-untouched asserts. Late tRNS stays lenient (existing vectors, harmless).

## 4. Completed repairs and deferred text work

Do not reassign closed findings because an old paragraph said “still missing”.
The original reproductions remain in history; keep their regression coverage.

| Stable IDs / stages | Current disposition |
| --- | --- |
| R01/R02 | Original visible-glyph/contour, sibling/CDATA/empty-text and XML-tail repairs retained. |
| R03/R08/R10 | Shared style/admission and selected text/outline semantics repaired for the declared subset. Wider span/font/fallback/GPOS behavior remains S7/S15 work, not a shape-logo prerequisite. |
| R04/R12; S1–S5 | Original type sizing, surface cleanup, glyph sampling/decoder ownership, numeric boundaries, aggregate work, XML workspace/copy/membership charges repaired. Preserve phase-denial/cancellation tests. New paint/image gaps are R29. |
| R05/R06/R07/R09/R10/R11/R16 | Original matrix/cmap/face/identity/lifetime/diagnostic defects have retained fixes. Wider ordinary-font fidelity and sanitized diagnostic coverage remain separate requirements. |
| R13; S7 | Ordinary-font pixel/metrics evidence remains open. Synthetic glyph tests and successful private source access do not close it. |
| R14/R15/R19 | Several sharing/cache/typed-sizing repairs and allocation baselines completed. Remaining scoped duplication/GC work is R30, not an unlimited cleanup milestone. |
| R17 | Discovery collection reservations and fresh-process probes closed. |
| R18 | Original root-count/eligibility divergences repaired; retain discovery/lowering parity and add only discriminating new boundaries. Stop open-ended row enumeration. |
| R20 | First-write-dependent IDAT buffering/output-limit repair closed; common CRC/filter kernels shared. Preserve high-entropy and six-output equivalence tests. |
| R21 | Prepared-scene detachment after disposal/consumption repaired; borrowed-memory lifetime rules still apply. |
| R22 | Portable fixture bytes, pixel-vs-PNG-byte comparisons and provenance validation repaired. |
| R23 | Original bounded CDP/event/cleanup defects repaired; remaining capture wrapper is R31. |
| R24 | Impossible channel wrap fixed by 476fd00. Remaining one-step alpha/RGB differences are unresolved S11 fidelity work; see R27. |
| R25 | Definition ancestor-transform fault fixed by bb63df3; inherited definition presentation is not thereby fully solved. |

S6's single-use prepared scene and shared preparation-through-output accounting
exist. S7's generic opt-in font host exists, with local/stdin/HTTP/lazy/retry and
bounded-cache tests. The remaining S7 step is actual intended-font inventory,
metrics and pixel validation under section 6. No new private-source authorization
is needed, but no private lookup is needed to deliver outlined wordmarks either.

## 5. Ordered implementation queue — take this before peripheral extensions

Keep one active delivery slice plus its necessary correctness blockers. P0 safety
issues can interrupt it; unrelated font hardening or arbitrary feature combinations
cannot. Section 8 is a requirements checklist, **not ten serial prerequisite
projects**. S9 need not be complete before S10/S11 starts.

Before another large Windows oracle batch, take the bounded R31a sandbox setup
repair above; avoid repeated failed browser launches, then resume the active
general-path slice. This does not reopen the broader tooling backlog as a gate.

| Order | Bounded work package | Exit / next handoff |
| --- | --- | --- |
| 0 — finish the in-flight slice | Reconcile current mask/context edits and tests; incorporate any changed evidence here. Preserve R25 and exact references. | Clean, buildable change; no unrelated audit rewrites or additional mask features. |
| 1 — make current results trustworthy | Retain R28 cases; fix or gate the bad paint maps. Fix R29/R32's newly introduced resource/error violations. Split actual support/fidelity in the corpus and run every entry (R27). Begin representative inventory (R26). | Known mismatches cannot masquerade as unsupported-but-safe or exact. Each repaired semantic domain has an independent regression and bounded cleanup/work proof. This is a finite repair pass, not a new hardening campaign. |
| 2 — **critical path: general filled paths** | Deliver S10/S11 together through the vertical slices below, with only the shared style/paint-map support needed by each. Use existing negative research instead of restarting supersampling experiments. | Exact non-orthogonal flat marks and outlined wordmarks across fractional coordinates, transforms, holes and output scales. This is the first useful logo milestone. |
| 3 — common stroke/export combinations | General stroke outlines/caps/joins, then corpus-required dashes/non-scaling behavior (S12); practical cascade/local use/symbol support and arbitrary path clips (S9/S14). | Independent exporter examples and mixed fill/stroke/clip/group-opacity cases, not only isolated syntax. |
| 4 — complete common paints/composition | Correct reusable linear gradients, transparent/multiple stops and radial gradients; nested clips/groups and corpus-required masks (S13/S14). | Exact transformed/clipped/alpha cases across frozen document variants. Do not extend the current per-paint geometry hierarchy. |
| 5 — coverage-driven completion | Live-font wordmarks (S7/S15) and images/patterns/markers/filters (S16), ordered by measured blockers. Package/platform/resource report (S17) runs throughout and closes the milestone. | Agreed exact document-coverage target achieved; categorized residual gaps and Windows/Linux evidence. |

### S10/S11 vertical slices and decision points

1. **Characterize the raster route and compositor.** Retain the existing
   integer/fractional/half-grid triangles, overlaps, circle and cubic references.
   Add a small hold-out family spanning phase, orientation, size, opacity and
   background. Establish which browser raster/compositing mechanisms explain the
   observations; source revision alone does not prove the executed backend.
   Record the hypothesis before implementation and its complete result afterward.
   Resolve the R24/R27 alpha cases here with general rules, not fixture-specific
   branches. A mechanism that fails the hold-out is not an admission candidate. Status 2026-09-24: the hold-out gains two apex partial-row probes (diagonal-blue-apex-8x8 promotes the blue-alone residual configuration to retained evidence; diagonal-shallow-apex-8x8 isolates a two-edge fractional apex); both show the same directional analytic-deviation family with repeated +19/+11 absolute left-edge offsets. The exact replica remainder is still open; the diagonal gate stays held. 2026-09-24 variant round: exact-subdivision, partial-HALF-gating, tops-bare and acc-carry-removal eliminated with 14-harness evidence; row-2 grid maps the solution manifold but needed values are overconstrained under verified formulas. Gate still held; next is goY and joint-switch transcription. 2026-09-24 header round: pinned SkAnalyticEdge.h fetched read-only; goY, SkFixed semantics, inverse table (0 diffs), branches and convex HALF handling all match the replica; grid manifold refined with FAM-A minimal solution; overlap rows 3-4 reframed as compositing follow-up. Next is a switch-vertex micro-probe family. 2026-09-24 switch family: 2/5/0 mismatches across switch snaps 2.25/2.5/2.75 (275 exactly clean); yShift clamp refuted (breaks 6 exact); five input variants byte-identical (inputs faithful). Next is a denser same-bucket pair sweep. 2026-09-24 joint-continuity round: five input variants byte-identical (inputs faithful); needed row-2 values reframed as joint-local shift of dying and birthing edges with surviving edges fixed; uniform families stay eliminated. 2026-09-24 dense sweep: 0/2/0/5/0/0/0/0 across eight switch positions; same-bucket pairs diverge (exact-geometry tracking, buckets dead); kMaxAbsA confirms table path everywhere; five probes retained manifest-gated. 2026-09-24 slope family: identical five-pixel residuals (plus 1/3/2/3) across left slopes 0.6/0.8/1.0/1.1; deltas constant while absolutes move; three probes retained. 2026-09-24 joint-switch round: handoff traces show carried equals fresh fX at every switch top; V6 fresh-fX is bit-identical on all eight singles, V7/V8 span-formula routing is load-bearing elsewhere but blue-invariant, V9 birth-phase freedom breaks twelve probes while blue stays at 5; gate held, next is continuing-acc masking or R2 dispatch. 2026-09-24 onset mapping round: residual switches off discontinuously between A.y 2.40625 (5) and 2.4375 (0) with intermediates clean; branch paths identical across the boundary, only strip-2 row-2 ll differs by one quantum (81920 versus 86016) from dx64 table entries 320 versus 318; gate held, next is unmasked-operand branches or table-entry rounding. 2026-09-24 rational audit round: exact nL sits on the snap boundary (86016) with replica one below; pinned quick_div confirms the table path so dx is dead at source level; swap grids prove no nearby inputs reach oracle row 2 (structural gap, not parametric); gate held, next is narrow-span micro-probes or blitter selection re-read. 2026-09-24 blitter re-read round: pinned dispatch, swaps, joins, narrow-span selection and both alpha primitives all match the transcription for blue inputs (one real but unfired collapse-formula divergence); transcription verified twice, promoting rev-versus-backend mismatch to prime suspect; gate held, next is narrow-span micro-probes. 2026-09-24 attribution grid round: row-2 split into strip parts shows (ul 49152, ll 77824) nails x=0,1 exactly but x=2,3 need an incompatible strip-1 lDY; 340-combo grid never reaches oracle row; input-side search exhausted, structural difference confirmed; gate held. 2026-09-24 translation round: rigid plus-one-pixel translation keeps replica identical (0 shifted diffs) but oracle goes from 5 mismatches to fully exact; backend absolute-position dependence proven with confounds ruled out; position-independent formula theories reframed as suspect; gate held, next is fixed-position micro-probes. 2026-09-24 position-map round: seven rigid x-translations show the residual at exactly one absolute x (narrower than plus-or-minus 1/16px), plus-1 y preserves all five values shifted, RGB pure blue; x-singular y-invariant alpha-only defect outside modeled fixed-point math; gate held, next is Blink-side or backend-selection evidence. 2026-09-24 paint-independence round: red and green variants show byte-identical alphas to blue (residual included) with exact paint RGB; coverage factorization holds, residual purely geometric and any fix paint-universal; gate held. 2026-09-24 compositing closure round: harness composite rule with true blue masks closes all five overlap pixels exactly (alpha and RGB, zero free parameters); overlap independently confirms blue solo values; compositing exonerated, full residual in coverage; gate held. 2026-09-24 render-entry audit round: mask-plus-convex path verified for small probes, snapAlpha refuted behaviorally, bounds/fLeft/composition exact; today blue captures byte-identical to March reference (oracle stable across sessions, position effect is input-driven); gate held. 2026-09-24 audit-scares-clean round: swap-grid lDY values re-verified direct from Edge2 (blue 10922, on 10979 stand); dy dispatch keys on computed dx magnitude; three hand-arithmetic slips caught before the record; gate held. 2026-09-24 split-necessity round: integer-switch probe still shows seven oracle-above mismatches (splits unnecessary); sliver shortfall identical across partial and full rows with same span inputs (row-kind independent); wide-span control clean; gate held. 2026-09-24 cross-version round: Chrome 132 renders blue byte-identically to 153 (recent regression dead); identity-transform byte-identical (CTM clean); smooth-jump verified non-firing; gate held, next is fixed-position micro-probes. 2026-09-24 interaction-table round: real 225 rerun confirms two mismatches (wrong-triangle five-diff run discarded); mismatch iff pixel 0 meets first 12288 or 28672 with T1 pixel-1 control; src-over eliminated by direction; strip inversion rejected as geometrically absurd; gate held. 2026-09-24 toolchain audit round: ffmpeg agrees byte-for-byte with the harness reader; residual fires iff apex-x is 0.78125 (m1/m2/slopes) with an invariant exact sliver doubling (8 versus 4) and pure-blue RGB; no single input tweak reaches oracle values under verified rules; gate held.
2. **First non-orthogonal end-to-end slice.** Typed contour buffer + full affine
   mapping + solid fill + bounded coverage for a diagonal closed polygon.
   Separate geometry expectations from oracle coverage; include rotations,
   reflection, fractional phases, clipped/off-canvas edges and output scaling.
   Integrate through the public renderer, preserving rectangle equivalence. Status 2026-09-24: pure-geometry affine kernel landed (SvgAffineTransform: matrix/translate/scale/rotate-about-center/skew composition with LimitExceeded on non-finite Apply, 18 unit tests); no renderer wiring and no admitted-syntax change; contour buffer chaining affine/flatten/arc-sampling landed (SvgContourBuilder with fixed explicitly owned storage plus SvgEllipticalArc.Sample, 17 unit tests); still no renderer wiring and no admitted-syntax change. Status 2026-09-24: first renderer wiring landed for solid single-contour diagonal polygon/polyline/path fills (SvgPolygonFill through winding/evenodd coverage under the rectangle quarter-pixel output discipline, 12 end-to-end tests); multi-contour, curved and hole wiring still open. Status 2026-09-24: multi-contour holes wired (retained contour ends plus even-odd nesting signs, opposite-wound nonzero and nested even-odd holes with analytic alpha sums, touching contacts still rejected, 17 end-to-end tests); curved fills still open. Status 2026-09-24: curved fills wired (integer C/S/Q/T plus uniform-positive arcs through affine flattening at quarter-pixel flatness into the shared polygon machinery with finite vertex mapping, anisotropic output/distortion guards and bounded expansion, 12 end-to-end tests); slice 3 geometry complete except touching admissions and element-level shapes. Status 2026-09-24: element shapes wired (circle/ellipse elements plus nonzero-rx/ry rectangles through sampled outlines with quarter-discipline coordinates, paint-server and stroke combinations explicitly rejected, 18 end-to-end tests); remaining slice-3 work is touching admissions, anisotropic arcs and text. Status 2026-09-24: fractional path coordinates wired (straight fractional paths through the full-parser device pipeline with finite output mapping, integer paths unchanged on the exact pipeline, 5 end-to-end tests; 18 diagonal research probes promoted to measured known-differences with oracle references retained); fractional curves stay explicitly rejected pending calibration closure. Status 2026-09-24: fractional point lists wired (lexical routing shared with fractional paths, double-precision tokenizing with path-mirrored malformed taxonomy, device-mapped fills plus device-contour strokes through the shared polygon machinery, 3 cross-element equivalences byte-exact plus malformed/degenerate negatives, 7 end-to-end tests). Status 2026-09-24: empty and whitespace-only transforms lower as identity across element, gradient and pattern transforms (3 end-to-end tests).
3. **Compound and curved fills.** Extend the same representation to multiple
   contours, nonzero/evenodd, touching/coincident/self-intersections; quadratic/
   cubic curves and smooth commands; then elliptical arcs and normalized circles,
   ellipses/rounded rectangles. Use bounded device-space subdivision/edge work.
   Admit each family only when its full oracle family is exact; do not publish
   parser-only completion as rendering support. Status 2026-09-24: pure-geometry elliptical-arc normalization kernel landed (SvgEllipticalArc: endpoint-to-center conversion with radii correction, rotation, large-arc/sweep selection and line degeneracies, LimitExceeded on overflow, 14 unit tests); no parser/renderer wiring and no admitted-syntax change. Status 2026-09-24: nonzero winding coverage landed (SvgPolygonCoverage.GetWindingCoverage: signed multi-contour sums over flat points plus explicit ends, opposite-wound holes subtract, 8 unit tests); evenodd nesting still open, still no renderer wiring. Status 2026-09-24: evenodd nesting landed (SvgContourNesting parity signs plus SvgPolygonCoverage.GetEvenOddCoverage: orientation-free holes, 15 unit tests); touching/coincident contours still excluded, still no renderer wiring. Status 2026-09-24: contour structure classification landed (SvgContourStructure: zero-length/crossing/coincident/touch flags within and between contours with strict nonzero-sign crossing test, 12 unit tests); touching family detected but unadmitted, still no renderer wiring. Status 2026-09-24: line path-data parser landed (SvgPathDataParser: M/L/H/V/Z with repeats, implicit lineto, compact decimals/exponents/sign-abutment, MalformedInput versus UnsupportedFeature taxonomy, command/work/point budgets, 19 unit tests); curves still unadmitted, still no renderer wiring. Status 2026-09-24: full path-data commands landed (SvgPathDataParser.ParsePath: C/S/Q/T with reflected controls, A with strict flags, eager flattening through the contour buffer, 13 curve tests); arc radii pass in the device frame, still no renderer wiring. Status 2026-09-24: pure-geometry quadratic/cubic flattening kernel landed (SvgCurveFlatten: adaptive device-space midpoint subdivision with explicit flatness/depth/expansion policy, LimitExceeded on depth/exhaustion/overflow, 20 unit tests); no parser/renderer wiring and no admitted-syntax change. Status 2026-09-24: closed-curve shapes landed (SvgEllipticalArc.SampleFull full-ellipse loops plus SvgContourBuilder.AddEllipse/AddRoundedRect with clamping and sharp fallback, 14 unit tests); element-attribute parsing deferred to wiring, still no renderer wiring.
4. **Outlined-wordmark checkpoint.** Retain independently selected outlined text
   logos with counters and many contours, plus flat curved marks, at intended,
   small and large sizes. Run all backgrounds/scales declared for each document.
   Report which earlier inventory blockers disappeared and which remain.

Limit each calibration iteration to one new discriminating mechanism and a
written exact result. If two successive iterations make no cross-family progress,
record the eliminated hypotheses and a new investigation/architecture decision;
do not divert into another week of easy pattern/attribute admissions. Keep
pixel-exactness and dependency constraints unchanged. Escalate genuine backend/
feasibility uncertainty rather than implying that adding more tests removes it.

Every implementation change includes its regression, relevant support docs and
actual verification. Tests should demonstrate a new semantic boundary, resource
failure or independently checked image—not merely add another spelling to a
large acceptance matrix. Update status/next action in place, not another “Landed”
paragraph. Keep this plan free of contradictory dates and obsolete totals.

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
| Accounted renderer storage | 256 MiB; renderer-owned accounting, not process/host memory. New gaps: R29. |
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

This section preserves the broader scope; **section 5 determines execution order**.
Existing narrow support is partial implementation, not completion of a stage.
A path-only logo must work without a font provider. “Good enough” means enough
documents supported exactly, not approximate output for every SVG.

| Stage | Current status | Remaining decisive result |
| --- | --- | --- |
| S8 — representative inventory/oracle | Synthetic census and retained oracle mechanism exist; representativeness incomplete (R26/R27). | Independent logo-export inventory, honest per-document/all-variant outcomes and agreed target. |
| S9 — static export/style/reference handling | Partial restricted shared resolver, nested viewports/use/symbols/CSS. | Practical exporter combinations with a coherent typed paint/reference model. |
| S10 — general geometry/coordinates | Orthogonal bands only; integer axis transforms. | Complete planned paths/basic shapes, compound contours, fractional coordinates and affine maps. |
| S11 — exact coverage/composition | Rectangle fast paths and substantial negative calibration evidence; general solution missing. | Cross-phase/scale/alpha exact coverage and source-over for general contours. Critical path. |
| S12 — strokes | Restricted even-width orthogonal bands and no-op default declarations. | Shared outline coverage, caps/joins/fractional widths and corpus-required dashes/transforms. |
| S13 — paint servers | Restricted 2–8-stop opaque linear gradients and paint-server strokes; R28 defects. | Correct paint maps, alpha/stops/radial gradients, reuse and interaction coverage. |
| S14 — clips/masks/layers | Restricted rectangle clips/masks/isolated opacity; concurrent mask work. | Arbitrary clip paths, nested composition and corpus-required general masks. |
| S15 — live text | Font infrastructure and experimental synthetic text; S7 oracle incomplete. | Same verified intended-font layout/pixels integrated with shared geometry/paint. |
| S16 — extensions | Narrow single-rect patterns and solid-PNG-as-rectangle support. | Only extensions justified by measured document blockers; no general image/effect claim. |
| S17 — logo milestone validation | Strong component/engineering tests; representative milestone unproven. | Frozen coverage target, all variants exact for admitted documents, package/platform/resource evidence. |

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
Status 2026-09-24: direct local use of polygon/polyline/path/circle/ellipse fills wired (list-holding definitions with transform/placement/opacity/clip composition, backward and forward references, 13 end-to-end tests); forward multi-geometry references wired (in-place placeholder expansion preserving paint order, 2 end-to-end tests); solid-fill use instancing complete, paint-server instances stay rectangle-only. Status 2026-09-24: definition-embedded linear-gradient bands instance per band through the shared vector and opacity mapping (3 end-to-end tests including a pattern boundary negative); pattern-filled definitions stay explicitly unsupported. Status 2026-09-24: symbol-held linear-gradient bands wired through viewBox composition into the shared instance lowerer (2 end-to-end tests). Status 2026-09-24: definition-embedded pattern bands instance per band through lossless tile synthesis (2 end-to-end tests); symbol-held pattern content stays explicitly unsupported. Status 2026-09-24: stroked-rectangle definitions instance through direct local use (uniform width scaling, multiplied opacity and viewport containment, 7 end-to-end tests including non-uniform, symbol and boundary negatives); symbol-held strokes stay explicitly unsupported. Status 2026-09-24: symbol-held fill-plus-stroke polygon pairs admitted with uniform outline scaling (2 end-to-end tests, no production change required). Status 2026-09-24: gradient-stroke rectangle definitions route through the stroke lowerer and instance with mapped bands, with line instances locked equivalent (3 end-to-end tests). Status 2026-09-24: symbol-held gradient-stroke rectangles wired through viewBox composition into the shared stroke lowerer (2 end-to-end tests). Status 2026-09-24: repaired the missing nested-viewport frame shift on direct polygon-use instances (points now compose exactly like rectangles, lines and bands; the silently-wrong offset is closed with 2 end-to-end tests). Status 2026-09-24: symbol-held polygon and band fills wired through uniform viewBox maps (per-point and per-band mapping with placement/transform/opacity/viewport-clip composition, backward and forward references, 9 end-to-end tests); non-uniform symbol polygon maps stay explicitly unsupported. Status 2026-09-24: masked polygon fills wired through shared contour storage with ownership transfer (direct, stroked-pair, circle and use-instance masks, 6 end-to-end tests). Status 2026-09-24: fill overrides on unstyled single-geometry polygon and band definitions wired (4 end-to-end tests). Status 2026-09-24: forward-expansion hardening (two interleaved multi-geometry forwards preserving order, paint-by-forward equivalence, masked-group scope expansion, 3 end-to-end tests, no production change required). Status 2026-09-24: reference-boundary hardening (non-geometry definitions stay inert with explicit use failures, masked forward references resolve identically, 2 end-to-end tests, no production change required). Status 2026-09-24: ordered early root stylesheets accumulate with document-order cascade (3 end-to-end tests plus the reshaped late-sheet gate theory). Status 2026-09-24: symbol-held solid rectangle strokes wired through uniform viewBox maps as fill plus four viewport-clipped stroke bands (uniform-scale even widths, integer-mapped geometry, paint order preserved; 2 direct-equivalence, 1 viewport-clip and 3 boundary negatives, superseding the earlier symbol-stroke gate); non-uniform symbol/instance scales and fractionally-mapped strokes stay explicitly unsupported. Status 2026-09-25: symbol-held pattern bands wired through viewBox composition into the shared instance lowerer (tile origin, dimensions and content mapped through the viewBox with integer-grid gating; identity and scaled direct-equivalences plus non-integer tile gate and forward/backward equivalence, 4 end-to-end tests; scaled 64x64 probe renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-symbol-pattern-mark).

### S10 — General filled geometry and transforms

- Parse absolute/relative M/L/H/V/C/S/Q/T/A/Z, repeated parameters, implicit lines
  after moveto, reflected curve controls, compact signed decimal/exponent syntax
  and strict arc flags. Preserve contour starts and closure. Handle radii
  correction, arc rotation, degeneracies and malformed tails explicitly.
- Preserve multiple contours/holes, either orientation, touching/coincident and
  self-intersecting contours under nonzero/evenodd. Distinguish implicit fill
  closure from explicit stroke closure.
  Status 2026-09-24: integer straight-path fills seal every unclosed subpath implicitly (plus explicit return-to-start), lowering identically to the closed form and matching the general curved-path parser; stroke keeps explicit-closure tracking.
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
Status 2026-09-24: polyline-subset kernel landed (SvgStrokeOutline: butt caps, miter/bevel joins, open/closed/degenerate paths, 13 kernel tests); curves, round caps/joins, dashes and renderer wiring remain open. Status 2026-09-24: admitted-subset stroke wiring landed (solid even-width butt/miter polygon/polyline/path strokes with per-subpath closure tracking, uniform-scale gating and nonzero outline fills, 17 end-to-end tests); round/square caps, round/bevel joins, dashes, odd widths, shape strokes and non-uniform scales remain open. Status 2026-09-24: solid circle strokes wired as opposite-wound sampled ring fills through the shared polygon machinery (positive widths, validated-only miterlimit, fill-plus-stroke paint order, definition/use/mask composition; renders with measured oracle delta, 267 pixels with maximum channel error 255 on faint slivers and maximum alpha error 46, retained as known-difference census entry original-logo-circle-stroke-mark mirroring the circle-fill standing; 13 end-to-end tests); non-uniform ellipse stroke maps stay explicitly unsupported. Status 2026-09-25: solid ellipse strokes wired as opposite-wound sampled ring fills through the shared polygon machinery (uniform-positive scales keeping width exact, validated-only miterlimit, circle-sized ellipses collapsing byte-exactly onto circle strokes, dashed ellipse strokes gated; renders with measured oracle delta, 235 pixels with maximum channel error 255 on faint slivers and maximum alpha error 63, retained as known-difference census entry original-logo-ellipse-stroke-mark mirroring the circle-fill standing). Status 2026-09-24: round caps and round joins wired through the shared stroke outline kernel as flatness-bounded arc fans (outer arcs with plain inner corners, semicircle reversal tips, presentation-attribute cap/join threading with butt/miter defaults; 3 kernel geometry tests plus 6 end-to-end tests with band-stroke butt gates on rectangle, line, gradient and symbol-rectangle strokes); renders with measured oracle delta, 78 pixels with maximum channel error 59 and maximum alpha error 59, retained as known-difference census entry original-logo-stroke-round-mark; square caps, dashes and round caps on band strokes stay explicitly unsupported. Status 2026-09-25: square caps wired through the shared stroke outline kernel as half-width tip extensions (butt outline with extended tips, closed contours provably unaffected, presentation-attribute threading; 1 kernel geometry test plus 3 end-to-end tests with a square-cap band-stroke gate; renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-stroke-square-mark). Status 2026-09-25: dashes wired through the lowerer as device-space pattern runs sharing one nonzero outline fill (bare-number/px entries with at most eight, odd-doubling, negative rejected as malformed, all-zero patterns painting no stroke, zero-length dashes gated with round/square caps, seam-touching runs on closed loops merged preserving joins; an initial split-at-seam formulation mismatched the closed oracle by 4 pixels at the seam miter and an unconditional merge now matches byte-exactly including pattern-aligned seams; butt-offset probes byte-exact pinning the phase convention while round-cap dash ends carry the standing round-fan residual; 8 parse plus 7 pixel tests with band/circle/inline dash gates; retained as exact oracle entries original-logo-stroke-dash-mark and original-logo-stroke-dash-closed-mark). Status 2026-09-25: pathLength normalization admitted scaling dash patterns by computed over asserted length across whole elements (zero pinning a solid stroke, negative rejected as malformed, inline path lengths gated; an initial inverted factor mismatched by 78 pixels and the corrected direction matches with cut-edge-only residuals; multi-subpath totals byte-exact; 3 parse plus 3 pixel tests; single-subpath probe renders with measured oracle delta, 5 pixels with maximum channel error 102, retained as known-difference census entry original-logo-stroke-pathlength-mark). Status 2026-09-25: non-scaling-stroke admitted on kernel and ring strokes with fixed width through element transforms including non-uniform ones (uniformity still required for ring geometry and scaled viewports; dashes keep scaling normally with non-uniform dashed strokes gated; band strokes and stylesheet/inline/inherited vector effects stay explicitly gated; uniform, non-uniform, dashed and circle equivalences byte-exact across 7 pixel tests with unknown-value and band gates; non-uniform 64x64 probe renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-stroke-nonscaling-mark). Status 2026-09-25: pattern-url strokes admitted on rectangles and lines through shared tile-grid evaluation (solid fill plus four tile bands on rectangles, single tile band on lines; even widths, butt caps, unscaled viewports, pattern dispatch in direct, definition and line paths with fill combinations still gated; converted rect rejection plus line admission and solid-tile equivalence, 3 end-to-end tests; half-tile rect and line 64x64 probes each renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entries original-logo-pattern-stroke-mark and original-logo-pattern-line-stroke-mark). Status 2026-09-25: gradient fills pair with pattern-evaluated stroke bands through the shared fill helpers (pattern-stroke lowering resolves linear and radial fills with categorized pattern-fill and missing-reference gates, pattern dispatch ordered before the syntactic gradient dispatch, presentation bypass for url fills; linear and radial admissions plus a solid-pair plumbing equivalence, 3 end-to-end tests; linear 64x64 probe renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-gradient-pattern-stroke-mark, while the radial probe renders with measured oracle delta of 6 pixels at maximum channel error 1 and maximum alpha error 0, retained as known-difference census entry original-logo-radial-pattern-stroke-mark in the standing one-step family; pattern fills with gradient or pattern strokes stay explicitly gated). Status 2026-09-25: pattern fills pair with gradient-evaluated stroke bands through fill-plus-band geometries (pattern lowering resolves linear and radial stroke gradients with degenerate and single-stop solid rings, multi-geometry return unwrapped through the paint-server funnel, categorized pattern and missing-reference gates; linear and radial admissions plus a solid-pair plumbing equivalence, 3 end-to-end tests; pattern-fill linear 64x64 probe renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-pattern-fill-gradient-stroke-mark, while the radial probe renders with measured oracle delta of 3 pixels at maximum channel error 1 and maximum alpha error 0, retained as known-difference census entry original-logo-pattern-fill-radial-stroke-mark in the standing one-step family; pattern fills with pattern strokes stay explicitly gated). Status 2026-09-25: pattern fills pair with pattern-evaluated stroke bands through the shared fill helper (pattern-stroke lowering resolves pattern fills with the fill-only path refactored onto the helper, fill-plus-band geometries; admission plus a solid-pair plumbing equivalence, 2 end-to-end tests; two-tile 64x64 probe renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-pattern-fill-pattern-stroke-mark, completing the rectangle fill-by-stroke paint matrix). Status 2026-09-25: square caps admitted on axis-aligned line bands through endpoint extension (half-width tip growth in all four line lowerings with a shared gate and attribute-policy allowance, round caps still gated; extension parse plus extended-butt pixel equivalence, 3 end-to-end tests; solid 64x64 probe renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-line-square-cap-mark).

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
interpolation across unseen colors/slopes, not just a four-pixel red-blue ramp. Status 2026-09-24: fill opacity composes onto gradient and pattern fills while solid strokes keep element opacity (7 end-to-end tests with opacity-transitive equivalence). Status 2026-09-24: single-stop linear gradients render their stop color as a solid, bypassing dithered interpolation (6 end-to-end tests); single-stop gradient strokes stay explicitly unsupported. Status 2026-09-24: translucent gradient stops admitted through the calibrated interpolation rule (undithered linear alpha, premultiplied-quantized dithered RGB; 4 end-to-end tests with stop-opacity and rgba equivalence, opaque suite proving identity). Status 2026-09-24: translucent fades compose through direct, forward, symbol, masked and nested-viewport paths identically (5 end-to-end tests, no production change required). Status 2026-09-24: fade probe promoted to a ratcheted known-difference census entry (original-logo-gradient-fade-mark, 62 pixels, max channel 128 at near-zero alpha, max alpha 0, three identical captures retained). Status 2026-09-24: transparent-stop interpolation calibrated offline against three byte-identical pinned captures (straight sRGB ramp, undithered linear alpha, premultiplied-quantized dithered RGB reducing to the current rule at full opacity; 962/1024 exact with boundary-only residuals, recorded in RASTERIZATION_DESIGN.md). Status 2026-09-24: duplicate/falling stop offsets admitted as hard edges (offsets rise to the previous stop, later stop wins at the shared offset; zero clamped ranges stay unsupported) with segment-walk later-wins evaluation; 3 parse tests plus an analytic even-width pixel test and a retained exact oracle entry (original-logo-gradient-duplicate-mark, 9x2 odd-width exact-hit, three identical captures, renderer byte-exact). Status 2026-09-24: concentric objectBoundingBox radial gradients wired end to end (dedicated parse/template/registration parallel to linear with shared stop scratch and clamp-up ordering, new radial geometry kind with shared scene stop reservations, shared stop-interpolation ramp with linear, normalized-space evaluation on rects and orthogonal bands; 15 parse, 6 pixel and 1 budget tests (12 boundary negatives); renders with measured oracle delta, 51 pixels with maximum channel error 1 and maximum alpha error 0, retained as known-difference census entry original-logo-radial-mark; research-radial-concentric-64x64 graduated to measured rendering in place; 8-bit blend-quantum hypothesis for the one-step residual refuted by 2116-pixel recomputation; spreads, foci, transforms, strokes, masks and uses stay explicitly gated). Status 2026-09-24: userSpaceOnUse radial circles wired through the referencing transform (uniform-scale gating, percentage and transformed-viewport rejection, href units inheritance, no reflection mirroring needed; bounding-box equivalence, scaled-mapping, reflection-invariance and href-units equivalences byte-exact, 3 parse plus 4 pixel tests with 3 boundary negatives). Status 2026-09-24: offset radial foci strictly inside the circle admitted (focal defaulting to the center, registration-time inside-circle check, href focal inheritance with partial-override gating, userSpace focal mapping, ray-circle evaluation with a concentric fast path; focal-at-center, band, href and userspace-mapping equivalences byte-exact plus an analytic focal-pixel test, 8 parse plus 6 pixel tests; offset 64x64 probe renders with measured oracle delta, 56 pixels with maximum channel error 1 and maximum alpha error 0 across three identical pinned-Chrome captures, the same one-step residual family as the concentric entry; on-or-outside-circle foci, focal radii, spreads, transforms, strokes, masks and uses stay explicitly gated). Status 2026-09-24: radial repeat/reflect spreads admitted on the circle parameter with href spread inheritance and overriding (pad clamping preserved; pairwise spread renders differ, repeat band and spread-href equivalences byte-exact, analytic wrap tests prove repeat returns to the start stop at the circle edge and both wraps return two radii out; 64x64 repeat and reflect probes each render with measured oracle delta, 65 pixels with maximum channel error 1 and maximum alpha error 0 across three identical pinned-Chrome captures per probe, the same one-step residual family as the concentric entry; focal radii, radial transforms, strokes, masks and uses stay explicitly gated). Status 2026-09-24: radial gradientTransform admitted for finite translate, uniform positive scale and uniform positive axis-aligned matrix forms composed eagerly into the circle at registration (center, focal point and radius; inherited circles included) with rotated, skewed, non-uniform, non-positive and non-finite forms gated; translate/scale/matrix, band and transform-href equivalences byte-exact, 7 parse plus 4 pixel tests; translate and scale 64x64 probes each render with measured oracle delta, 51 pixels with maximum channel error 1 and maximum alpha error 0 across three identical pinned-Chrome captures per probe, exactly the concentric-entry residual; escaping focal circles, strokes, masks and uses stay explicitly gated). Status 2026-09-24: contained focal radii admitted through two-circle cone evaluation (radius defaulting to zero with negative rejected as malformed, strict focal-circle containment validated at registration, href inheritance with partial-override gating, gradient-transform and userSpace scaling; concentric and offset bands plus focal-radius href equivalences byte-exact with an analytic focal-circle-edge test; an initial distance-ratio formulation for offset circles mismatched the oracle by 1769 pixels at max channel error 4 and was replaced by the cone root, after which concentric and offset 64x64 probes render with measured oracle deltas of 43 and 51 pixels with maximum channel error 1 and maximum alpha error 0 across three identical pinned-Chrome captures per probe, the same one-step residual family; 9 parse plus 5 pixel tests; gradient fills with gradient strokes, single-stop and degenerate radial strokes, bounding-box line strokes and pattern strokes stay explicitly gated while radial use, symbol and mask composition is admitted (direct and symbol instances with bounding-box and userSpace placement, paint-server fills, single-value masks on rects and bands exactly like linear fills with non-uniform userSpace symbol scaling gated; use, symbol, paint-server, white-mask and masked-use equivalences byte-exact across 4 parse plus 7 pixel tests; white-masked and twin-instance use 64x64 probes render with measured oracle deltas of 51 and 54 pixels with maximum channel error 1 and maximum alpha error 0 across three identical pinned-Chrome captures per probe, the same one-step residual family). Status 2026-09-24: radial strokes admitted as shared fill-kind evaluation with no new geometry variant (rectangle strokes lower to one solid fill plus four radial-evaluated bands sharing the placement box through the widened multi-geometry rectangle funnel; line strokes lower to one radial-evaluated band and require userSpaceOnUse exactly like linear lines after the oracle rejected bounding-box line bands by 384 pixels at max channel error 255 over the degenerate zero-height line box; butt caps/joins, even widths, unscaled viewports and definition/use/symbol/mask composition free; band-equality, href and stroke-use equivalences byte-exact across 5 parse plus 4 pixel tests; rectangle and userSpace line 64x64 probes render with measured oracle deltas of 3 and 5 pixels with maximum channel error 1 and maximum alpha error 0 across three identical pinned-Chrome captures per probe). Status 2026-09-25: single-stop linear and radial gradient strokes render as solid stop-color bands through raster-side constant evaluation (opaque stops provably dither-neutral; 3 converted admissions plus a solid-stroke pixel equivalence; rectangle 64x64 linear and radial probes each renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entries original-logo-gradient-single-stop-stroke-mark and original-logo-radial-single-stop-stroke-mark); degenerate multi-stop strokes stay explicitly unsupported. Status 2026-09-25: degenerate pad-spread gradient strokes lower to solid end-stop rings exactly like degenerate fills (rect sites mirroring the admitted line lowerings, non-pad degenerate still gated; 2 converted admissions plus an end-stop pixel equivalence; red-to-blue linear and radial 64x64 probes each renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entries original-logo-gradient-degenerate-stroke-mark and original-logo-radial-degenerate-stroke-mark). Status 2026-09-25: gradient fills pair with gradient-evaluated stroke bands through fill-plus-band geometries sharing the placement box (stroke methods own combined lowerings with fill helpers mirroring fill-only semantics, fill-less stroke placeholders preventing double-painting, presentation bypass for url fills; converted rejection plus radial and plumbing equivalences, 4 end-to-end tests; linear 64x64 probe renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-gradient-fill-stroke-mark, while the radial probe renders with measured oracle delta of 9 pixels at maximum channel error 1 and maximum alpha error 0, retained as known-difference census entry original-logo-radial-fill-stroke-mark in the standing one-step family).

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
operation order, exact pixels and bounded intermediate lifetime/work. Status 2026-09-24: masked groups admit polygon-fill children (white pass-through, gray isolation with combined factors, forward-use integration, 4 end-to-end tests). Status 2026-09-24: objectBoundingBox clip fractions resolve per referencing band and straight-contour polygon fill through pre-tokenized bounds (5 end-to-end tests); curved contours stay explicitly unsupported. Status 2026-09-24: objectBoundingBox clips on direct use instances resolve per mapped definition box (4 end-to-end tests). Status 2026-09-25: objectBoundingBox clips on symbol instances resolve through the viewBox-mapped content box clipped to the use viewport, with plain symbols reusing the direct union path and sizeless symbol uses gated (fractional resolution, symbol/direct equivalence and half-render pixel tests, 3 parse plus 2 pixel tests; 64x64 probe renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-symbol-clip-mark); forward references stay explicitly unsupported. Status 2026-09-25: objectBoundingBox clips on forward use references resolve at document end through the same direct-union and symbol viewBox-mapped boxes, carrying the clip id with its parent frame and use depth on the pending use (2 parse plus 2 pixel tests with forward/backward equivalences byte-exact); forward clip-path definitions stay explicitly unsupported. Status 2026-09-25: forward clip-path definitions admitted on use and symbol instances by deferring clip-box resolution to document end through the same direct-union, symbol viewBox-mapped and userSpace boxes (unknown clip references force deferral even when the definition is known; 3 parse plus 2 pixel tests with forward/backward equivalences byte-exact plus never-defined and direct-leaf gates); forward clip-path definitions on direct leaves, groups and viewports stay explicitly unsupported. Status 2026-09-25: forward mask definitions admitted on use and symbol instances by deferring mask-value resolution to document end through the same mask values, regions and coverage checks (unknown masks force deferral even when the definition is known, with custom-region gates re-checked at resolve time; 3 parse plus 2 pixel tests with forward/backward equivalences byte-exact plus never-defined and direct-leaf gates); forward mask definitions on direct leaves, groups and viewports stay explicitly unsupported. Status 2026-09-25: explicit userSpaceOnUse mask regions admitted on use and symbol instances through the shared instance mask lowerer with the registered region replacing the default bounding-box region under the established viewport gate (converted custom-region gate test plus symbol, forward and viewport-gate tests and forward/backward pixel equivalence, 4 parse plus 1 pixel test; 64x64 probe renderer byte-exact against three identical pinned-Chrome captures, retained as exact oracle entry original-logo-use-mask-region-mark).

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
| Embedded raster images | Bounded real multi-color decoding, data/reference policy, intrinsic sizing, transforms, resampling, alpha and color/orientation metadata. The current uniform-PNG-to-rectangle feature does not cover this. Repair R29/R32 without expanding formats now. |
| Patterns | Reusable multi-element tile scenes, patternUnits/contentUnits, viewBox/aspect, transform, clipping/overflow and alpha through shared scene/paint evaluation. First repair R28 and repeated tile work; no separate pattern-only renderer. |
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
