# Lokad.Svg — finite renderer delivery plan

Updated 2026-10-16 at the qualified E2 linear-restoration checkpoint.
Preserve the current implementation work, qualified `inv-146` correction and every
established exact result, original SVG/font/reference bytes, options and default
resource limits. This file owns delivery order, required exits and scope
decisions; [SUPPORT_MATRIX](docs/SUPPORT_MATRIX.md) owns admission,
[INVENTORY](docs/INVENTORY.md) owns document evidence,
[RASTERIZATION_DESIGN](docs/RASTERIZATION_DESIGN.md) owns mechanisms/history and
[BENCHMARKS](docs/BENCHMARKS.md) owns measurements.

The release scope is the finite G2–G9 subset and S17 contracts below. **No feature
scope deferral is proposed or approved.** An unsupported outcome inside that subset
is remaining work. A future deferral must identify the promise, missing acceptance,
reason and release consequence, and remain required until explicitly decided.
Optional maintenance below adds no feature-completion prerequisite.

## Qualified work to preserve

| Completed commitment | Evidence to retain |
| --- | --- |
| Frozen corporate cohort | **27/27 documents / 258/258 variants exact on Windows/Linux**, including 13/13 marks and 14/14 outlined wordmarks. Preserve membership, original bytes, options, backgrounds and references in the [manifest](tests/References/inventory/corporate-cohort-v1.json) and [measurement](tests/References/inventory/corporate-cohort-current.json). |
| Selected development documents and original fixtures | **50/50 complete**, including whole `inv-146`; original marker/drop-shadow fixtures **2/2**, supplied-font label/path fixtures **2/2**, SVG Translate, `inv-127` and `inv-145`. Preserve the [gap ledger](tests/References/inventory/svg-gap-cases.json), [supplied-font banks](tests/Research/supplied-font-documents/README.md), [curved labels](tests/Research/curved-font-documents/README.md) and [inventory](docs/INVENTORY.md). Failure-selected inputs establish no representative success rate. |
| `inv-146`, paint recording and retained libraries | Preserve whole `inv-146`, eight oval/text controls and four [merge controls](tests/Research/filter-merges/README.md), with the [qualification receipt](artifacts/maintenance-review-20261012/merge-qualified-results.json). Paint recording is **149/149 exact**; I Ching is **24 exact / one out-of-scope animation boundary**. |
| Additional complete documents | **Thirty authored documents** (twenty CC0, ten MIT) and **one sourced W3C decoration document**, exact on both platforms and separate from both cohorts. [INVENTORY](docs/INVENTORY.md) owns the document list, controls and immutable assets/options. The [tile-mask bank](tests/Research/tile-mask-exit-sheet/README.md) is **1/1 document / 2/2 controls**; retain every earlier bank and direct/curved decoration boundary. |
| Shared geometry, paint and scope dependencies | Fractional use widths and symbol frames, nonuniform curves/affine oval strokes, non-scaling conics, composed cubic clips, custom mask regions/container opacity, inherited symbol strokes, cubic-pair clip unions, normalized mask content, tile-local clips and axis/affine ancestry at nested viewports are qualified. Reuse [geometry/scope evidence](docs/INVENTORY.md), [existing map witnesses](artifacts/shared-stroke-pattern-maps-20261015/existing-map-witnesses.json), [tile-content qualification](artifacts/maintenance-review-20261012/pattern-content-qualified-results.json) and [shared-map qualification](artifacts/maintenance-review-20261012/shared-maps-qualified-results.json). These are available dependencies, not blockers to repeat. |
| Text instances and paints (T1) | The unchanged [instance/paint sheet](tests/Research/instance-paint-exit-sheet/README.md) is **1/1 complete document / 4/4 exact controls** on both platforms. Forward definition/font discovery, masked path-resource instances and linear/radial/pattern/constant/empty text paints are qualified with focused boundary and material ownership protection. Preserve the [paint qualification](artifacts/maintenance-review-20261012/text-paints-qualified-results.json) and [dormant forward-paint follow-up](artifacts/maintenance-review-20261012/dormant-paints-qualified-results.json), which closes the unchanged diagnostic and its live/unused/missing outcomes. |
| Composite placement flags (T2) | The unchanged [composite-flag sheet](tests/Research/composite-flag-exit-sheet/README.md) is **1/1 complete document / 3/3 exact controls** on both platforms. Overlap/scaled/unscaled flags, native child strikes and metric origins, composite light-alignment samples and default small-strike tip dropouts are qualified. Preserve [22 independent contour witnesses](tests/Research/composite-flag-exit-sheet/diagnostics/native-contours.json), malformed/resource boundaries and the [qualification receipt](artifacts/maintenance-review-20261012/composite-flags-qualified-results.json). |
| Non-solid affine sources (E1) | The unchanged [affine-source sheet](tests/Research/affine-source-exit-sheet/README.md) is **1/1 complete document / 4/4 exact controls** on both platforms, from **0/1 / 0/4**. Shared geometry/paint/glyph placement preserves clipped linear sources, radial opacity, bounded patterns, clipped supplied fonts and sibling scope. Native padded capture, font layout bounds and baseline snapping are protected by the [qualification receipt](artifacts/maintenance-review-20261012/affine-sources-qualified-results.json), intended-font proof and resource/unsupported-boundary regressions. |
| LinearRGB shadow/restoration (E2) | The unchanged [retained document](tests/Research/linear-restoration-exit-sheet/README.md) is **1/1 complete / 3/3 exact controls** on both platforms. Its 3,312-pixel difference and colored/translucent shadow gates are resolved; identity/zero-offset outcomes stay exact. Native working-space tint/source composition, integer/fractional offsets and packed nonopaque restoration reuse existing playback. Preserve the [qualification receipt](artifacts/maintenance-review-20261012/linear-restoration-qualified-results.json), unchanged original observation and material resource protection. |
| Filters and font infrastructure | Preserve [filter-region results](tests/Research/filter-regions/manifest.json) **26/26**, [colored-effects bounds/restoration](tests/Research/colored-effects-markers/README.md), [affine effect qualification](artifacts/maintenance-review-20261012/affine-filters-qualified-results.json), intended-font/metrics proof, catalog conformance and the implemented [FontProbe host](tools/Lokad.Svg.FontProbe/README.md) with six transport/input regressions. Private deployment validation remains required. |
| API/resource/package contracts | Preserve bounded preparation/raster/encoding, size/work denial, cancellation/retry, partial cleanup, immutable borrowed bytes, single-use prepared ownership and output integrity. Existing [API contracts](docs/API_CONTRACT.md), package and installed-consumer protections remain required. |

