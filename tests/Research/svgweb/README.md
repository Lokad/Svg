# Hosted SVGWeb review

[Originals](originals.json) accounts for all 204 direct `.svg` entries in the
[hosted directory](https://dev.w3.org/SVG/tools/svgweb/samples/svg-files/): URL,
source hash/size, declarations, lexical font/resource use and platform outcomes.
Fetch bodies into bounded memory and discard them. Ordinary tests are offline;
this directory contains metadata and original orthogonal controls.

The current default conversion renders 161 originals per host: **161 exploratory
exact, none different**, with 40 UnsupportedFeature and 3 MalformedInput outcomes. The
[current outcomes](default-outcomes.csv) list every source's status and first gate;
the inventory retains historical observations separately.
These observations use transparent output without supplied fonts/resources.
Exact default output does not qualify other font, resource or static-state contexts;
whole-corpus acceptance remains open.

The [supplied-program inventory](supplied-original-contexts.json) pins all 19 font
contexts and three stable healthy captures per host. The [current target correction](windows-conditional-targets.json)
closes eleven complete originals, including helloworld, mysvg and snake, in those
declared contexts. One Windows tiny-serif discrepancy and seven legacy SVG-font
originals remain open; transported programs do not close legacy declaration semantics.

Resolve gaps by mechanism, preserving original-to-control mappings and independent
references. A successful render, ambient font substitution or equal images alone
cannot establish correctness or redundancy. Font declarations are lexical inventory,
not resolved requests; navigation is separate from image/CSS/media dependencies.

## Validation responsibilities

- Mechanisms pin numerical stages, authored geometry, parsed lengths, bounds and
  rounding directly. Keep meaningful boundary coverage in these tests.
- Representative ownership tests protect refusal, cancellation, cleanup and retry.
  Preserve distinct allocation paths without repeating them for every image.
- Rendering controls normally use canonical conversion. Reference admission requires
  three fresh healthy, profile-cleaned captures on each platform and exact existing
  comparisons. Preserve meaningful platform differences and failing-before cases.
- Recheck all original hashes, outcomes, diagnostics, dimensions and rendered output
  after shared fixes. Hosted bodies and fragments remain in memory only.

The [capture model](capture-model.json) distinguishes image/Blob, background and
viewport contexts. Match logical image viewport separately from screenshot size;
label static-state changes and resource materialization. Scripts are inactive in
image documents; animation, ambient fonts and unresolved resources need explicit
qualification. The [tool procedure](../../../tools/README.md#hosted-svgweb-inventory)
reproduces the inventory without retaining SVG files.

## Shared rendering model and evidence

Each record links mechanisms, original controls, native provenance and scoped
acceptance. Measurements and rejected experiments remain in the records; the
current inventory above governs overall status.

| Responsibility | Evidence and retained contracts |
| --- | --- |
| Filter source opacity | [Control responsibilities](../filter-source-opacity/README.md), [recording bounds](filter-recording-bounds.json) and [requested input storage](filter-deferred-input-storage.json). Shared recording/device bounds and retained input views close the original stroke-group, colour and wide-alpha discrepancies. Canonical regressions remain exact. |
| Mask source bitmap clips | [Shared opacity query](mask-opaque-bitmap.json), [own control](../mask-source-clip/README.md). Polygon and hairline consumers retain binary dispatch within an opaque bitmap intersection. The original's five masked-group differences are closed and its alpha plane is exact. |
| Windows movement compensation | [Movement state and rounding](windows-movement.json). Component-rounded compensation, explicit copy state, signed horizontal division and exact-ratio placement share one movement path. Eight direct mechanisms and one canonical original-font image protect independent platform behavior. Scoped matched Arial originals and all 173 diagnostic coordinates agree. |
| Stack projection words | [Stack-vector loading](windows-stack-vectors.json). Preserve supplied Windows F2Dot14 values across query/load and following movement, while keeping line normalization, classic normalization and freedom independent. One canonical original-font image retains both platforms. |
| Conditional hint functions | [Native recognition](windows-conditional-shift.json), [shared target eligibility](windows-conditional-targets.json). Conditional shifts and ordinary deltas share the vertical/composite target predicate while keeping their compatibility policies. Direct coordinates protect touch, IUP, loop, waiver and vector distinctions; two complementary canonical controls retain platform behavior. The complete matched-font snake original is exact. |
| Font declarations and supplied programs | [Font contexts](font-contexts.json), [shared program model](shared-font-model.json), [original context inventory](supplied-original-contexts.json), [current outcomes](windows-conditional-targets.json). Inline sizes retain their source context; GPOS extensions share payload resolution and freedom-copy hints preserve VM state. Explicit generic and named bindings close eleven complete originals. One Windows tiny-serif discrepancy and seven legacy SVG-font originals remain open. |
| Glyph recording and shader drawing | [Position recording](glyph-recording.json), [separate drawing binding](glyph-drawing-binding.json), [own controls](font-stages/capture.json). Filled glyphs share font recording while shaders retain their drawing map; layout, tangents and stroke semantics stay independent. Flat, root, group and curved-path graphs remain separate, with one canonical call per source. |
| Light-fitted glyph edges | [Alignment-zone fitting](latin-blue-zone-fitting.json). Zone matching and completed placement have separate state. Contour-order linked fitting preserves interpolation anchors and shared-edge updates. Two direct topology regressions protect the cause; the remaining Windows coverage differences stay separate. |
| Windows fitted outlines and scan curves | [Scan boundary](windows-scan-boundary.json), [original counter](../windows-scan-boundary/README.md). Fitted points retain half-unit precision; ink measurement and painting share profile-grid rounding and bounded extrema preparation. Four direct phase vectors, two temporary-lifetime cases and one canonical native image protect distinct responsibilities. |
| Nested filter source pictures | [Prepared drawing stages](prepared-source-drawing.json), [recording devices](filter-recording-devices.json), [controls](../nested-filter-placement/README.md). Recording precedes native capture across recursive affine replay, geometry, parameters and shaders. Placement controls and the complete default original are exact. |
| Nested filter placement | [Earlier source/layer review](nested-filter-placement.json). Isolation bounds and replay share the placed child binding. The subsequent prepared-drawing model replaces the ideal-linear helper while preserving source layout and capture bounds. |
| Rendered use sources | [Shared identity and replay](rendered-use-sources.json), [native controls](../rendered-use-sources/README.md). Root/group aliases share retained use nodes and document ownership; group opacity and filters reuse existing replay. Canonical controls and the affected default original are exact. |
| Descriptive link titles | [URI-aware attribute admission](metadata-attributes.json), [canonical control](../metadata-attributes/README.md). One predicate serves ordinary elements and defs; six direct boundary cases and one independent native image retain reference/namespace contracts. Both unmodified metadata originals are exact on both hosts. |
| Inert event attributes | [Static admission](inert-events.json), [canonical control](../metadata-attributes/inert-events.svg). One predicate serves element, defs, stylesheet, filter and span/path validators. Ten direct cases, one prepared-font contract and one native image separate responsibilities. The default rectangles original is exact; deeper static gaps continue under script, font, attribute and CSS review. |
| Non-executing script content | [Shared traversal](inert-scripts.json), [canonical control](../metadata-attributes/inert-scripts.svg). Preparation, selection and lowering share a bounded consumer. Retained empty markers preserve text nodes and lexical counts without copying bodies. Direct limits/namespace/XML cases, representative text contracts and one native image protect distinct roles. The default videos original is exact; three newly reached font gates remain open. |
| Static CSS state and source | [Shared admission and source boundaries](static-css.json), [canonical control](../metadata-attributes/static-css-state.svg). Inactive interaction rules retain no selector/value storage or ancestor branch; float does not affect image flow. Mechanisms, representative font acquisition and one native image separate responsibilities. Default faux-art/mail are exact. Root box layout has separate border/padding evidence. |
| Fixed geometry and rotation centres | [Shared lengths and stages](geometry-fixed-lengths.json), [canonical control](../geometry-fixed-lengths/README.md). Consumer-specific ranges and percentage stages remain distinct. Native rotation-centre sequencing fixes fractional pattern sampling without a shader adjustment. Default circles1 is exact; all 159 previous PNGs remain unchanged. |
| Root canvas and generation | [Canvas boundaries](root-canvas-background.json), [native controls](../root-css-box/README.md) and [box review](root-box-native-review.csv). Canvas color stays outside element opacity/visibility and child filter sources. Root image generation preserves computed display for explicit inheritance. Default photos is exact; solid border/content layout and CSS anchoring are closed in their linked follow-ups. |
| Root box/content layout | [Shared padding and canvas coverage](root-css-padding-layout.json), [padding controls](../root-padding/README.md) and [canvas edge controls](../root-canvas-density/README.md) separate CSS layout, snapped box projection, content clipping and solid paint coverage. [Border composition](root-css-border-layout.json) admits the fourteen frozen [border contexts](../root-box-layout/README.md) plus four edge interactions; solid borders and padding share layout stages. References stay fixed. |
| Text anchors and chunks | [Shared CSS inheritance and point precision](text-anchor-layout.json), [four canonical controls](../text-anchor/README.md). Cascade, span compaction and absolute chunk positioning retain current-scope anchors without splitting shaping. Initial Windows X points share strike precision across simple/composite loaders. Helloworld reaches the supplied-font gate; its complete rendering remains open. |
| Windows glyph origins | [Shared point/metric handoffs](windows-phantom-origins.json), [native readouts and masks](../windows-font-unit-scaling/phantom-origins.json). Keep current/original coordinates in the aligned frame, advance rounding separate from absolute coordinates, and parent-program phantoms separate from the inherited child paint origin. |
| Structural traversal | [Shared metadata and pattern locations](shared-traversal.json). Keep recognized foreign descriptions inert through preparation/lowering, preserve the first rejection context, and align rendered-group pattern registration with content selection. Mechanisms, one prepared-font contract and independent root/group/metadata fixtures protect distinct roles. |
| Root image layout and placement | [Root model](root-image-model.json), [projection stages](root-image-projection.json), [image boundary](root-image-boundary.json). Logical sizing, subpixel layout, recorded scene and destination coefficients stay distinct. Direct matrices and large viewBox controls protect stages that small images cannot distinguish. |
| Root experiments | [Viewport review](root-viewport-review.json), [stage review](root-image-stages.json). Preserve rejected output-only stretching and context distinctions. |
| Visible image source | [Source rectangle](root-visible-source.json). A clipped fractional image remaps its source before residual scale; keep the distinct float stages. Four direct cases and one independent transformed ellipse protect the rule. |
| Filter images and restoration | [Image model](filter-image-model.json), [bitmap restoration](filter-bitmap-restoration.json), [Gaussian aliases](gaussian-alias-restoration.json). Retain deferred/color-filter state, cropped storage, padding, enclosing opacity and final sampling roles. |
| Filter source requests | [Source capture](filter-source-capture.json), [request propagation](filter-source-request.json), [bitmap clip requests](filter-bitmap-clip-request.json). Ordinary, nested and image-containing sources use propagated requests; each bordered destination bitmap uses its own nonzero clip bounds. |
| Parameter source enclosure | [Capture and output hints](filter-parameter-source-enclosure.json). Enclose source visuals before graph expansion and the result before device mapping. Ordinary and affine capture share the parameter stage; recorded paint hints and captured content remain distinct. |
| Retained source stages | [Visual bounds and capture maps](filter-retained-source-stages.json). Keep authored visual/stroke extents before scene narrowing, and recording/destination stages through bitmap placement. Direct bounds/map tests and one canonical gradient ellipse protect zero-blur capture. |
| Captured gradient references | [Drawing reference](source-shader-reference.json). Compose source placement with recorded layout before native destination narrowing. One nonzero-blur fixture and existing direct map cases protect the rule; green grapes and all mapped consumers are exact. |
| Source raster windows | [Clip construction](source-clip-construction.json), [raster window](filter-raster-window.json). Admitted numerical paint and construction padding remain separate from tight visual extents and captured content. |
| Nested pictures and affine capture | [Nested picture](filter-nested-picture.json), [transform review](filter-transform-review.json). Keep original descendant paint hints, filtered output and parameter capture enclosure distinct. Both opposed affine controls are exact canonical regressions. |
| Filter numerical stages | [Black linear stages](black-linear-stages.json), [bilinear weights](filter-bilinear-review.json), [numeric vectors](../../Lokad.Svg.Tests/TestData/highp-bilinear.json), [browser probe](highp-bilinear.js). Transfer, byte storage, packed alpha, high-precision weights and addressing have separate responsibilities. |
| Clip source geometry | [Referenced paths](clip-use-construction.json), [source placement](clip-source-placement.json), [canvas boundaries](clip-canvas-boundaries.json). Retain authored quadratic/cubic/arc operands, local resource placement and referencing-canvas concatenation. |
| Clip coverage and bounds | [Replay state](clip-replay-state.json), [visual bounds](clip-visual-bounds.json), [analytic alpha](analytic-alpha-bytes.json). Separate silhouette coverage, stroke/layout enclosures, numerical raster ink and native byte narrowing. |
| Clip definition sources | [Sources and intersections](clip-definition-sources.json). Ordered uses share geometry staging; whole-resource intersections reuse the curve boundary solver. Painted unions retain child/canvas clips and allocated construction bounds. Twelve canonical native controls, direct geometry/dependency/unit cases and selected ownership inputs protect separate responsibilities. The default clippath.svg is exact on both hosts; all prior default PNGs are unchanged. |
| Radial paint binding | [Source binding](radial-source-binding.json), [primitive binding](radial-primitive-binding.json). Retain factory coordinates before local/reference maps across primitives, paths, instances, clipping and inherited stops. Direct stage tests and one canonical reflected radial protect primitive rounding. |
| Gradient bitmap coordinates | [Recorded stages](gradient-recorded-bitmap-origin.json). Subtract the owning bitmap origin before replaying child transforms. Linear/radial native controls and direct mapping/ownership tests protect the shared order. |
| Opaque gradient coverage | [Program opacity and coverage](opaque-gradient-coverage.json). Retain factory eligibility and direct-span/A8 interpolation stages. One canonical edge fixture and six compiled native vectors protect the shared rule; the complete default Gallardo image is exact. |
| Completed curve strips | [Direct painting and clip construction](completed-strip-coverage.json). Completed direct fringes use the paint clip; pending mask rows and clip definitions keep path bounds. Circle, authored arcs and rounded rectangles protect distinct source builders. |
| Short authored curves | [Closing chords](thin-authored-contours.json). Fallback sample count cannot suppress authored curved ink. Direct preparation/index tests, explicit suppression and ownership contracts, and three canonical fill/shader/clip fixtures protect distinct responsibilities. |
| Fixed stroke lengths | [Shared unit conversion](fixed-stroke-lengths.json). Root, marker and stroke factors share one parser seam; callers retain sign/range/base/narrowing rules. Direct mechanisms, two existing selection contracts and one canonical native fixture protect distinct responsibilities. |
| Filter recording and bitmap devices | [Shared recording, origins and padding](filter-recording-devices.json). Canvas and shader products stay distinct; all four capture paths share root setup. Three canonical controls protect fractional child hints, axis replay and isolated opacity; the complete default lingerie original is exact on both hosts. |
| Filter recording bounds | [Source enclosures](filter-recording-bounds.json). Font line boxes and retained local stroke extents determine capture hints independently of scene ink. Existing platform font and gradient-stroke controls protect clip and sampling origins; all155 rendered originals remain exact. |
| Deferred filter storage | [Requested input views](filter-deferred-input-storage.json). Retain pre-offset pixels outside the terminal output, bounded by actual image extents and crops. Seven direct mechanisms and one canonical control protect storage separately from sampling phase and API ownership. |
| Isolated opacity and source raster scope | [Local enclosure and shared raster binding](opacity-source-scope.json). Recorded primitive strokes enclose before placement; filter hints stay fractional. Hairlines borrow the compositor grid and preserve source clip, origin and owned interior independently. Direct mechanisms and three leaf/group fixtures protect distinct recording graphs. |
| Gradient declarations | [Typed lengths](gradient-lengths.json), [inheritance fixture](../../Fixtures/original/radial-coordinate-inheritance.svg), [stop identity fixture](../../Fixtures/original/gradient-stop-identities.svg). Preserve number/percentage intent, missing/default values, units, per-field inheritance, viewport resolution and metadata. |
| Nested pattern content | [Content graph and native controls](nested-patterns.json). Borrow shared sealed scenes, validate cycles and actual content depth once, and retain forward/template/mask/glyph interactions. |
| Picture repeat sampling | [Floating tap addresses and fragment drawing](pattern-repeat-addressing.json). Repeat each tap before gather clamping; keep shader drawing separate from fitted glyph ink. Direct native vectors and one canonical nested image close the complete metal-effect original while preserving its glyph companions. |
| Paint selection and text evidence | [Paint references](paint-references.json), [filter-source review](filter-source-review.json). Empty paint content and unavailable servers differ; the synthetic font needs valid bounds before its rendering can diagnose the library. Exploratory single captures do not admit new references. |

## Original acceptance mappings

The [280-leaf Gallardo map](gallardo-paint-map.csv) retains 58 native paint isolations
and the [referenced-clip record](clip-use-construction.json) maps 35 filter consumers.
The [opaque coverage record](opaque-gradient-coverage.json) rechecks both
collections. The clipped `path3850` blur and both captured-gradient consumers
(`use4125`, `use4171`) are exact;
the complete default Gallardo image matches three fresh captures per host. The
[Scimitar scope map](scimitar-scope-map.csv) preserves all 43 scopes; the
[inner-filter map](scimitar-inner-filter-map.csv) preserves 20 companions, including
the complete 35-child shadow. These and the complete Scimitar original remain exact.

The [green-grapes map](green-grapes-paint-map.csv) retains all 76 consumers at the
original fractional image viewport. Its `sourceReference` columns show all 76
isolations exact; earlier columns retain their qualified observations. The
[drawing-reference record](source-shader-reference.json) also closes the complete
default original against three fresh healthy captures on each host.

The [thin-contour record](thin-authored-contours.json) closes Ford's six default
pixel differences against three fresh captures per host. All 21 source-preserving
subset controls are exact, including the native-positive branch omitted by
selecting paints from managed coverage alone. No hosted paths are retained;
the three small regression fixtures use independently authored geometry.

Fixed stroke lengths remove three `0pt` gates; the lingerie original reaches a
separate local-reference gate. The [length record](fixed-stroke-lengths.json)
retains those outcomes. The shared [opacity-source correction](opacity-source-scope.json)
closes the complete camera against three fresh healthy captures per host and all
82 paint isolations. Shared [radial primitive binding](radial-primitive-binding.json)
also closes radialgradient1 and its rounded-rectangle isolation against fresh native
evidence. All 149 other rendered PNGs remain byte-identical after that correction.

Shared [traversal](shared-traversal.json) removes foreign-description gates from five
static originals and aligns group-declared patterns. Those originals now need explicit
font snapshots; their full acceptance remains open. Dynamic samples keep their scope
rejections, with the recorded originating context preserved. All 150 rendered PNGs
remain byte-identical. Original bodies and fragments are never retained.

Mapped crop/flood-offset/shadow, supported root CSS box paint and root/filter cases
are closed for their declared static contexts. Remaining work includes style and
attribute applicability, the supplied-font cases above, child traversal and
host-bound raster resources. Follow actual parent/traversal state
before attributing a rejection to its reported child. Keep malformed, unsupported
and font/resource-dependent cases individually classified; removing one blocker
can expose another.
