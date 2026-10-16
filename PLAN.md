# Lokad.Svg — finite renderer delivery plan

Updated 2026-10-16: the completion audit and bounded survey reopen standalone
delivery for six named corrections: pattern clips, retained instance/marker
clips, clipped mask content, nested clip intersections and filtered gradients.
The `e21f956c` qualification remains valid for its declared cases; it does not
close these newly retained gaps. F1 below also replaces the full SVG fixture
collection with documents selected systematically from established test suites.
Private deployment adoption remains a separate pending gate.
Preserve current implementation work, micro-tests, known-bug regression coverage
and default resource limits. F1 governs replacement of full-document fixture
membership; retained SVG/font/reference bytes and options remain immutable.
This file owns delivery order, required exits and scope
decisions; [SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md) owns admission,
[INVENTORY](docs/INVENTORY.md) owns document evidence,
[RASTERIZATION_DESIGN](docs/RASTERIZATION_DESIGN.md) owns mechanisms/history and
[BENCHMARKS](docs/BENCHMARKS.md) owns measurements.

The release scope is the finite G2–G9 subset and S17 contracts below. **No feature
scope deferral is proposed or approved.** An unsupported outcome inside that subset
is remaining work. A future deferral must identify the promise, missing acceptance,
reason and release consequence, and remain required until explicitly decided.
Optional maintenance below adds no feature-completion prerequisite.

**Approved release-criteria change (2026-10-16):** T3-public is required for
standalone Lokad.Svg qualification. T3-private is required before production
adoption of the existing deployment and is not a standalone release prerequisite.
Its original four deployed-manifest requests remain pending; no deployment
compatibility is claimed. Font-format/shaping boundaries and renderer promises
are unchanged. This explicitly supersedes earlier private-access prerequisites.

**Approved full-fixture change (2026-10-16):** replace the list of full SVG
fixtures using established W3C/resvg/WPT collections under F1. Micro-tests stay,
and pinned Chrome remains the sole pixel oracle. This is a document-collection
replacement using the existing harness, not a test-framework or renderer-scope
redesign. Once F1 replaces the collection, legacy logo/export/authored-sheet
membership and cohort counts are historical evidence, not mandatory release
criteria. This explicitly supersedes contrary full-document preservation rules
and old cohort percentage targets; focused behavior/API/resource protections stay.


## Qualified work to preserve