The latest complete local [qualification](artifacts/maintenance-review-20261012/linear-restoration-qualified-results.json)
is **8,056 tests/platform**, normal incremental Release build plus full tests in
**77.8s Windows / 263.3s Linux**. It preserves every preceding semantic case and
established exact output. The receipt owns the exact implementation revision and
hosted CI outcome, including package/installed-consumer validation; batch pushes
and reuse closed exact-revision audits without repolling. Unchanged documentation
and completed accounting work do not reopen qualification.

Items 1–3 (G2–G7 and C1–C6c) are complete under the linked evidence.
T1, its dormant forward-paint follow-up, T2, E1 and E2 are also complete. The remaining
font exit is T3; filtered-source extensions belong to E3. Preserve
`inv-146` and every established exact result.

<a id="delivery-order"></a>
<a id="3-ordered-support-queue"></a>

## Ordered delivery checklist

**Next three deliverables:** **T3 private font requests → E3 filtered-source
boundaries → E4 marker semantics.** Then finish S17.
T3 needs the existing private deployment source; while that local input is pending,
advance the independent public E3 work. T3 remains a required release exit.
Bring a shared dependency forward only when it advances the next acceptance case.
Do not make an exhaustive G2 or shape × paint × instance grid a prerequisite.

Each row owns one required exit. All visual exits require exact unchanged declared
outputs on Windows/Linux, applicable negative/degenerate outcomes and affected
material ownership checks. Run full qualification at the finished shared
correction, under the validation policy below. A shared witness can close several
rows; it is measured once, without duplicate API or combination requirements.

