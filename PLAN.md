# Lokad.Svg — finite renderer delivery plan

Updated 2026-10-16 at the text-definition/font-admission checkpoint.
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
| Additional complete documents | **Twenty-six authored documents** (nineteen CC0, seven MIT) and **one sourced W3C decoration document**, exact on both platforms and separate from both cohorts. [INVENTORY](docs/INVENTORY.md) owns the document list, controls and immutable assets/options. The [tile-mask bank](tests/Research/tile-mask-exit-sheet/README.md) is **1/1 document / 2/2 controls**; retain every earlier bank and direct/curved decoration boundary. |
| Shared geometry, paint and scope dependencies | Fractional use widths and symbol frames, nonuniform curves/affine oval strokes, non-scaling conics, composed cubic clips, custom mask regions/container opacity, inherited symbol strokes, cubic-pair clip unions, normalized mask content, tile-local clips and axis/affine ancestry at nested viewports are qualified. Reuse [geometry/scope evidence](docs/INVENTORY.md), [existing map witnesses](artifacts/shared-stroke-pattern-maps-20261015/existing-map-witnesses.json), [tile-content qualification](artifacts/maintenance-review-20261012/pattern-content-qualified-results.json) and [shared-map qualification](artifacts/maintenance-review-20261012/shared-maps-qualified-results.json). These are available dependencies, not blockers to repeat. |
| Filters and font infrastructure | Preserve [filter-region results](tests/Research/filter-regions/manifest.json) **26/26**, [colored-effects bounds/restoration](tests/Research/colored-effects-markers/README.md), [affine effect qualification](artifacts/maintenance-review-20261012/affine-filters-qualified-results.json), intended-font/metrics proof, catalog conformance and the implemented [FontProbe host](tools/Lokad.Svg.FontProbe/README.md) with six transport/input regressions. Private deployment validation remains required. |
| API/resource/package contracts | Preserve bounded preparation/raster/encoding, size/work denial, cancellation/retry, partial cleanup, immutable borrowed bytes, single-use prepared ownership and output integrity. Existing [API contracts](docs/API_CONTRACT.md), package and installed-consumer protections remain required. |

The latest complete local [qualification](artifacts/maintenance-review-20261012/definition-font-qualified-results.json)
is **7,985 tests/platform**, normal incremental Release build plus full tests in
**81.7s Windows / 296.0s Linux**. The same receipt owns the substantive revision's
hosted CI, package and installed-consumer checks. Reuse closed exact-revision
audits without repolling; unchanged documentation does not reopen qualification.
Completed work stays closed under the existing coverage/accounting policy.

Items 1–3 and their declared C1–C6c exits are complete. Remaining glyph
maps/admission belong to item 4 and filtered-source boundaries to item 5.
`inv-146` and completed mechanisms need no requalification.

<a id="delivery-order"></a>
<a id="3-ordered-support-queue"></a>

## Ordered delivery checklist

**Next three deliverables, in order:**

1. **Finish the frozen instance/paint document (T1).** Definition/font admission
   is exact. Next transfer deferred text paths and resource scopes through the
   existing source replay, then close forward/object-box/radial/pattern and
   constant/empty text paints. Finish the whole document and all four controls.
2. **Close the three named composite flags (T2).** Reuse the bounded decoder and
   fitting paths; establish exact public overlapping/transformed-offset witnesses.
3. **Complete the four private font requests (T3).** Freeze regular, bold, italic
   and fallback requests, retain sanitized outcomes and resolve any in-scope defect
   using a public reproduction. Private validation cannot replace public acceptance.

Items 1–3 and their C1–C6c exits are completed dependencies, owned by the evidence
above and boundary tables below. Do not reopen them as prerequisites. After T1–T3,
finish E1–E4 and S17 in order. No exhaustive G2 or shape × paint × instance grid.

The [instance/paint bank](tests/Research/instance-paint-exit-sheet/README.md)
retains thirty-six healthy captures and its measured `dbfc42bc` rejection
[baseline](artifacts/instance-paint-exits-20261016/declaration-qualification.json).
It is **0/1 complete document / 1/4 exact controls** on both platforms; definition
admission is closed, remaining path/resource and paint gates are explicit.
Preserve these bytes/options/references through every correction.