| Completed commitment | Existing evidence and required preservation |
| --- | --- |
| Historical full-document cohorts | **27/27 corporate documents / 258/258 variants exact on Windows/Linux**, with unchanged [membership/recipes](tests/References/inventory/corporate-cohort-v1.json) and [measurement](tests/References/inventory/corporate-cohort-current.json). **50/50 selected documents complete**, including whole `inv-146`, original marker/shadow and supplied-font fixtures, SVG Translate, `inv-127` and `inv-145`: [gap ledger](tests/References/inventory/svg-gap-cases.json), [inventory](docs/INVENTORY.md). These results remain historical evidence; F1 replaces their mandatory full-fixture membership/counts. Failure-selected inputs establish no representative success rate. |
| Historical integration documents and retained micro-tests | **36 authored documents** (26 CC0, 10 MIT) and **four sourced documents** (one W3C decoration document and three [MIT Feather icons](tests/Research/feather-line-documents/README.md)) were exact on both platforms, separate from the cohorts; [INVENTORY](docs/INVENTORY.md) owns the list, controls and immutable assets/options. F1 replaces full-document membership while preserving unique behavior such as `inv-146`'s oval/text and [merge controls](tests/Research/filter-merges/README.md), [merge qualification](artifacts/maintenance-review-20261012/merge-qualified-results.json), **149/149 paint-recording results** and the I Ching regressions through retained/reduced micro-tests. |
| G2–G7 / C1–C6c qualified witnesses and shared dependencies | Preserve the witnessed instance/viewport maps, retained curves and affine/non-scaling strokes, shader placement, clip unions/forward/object-box binding, mask regions/opacity, bounded tile content/clips/masks, image placement and enumerated static CSS under the [boundary/evidence index below](#required-support-boundaries-and-finite-exits), [existing map witnesses](artifacts/shared-stroke-pattern-maps-20261015/existing-map-witnesses.json), [tile-content qualification](artifacts/maintenance-review-20261012/pattern-content-qualified-results.json) and [shared-map qualification](artifacts/maintenance-review-20261012/shared-maps-qualified-results.json). Their shared mechanisms remain available dependencies; A1–A5 identify missing bindings/intersections beyond those witnesses. |
| G9 public text / T1–T2 | The [instance/paint sheet](tests/Research/instance-paint-exit-sheet/README.md) and [composite-flag sheet](tests/Research/composite-flag-exit-sheet/README.md) are complete with **4/4** and **3/3** exact controls on both platforms. Retain [paint qualification](artifacts/maintenance-review-20261012/text-paints-qualified-results.json), [dormant forward-paint follow-up](artifacts/maintenance-review-20261012/dormant-paints-qualified-results.json), [composite qualification](artifacts/maintenance-review-20261012/composite-flags-qualified-results.json), intended-font/metrics proof and the finite [G9 boundary](#g9-supplied-font-labels-and-text-paths). Catalog conformance and the [FontProbe host](tools/Lokad.Svg.FontProbe/README.md), including six transport/input regressions, are implemented; the four end-to-end public requests are T3-public; deployed-manifest evidence remains T3-private. |
| G8 qualified effects / E1–E2 and E3 source/owner placements | [Affine sources](tests/Research/affine-source-exit-sheet/README.md) **1 document / 4 controls**, [linearRGB restoration](tests/Research/linear-restoration-exit-sheet/README.md) **1 / 3**, [source boundaries](tests/Research/filter-source-exit-sheet/README.md) **1 / 8** and [owner/source composition](tests/Research/filter-owner-exit-sheet/README.md) **1 / 8** are exact on both platforms. The last bank reuses one offset witness: seven new controls, no duplicate canonical test. Preserve the three [source-offset controls and qualification](artifacts/maintenance-review-20261012/affine-offset-qualified-results.json), [filter-region results](tests/Research/filter-regions/manifest.json) **26/26**, and bank-linked qualification/resource evidence. The retained-instance exit is qualified below. |
| E3 retained-instance effects | The unchanged [sheet](tests/Research/retained-instance-effect-sheet/README.md), **8/8 declared controls** and **2/2 focused translated-wrapper controls** are exact on Windows/Linux. Preserve 72 healthy stable captures, intended-font proof, own-use and retained path/group/text filter scopes, scene-local bindings, x/y effect placement and native clip/mask/filter ordering. [Qualification](artifacts/maintenance-review-20261012/retained-instance-qualified-results.json) owns prior-case preservation and material resource/prepared-retry checks. E3 is complete. |
| E4 markers — qualified length/direction/presentation witnesses | The unchanged [length/direction sheet and six controls](tests/Research/marker-completion-sheet/README.md) and [presentation sheet and two controls](tests/Research/marker-presentation-sheet/README.md) are exact on Windows/Linux. Preserve em/ex dimensions/anchors, lazy supplied-face discovery, native directional/pose rounding, marker display/visibility and inherited stroke/child overrides. [Length qualification](artifacts/maintenance-review-20261012/marker-qualified-results.json), [presentation qualification](artifacts/maintenance-review-20261012/marker-presentation-qualified-results.json) and the retained [font oracle correction](artifacts/marker-completion-20261016/font-length-oracle-correction.json) own diagnostics, prior-case preservation and material resource checks. Those exits remain qualified; A3 reopens missing marker-content clipping. No established reference changed. |
| API/resource/package contracts | Preserve bounded preparation/raster/encoding, size/work denial, cancellation/retry, partial cleanup, immutable borrowed bytes, single-use prepared ownership and output integrity. Existing [API contracts](docs/API_CONTRACT.md), package and installed-consumer protections remain required. |

The earlier marker full local [qualification](artifacts/maintenance-review-20261012/marker-presentation-qualified-results.json)
is **8,133 tests/platform**, normal incremental Release build plus complete tests
**80.6s Windows / 248.9s Linux**. All 8,123 preceding semantic cases and
established exact outputs are preserved. The receipt owns the exact implementation
revision and hosted CI outcome, including package/installed-consumer checks. Reuse closed exact-revision CI audits without repolling;
PLAN-only edits and completed accounting work do not reopen qualification.

Prior standalone [qualification](artifacts/standalone-font-qualification-20261016/standalone-release-qualified-results.json)
is complete for substantive revision `a3346605`: **8,137 tests/platform** in
[terminal green CI](https://github.com/Lokad/Svg/actions/runs/38941225820), including
all four actual-host public font requests, package/notices and installed consumers.
Normal build plus complete tests: **91s Windows / 115s Ubuntu**. The prior 8,133-case
local qualification and seven-witness measurements above are reused unchanged;
only four focused host requests and one new italic visual were needed. Private
configuration is independent of ordinary builds/CI. Documentation-only finalization
reuses this exact substantive-revision audit without another CI run.

Preserve qualified G2–G7/C1–C6c witnesses, T1–T2, E1–E4 and T3-public.
The audit below demonstrates missing behavior within G2/G5–G8, so their owning
exits and final S17 are reopened for A1–A6. Unchanged qualified
mechanisms need no repeat implementation or new accounting policy. T3-private
stays pending before production adoption. Historical rejection notes,
`observedGate` fields and pending-CI narratives in linked checkpoint records do
not override qualified outcomes. Detailed mechanism/history belongs in those
records; the queue below owns remaining work.

<a id="delivery-order"></a>
<a id="3-ordered-support-queue"></a>

## Ordered delivery checklist

**Standalone delivery is open.** Finish the six demonstrated corrections A1–A6,
complete the approved full-fixture replacement F1, then close A7 on the finished
revision. The bounded completion survey is complete; its closure does not close
the separately approved F1 collection work. Freeze F1 selection before fresh
candidate comparisons; it can be prepared alongside the corrections.
T3-public and the seven-witness measurements remain qualified. Private deployment
configuration does not block this queue. Reuse existing retained-source binding,
object bounds, native clip replay, filter source capture and resource ownership.
Freeze original inputs/options and healthy platform references before each
correction; reuse the retained audit inputs below. Promote the necessary original
inputs and platform references into the existing tracked research/test banks when
implementing each correction; local diagnostic artifacts alone cannot close it.
When beginning that batch, align current README/SUPPORT_MATRIX status with this
open queue while retaining their dated qualification evidence. A7 records the
finished qualification.

| Required correction, in order | Demonstrated missing behavior | Acceptance and completion condition |
| --- | --- | --- |
| [x] **A1 — clips on pattern leaves** (G5–G7 / C5) | Clipped pattern leaves now use the existing rendered-leaf/deferred resource path. | The two unchanged audit inputs and the translated/forward-definition/sibling control are exact on Windows/Linux: [tracked bank](tests/Research/completion-pattern-clips/README.md), [targeted qualification](artifacts/audit-corrections-20261016/a1-qualified-results.json). **126 pattern unit / 199 visual-resource checks pass per platform**, including material denial/cancellation/cleanup/retry. Source/reference bytes and tile scope are preserved. Finished-batch verification remains A7. |
| [x] **A2 — object-box clips in pattern content** (G5–G7 / C5–C6) | Pattern groups use the existing pre-clip bounds collector and native object-box clip machinery, with bounds/resources owned by their tile scene. A1 already closes the leaf variant. | The unchanged group, leaf and image sources and one transformed multi-child/degenerate target/sibling control are exact on Windows/Linux: [tracked bank](tests/Research/completion-pattern-object-clips/README.md), [targeted qualification](artifacts/audit-corrections-20261016/a2-qualified-results.json). **159 pattern/object-box/scope unit / 217 visual-resource checks pass per platform**, including ordinary/user-space controls and material denial/cancellation/cleanup/retry. Finished-batch verification remains A7. |
| [x] **A3 — clips in retained symbols, groups and markers** (G2/G5/G8) | Clipped leaves force source replay; retained group/use scopes bind clips through the shared native/object-box operations. Symbols retain their viewport even without a viewBox. | All seven unchanged audit failures, unchanged unclipped marker and two affine/forward-resource/local-use/sibling controls are exact on Windows/Linux: [tracked bank](tests/Research/completion-instance-clips/README.md), [targeted qualification](artifacts/audit-corrections-20261016/a3-qualified-results.json). **370 unit / 781 visual-resource / 13 retained-effect/font checks pass per platform**, including material denial/cancellation/cleanup/retry. Finished-batch verification remains A7 after F1. |
| [x] **A4 — clips on vector mask content** (G5 / C3/C5) | Clipped vector mask children now use existing ordered source replay. Object-box clips retain their native id/map until emission in the final mask scene owner. | All four unchanged leaf/group user-space/object-box sources are exact on Windows/Linux: [tracked bank](tests/Research/completion-mask-clips/README.md), [targeted qualification](artifacts/audit-corrections-20261016/a4-qualified-results.json). **189 unit / 174 visual-resource checks pass per platform**, preserving alpha/luminance, transformed/object-box mask and material denial/cancellation/cleanup/retry outcomes. Image-inside-mask admission remains separate; F1 and finished-batch A7 remain open. |
| [x] **A5 — nested clip-child intersections** (G5 / C1/C6, G6 placement) | Path/rectangle children retain nested references and source maps; groups, patterns and retained sources share native stencil replay with final-owner registration. Iterative binding rejects cycles without recursive graph traversal. | All three unchanged visible audit sources and one affine-child/rectangle-shortcut/sibling control are exact on Windows/Linux: [tracked bank](tests/Research/completion-nested-clips/README.md), [targeted qualification](artifacts/audit-corrections-20261016/a5-qualified-results.json). **272 unit / 632 visual-resource checks pass per platform**, including retained-symbol/mask parity, unions/bounds, graph failures and material denial/cancellation/cleanup/retry. Container-attribute admission remains distinct; F1 and finished-batch A7 stay open. |
| [x] **A6 — filtered gradient source fidelity** (G6/G8 / E1–E3) | Gradient paints retain the native padded source-bitmap origin already used by image replay, including compact captures. Shader-coordinate and color/blur arithmetic stay unchanged. | Both unchanged failures, both unchanged discriminators and one translated zero/asymmetric/wider-blur control are exact on Windows/Linux: [tracked bank](tests/Research/completion-filtered-gradients/README.md), [targeted qualification](artifacts/audit-corrections-20261016/a6-qualified-results.json). **371 unit / 558 visual-resource checks pass per platform**, preserving colored/linearRGB filters, tile phase/coverage and material denial/cancellation/cleanup/retry. F1 and finished-batch A7 remain open. |
| [ ] **F1 — replace the full SVG fixture collection** | The current logo/export/icon/authored-sheet selection is replaced by a finite, feature-indexed collection of full standalone SVG documents from established suites, under [the F1 specification below](#full-svg-fixture-replacement-f1). | Commit the replacement manifest, pinned upstream SVGs/resources/notices and stable Windows/Linux Chrome reference PNGs. Exercise the declared profile and meaningful interactions; retain micro-tests and known-bug protections. Every selected in-scope output must be exact. Retire superseded full-document checks and their old count/percentage gates after recording coverage disposition. Reuse the existing harness and capture tools. |
| [ ] **A7 — finished-correction S17 verification and admission documentation** | A1–A6 and F1 are closed; the eight boundary outcomes below have an explicit disposition consistent with the original finite promises. | Preserve micro-tests, retained behavior/API/resource contracts and public font qualification. Run affected checks while correcting and migrating, then one complete Windows/Linux qualification for the coherent finished batch and exact-revision terminal CI with package/installed consumers. Reconcile README/SUPPORT_MATRIX/INVENTORY/API status with the replacement full-fixture collection and retained audit evidence; investigate only material benchmark concerns. T3-private stays separate. |

### Full SVG fixture replacement (F1)

F1 changes the list of full SVG fixture documents. Existing micro-tests, including
small semantic/pixel regressions, API/resource/lifetime checks and font-host
contracts, stay. Reuse current manifests, visual-test infrastructure and pinned
Chrome capture/comparison tools. This task adds no replacement harness, new
rendering oracle or renderer dependency.

1. **Select by declared feature coverage.** Review pinned inventories of the
   [accepted static W3C SVG 1.1 tests](https://dev.w3.org/SVG/profiles/1.1F2/test/harness/index.html),
   [resvg test suite](https://github.com/linebender/resvg-test-suite) and applicable
   standalone SVG cases from [WPT](https://github.com/web-platform-tests/wpt/tree/master/svg).
   Use their feature organization to choose a finite collection before fresh
   Lokad.Svg comparisons. Cover admitted geometry/strokes, transforms/viewports,
   paints, groups/instances, clips/masks, patterns, PNG images, finite filters and
   supplied-font text, plus meaningful combinations such as clipped pattern
   content, nested resource intersections and filtered instances. Record each
   document's purpose; avoid duplicate upstream cases and repetitive size/API
   variants. Neither a fixed document quota nor importing every upstream test is
   required.
2. **Commit the replacement manifest and original assets.** Extend the existing
   fixture records with upstream URL/revision/test identity, license and original
   SVG/resource hashes, covered feature groups, reason for inclusion, output
   dimensions/background/scale and exact supplied fonts. Keep original documents
   and necessary resources unchanged. Record any viewport/font/resource transport
   separately and preserve the test's behavior. Classify browser-only HTML/DOM/
   script tests and features outside the existing profile explicitly. A renderer
   rejection or pixel difference cannot justify excluding an otherwise in-scope
   document. Cases requiring changes to the behavior under test do not qualify as
   unchanged upstream full fixtures.
3. **Generate Chrome ground truth.** The suites supply documents and coverage
   organization; pinned Chrome supplies every expected pixel. Capture each
   declared output on Windows/Linux using three healthy stable fresh-process
   captures per platform, with supplied-font proof where applicable. Preserve
   captured bytes/provenance and existing exact decoded RGBA8 comparison, ignoring
   RGB only when both alphas are zero. Upstream PNGs, reference documents and
   pass/fail tables do not replace the Chrome oracle. Keep rejection, difference,
   oracle failure and unmeasured distinct; no tolerance or candidate-driven
   recapture closes a failure.
4. **Replace active full-document membership and qualify.** Once the manifest,
   captures and coverage mapping are reviewable, retire the superseded full
   logo/export/icon/authored-sheet checks and unneeded fixture assets. Record each
   retired document's disposition in INVENTORY: upstream replacement, redundant
   coverage, or unique behavior retained as a focused micro-test. Existing
   micro-tests stay; a whole document with unique bug coverage may be reduced to
   a focused regression. Known A1–A6 defects and other admitted behavior cannot
   disappear through fixture retirement. Historical qualification remains dated
   evidence in Git/documentation; keeping the old full collections in active CI
   is not an acceptance condition. Benchmarks and host-contract fixtures remain
   governed by their own contracts.

**F1 closes when** the new manifest/assets/platform references are tracked, every
selected in-scope output matches Chrome exactly on both platforms, micro-tests
and required contracts pass, and current documentation/CI/release counts describe
the replacement collection. Record newly exposed in-scope defects as concrete
remaining work and correct them before closing F1/A7. The G2–G9 feature boundary,
T3-public/private split, default limits and under-ten-minute routine test policy
remain unchanged. Selected test assets are committed for offline tests;
`external/` remains an optional source checkout.

F1 selection is frozen at `460a67df` in the [26-document upstream manifest](tests/Research/upstream-static-documents/README.md), before fresh comparisons.
All original assets/notices and viewport/font transports are tracked. **174 healthy,
stable, cleaned Chrome captures** freeze all Windows/Linux references and three
native font-use discriminators. First Windows comparison: **20 exact, two pixel
differences, four rejections**. Every selected case remains required; the
[initial results](artifacts/full-fixture-replacement-20261017/first-comparison-failures-windows.json)
retain the failures. Resolve this finite dependency order before fixture retirement:

| Required F1 work | Missing behavior and acceptance | Completion condition |
| --- | --- | --- |
| [ ] **F1.1 - shared rectangle source replay** | `nested-affine` rejects while registering an affine rectangle id; `pattern-affine` rejects a rotated rounded rectangle. Reuse native shape sources and composed paint maps, preserving ordinary/instance paths. | Both unchanged documents exact on Windows/Linux; focused source/map/ownership regressions pass. |
| [ ] **F1.2 - rounded resource clips** | `object-box-clip` loses rounded corners (424 differing Windows pixels). Use the shared rounded source with the resource's object-box map. | Unchanged document exact on both platforms; sharp/rounded and native/object-bound clip controls stay exact. |
| [ ] **F1.3 - nested SVG transform binding** | `wpt-nested-viewport` rejects its nested SVG transform. Compose it outside the child viewBox with the existing viewport/clip map. | Unchanged document exact on both platforms; parent maps, physical clips and siblings remain protected. |
| [ ] **F1.4 - embedded PNG metadata** | `embedded-png` rejects its significant-bit chunk. Resolve valid fully significant 8-bit metadata without adding color conversion or orientation support; retain meaningful format/chunk boundaries. | Original embedded PNG document exact on both platforms with focused valid/malformed/unsupported chunk outcomes. |
| [ ] **F1.5 - composed effect fidelity** | `filter-mask-clip` differs by 395 Windows pixels (267 alpha differences). Diagnose source capture, mask/clip ordering and native coverage through the existing effect machinery. | Unchanged document exact on both platforms; A6, existing mixed-space/owner/source and resource controls stay exact. |
| [ ] **F1.6 - replace active full-document membership** | Record coverage dispositions and retire superseded full-document checks/assets after the replacement passes. Keep focused known-bug cases, API/resource/font-host contracts and benchmark inputs. | Replacement manifest and tests are active; every retired document has a recorded replacement/redundancy/focused-regression disposition; all 26 selected outputs are exact on Windows/Linux. |

F1 remains open, followed by finished-batch A7. No reference replacement,
tolerance, candidate-based exclusion or new measurement framework closes a gap.


### Completion audit (2026-10-16)

The [audit summary](artifacts/completion-audit-20261016/audit-summary.json) and
[direct API outcomes](artifacts/completion-audit-20261016/diagnostic-results.json)
retain six authored diagnostic inputs at `e21f956c` (documentation head
`da7e81a4`), scale one, 64×64 transparent, default resource limits. Ordinary
object-box clipping, an unclipped pattern and a user-space clipped pattern group
render. Direct leaf clips and the object-box clipped pattern group reject.
Three healthy fresh-process Windows captures of each of the three retained
discriminators produce the same PNG bytes in pinned Chrome 153.0.8010.52, with
successful profile cleanup. These are Windows diagnostic findings, not finished
Windows/Linux correction qualification. Preserve original SVG/capture bytes.

**Completed bounded survey:** the [initial manifest](artifacts/completion-audit-20261016/survey/manifest.json)
declared 36 cases before execution; [12 subsequent discriminators](artifacts/completion-audit-20261016/survey/controls/manifest.json)
were declared after observing those outcomes, before their own execution. The
survey checks existing clip/instance/pattern/mask/filter promises, images and
affine/stroke controls. It reuses MemoryProbe, Chrome and VisualDiff; renderer
sources and default limits are unchanged. The [summary and per-input hashes](artifacts/completion-audit-20261016/survey/summary.json),
[initial results](artifacts/completion-audit-20261016/survey/results.json),
[control results](artifacts/completion-audit-20261016/survey/controls/results.json)
and [direct rejection messages](artifacts/completion-audit-20261016/survey/diagnostic-results.json)
retain every outcome. Initial scope labels are retained; the summary records the
subsequent admission assessment rather than rewriting those labels.

| Survey assessment | Exact | Pixel difference | Rejected | Expected negative |
| --- | ---: | ---: | ---: | ---: |
| Demonstrated declared scope — 36 inputs | 16 | 8 | 12 | 0 |
| Boundary to document — 8 inputs | 0 | 0 | 8 | 0 |
| Documented exclusions / required cycle rejection — 4 inputs | 0 | 0 | 0 | 4 |
| Total — 48 inputs | 16 | 8 | 20 | 4 |

All **44 native-compared inputs / 132 fresh-process Windows captures** have
healthy startup, successful cleanup and byte-identical triples in pinned Chrome.
No oracle failure or unstable capture was counted as a renderer defect. The four
negative controls reject with the expected categories: percentage line endpoints,
overlay blend, external image fetching and cyclic use. Controls also confirm
exact skew/reflection, ordinary image/object-box clips, clipped image tiles,
pattern uses, stroked/diagonal geometry and existing filtered-target clips.
These failure-selected diagnostics establish no general success rate. Linux
references and correction qualification remain required before closing A1–A6.

**Finite admission reconciliation:** eight rejected probes need explicit public
boundary wording; they do not independently establish new renderer scope:

- Two inline/stylesheet `clip-path` probes reach a property absent from the
  enumerated style implementation. Reconcile the exact supported property list
  with the existing G7 promise; general CSS completeness was never an exit.
- Four probes put `clip-path` on the `clipPath` container itself. Distinguish
  that property location from the visibly broken clipped-child intersections in
  A5; ordinary nested clipping alone does not prove every resource attribute is
  admitted.
- Clipped and unclipped image-inside-mask probes both reject on image-content
  admission. G7's clips/masks on image targets and G6's image tiles do not by
  themselves promise image children inside masks. Preserve the existing finite
  vector-mask boundary while reconciling its wording.

Resolve these three precise admission questions in SUPPORT_MATRIX under A7.
If an original promise covers a probe, its demonstrated failure becomes required
work; do not silently turn it into a new exclusion. No scope deferral is approved
here. Do not expand this finished survey into a combination census, acquisition
quota or new framework. Promote only the independent regression sources needed
by each correction, while retaining all local diagnostic evidence.

The following earlier acceptance remains evidence for its exact revision:

| Order / required exit | Missing behavior and dependencies | Concrete acceptance and completion condition |
| --- | --- | --- |
| [x] **1. T3-public — public fonts** (item 4 / G9) | The [four public requests](tests/Research/public-font-qualification/README.md) are qualified on Windows/Linux, including the new real italic witness; regular/bold/fallback assets and references are reused unchanged. Use redistributable static TrueType programs, the existing manifest/catalog/provider interfaces and FontProbe host. No private access, new schema or other-renderer dependency. | Four regular/bold/real-italic/intentional-fallback requests complete acquisition and selection through the existing host. Verify independent metrics, intended-font use and exact Windows/Linux pixels against unchanged qualified references; add only missing cases. Categorized format/shaping boundaries, limits, cancellation, cleanup and ownership remain protected. All four actual-host outcomes, metrics, face-use controls and exact platform references are recorded in [qualification](artifacts/standalone-font-qualification-20261016/public-font-qualified-results.json). Final complete-suite/package/consumer CI is green under S17 below. |
| [x] **2. S17 — public measurements and documentation** (item 6) | Public measurements are complete for qualified revision `393e19d5`. Reuse completed maintenance/resource evidence; later corrections need only affected follow-up. | The [seven-witness results](docs/BENCHMARKS.md#finite-s17-measurement-set-2026-10-16) and [public receipt](artifacts/s17-release-20261016/s17-public-evidence.json) close release conditions 4–5 for that revision. Reconcile public documentation with the approved T3 split and final qualification. No speculative optimization, new framework or completed-memory requalification. |
| [x] **3. S17 — prior-revision standalone verification** (historical evidence) | `a3346605` **8,137 tests/platform** and `e21f956c` **8,147 tests/platform**, including package/installed consumers, were qualified for their retained cases. | Preserve that exact-revision evidence. Newly demonstrated defects reopen current delivery in A1–A6 and final verification in A7; this historical row is not a second execution queue. Document/test counts alone do not establish completion. |

**Separate production-adoption gate — [ ] T3-private.** The original regular,
bold, italic and fallback requests against the unchanged deployed manifest remain
unexecuted until its existing configuration is available. Retain sanitized
selection/metrics/pixel outcomes; convert in-scope defects into public
reproductions. This gate cannot be marked passed by public qualification, and
standalone completion makes no claim of compatibility with that deployment.

**Public SVG follow-up (2026-10-16): complete for substantive revision `e21f956c`.**
The finite delivery sequence is:

| Exit | Acceptance and status |
| --- | --- |
| [x] **P1 — symbol presentation** | The [whole sprite sheet and two controls](tests/Research/symbol-presentation-sheet/README.md) are exact on Windows/Linux. Retained source presentation preserves display, inherited visibility, explicit child overrides and source-ancestor exclusion. Invalid values and representative material resource checks pass. Pinned native symbol/marker display distinctions remain documented. |
| [x] **P3/P4 — bounded public scan and fractional line endpoints** | Six unchanged MIT Feather SVGs were screened; three passing screens add no corpus cases. The three retained failures—Aperture, Feather and Sun—now match their original 24×24 transparent outputs exactly on Windows/Linux, alongside [two controls](tests/Research/feather-line-documents/README.md). Existing source/native strokes admit finite fractional bare-number/px endpoints while preserving integer fast paths. Nine old fractional-line rejection cases now compare exactly against their unchanged references; independent Linux triples confirm those original golds. No new stroker, API grid or acquisition quota. |
| [x] **P2 — finished-correction verification** | [Full local qualification](artifacts/feather-line-documents-20261016/qualified-results.json) passes **8,147 tests/platform**, **80s Windows / 252s Linux** for build plus tests. One batched push qualified both corrections in [terminal green CI](https://github.com/Lokad/Svg/actions/runs/38952070864) for exact source `e21f956c`: all ten required checks/platform, including package/consumer and resource checks. Hosted build plus full tests: **83s Windows / 122s Ubuntu**. [Completion audit](artifacts/feather-line-documents-20261016/release-follow-up-qualified-results.json) maps every S17 condition to evidence; unchanged benchmarks, approximate-memory and maintenance evidence are reused. |

Sources, original MIT notices, declared outputs and stable native captures are
retained; the [six-document scan](artifacts/public-svg-scan-20261016/selection.json)
establishes no representative success rate. Both shared corrections preserve all
old SVG/font/reference bytes and options, **27/27 / 258/258**, selected **50/50**,
resource limits, cancellation/cleanup, ownership and font/shaping boundaries.
All three exits and every retained public gap from this batch are closed.
T3-private remains a separate pending adoption gate. F1 now owns replacement of
the full-document collection; these historical counts do not constrain it.

The S17 measurement set is finite: existing `SvgCurveBenchmarks` outlined-wordmark,
gradient-clip and isolated-opacity methods; `SvgPatternBenchmarks` identical instances;
`SvgSuppliedLabelBenchmarks` public label; and the retained
[masked filter source](tests/Research/filter-source-exit-sheet/masked-source.svg) and
[image resource scope](tests/Research/affine-image-maps/image-resource-scope.svg).
Use the existing BenchmarkDotNet and MemoryProbe routes; report warmed conversions
and fresh-process tool observations separately. Equivalent existing evidence may be
reused. These seven witnesses add no feature or API-combination prerequisite.

Before implementing an exit, inspect current code and qualified manifests. An
admitted behavior may need only evidence; an old guard/test name is not a live
blocker. E4's original witnesses remain qualified; new live A3 findings reopen
marker-content clipping. Reuse covered spellings, retain original inputs when
replacing obsolete rejection
assertions, and add only missing independent cases. Every visual exit requires
exact declared outputs and applicable negative/degenerate/material ownership
checks. One witness can close several promises without duplicate API rows or
shape × paint × instance grids. The ordered exits and public follow-up above own
the execution queue.

## Required support boundaries and finite exits

The following preserves the promised useful subset; it adds no combination quota.
Close each group only when its owning exits are resolved, existing exact results
are preserved, and negative/degenerate outcomes agree with the implementation.
A newly demonstrated in-scope defect remains required in its owning item; broader
features require an explicit scope decision.

<a id="g2g4-instances-geometry-maps-and-strokes"></a>

### G2–G4: instances, geometry, maps and strokes — items 1–2

- **G2:** backward/forward local uses and child aliases; ordinary leaves,
  group/symbol/use targets; root/defs/rendered-group/symbol/nested-SVG definition
  and presentation locations; authored/inherited precedence and source-ancestor
  exclusion. Compose static instance maps through viewport, opacity and clip/mask
  boundaries; missing/cyclic references have categorized outcomes.
- **G3:** rect/rounded rect, circle/ellipse, line, polygon/polyline, retained
  line/quadratic/cubic/arc paths, nonzero/even-odd compound fills; translation,
  rotation, skew, reflection and nonuniform affine scaling at root/group/leaf and
  admitted instances. Preserve directed source curves. Finite degenerates have
  measured nonpainting/categorized outcomes; perspective is outside scope.
- **G4:** that geometry with solid/linear/radial/pattern paints; fractional and
  effective instance widths, ordinary/non-scaling strokes, butt/round/square caps,
  miter/bevel/round joins and miter limits, finite dash arrays/offsets, independent
  fill/stroke opacity, dashed curved paints and nonuniform placement. Use shared
  stroke/paint machinery while preserving native geometry distinctions.

The original item 1–2 map/geometry witnesses are qualified; the [geometry/scope inventory](docs/INVENTORY.md),
[existing map witnesses](artifacts/shared-stroke-pattern-maps-20261015/existing-map-witnesses.json)
and [shared-map qualification](artifacts/maintenance-review-20261012/shared-maps-qualified-results.json)
own their evidence. Retain cap/dash/dot, inherited shader, reflected phase,
nonpainting/device-width and degenerate/cleanup regressions. Root viewBox,
pattern-content viewBox and stroke host maps remain distinct native stages.
Retained instance clips reopen under A3. Preserve qualified T1 glyph contexts and
E4 marker lengths/presentation while correcting the missing resource bindings.

<a id="g5-viewports-clips-masks-and-composition"></a>
<a id="g6-patterns"></a>
<a id="g7-embedded-pngs-and-static-css"></a>

### G5–G7: composition, patterns, embedded PNGs and static CSS — items 2–3

| Group | Retained support boundary |
| --- | --- |
| G5 | Nested SVG/symbol viewBox/aspect maps and percentage bases; local paints; G3 clip shapes in user-space/object-box coordinates, ordered child unions and compound/winding behavior; alpha/luminance masks with both region/content units, transformed finite regions and implicit root bases. Nested leaf/group/use opacity, clips/masks and mask-container opacity; root/group/defs/symbol/nested-SVG locations. Preserve forward binding, source order, distinct object/visual/percentage bounds and native AA/BW consumers. |
| G6 | Ordered G3 vector leaves/groups/local uses, solid/gradient paint, opacity and admitted image/filter/supplied-font tile content; both tile/content unit systems, viewBox/aspect maps, fractional finite origins/sizes and composed affine transforms. Preserve inheritance, borders/phase/alpha, transformed fill/stroke and bounded repeat work; recursive pattern/paint dependencies reject. Item 2 closes the basic map routes; C5 closes the declared presentation/content scopes; remaining glyph/effect-specific boundaries belong to items 4–5. |
| G7 images | Base64 data-URI, non-interlaced 8-bit RGB/RGBA, grayscale/grayscale-alpha and indexed PNG, including tRNS, with chunk/CRC/decompression checks. Finite geometry/viewBox percentages, all meet/slice alignments and `none`, fractional/general affine placement, clips/masks/opacity. Keep image sampling/matrix rounding distinct from gradient/glyph arithmetic and bounded decode ownership. |
| G7 CSS | Existing ID/class/attribute/selector lists, SVG element and element+ID/class, descendant/child selectors through admitted trees; specificity/source order, `!important`, inline priority/inheritance for enumerated presentation/font properties and parsed width/height. Root/resource collection and source-ancestor exclusion are implemented. `paint-order` supports normal/distinct fill/stroke/markers lists with omitted operations appended; duplicate/unknown tokens reject. Apply each resource's applicable properties. |

The declared C witnesses remain qualified. The completion audit reopens their
owning G5–G7 exits for A1–A5, including missing resource-clip bindings and nested
clip-child intersections.
Retain the referenced independent cases and useful resource regressions. The
glyph-mask instance extension remains exact in the qualified path control and
complete T1 document.

| Completed dependency | Existing acceptance evidence |
| --- | --- |
| C1 — mixed/compound clip unions, cubic pairs and fallback | [Clip sheet](tests/Research/clip-union-exit-sheet/README.md), [cubic sheet](tests/Research/cubic-viewport-exit-sheet/README.md). |
| C2 — affine ancestor at nested viewport | [Cubic/viewport document and control](tests/Research/cubic-viewport-exit-sheet/README.md), including physical clipping and siblings. |
| C3a/C3b — mask color/maps, regions/defaults and viewport scopes | [Composition](tests/Research/composition-resource-exit-sheet/README.md), [color/affine](tests/Research/mask-color-affine-exit-sheet/README.md) and [mask/viewport](tests/Research/mask-viewport-exit-sheet/README.md) banks. |
| C4 — independent image alignment offsets | [Active-axis evidence](artifacts/shared-map-exits-20261015/existing-image-alignments.json), [xMin control](tests/Research/composition-resource-exit-sheet/image-horizontal-min.svg) and [aspect spellings](tests/Lokad.Svg.Tests/SvgAspectRatioTests.cs). |
| C5a/C5b — resource presentation, tile-local clips and masks | [Composition](tests/Research/composition-resource-exit-sheet/README.md), [tile masks](tests/Research/tile-mask-exit-sheet/README.md), [resource cascade](tests/Research/root-resource-cascade/manifest.json), [mask presentation](tests/Research/mask-presentation/manifest.json) and [pattern priority](tests/Lokad.Svg.Tests/SvgPatternContentTests.cs). |
| C6a/C6b — clip/opacity across viewports, forward/object-box binding | [Viewport scopes](tests/Research/viewport-clip-opacity-exit-sheet/README.md) and [forward/object-box bank](tests/Research/forward-bbox-clip-exit-sheet/README.md). |
| C6c — affine user-space rectangle clips | [Original input and visible controls](tests/Research/affine-rectangle-strokes/README.md), [qualification](artifacts/maintenance-review-20261012/affine-rect-clip-qualified-results.json). |

Keep source order, pre-clip object bounds, native AA/BW/luminance distinctions,
physical viewport clips and unaffected siblings under these existing protections.
Preserve all original image/CSS positive/negative outcomes; the survey's image
tile/object-box finding is A2 and its precise admission questions are under A7.
Outside scope with explicit outcomes: interlaced/16-bit/color-managed PNG,
JPEG/GIF/WebP/external image fetching; CSS imports/media/supports, pseudo
classes/elements, siblings, layout, variables, animation and web fonts.

<a id="g9-supplied-font-labels-and-text-paths"></a>

### G9: supplied-font labels and text paths — item 4

Retain standalone static TrueType face 0, catalog/style/coverage fallback, bounded
pair positioning and default Basic Latin ligatures: Latin/DFLT default language,
one unfiltered type-4 lookup, format-1 subtables, coverage 1/2, two to eight
components in font order. Nonzero spacing/positioned-span boundaries prevent merging.
Retain horizontal LTR ASCII/tested precomposed Latin and covered supplementary
scalars; text/CDATA/whitespace/nested tspan order; inherited/per-span family, size,
weight, style, paint/visibility; scalar/list x/y/dx/dy/rotate; start/middle/end
anchors, letter/word spacing, explicit lines, textLength spacing/spacingAndGlyphs,
alphabetic/middle/hanging baselines, affine glyph maps, ordinary fill/stroke paints
and solid underline/overline/line-through. Paths retain one local open/closed G3
path per run, supplied advances/kerning, length/percentage startOffset, anchors/
tangents, method align/spacing exact, curved/transformed sources and measured
out-of-range/zero-length outcomes. Qualified fitting, decorations, positioned
spans, dashed/non-scaling/stroke-before-fill/path strokes and metrics stay closed.

T1, T2 and T3-public are complete for standalone G9 qualification. T3-private remains required before deployment adoption. The
[resource](tests/Research/glyph-resource-exit-sheet/README.md) and
[text-map](tests/Research/text-map-exit-sheet/README.md) documents stay complete.
Preserve original masked inputs, source order, endpoint continuation and
scalar-list consumption, plus [parent-hint/USE_MY_METRICS/nested-accent checks](tests/Lokad.Svg.VisualTests/SvgCompositeMetricsTests.cs),
[contours](tests/Research/curved-font-documents/diagnostics/composite-metrics-contours.json),
Windows LCD/dropout and Linux light fitting. Executed unsupported opcodes/formats
retain explicit outcomes. No font/size/map or paint × glyph × placement grid.

Outside scope: wrapping, bidi/joining/complex scripts, general GSUB/GPOS/mark
positioning or other lookup flags/types/formats, vertical text, path stretch/auto
spacing, variable/color/bitmap fonts, CFF/CFF2, WOFF/WOFF2 and machine-font discovery.
Required missing programs/glyphs cannot disappear or silently substitute a machine
font. The font/host contracts below remain in force.

<a id="g8-markers-and-finite-static-effects"></a>

### G8: markers and finite static effects — item 5

Retain local start/mid/end markers on G3 lines/point lists/paths and instances;
userSpaceOnUse/strokeWidth, refX/refY, finite sizes/lengths, viewBox/aspect maps,
fixed/auto/auto-start-reverse, ordered vector/group/use content, ordinary paint,
source order, overflow/container presentation. Qualified absolute lengths,
known-axis percentages, angle units and literal hidden/visible overflow stay closed.
Retain feGaussianBlur, feOffset, feFlood, feComposite over/in, feMerge, feBlend
normal/multiply/screen and feDropShadow; SourceGraphic, SourceAlpha, earlier local
results, filter/primitive regions, both unit systems, sRGB/linearRGB and local CSS
references. Preserve per-primitive spaces, binary/merge transfers, distinct
CPU-kernel/shadow consumers, premultiplied arithmetic, bounded regions/work,
cancellation and cleanup. A flood override alone proves no color transition.

E1–E4's existing witnesses remain qualified under the evidence index above.
Marker-content clips reopen under A3 and filtered gradient sources under A6.
Open/closed path auto orientation, ordinary curve tangents/degenerates, arcs, point lists and
instance CSS priority already have exact [path](tests/Research/path-marker-content/manifest.json)
and [instance](tests/Research/marker-instance-content/manifest.json) cases; preserve
them together with the two completed E4 banks. Preserve the
[whole effects sheet and eleven controls](tests/Research/effects-marker-sheet/README.md),
[filter-region results](tests/Research/filter-regions/manifest.json) and qualified
colored-effects, affine capture, output-clip and nested-halo witnesses. Reuse
covered micro-cases; F1 owns full-document selection. Preserve native origins/nearest
offsets, mixed-space consumers, region/opacity and graph/resource protections.

Outside scope with explicit outcomes: backdrop inputs, external filter images,
displacement, turbulence, lighting, morphology, convolution, arithmetic composite,
other blends, backdrop-filter and CSS filter-function lists. Recursive effects and
forward/missing result names retain categorized outcomes. `inv-146` source/parent-
device rounding and padded capture remain closed.

## Release completion (S17)

**Standalone status: open for A1–A6, the full-fixture replacement F1, and final
A7 verification/admission documentation.** The [earlier revision audit](artifacts/feather-line-documents-20261016/release-follow-up-qualified-results.json)
preserves the `e21f956c` qualification and every established result; it does not
close newly demonstrated renderer defects. Reconcile the finished revision under
A7. T3-private remains pending before production adoption and is not a standalone
prerequisite.

Standalone release qualification is complete only when all of these hold for the **finished revision**. Private deployment access is not a prerequisite:

1. T3-public, A1–A6, F1 and the S17 evidence/documentation exit above are closed,
   with G2–G8/T1–T2/E1–E4 behavior protected by retained micro-tests and the
   replacement full fixtures. Retain linked
   exact acceptance, measured valid-degenerate outcomes and applicable negative/
   resource regressions. No checklist row remains candidate-only. Reused evidence
   is sufficient; unverified behavior, actual gates and known in-scope defects stay
   open. No unapproved deferral is hidden by an error.
2. Every selected in-scope F1 document/output is exact against its frozen
   Windows/Linux Chrome reference, and retained micro-test outputs remain exact.
   Measure every declared variant. Rejection, difference, oracle failure and
   unmeasured stay distinct; no tolerance/quirk budget or silent denominator
   change. F1 explicitly replaces legacy **27/27 / 258/258**, selected-document
   count and [80% cohort criteria](docs/S17_DECISIONS.md); those remain historical
   evidence and cannot substitute for the replacement collection's exactness.
3. Windows/Linux Release builds and complete offline tests, package/notices and
   isolated installed-consumer checks pass; the managed/dependency/API/lifetime
   contracts below hold. Hosted CI is terminal green for the exact substantive
   release revision. Reuse that audit for unchanged documentation; historical
   pending-CI notes cannot reopen green deliveries. Routine qualification meets
   the under-ten-minute policy below.
4. Existing representative normal/adversarial workloads preserve size/work limits,
   cancellation/retry, cleanup/output integrity and approximate memory accounting.
   Completed ownership work is not reopened for exact accounting.
5. Existing benchmarks report latency and allocated bytes for a small set spanning
   outlined geometry, instances/paint, clips/layers, filters/images and supplied
   fonts, with hardware/options/setup and selection limits. Investigate material
   regressions against retained measurements without trading exact pixels. Update
   README/API_CONTRACT/SUPPORT_MATRIX to the qualified subset and retain platform
   oracle provenance. No speculative optimization or new framework is a gate.

Cohort/document counts prove only their declared outputs. F1's systematic
replacement is required; acquisition beyond its declared collection needs an
identified coverage gap. No general-SVG success claim or open-ended
representativeness quota delays this release.

## Orthogonal validation and execution

- During development, build/run affected semantic, visual and resource checks.
  At a finished shared correction run full Windows/Linux qualification once;
  package/consumer checks apply when their contracts change and at release.
  Reuse unchanged evidence; PLAN-only edits need consistency/diff review.
- Preserve micro-tests and all retained SVG/font/reference identities and options;
  F1 explicitly replaces full-document membership and records retired coverage.
  Use one canonical rendering API per ordinary visual and the existing small
  representative matrix for six raw and six prepared sinks (supplied font,
  patterned mask on white, nested instanced mask on transparency). Preserve useful
  API/feature regressions, not an exhaustive visual × API product. Previously
  qualified twelve-API banks need no requalification under this policy.
- Preserve capacity/overlap/non-array-memory, named errors, independent results,
  single-use disposal, async scheduling/backpressure, token cancellation, stream
  failure/publication and cleanup in [PublicContractTests](tests/Lokad.Svg.Tests/PublicContractTests.cs)
  and [PngEncoderTests](tests/Lokad.Svg.Tests/PngEncoderTests.cs).
- **Routine incremental Release build plus complete tests must stay below ten
  minutes per platform.** The qualified platform measurements above meet this.
  Include normal build/startup; avoid forced compiler shutdown. Measure before/
  after only when routine cost materially changes. Remove redundant API work if
  needed while preserving distinct semantics. Browser captures, optional benchmarks
  and final package/release work are separate checkpoints, not every routine run.
- Select full-document fixtures through F1; add focused micro-cases for identified
  gaps/boundaries/defects. Before correction freeze licensed original bytes/options
  and references from three stable healthy fresh-process captures per platform.
  Authored focused controls remain permitted micro-tests. Compare decoded RGBA8
  exactly, ignoring RGB only when both alphas are zero; no silent reference replacement. Supplied
  fonts retain intended-font proof. Keep rejected models in existing evidence.
- Re-render affected inventories for narrow fixes and all declared variants for
  broad geometry/map/composition changes. Record complete before/after differences;
  pixel counts/maxima alone cannot identify an output. Oracle-failed/missing/
  unmeasured outputs stay in their denominator; count controls separately.
- Git identifies code. Do not hash our source/scripts/builds/packages/adapters/
  reports in tests/manifests. Preserve SVG/font/reference/tool-asset identities,
  upstream revision/license and dependency integrity. Ordinary edits need no
  metadata refresh. Detailed run records remain ignored artifacts; extend existing
  tests/benchmarks without new infrastructure or exhaustive combination matrices.
- Make small, buildable commits with affected tests/docs. Serialize local build/
  test/measurement jobs and avoid competing timing load. Push qualified substantive
  batches and inspect exact-revision terminal CI with sparse polling; never repoll
  a terminal run merely to refresh status.

## Maintenance decisions and evidence

Integrated review: [REVIEW.md](artifacts/maintenance-review-20261012/REVIEW.md).
These S01–S14 review IDs are separate from stages S1–S17. Rendering correctness
has priority; maintenance does not add prerequisites to support-group exits.
Completed maintenance remains closed; the table owns its status and the linked
records own implementation and qualification history.

| Maintenance status | Decision / retained evidence |
| --- | --- |
| Complete | S01 first tranche, S02 and S04–S12. Keep direct contour storage, prepared gradient/glyph data, retained-string/CSS reductions, bounded tile reuse, cheap unarmed probes, shared stroke admission and helper cleanup. Existing exact/resource regressions remain; mechanisms and measurements are recorded in the [review](artifacts/maintenance-review-20261012/REVIEW.md), [design record](docs/RASTERIZATION_DESIGN.md) and [maintenance artifacts](artifacts/maintenance-review-20261012/). |
| Optional / deferred | S03 preblend-cache prototype was removed after public-label comparisons **5.49 → 6.43 ms** and **6.59 → 6.86 ms**, allocation **142.15 → 142.51 KiB**, without established benefit. S13 wider glyph cache, S14 immutable table/profile or VM preparation, vertical glyph-scan reuse and wider polygon/cross-boundary tile reuse require representative profiling before reconsideration; none is a release gate. |

Preserve the measured improvements, with their original workload limits:

| Completed measurement | Retained result |
| --- | --- |
| S01 orthogonal test coverage | Suite-only Windows **5m51s → 1m58s**, Linux **5m34s → 1m43s** at that checkpoint; every distinct prior case retained plus two representative cases. [Existing receipt](artifacts/maintenance-review-20261012/s01-results.json). Latest suite timing is separately reported above. |
| S02 direct contour storage | Synthetic contour conversion allocation **688 → 480 bytes**; styled lowering about **202,613 → 197,700 bytes**. Short timing ranges overlap; this is an allocation result, not a universal speedup. |
| S04 prepared gradient ramps | Retained `inv-140` owned conversion **100.5 → 89.93 ms**, about **2.7 KiB** more allocation around 5.32 MiB. Preserve arithmetic/dithering and bounded preparation. |
| S05 retained parser strings | Attribute-parser allocation **820.67 → 599.79 KiB**; definition/use **751.03 → 653.16 KiB**; complete attribute conversion about **853.5 → 632.6 KiB**. Timing is mixed; reduced allocation is established. |
| S06 CSS identity | Small timing change with overlapping uncertainty and unchanged allocation; no speedup claim. |
| S07/S08 glyph preparation | Selected public-label local time **30–34% lower** for about 16 KiB more allocation; verified fallback **29–35% lower** for about 0.54 KiB. Optional preparation yields under resource pressure. These are distinct workload claims. |
| S09 tile reuse | Selected stroke/repeated-fill allocation **13%/37% lower**. Wider tile reuse and timing outside these workloads remain conditional. |
| S10–S12 | Unarmed probe microbenchmarks improve without allocation; shared stroke parsing and unused-helper removal preserve behavior. No general rendering-throughput claim. |

Detailed measurements/receipts are retained under the existing ignored
[maintenance artifacts](artifacts/maintenance-review-20261012/). No new
measurement framework or completed-memory requalification is required.

Speculative optimization, pooling/global caches and new test/evidence
infrastructure are deferred from release. Existing source/PNG/font lease and prepared-owner
protections are not redesign targets merely because a copy is visible.

**Scope-deferral decisions:** the maintenance deferrals above are explicit and
narrow no G2–G9 promise. The former 200-case and 10–20-diagram guidance is
optional coverage guidance, not an exhaustive acquisition or release quota.
There are **no newly approved feature-scope deferrals**
in this revision. Any future proposal must name the promised behavior, missing
acceptance, reason and release consequence, and remain open pending an explicit
decision. Current unsupported gates inside the subset cannot count as completion.

## Product and approximate memory contract

Keep one pure-managed C# net10.0 library for Windows/Linux. Retain the project's
MIT license and the adapted clipping code's BSD license/notices in
[THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt). Bounded managed source
adaptations follow the optional reference-source policy. Preserve zero third-party runtime packages, native
assets/PInvoke, System.Drawing, installed-font engines, browser rendering services
or other Lokad runtime dependencies. Rendering consumes
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
| Renderer-accounted live storage | 256 MiB budget under approximate accounting; shared runtime/host overhead and borrowed storage are identified separately. Audit material R38/R39 ownership/allocation changes. |
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

Memory accounting is approximate: directionally no more than **20% of
conversion-attributable live memory** should be unaccounted for across
representative workloads. This is an engineering target, not an exact guarantee
at every instant. Identify shared runtime/host overhead and borrowed storage
separately. Prefer conservative estimates and aggregate allowances for dominant
allocations: pixel buffers, decoded images, intermediate surfaces, geometry,
glyph data and large collections. Avoid excessive overcharging.

Completion does not require byte-exact small-object accounting, individual
metadata receipts, GC-round retirement proofs or precise accounting of the
bookkeeping machinery. Interpret ownership audits according to material memory
impact. Preserve meaningful size/work limits, cancellation, cleanup and output
integrity; validate through a small representative normal/adversarial workload
set using existing scenarios, without creating another measurement framework.

These requirements supersede conflicting exact-accounting completion wording.
Rendering correctness remains the priority. Preserve completed work and useful
protections; do not reopen them for requalification under this clarification.

## Font, text and host contracts

### Existing Lokad.Svg manifest and host contract

Preserve Lokad.Svg's existing manifest compatibility and selection semantics.
Use the existing catalog, font-provider interfaces and FontProbe host; endpoints,
credentials, I/O, retry/concurrency and bounded caches belong to the host. No
cross-renderer comparison, dependency, coordination or shared font-management
project is required for standalone qualification or deployment validation.

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

| Existing property | Required preservation / evidence |
| --- | --- |
| Manifest-first lazy programs | `SvgFontPackCatalog` stays metadata-only; selected immutable caller bytes are checked by declared length/SHA-256 during async preparation. |
| Style/coverage selection | Case-insensitive families/aliases, real italic matching, numeric weight selection, deterministic ties and configured fallback retain self-contained conformance vectors. Metadata selection evidence does not replace T3-public real-style pixels. |
| Host acquisition | Existing coalescing/cache/retry regressions and FontProbe remain. Manifest reads cap at 1 MiB, selected reads at the smaller of declared size and 64 MiB; library caps remain 32 MiB distinct borrowed programs / 256 faces. |
| Formats and ownership | Standalone static TrueType face 0 remains the admitted program format. Unused unsupported entries do not poison the catalog; selected unsupported entries reject explicitly. Preserve async acquisition, borrowed immutable bytes, cancellation and cleanup. |

### Public font qualification (T3-public)

Use the existing public Noto Sans Regular and Noto Sans Mono Bold assets and their
qualified controls, the intentional fallback control, and one redistributable
real Noto Sans Italic program. Retain upstream identity/license as asset metadata;
no new manifest field is required. Exercise four finite requests end to end through
catalog selection, the provider interfaces and FontProbe. Reuse original regular,
bold and fallback references and independent metrics. Freeze the missing italic
source/options, independent metrics and three healthy stable captures/platform
before correcting any rendering defect. Verify actual face use inside the SVG
image, including real italic versus regular/substituted or missing faces. Keep
public tests/CI offline after assets are retained and independent of private input.

### Authorized private font testing

User authorization covers testing with the existing deployment's blob storage and
manifest. It does not authorize committing or publishing any platform content.
Never commit the storage address, account/container identifiers, credentials or
SAS values, deployment pack identifier, actual manifest, font binaries, platform
configuration/code, or derived artifacts that disclose them. This includes
embedded-font SVG/data URLs, request/response traces, exception text and reports.
Any retained private inputs, font caches and captures belong outside this repo or
under its already-ignored `artifacts/private-fonts/`; check that they are untracked
before use. Use the existing ignored local-validation layout. Ordinary builds/tests and public CI must remain offline and independent of
the private source and sibling checkouts; do not upload private test artifacts.

The earlier one-program format inspection establishes no whole-pack or rendering
qualification; its historical detail stays in Git. The host and catalog are
implemented. Preserve them rather than rebuilding them as another S7 queue.

**T3-private — required before production adoption, not standalone release:** freeze one regular, one
bold, one italic and one fallback request before execution. Use the unchanged
deployed manifest to prepare their required programs; inventory their relevant
formats, coverage, composites and positioning
tables, and verify selection against the unchanged deployed catalog. For those requests,
compare curves/counters/composites, AV/space and fractional labels using identical
verified bytes in Svg and an SVG-image-local browser font embedding. Existing
intended-font controls, three stable captures and independent metrics/pixels apply.
Completion is a sanitized outcome for every selected request, with private gaps
categorized and reproducible in-scope defects converted to public synthetic cases
and resolved under G9. Unsupported formats retain explicit outcomes; no whole-pack
combination quota or new host is required. Private success cannot replace public
acceptance. All four deployed requests remain pending until configuration is supplied; public qualification neither passes this gate nor claims deployment compatibility. The security/retention rules above remain mandatory.

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

### Retained text foundation

The finite G9 boundary above is the single text completion checklist. Preserve
SFNT/range and `head/maxp/hhea/hmtx/cmap/loca/glyf` validation, optional name/OS/2
metadata, simple curves/implied points, bounded composites, supplied coverage
fallback, pair positioning and admitted ligatures. Their evidence is in the
[font tests](tests/Lokad.Svg.Tests/OpenTypeFontTests.cs),
[catalog tests](tests/Lokad.Svg.Tests/SvgFontPackCatalogTests.cs) and qualified
supplied-font document banks. Existing fallback/preparation is implemented;
general shaping is outside scope. Qualified positioning, spacing, baselines,
length policies and the complete path sheet stay closed. Only item 4 and the
finite G9 boundary own remaining text work; historical rejection wording in
linked checkpoint records does not reopen completed mechanisms.

### Host/oracle/corpus acceptance

Preserve the implemented [FontProbe host](tools/Lokad.Svg.FontProbe/README.md),
including bounded nonseekable input with preserved XML/BOM bytes and growth-time
limits. A new public stream-input overload is not a release requirement. Host
buffers/caches are separate budgets; concurrency, cancellation isolation, retry,
backpressure and partial-output publication keep their existing contracts. Public
supplied-font documents provide real-label evidence alongside transport tests.

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
oracle results are not canonical Linux evidence. F1 owns the required full-fixture
replacement; other acquisition and historical coverage guidance follow the
validation/scope decisions above without a further acquisition quota.

<a id="optional-reference-sources"></a>

## Reference sources

The local reference review in the earlier draft remains useful design guidance.
F1 also uses established suites as sources for committed SVG fixture assets;
upstream renderer code and expected PNGs do not become runtime dependencies or
pixel oracles. W3C/WPT revisions and document notices are recorded during F1
selection; the existing resvg-suite pin below is available for that inventory.
Keep `external/` optional, ignored and unreferenced by builds, tests, packages and
runtime: no submodule, CI download or hidden Rust/C++/Node dependency. Do not modify
another worker's reference checkout to test its absence. Ordinary
build/test/package/consumer workflows need no sibling checkout.

Permit bounded C# adaptations of numerical algorithms needed by a demonstrated
renderer dependency, as with the clipping kernel at `57c6a7e3`. Record the exact
source revision/files, retain copyright and license notices in source and packages,
and verify independent vectors plus the shared ownership/work/cancellation
contracts. Adaptations must remain self-contained managed code with portable
regressions. Whole-renderer vendoring or translation remains outside the plan.

| Inspected source / revision | Bounded use |
| --- | --- |
| resvg `75b6bbadd7999d0516dcd7153b4a321bfdf8670a` | `usvg` style/tree, use/clip/paint binding and renderer operation order; typed resolved state and distinct geometry/stroke/layer bounds. |
| tiny-skia `5d4754777746eef0828be166896eaf482c49f8f2` | Edge/span organization, compact paths, stroke/dash expansion and storage reuse. Its fourfold supersampling is not Chrome-exact analytic AA. |
| resvg-test-suite `d8e064337faf01bc5a9579187a56dbdbe3eacc72` | F1 full-fixture candidate collection and semantic counterexample index. It overlaps heavily with resvg's bundled suite; deduplicate selected documents. Pinned Chrome remains the PNG oracle. |
| NanoSVG `239e102ec2c691f2902e20ace2ed36ee4a35cfe6` | Secondary small-parser/scanline comparison only; five-subsample output is not the exactness target. |
| Matched Skia `f8b66b7597c4cc859d3ed190e9c6872241e6721c` | Release-tag scan, paint routing, stroker and curve construction for demonstrated S11/S12/S14 dependencies. The bounded clipping/solver adaptation retains this pin and BSD notices; compiled-native vectors and experiment provenance are in the design record. |

The recorded Skia comparison `8b8c3872fbc03f025855db96ce683f34ec98a815`
is a main revision, not the browser release pin. Use explicit matched
source reads without changing it implicitly. resvg's registry tiny-skia dependency
also need not be byte-identical to the sibling checkout despite equal version text.