| Order / required exit | Missing behavior or evidence; dependencies | Concrete acceptance and completion condition |
| --- | --- | --- |
| [ ] T3 — private requests (item 4 / G9) | End-to-end deployment evidence remains missing; host/catalog and public conformance are implemented. Use the [authorized private workflow](#authorized-private-font-testing), with T1/T2 fixes only if a request needs them. | Freeze one regular, bold, italic and fallback request against the unchanged deployed manifest. Retain four sanitized selection/metrics/pixel outcomes; convert in-scope defects into public reproductions and resolve them. Private success cannot replace public acceptance. |
| [ ] E3 — filtered-source boundaries (item 5 / G8) | Object-box output clips; uncovered mask, nested-viewport, affine nested-filter, marker, text and local-use sources and applicable filter presentation. Reuse qualified output clips/nested halos, C/T scopes and E1–E2 playback. | Exact filtered target with an object-box clip, plus one witness for each listed uncovered source boundary. Preserve source/output order, halos and scope termination. Bring an E4 marker dependency forward only if needed; no content × graph × container census. |
| [ ] E4 — marker semantics (item 5 / G8) | Font-relative sizes, inherited container paint/child override and stylesheet/inline priority; remaining auto/auto-start-reverse tangents. Reuse [path](tests/Research/path-marker-content/manifest.json), [instance](tests/Research/marker-instance-content/manifest.json), angle/overflow and source-geometry machinery. | Exact font-relative and cascaded-paint markers, and open/closed/zero-length-path orientation witnesses. Valid degenerates have measured outcomes; missing/recursive references remain categorized errors. Reuse qualified spellings and controls. |
| [ ] S17 — release (item 6) | Finished T1–T3/E1–E4 plus retained product, font, resource and packaging contracts. | Every [release condition](#release-completion-s17) holds together for the finished revision. Counts alone cannot establish completion. |

Before implementing a row, check current code and qualified manifests. Distinguish
an actual rejection/difference from admitted behavior needing evidence; historical
`observedGate` text and test names are not live blockers. Unlinked recipes above
are planned acceptance, not measured failures. Reuse exact witnesses, retain the
original input when replacing obsolete rejection assertions, and add only the
missing independent case. Detailed mechanisms/history belong in the linked design
and inventory records; this checklist is the sole remaining execution queue.

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

Items 1–2 are complete; the [geometry/scope inventory](docs/INVENTORY.md),
[existing map witnesses](artifacts/shared-stroke-pattern-maps-20261015/existing-map-witnesses.json)
and [shared-map qualification](artifacts/maintenance-review-20261012/shared-maps-qualified-results.json)
own their evidence. Retain cap/dash/dot, inherited shader, reflected phase,
nonpainting/device-width and degenerate/cleanup regressions. Root viewBox,
pattern-content viewBox and stroke host maps remain distinct native stages.
Composition/resource exits are complete under item 3, including T1 glyph
contexts. Only filtered/marker source extensions (E3–E4) remain required here.

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

All declared C exits are complete. This index replaces their historical
implementation narratives; retain the referenced independent cases and useful
resource regressions. The glyph-mask instance extension is also exact in the
qualified path control and complete T1 document.

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
Image/CSS qualification and all original positive/negative outcomes remain closed.
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

T1 and T2 are complete; T3 is the remaining G9 exit. The
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

E3–E4 in the ordered checklist are the only remaining G8 exits. Preserve the
[whole effects sheet and eleven controls](tests/Research/effects-marker-sheet/README.md),
[filter-region results](tests/Research/filter-regions/manifest.json) and qualified
colored-effects, affine capture, output-clip and nested-halo witnesses. Reuse
covered panels before acquiring another document. Preserve native origins/nearest
offsets, mixed-space consumers, region/opacity and graph/resource protections.

Outside scope with explicit outcomes: backdrop inputs, external filter images,
displacement, turbulence, lighting, morphology, convolution, arithmetic composite,
other blends, backdrop-filter and CSS filter-function lists. Recursive effects and
forward/missing result names retain categorized outcomes. `inv-146` source/parent-
device rounding and padded capture remain closed.

## Release completion (S17)

Release is complete only when all of these hold for the **finished revision**:

1. Completed G2–G7 dependencies and all T1–T3/E1–E4 exits are closed with linked
   exact acceptance, measured valid-degenerate outcomes and applicable negative/
   resource regressions. No checklist row remains candidate-only. Reused evidence
   is sufficient; unverified behavior, actual gates and known in-scope defects stay
   open. No unapproved deferral is hidden by an error.
2. All established exact outputs remain exact, including **27/27 / 258/258**,
   selected documents and useful visual regressions. Every declared document
   variant must be measured. Rejection, difference, oracle failure and unmeasured
   stay distinct; no tolerance/quirk budget or silent denominator change. The
   original [S17 overall/per-stratum 80% criterion](docs/S17_DECISIONS.md) is
   exceeded and permits no regression from achieved fidelity.
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

Cohort/document counts prove only their declared outputs. More corpus acquisition
must demonstrate an identified remaining gap; no general-SVG success claim or
open-ended representativeness quota delays this release.

## Orthogonal validation and execution

- During development, build/run affected semantic, visual and resource checks.
  At a finished shared correction run full Windows/Linux qualification once;
  package/consumer checks apply when their contracts change and at release.
  Reuse unchanged evidence; PLAN-only edits need consistency/diff review.
- Preserve every distinct SVG, viewport, scale, background and platform reference.
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
- Add cases only for identified gaps/boundaries/defects. Before correction freeze
  licensed original bytes/options and references from three stable healthy fresh-
  process captures per platform. Prefer a suitable redistributable export; authored
  CC0 integration sheets are permitted. Compare decoded RGBA8 exactly, ignoring
  RGB only when both alphas are zero; no silent reference replacement. Supplied
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
| Case-insensitive requested/resolved family matching; italic match, closest 400/700 weight, then deterministic family/path/face tie-breaks; ordered configured fallback. | Existing Svg selection follows that order and accepts numeric SVG weights. Independent conformance vectors cover aliases, four real styles, fallback (`b0d4d5d`), plus unused collection entries that parse and select without poisoning the catalog while snapshots reject selected collection faces before acquisition. OoxPdf's bold request maps to 700; preserve SVG's numeric weight semantics. |
| Shared per-file sources coalesce concurrent downloads, cache successful bytes and use at most three attempts for transient transport failures. Manifest reads cap at 1 MiB; font reads cap at the smaller of the declared size and 64 MiB. | Implemented in the opt-in [FontProbe host](tools/Lokad.Svg.FontProbe/README.md), with synthetic transport regressions. Preserve Svg's separate 32 MiB aggregate borrowed-program and 256-face caps; the source's per-file cap does not replace them. |
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

The earlier one-program format inspection establishes no whole-pack or rendering
qualification; its historical detail stays in Git. The host and catalog are
implemented. Preserve them rather than rebuilding them as another S7 queue.

**Required private validation, attached to item 4 (G9):** freeze one regular, one
bold, one italic and one fallback request before execution. Use the unchanged
deployed manifest to prepare their required programs; inventory their relevant
formats, coverage, composites and positioning
tables, and compare selection with the public OoxPdf contract. For those requests,
compare curves/counters/composites, AV/space and fractional labels using identical
verified bytes in Svg and an SVG-image-local browser font embedding. Existing
intended-font controls, three stable captures and independent metrics/pixels apply.
Completion is a sanitized outcome for every selected request, with private gaps
categorized and reproducible in-scope defects converted to public synthetic cases
and resolved under G9. Unsupported formats retain explicit outcomes; no whole-pack
combination quota or new host is required. Private success cannot replace public
acceptance. The security/retention rules above remain mandatory.

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
oracle results are not canonical Linux evidence. Corpus acquisition and optional historical coverage guidance follow the
validation and scope decisions above; no further acquisition quota applies.

## Optional reference sources

The local reference review in the earlier draft remains useful design guidance.
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
| resvg-test-suite `d8e064337faf01bc5a9579187a56dbdbe3eacc72` | Semantic counterexample index. It overlaps heavily with resvg's bundled suite; neither is a separate real-logo cohort or our PNG oracle. |
| NanoSVG `239e102ec2c691f2902e20ace2ed36ee4a35cfe6` | Secondary small-parser/scanline comparison only; five-subsample output is not the exactness target. |
| Matched Skia `f8b66b7597c4cc859d3ed190e9c6872241e6721c` | Release-tag scan, paint routing, stroker and curve construction for demonstrated S11/S12/S14 dependencies. The bounded clipping/solver adaptation retains this pin and BSD notices; compiled-native vectors and experiment provenance are in the design record. |

The recorded Skia comparison `8b8c3872fbc03f025855db96ce683f34ec98a815`
is a main revision, not the browser release pin. Use explicit matched
source reads without changing it implicitly. resvg's registry tiny-skia dependency
also need not be byte-identical to the sibling checkout despite equal version text.