| Required item, in order | Missing behavior and dependencies | Acceptance and completion condition |
| --- | --- | --- |
| [ ] 4 / T1. Text instances and paints (G9) | Deferred instanced path/resource transfer and remaining text paint routes; reuse qualified source tokens/CSS, instance fonts, native glyph paints and resource binders. | The unchanged instance/paint document and four controls are exact on both platforms; constant/empty/missing outcomes and affected ownership checks pass. Definition/font admission is already exact. |
| [ ] 4 / T2. Composite flags (G9) | OVERLAP_COMPOUND, SCALED_COMPONENT_OFFSET and UNSCALED_COMPONENT_OFFSET; reuse bounded composite decoding/fitting. | Public overlapping/transformed-offset witnesses establish all three flags exactly on both platforms, with affected failure/ownership checks. |
| [ ] 4 / T3. Private validation (G9) | Four fixed private requests through the implemented host/catalog. | Four sanitized outcomes; discovered in-scope defects have resolved public reproductions. |
| [ ] 5. Static effects and markers (G8) | E1–E4: non-solid affine sources, named linearRGB restoration/shadows, object-box/source boundaries and marker size/tangent/style semantics. Reuse bounded capture, shared paint maps and qualified scopes. | Every named exit is exact on both platforms, including the retained standalone linear-image defect; affected graph/region/resource checks pass. Preserve the whole effects sheet and eleven controls. |
| [ ] 6. Release (S17) | Finished revision after items 1–5, retaining the product/font contracts below. | All [release conditions](#release-completion-s17) hold together. Document/test counts alone cannot establish completion. |

The tables below specify these exits; they are not another execution queue.
Unlinked acceptance recipes are **planned**, not measured failures. Before changing
code, reconcile each with current implementation and qualified manifests: reuse an
exact witness, distinguish admitted-but-unverified behavior from actual rejection/
difference, then add only the missing independent case. Historical `observedGate`
text and test names alone cannot establish a live blocker. Keep original inputs
when replacing obsolete rejection assertions. Shared witnesses count once across
groups and APIs. Use existing banks/tests; no new tracking framework is required.

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

Items 1–2 are complete under their linked acceptance/qualification records.
Preserve the original cap/dash/dot inputs, inherited shader controls, reflected
offscreen nonpainting, visible pattern phase and device-width checks. Root
viewBox scaling, pattern-content viewBox and stroke host maps retain their
distinct coordinate stages; the [design record](docs/RASTERIZATION_DESIGN.md)
owns implementation history. Composition/resource exits are qualified under item 3; glyph maps belong
to item 4 and filtered/marker sources to item 5.

Completed inherited-symbol, reflected-phase, curved-target and viewport-map
witnesses remain closed. Zero-size oval/object-box nonpainting and categorized
singular-pattern/cleanup outcomes in the existing [coordinate](tests/Lokad.Svg.Tests/SvgPatternCoordinateTests.cs)
and [template](tests/Lokad.Svg.Tests/SvgPatternTemplateTests.cs) tests need no new grid.

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

| Required exit (item 3) | Concrete acceptance, dependency and completion |
| --- | --- |
| C1. Clip unions — complete | The [original sheet and five controls](tests/Research/clip-union-exit-sheet/README.md) close mixed rectangle/path, overlapping conics, independent compound/concave winding and 43-child fallback, including applicable C5 clip-rule cascade. The unchanged [cubic document/control](tests/Research/cubic-viewport-exit-sheet/README.md) now close the cubic-pair solver on both platforms, with analytic crossing/coincident/degree-nine and degenerate cubic-line checks plus affected resource protection. Reuse this qualified dependency. |
| C2. Affine ancestor at a nested viewport — complete | The same whole document and 192 by 160 ancestor/viewport control are exact on both platforms. Physical clipping and unaffected siblings inside/after the ancestor are protected, with zero-size nonpainting/cleanup controls. Ancestor/viewBox rebasing and the existing native physical clip machinery are shared dependencies. |
| C3a. Mask units, color and general maps — complete | The [composition/resource bank](tests/Research/composition-resource-exit-sheet/README.md) closes unit/restoration acceptance; the [mask color/affine bank](tests/Research/mask-color-affine-exit-sheet/README.md) closes colored luminance and general-affine user-space/object-box target maps. Original colored-stroke/mixed-paint inputs, tight mapped curve bounds, leaf/group/use regions and affected resource/sibling checks are protected. Preserve native source replay and AA/BW/luminance-layer distinctions. |
| C3b. Mask region defaults and nested-viewport scopes — complete | The unchanged [document and five controls](tests/Research/mask-viewport-exit-sheet/README.md) are exact on Windows/Linux. Four original rejected SVGs now protect every output pixel and cleanup; spatial group/viewBox content and following siblings are exact. Focused percentage/implicit-root defaults, partial group sources, pre-clip object bounds and affected denial/cancellation/retry pass. Reuse C3a source replay, native target bounds and viewport rebasing. Glyph-only masks remain T1. |
| C4. Independent image alignment offsets — complete | Existing [active-axis witnesses](artifacts/shared-map-exits-20261015/existing-image-alignments.json) cover xMid/xMax and yMin/yMid/yMax. The frozen [active xMin control](tests/Research/composition-resource-exit-sheet/image-horizontal-min.svg) now matches healthy platform triples through the canonical visual API; its wider meet viewport has a real horizontal alignment choice. Existing [aspect tests](tests/Lokad.Svg.Tests/SvgAspectRatioTests.cs) preserve all nine spellings, `none` and omitted-meet equivalence. No production fix or alignment/placement census is required. |
| C5a. Qualified resource presentation and tile-local clips — complete | The unchanged [mixed tile/document](tests/Research/composition-resource-exit-sheet/README.md) qualifies bounded image/vector content with a native local group clip and forward-source/sibling regressions. Reuse clip-rule container/child stylesheet/inline priority from the [clip bank](tests/Research/clip-union-exit-sheet/README.md), [mask presentation](tests/Research/mask-presentation/manifest.json), [inline pattern inheritance/override](tests/Research/paint-order/pattern-presentation-alias.svg), and the two analytic [pattern CSS priority](tests/Lokad.Svg.Tests/SvgPatternContentTests.cs) controls. [Root/resource cascade](tests/Research/root-resource-cascade/manifest.json), selectors and paint-order witnesses stay qualified. Marker/filter presentation belongs to E3/E4 and text/span paint to T1. |

Qualified CSS, varied/affine image pixels, source-cropped slice, partial image-use
masks, tile content and scoped composition remain closed; use the evidence table.
Outside scope with explicit outcomes: interlaced/16-bit/color-managed PNG,
JPEG/GIF/WebP/external image fetching; CSS imports/media/supports, pseudo
classes/elements, siblings, layout, variables, animation and web fonts.

The declared C5b/C6a–C6c bridges below are complete. Reuse their qualified
mechanisms; glyph and filtered-source contexts remain required under items 4–5.
Original rejection inputs are retained as positive pixel/resource regressions.
This closes named exits without adding a shape × paint × instance census.

| Required bridge (item 3) | Dependency, acceptance and completion |
| --- | --- |
| C6a. Clip/opacity scopes across viewports — complete | The unchanged [document and six controls](tests/Research/viewport-clip-opacity-exit-sheet/README.md) are exact on Windows/Linux, preserving all six original Core inputs. Shared clip placement includes viewBox scale; viewport-owned layers compose nested/root/group opacity; crossing strokes reuse native source geometry and physical clips. Original nested/inherited/masked opacity, empty public output and font acquisition now have positive regressions. The retained fractional frame and cropped-opacity controls, native overlap bytes, siblings and affected ownership checks pass. Reuse the qualified dependency. |
| C6b. Forward/object-box clip binding — complete | The unchanged [document and six controls](tests/Research/forward-bbox-clip-exit-sheet/README.md) are exact on Windows/Linux, preserving all six original Core inputs and forty-two healthy captures. Shared geometric bounds and deferred binding handle definition order, images/groups/lines/circles and transformed normalized content. Zero-height lines and disjoint clips are valid nonpainting; mask order, pre-viewport object bounds, siblings and affected resource checks pass. The retained rotated-stroke object-box input also passes; its user-space variant is qualified under C6c. Reuse [qualification](artifacts/maintenance-review-20261012/forward-bbox-qualified-results.json); no target × clip-shape census. |
| C5b. Masks inside pattern content — complete | The unchanged [document and two controls](tests/Research/tile-mask-exit-sheet/README.md) are exact on Windows/Linux. The original rejected leaf SVG now checks every pixel and cleanup. Spatial group masks, forward local uses and tile/root siblings are qualified; shared owning-scene binding, delayed sealing and native mask-before-opacity arithmetic preserve C5a clips/styles. Focused nested mask/clip-marker, forward leaf resource and anonymous-owner regressions plus affected denial/cancellation/retry pass. Reuse [qualification](artifacts/maintenance-review-20261012/tile-mask-qualified-results.json); no repeat-work or presentation cross-product quota. |
| C6c. Affine user-space rectangle clips — complete | The unchanged [original 96×72 input and visible control](tests/Research/affine-rectangle-strokes/README.md) are exact on both platforms. General leaf/group clips defer through shared native rectangle binding; the parent supplies an outward-rounded construction window while geometry retains fractional viewport coverage. Native rectangle contour order preserves existing vertical-piece combination. Original unit outcomes, quarter-turn source coordinates, nonpainting, fractional coverage, siblings and affected ownership are protected. The retained transformed-fill clip-crossing reference is also exact. Reuse [qualification](artifacts/maintenance-review-20261012/affine-rect-clip-qualified-results.json); no document or combination quota is added. |

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

| Required exit (item 4) | Concrete acceptance, dependency and completion |
| --- | --- |
| T1. Remaining glyph maps/paints/instances | The unchanged [resource](tests/Research/glyph-resource-exit-sheet/README.md) and [text-map](tests/Research/text-map-exit-sheet/README.md) documents are complete. Reuse qualified layout, source tokens/CSS, instance typography, native glyph paints and resource binders. **Remaining acceptance:** the frozen [instance/paint sheet](tests/Research/instance-paint-exit-sheet/README.md), currently **0/1 complete / 1/4 exact controls**: forward text/group/symbol definitions and instance-only font discovery are exact; a masked curved-label use and following sibling remain required; a row for forward direct-text shader binding and object-box/radial/pattern text paints, including constant/empty outcomes. The unresolved routes remain required, with no scope deferral. Add focused controls only to diagnose each shared fix. Completion requires those unchanged declared outputs exact on both platforms, categorized missing/degenerate outcomes, representative raw/prepared font discovery and affected ownership checks. Preserve original masked inputs, source order, endpoint continuation and scalar-list consumption. No font/size/map or paint × glyph × placement grid. |
| T2. OVERLAP_COMPOUND, SCALED_COMPONENT_OFFSET, UNSCALED_COMPONENT_OFFSET | Reuse bounded composite decode/fitting. Public synthetic overlapping/transformed-offset components must establish all three flags exactly on both platforms, with affected ownership/failure checks. Preserve [parent-hint/USE_MY_METRICS/nested-accent tests](tests/Lokad.Svg.VisualTests/SvgCompositeMetricsTests.cs), [contours](tests/Research/curved-font-documents/diagnostics/composite-metrics-contours.json), Windows LCD/dropout and Linux light fitting. Executed unsupported opcodes/formats reject explicitly; no general VM grid. |
| T3. Private deployment validation | Use the implemented host/catalog and unchanged manifest. Freeze one regular, bold, italic and fallback request; execute the [private workflow](#authorized-private-font-testing). Finish with four sanitized outcomes and public synthetic reproductions of in-scope defects resolved; private success cannot replace public acceptance. |

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

| Required exit (item 5) | Concrete acceptance, dependency and completion |
| --- | --- |
| E1. Non-solid affine paint/source maps | Reuse qualified solid-source affine capture and shared layer/paint maps. Exact linear/radial-painted affine source, patterned affine source and supplied-font affine source with source clip/nested resource scope; reuse covered panels. Preserve native origins/nearest offsets and all effects controls. |
| E2. Named linearRGB shadow/restoration gaps | Exact colored linearRGB shadow over translucent source; identity/zero-offset colored-source controls where not already qualified. Resolve the retained [single-linear-image/outer-opacity difference](artifacts/effects-marker-sheet-20261015/mixed-consumer-single-blur.svg) against healthy frozen references; one native observation is insufficient. Preserve region/opacity, mixed-space and 26/26 filter-region results. |
| E3. Object-box output clips and filtered-source boundaries | Reuse qualified output-clip/nested-halo replay. Exact filtered target with object-box clip, plus one witness for each uncovered mask, nested-viewport, marker, text and local-use source boundary and applicable filter presentation. Bring C/T dependencies forward only as needed. Protect source/output order, halos/scope termination; no content × graph × container census. |
| E4. Marker sizes, style and tangents | Exact font-relative sizing, inherited container paint/child override with stylesheet/inline priority, and auto/auto-start-reverse open/closed/zero-length-path semantics. Reuse qualified [path](tests/Research/path-marker-content/manifest.json), [instance](tests/Research/marker-instance-content/manifest.json), angle and overflow controls. Valid degenerates have measured outcomes; missing/recursive references remain explicit errors. |

Outside scope with explicit outcomes: backdrop inputs, external filter images,
displacement, turbulence, lighting, morphology, convolution, arithmetic composite,
other blends, backdrop-filter and CSS filter-function lists. Recursive effects and
forward/missing result names retain categorized outcomes. `inv-146` source/parent-
device rounding and padded capture remain closed.

## Release completion (S17)

Release is complete only when all of these hold for the **finished revision**:

1. Items 1–5 and their named G2–G9 exits are closed with linked exact acceptance,
   measured valid-degenerate outcomes and applicable negative/resource regressions.
   Reused evidence is sufficient; unverified behavior, actual gates and known
   in-scope defects stay open. No unapproved deferral is hidden by an error.
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
  minutes per platform.** The qualified 81.7s/296.0s checkpoint above meets this.
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
