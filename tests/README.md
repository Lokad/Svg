# Tests and provenance

Use focused checks while editing, affected integration before promotion, and full
Windows/Linux/package qualification at coherent milestones. The
[release baseline](Research/release-baselines/README.md) identifies retained source,
package and reference evidence; the [support matrix](../docs/SUPPORT_MATRIX.md)
defines supported behavior.

[Native math profiles](Research/native-math-profiles/README.md) preserve exact
legacy and CORE-MATH references for the same affected cases. Independent native
functions and healthy browser triples establish each companion; a fixed BCL scalar
probe selects the host profile without inspecting renderer output. An unrecognized probe result
fails qualification. Existing sources, references and assertions stay intact.

## Responsibilities

| Layer | Purpose and representative tests |
| --- | --- |
| Mechanism | Parsing, numerical boundaries, geometry, shaping and rounding directly. Keep useful exhaustive boundaries here: [alpha](Lokad.Svg.Tests/SvgAlphaBoundaryMechanismTests.cs), [parser metadata](Lokad.Svg.Tests/SvgRootParserTests.cs), OpenType and filter-stage tests. |
| Contract | Distinct execution, allocation and ownership paths: the [96-case API matrix](Lokad.Svg.Tests/SvgConversionApiContractTests.cs), [shared admission](Lokad.Svg.Tests/OpenTypeFontTests.SharedMemory.cs), retained font/scene data, cancellation, refusal, cleanup and retry. |
| Rendering | Each source/options/platform case keeps its independent native image. The [paint bank](Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs) compares production capture pixels directly; selected complete PNG cases retain transport interactions. |
| Integrity | Manifest identities, hashes, capture context, licensing, corpus partition and provenance. Existing combined fixtures retain these assertions until their responsibilities are separated. |

Research is a separate category, independent of layer. Extended numerical-context
products remain individually selectable with `Category=Research` and run in full
qualification; former failing interactions remain routine.

Parser characterization uses a [test-owned rectangle projection](Lokad.Svg.Tests/SvgRectangleCharacterization.cs)
of root or retained-scene geometry. It preserves the assertion view's stroke bands
and clipped bounds; rendering regressions retain their independent native images.
Its [qualification](Research/release-baselines/rectangle-characterization.json)
retains all 215 callers and 1,270 affected cases while removing three test-only
members from both shipping assemblies.

[Mixed filter/mask methods](Research/release-baselines/filter-test-responsibilities.json)
use individual responsibility traits: direct bounds/admission, storage/lifetimes and
relational rendering controls remain distinct. All 65 cases and assertions remain;
layer-only selection still requires affected integration and broader qualification.
The [empty-group replay partition](Research/release-baselines/filter-replay-responsibilities.json)
keeps eight canonical sources, one routine twelve-form contract and seven extended
Research matrices. Full qualification retains all 96 original API conversions;
raw checks construct no unused prepared document. Known ownership and interaction
regressions remain separate.

[Chrome launch contracts](Lokad.Svg.VisualTests/ChromeProcessTests.cs) inspect each
invocation's profile parent, preserving unrelated siblings and detecting leaked
profiles without comparing a machine-wide temporary-directory snapshot. Missing
executables must leave even the parent absent; cancellation and healthy browser
capture remain separate checks. Their [qualification](Research/release-baselines/chrome-cleanup-isolation.json)
retains the original failure and a negative control that omits profile deletion.

Bank ownership cases can share [asset loading and lifecycle assertions](Lokad.Svg.Tests/SvgRasterOwnershipAssertions.cs)
while retaining separate names, inputs and allocation paths. Add one representative
contract for a distinct path; rendering fixtures keep their canonical conversion.
The [shared-budget contracts](Research/release-baselines/shared-budget-ownership.json)
keep one allowance across parsing and rasterization, the complete refusal sweep,
explicit viewports and exact retry pixels. Their 54 named cases use `Layer=Contract`;
the existing separate-parser/raster lifetime checks remain independent.

[Filter command-view contracts](Research/release-baselines/command-view-qualification.json)
keep source-size admission separate from canonical rendering: offset, blur and
offset/merge replay retain refusal, cancellation, cleanup and retry without
copying every source command into raster storage.

[Graph-query contracts](Research/release-baselines/filter-query-scratch.json) cover
empty, single and maximum-size graphs, allocation-free metadata and graph borrowing.
Numerical bounds/storage cases and pixel-storage refusal remain separate checks.

[Capture-storage contracts](Research/release-baselines/filter-capture-storage.json)
separate aligned-buffer reuse, wider blur requests, color-matrix output and shifted
grid copying. Four allowance/refusal/retry cases retain pre-change pixel hashes;
existing stage and browser-reference tests protect filter arithmetic and interactions.

[Transparent-scope contracts](Lokad.Svg.Tests/SvgTransparentGroupStorageTests.cs)
distinguish zero opacity, constant-zero masks and zero-opacity filters. They check
surface admission, exact sibling output and borrowed-scene refusal/cleanup/retry;
existing bounds, clip and native regressions retain their separate responsibilities.
Their [qualification](Research/release-baselines/transparent-scope-storage.json)
includes failing-before controls, both hosts/runtimes, packages and original replay.

[Opacity/clip responsibilities](Research/release-baselines/opacity-clip-responsibilities.json)
retain all 58 cases: relational rendering, direct admission and API/cleanup contracts
stay distinct. The small six-output opacity interaction matrix remains intact;
fast selection preserves every member and adds eight admission/API checks.

[Affine geometry ownership](Research/release-baselines/affine-geometry-ownership.json)
keeps original scene indices through the rewritten command envelope. Small, large
and sparse source controls protect bounded storage, parent immutability and retry;
independent affine, nested-filter and supplied-font images remain separate.

[Path-bounds reuse](Research/release-baselines/path-bounds-reuse.json) retains the
existing direct numerical and independent rendering checks. Private observations on
six authored inputs verify fewer repeated fill scans without adding tests that
mirror the implementation; separate stroke scans and exact pixels remain unchanged.

[Shared shader polygon painting](Research/release-baselines/shader-polygon-paint.json)
retains existing analytic mechanisms, material ownership and native-reference cases.
Mid-paint cancellation under a shared allowance remains distinct from pre-cancelled
calls with separate budgets. The extraction changes no tests or frozen targets.

The [mixed shader responsibility map](Research/release-baselines/shader-test-responsibilities.json)
keeps all 93 cases: 25 direct mechanisms, 28 API/ownership contracts and 40 rendering
checks. Numerical vector loops, six-output parity and late-cancellation/refusal paths
remain intact. The fast selection gains exactly 53 cases and loses none.

[Scene-admission contracts](Research/release-baselines/invocation-ownership.json)
cover raw, catalog-prepared and HTTP-prepared rejection after lowering. They retain
the source diagnostic, one preparation, borrowed owners, released charges and
successful retry through the shared gate in
[invocation/output code](../src/Lokad.Svg/SvgConverter.Invocation.cs).

The [text-paint correction](Research/release-baselines/precision-current.md) combines
direct matrix/source-box mechanisms, all 39 original images across four banks and
three independent vector controls. Each image uses one canonical conversion;
output-density and opacity companions retain distinct interactions. Separate ownership
checks admit retained drawing stages before contour allocation and verify retry.
The [full 960-input screen](Research/release-baselines/text-screen-qualification.json)
remains `Category=Research`, with generated sources and exact native RGBA digests.
The shared typed bank loader, catalog and immutable font bytes remove repeated
setup; every snapshot and its lifetime stay independent.
The [paint-box replay partition](Research/release-baselines/text-paint-replay.json)
keeps all 242 native images routine, with 63 separate prepared contracts and 179
extended replay cases. [Focused selections](Research/text-paint-bounds/README.md)
separate rendering investigation from replay and broader qualification.
The [shader-context screen](Research/shader-contexts/README.md) keeps 42 routine
stage witnesses and 12,924 individually discovered research inputs. Both original
source roles remain, even when independently captured pixels agree; numerical
mechanisms and existing ownership contracts protect their separate responsibilities.
The [filter source contexts](Research/filter-source-contexts/README.md) retain
39 routine stage witnesses and all 9,216 originals, including 1,110 naturally
unequal native pairs. Extended inputs remain individually selectable as research.
The [symbol canvas screen](Research/symbol-fractional-frames/README.md) retains
all 960 original inputs and 16 authored stage controls. Its 50 routine images use
one owned-PNG path; 926 inputs remain separate research cases. Direct mechanisms
protect canvas rounding, material-independent recording and bitmap-local clip
containment. Existing text and ownership contracts retain their distinct duties.
The [rectangle source controls](Research/rectangle-shader-frames/admission-review.json)
keep 24 original admission regressions and six independent source-role images on
the same canonical path. Six mechanisms cover cached and reconstructed strokes;
existing rectangle ownership contracts retain refusal, cleanup and retry.
The [affine source controls](Research/affine-source-retention/README.md) preserve
84 failing-before native images across rectangle fills and hidden path/point-list
fills and strokes. Canonical owned-PNG rendering protects material, visibility,
source ownership and definition order; direct mechanisms inspect retained commands
and maps. Existing point-list/stroke ownership contracts keep their distinct duties.

The [singular-map controls](Research/singular-source-maps/README.md) preserve eight
original alias inputs and orthogonal geometry, ownership, instance, image and text
routes through canonical owned-PNG conversion. Mechanisms protect finite parsing,
aggregate command limits, source roles/bounds and font liveness; two representative
contracts cover prepared font selection and shared-owner refusal/cancellation/retry.
Historical singular arc/rectangle cases now assert exact unchanged native pixels.

The [unpainted filter sources](Research/filter-empty-sources/README.md) retain
eight originals and twelve independent controls through one owned-PNG path.
Two mechanisms distinguish source/scope roles and command order; one shared-budget
contract preserves refusal, cancellation, cleanup and retry.

The [alpha and gradient layer map](Research/release-baselines/alpha-gradient-responsibilities.json)
keeps 35 numerical mechanisms independently selectable from nine storage, API and
cleanup contracts. All 44 original cases, inputs and assertions remain unchanged.
The [opacity/mask layer map](Research/release-baselines/opacity-test-layers.json)
likewise separates 99 bitmap-rounding/command mechanisms from four parser/surface
ownership contracts, retaining all 103 cases and their distinct placements.

Core output copies the 24 selected text-node contract inputs and two separate
API/ownership fixtures. The visual suite still reads the complete bank from the
repository. [Copy qualification](Research/release-baselines/text-node-copy.json)
checks fresh output, unchanged test identities, ownership coverage and measured
before/after validation; reducing copied data does not remove rendering coverage.
The [shader/group/viewport copy lists](Research/release-baselines/shader-viewport-copy.json)
likewise retain their 54 contract inputs while their complete visual banks remain
in the repository. Pattern-source selection still needs every manifest source.

The [curve/use ownership audit](Research/release-baselines/curve-ownership.md)
retains 48 admission cases and five shared-budget stroke contracts. Its seven
core asset banks copy their 31 selected inputs; complete native banks remain.
The case mapping preserves input hashes while correcting stale rejection names.

[Canonical shaping](Research/svgweb/text-canonical-shaping.json) separates Unicode,
substitution, attachment and fitting mechanisms from two representative storage
contracts. Complete Turkic/Lithuanian controls, a four-row mark control and one
translucent/shader/path control retain independent native platform references;
each uses the canonical conversion path. Strike cutoff boundaries stay in direct
[sampling tests](Lokad.Svg.Tests/SvgGlyphStrikeSamplingTests.cs).

[Text-anchor coverage](Research/text-anchor/README.md) separates cascade/compaction
and chunk-layout mechanisms, two allocation/preparation contracts, and four
canonical browser comparisons. [Windows point vectors](Research/windows-font-unit-scaling/expanded-points.json)
protect strike arithmetic directly without multiplying it across rendering contexts.
The [phantom-origin mechanisms](Research/windows-font-unit-scaling/phantom-origins.json)
add coordinate readouts and six native masks for parent-program versus child-paint
ownership. They share the ordinary font/scan setup and add no API matrix.

[Line-height coverage](Research/text-line-layout/README.md) keeps numerical and
declaration boundaries in direct mechanisms, one canonical native image for
explicit baselines/anchors/path text, and four exploratory line-layout controls.

[Clip-child roles](Research/clip-child-roles/README.md) separate parser/font-selection
mechanisms, two ownership/preparation contracts and seven canonical native cases.
Resource contribution and independent ID selection remain distinct responsibilities.

[Color matrices](Research/filter-color-matrix/README.md) and
[terminal colour coverage](Research/filter-color-program-coverage/README.md) separate
numerical/bound mechanisms, representative ownership contracts and canonical native
controls. Only each contract's input and small hash manifest are copied into core
test output. Consumer, crop and sampling cases protect distinct execution paths;
they do not repeat the API matrix.

[Text-path styles](Research/text-path-styles/README.md) keep cascade, applicability,
reference limits and emitted-font discovery in direct mechanisms. Two contracts
cover selective preparation and borrowed storage; four canonical native images
cover distinct cascade, nesting and mixed-run interactions. Literal controls remain
independent diagnostic evidence rather than an additional API matrix.

The [responsibility map](Research/release-baselines/test-responsibilities.md)
records paint-bank membership, lazy loading, raster/PNG separation and measured
validation costs, including the core pixel helpers, parser ownership helper, API fixture allocation and
the [inert-paint split](Research/release-baselines/test-responsibilities.md#inert-paint-rendering-and-prepared-contracts).
The [preformatted alignment control](Research/preformatted-text-align/README.md)
keeps keyword and discovery mechanisms separate from one canonical image;
the empty-symbol and painted-symbol cases retain distinct traversal paths.
The [shaping-boundary control](Research/text-shaping-boundaries/README.md) keeps
original anchor intervals separate from selected physical font intervals: seven
direct mechanisms, one refusal/cleanup/retry contract and one canonical image
protect source validation, genuine size boundaries and cross-node ligatures.
The [shared-range control](Research/text-shared-anchor/README.md) adds direct
middle/end coordinate mechanisms and one native image for uniform child anchors.
The [case projection bank](Research/text-case-mapping/README.md) adds direct Unicode
and source-index mechanisms, representative temporary/prepared ownership, and three
uppercase native controls. Indexed expansion uses an independent literal reference.
Lowercase adds direct mapping/property boundaries and context mechanisms, two
deferred-owner failure paths and one prepared contract. Its span image uses native
CSS; the path image keeps both the observed browser CSS result and a literal
source-context reference.
Equal images alone never establish redundancy. Preserve distinct
source graphs, allocation paths, native companions, platform differences and
exclusions. Rendering fixtures do not each need all twelve API forms.
Shared data factories can have `MemberData` consumers in other partial class
files; include those consumers when selecting affected checks.
Filter and clip integration includes `SvgSymbolInstanceReferenceTests`: its
shared bank contains crop, rescaling and nested-effect regressions.

[Deferred clip coverage](Research/clip-definition-sources/README.md) separates
ordered child/range/unit and intersection mechanisms, five selected ownership
inputs, and twelve canonical native rendering cases. Path intersections precede
AA; painted unions retain child/canvas clips and allocated construction bounds.
A newly accepted first gate does not close a complete original.

The [foreign-description mechanisms](Lokad.Svg.Tests/SvgForeignMetadataTests.cs)
cover font eligibility, SVG descendants and structural accounting. [Pattern-location
cases](Lokad.Svg.Tests/SvgPatternDefinitionLocationTests.cs) separate eligible parent
roles from existing exclusions; diagnostic cases preserve the first rejected source.
One prepared-font contract protects their acquisition interaction. Three canonical
sources retain metadata and root/group declaration graphs with independent captures;
the two pattern images share verified PNG storage. [Evidence](Research/svgweb/shared-traversal.json)
records those responsibilities without adding a fixture/API matrix.
The [descriptive-title control](Research/metadata-attributes/README.md) adds URI-aware
attribute admission and one canonical native image. It preserves actual reference
and reserved-namespace checks without multiplying the API/ownership matrix.

[Fixed geometry lengths](Research/geometry-fixed-lengths/README.md) separate unit
and matrix mechanisms, one prepared-font contract and one canonical native image.
Resolved ranges, percentage bases and rotation-centre stages keep distinct checks;
the fractional-pattern interaction retains its independently frozen pixels.

[Root canvas controls](Research/root-css-box/README.md) separate canvas paint from
element opacity/visibility and root generation from computed display inheritance.
Mechanisms, two focused ownership/preparation contracts and five native inputs
retain those boundaries without repeating the API matrix.

[Image density controls](Research/root-image-density/README.md) retain five native
contexts, separating the integer image viewport from fractional edge coverage.
[Bitmap span controls](Research/image-fit-sampling/README.md) separate 85 direct
native dispatch boundaries from three canonical image conversions. Intrinsic,
fitted and wider tiled images protect distinct interactions; existing image
ownership contracts cover refusal, cancellation and retry independently.
[Root padding](Research/root-padding/README.md) adds direct layout/cascade mechanisms,
two focused contracts and ten native contexts. [Canvas edge controls](Research/root-canvas-density/README.md)
separate geometry coverage from image-boundary coverage and the host backdrop.
[Border controls](Research/root-box-layout/README.md) retain fourteen frozen layout
contexts; four [edge composition controls](Research/root-border/README.md) cover
mixed alpha, partial borders, transparent miters and current-color priority. Direct
mechanisms and two ownership paths protect parsing, used widths, scope placement,
refusal, refunds and retry. Padding and borders share prepared-font contract setup.

[Rendered use sources](Research/rendered-use-sources/README.md) separate source
identity/placement, two ownership paths, one prepared-text contract and five
canonical native images. Named use chains, empty targets and graph failures remain
individually selectable. The clip, title and rendered-use banks share one small
[canonical assertion](Lokad.Svg.VisualTests/SvgNativeControlAssertions.cs), retaining
source/reference hashes, three native receipts and exact pixel comparisons.

[Nested filter placement](Research/nested-filter-placement/README.md) has six direct
map mechanisms and seven canonical native cases. Two shader mechanisms protect
recording/capture order and 64/65-stage storage. One recursive-capture contract
covers the additional allocation path.
The [filter source-opacity bank](Research/filter-source-opacity/README.md)
separates thin curves, stroke layout, source recording and opacity restoration
through canonical conversion. Direct bounds and drawing-map mechanisms protect
their numerical contracts. One [opacity-parent regression](Research/svgweb/filter-opacity-source-origin.json)
protects filter-source creation inside the bitmap that will receive its result.
The wide control and complete hosted original remain
open; equal smaller images do not close those interactions.
The [mask source-clip control](Research/mask-source-clip/README.md) protects a
hairline crossing an opaque bitmap intersection. Direct mechanisms cover live
clip borrowing, partial coverage and cancellation; broader mask references retain
the polygon and composition interactions.
The [nested-pattern control](Research/nested-patterns/README.md) covers forward
aliases, shared tiles, rectangle/polygon sampling and supplied-font direct/path
text through canonical conversion, preserving distinct glyph platform references.
Direct graph mechanisms protect cycles and nesting boundaries; prepared-font and
refusal/retry checks cover ownership separately.
The [repeat-boundary control](Research/pattern-repeat/README.md) closes that
original's remaining pixel discrepancy. Fifteen direct native-vector mechanisms
protect tap addresses and floating boundaries; one canonical image protects nested
sampling. Existing glyph-pattern references preserve the fragment-drawing interaction
without adding destination or ownership matrices.
The reduced [group-opacity pair](Research/filter-group-opacity/README.md) uses two
overlapping rectangles: both canonical images are exact, and the downscale control
rejects a global alpha change. Four compiled native device-axis mechanisms protect
the distinct restore consumers without repeating the API/ownership matrix.

The [thin-curve mechanisms](Lokad.Svg.Tests/SvgThinAuthoredFillTests.cs) distinguish
authored ink from short flattened fallbacks and explicit suppression. One source
preparation contract and one added clip ownership case cover refusal, cancellation,
cleanup and retry. Three canonical native fixtures separate plain fills, shader
consumers and clip staging; [their evidence](Research/svgweb/thin-authored-contours.json)
records failing-before results and independent platform captures.
The [fixed stroke-length mechanisms](Lokad.Svg.Tests/SvgFixedStrokeLengthTests.cs)
pin unit conversion, sign/range admission and zero-width selection directly.
Existing font-discovery and inherited-paint cases each add one physical-zero
interaction. One [canonical native fixture](Fixtures/original/fixed-stroke-lengths.svg)
protects inherited width, mixed dash units and signed offsets; the
[evidence](Research/svgweb/fixed-stroke-lengths.json) retains the original
observations separately from successful length parsing.
The [dash-culling mechanisms](Lokad.Svg.Tests/SvgDashCullingTests.cs) cover axis
eligibility, phase, source calibration, empty contours and bitmap-origin rounding.
Three [ownership contracts](Lokad.Svg.Tests/SvgDashSourceOwnershipTests.cs) protect
thin/outline refusal, cleanup, retry, copies and transfer. Seven canonical
[native controls](Research/dash-image-width/README.md) cover width-dependent
construction and distinct mapped, non-scaling and shader/clip interactions.
The [recorded-enclosure mechanisms](Lokad.Svg.Tests/SvgRecordedSourceEnclosureTests.cs)
keep local drawing bounds separate from fractional filter visuals. Four
[scope cases](Lokad.Svg.Tests/SvgRasterScopeTests.cs) and two retained-origin
hairline cases protect construction clips and owned interiors directly. Three
canonical opacity fixtures retain shader strokes, solid hairlines and separate
parent-group paints. The latter graph keeps its own source and capture metadata
while sharing verified PNG storage. Existing ownership checks cover cancellation,
refusal, cleanup and retry; [evidence](Research/svgweb/opacity-source-scope.json)
maps these responsibilities and failing-before results.
The [analytic byte-alpha mechanism](Lokad.Svg.Tests/SvgAnalyticAlphaByteTests.cs)
walks a cubic directly; its [native fixture](Fixtures/original/analytic-alpha-byte.svg)
adds the specific blur/opacity interaction through canonical conversion. Existing
analytic ownership tests retain scratch admission, growth, cancellation and cleanup.
The [bilinear mechanisms](Lokad.Svg.Tests/SvgFilterBilinearSamplingTests.cs) keep
float weight boundaries in 16 direct cases, sharing independently captured native
vectors across filter and image-view sampling. View clamping and capture integrity
have separate checks; no rendering fixture or API matrix is multiplied.
The [clip placement fixture](Fixtures/original/clip-local-placement.svg) protects
child/reference/device ordering. Referenced quadratic, cubic and arc mechanisms
assert exact registered controls; one placed arc joins existing ownership coverage.
The [canvas mechanism](Lokad.Svg.Tests/SvgClipCanvasTests.cs) separates clip-resource
and referencing maps, including object-box normalization. Its
[native fixture](Fixtures/original/clip-canvas-placement.svg) protects a rounded
parent before child concatenation; existing scaled radial, inherited clip and
rotated dome references protect the contexts where collapsing those roles fails.
The [clipped filter-source fixture](Fixtures/original/filter-source-clip-padding.svg)
protects padded clip construction independently of the source request rectangle.
The [raster-window fixtures](Research/svgweb/filter-raster-window.json) separately
protect numerical curve coverage beyond tight extrema and clip classification
inside a filtered source. Both use canonical conversion; one existing ownership
case covers the stronger source without repeating the API matrix.
The [clip visual-bounds mechanisms](Lokad.Svg.Tests/SvgClipVisualBoundsTests.cs)
separate silhouette coverage from inherited stroke enclosures. Direct and referenced
native fixtures retain distinct source-lowering paths; their identical PNG storage
is shared. One ownership case covers the previously unconstructed referenced source.
The [nested-picture fixture](Fixtures/original/filter-nested-picture-bounds.svg)
protects a retained child blur and a distant gradient's dither origin through one
canonical conversion. Original paint bounds and filtered output remain distinct.
The [source-request fixture](Fixtures/original/filter-source-request-window.svg)
protects a blurred curve crossing the output boundary through canonical conversion;
integer/fractional and path/ellipse companions remain focused research controls.
The [bitmap clip request fixture](Fixtures/original/filter-bitmap-clip-request.svg)
protects a stepped clip narrowing one compositor bitmap before source capture.
It uses canonical conversion and independent native platform triples; existing
clip-bound mechanisms and filter ownership cases retain their own responsibilities.
The [parameter enclosure fixture](Fixtures/original/filter-parameter-source-enclosure.svg)
isolates source-device placement and gradient dither in sRGB. Direct native hint
mechanisms protect enclosure before expansion without multiplying rendering contexts.
The [source-coordinate fixture](Fixtures/original/filter-source-coordinate-origin.svg)
protects the source bitmap's coordinate seed under a tighter content clip and
linearRGB blur. Two direct gradient checks retain the independently captured source
byte under content and raster clips; the fixture uses canonical conversion.
The [image-edge fixture](Fixtures/original/filter-image-edge-construction.svg)
protects bitmap clipping during affine image restoration, with linearRGB blur and
enclosing opacity. Existing edge/clip mechanisms retain boundary coverage; the
fixture follows one canonical conversion path.
The [clamped-sampling fixture](Fixtures/original/filter-clamped-bilinear-accumulation.svg)
protects product rounding before color transfer and storage. Two compiled native
bit vectors distinguish clamped and generic wrapped/picture kernels directly; the existing weight,
clamping and stride tests remain separate. No API matrix is added.
The [linear placement mechanisms](Lokad.Svg.Tests/SvgLinearPaintPlacementTests.cs)
keep declaration, local and reference matrices separate across fills, strokes and
instances. Three ownership cases protect map admission, refusal, cancellation,
cleanup, retry and identity reuse. Independent rectangle/path, fractional-use and
local-matrix fixtures protect the distinct numerical routes through canonical
conversion; [their evidence](Research/svgweb/linear-source-placement.json) records
both hosts' captures and failing-before results.

The [radial binding mechanisms](Lokad.Svg.Tests/SvgRadialSourceBindingTests.cs) keep
factory coordinates independent of local/reference placement across primitives and
paths. Existing instance, viewport and focal-circle cases assert those exact stages.
One [canonical reflected-radial fixture](Fixtures/original/radial-primitive-definition.svg)
protects the rounding interaction; [evidence](Research/svgweb/radial-primitive-binding.json)
retains before failures and original-to-control mappings. No API matrix is added.
The [recorded shader mechanisms](Lokad.Svg.Tests/SvgRecordedShaderCoordinatesTests.cs)
protect bitmap-origin changes, initial placement and the stack/heap mapping boundary.
One ownership case covers deep-chain refusal, cancellation, cleanup and explicit
retry without publishing a stale coordinate cache. Separate linear and radial
clipped fixtures protect rounding through canonical conversion; their independent
captures and original mappings are in the
[stage evidence](Research/svgweb/gradient-recorded-bitmap-origin.json).
The [opaque coverage mechanisms](Lokad.Svg.Tests/SvgOpaqueGradientCoverageTests.cs)
use six compiled native vectors and factory/opacity eligibility checks. One canonical
colored-edge fixture protects rendering; [its evidence](Research/svgweb/opaque-gradient-coverage.json)
also maps full Gallardo acceptance and preserves direct-span/A8 distinctions.
The [direct-strip mechanisms](Lokad.Svg.Tests/SvgDirectPaintSpillTests.cs) separate
completed direct events from path-bounded mask rows and clip construction. One
growth-refusal case checks cleanup and explicit retry; existing cancellation cases
remain separate. Circle, authored-arc and rounded-rectangle fixtures retain distinct
source builders through canonical conversion. Their
[edge evidence](Research/svgweb/completed-strip-coverage.json) preserves all six
fresh native captures per fixture and the existing clip-resource regression.
The [affine-root control](Fixtures/original/filter-affine-root-boundary.svg) and
its [paired gap control](Fixtures/original/filter-affine-root-gap.svg) retain
distinct backdrop/capture interactions through canonical conversion. Direct
root-matrix and capture-enclosure mechanisms protect their numerical stages;
the [stage record](Research/svgweb/root-image-projection.json) preserves native
coefficients, large viewBox interactions and runtime/package qualification.
The [root-map mechanisms](Lokad.Svg.Tests/SvgRootImageMapTests.cs) also pin visible
source remapping before residual scaling, including the rounding boundary and
opposed axes. One [transformed ellipse](Fixtures/original/root-visible-source-rectangle.svg)
protects rendering through canonical conversion; its metadata retains fractional
image dimensions separately from the integer screenshot. Existing root ownership
cases cover the unchanged allocation paths. See the
[source-rectangle record](Research/svgweb/root-visible-source.json).

The [source-capture fixture](Fixtures/original/filter-retained-source-stages.svg)
protects retained visual bounds and recording/destination stages through a zero-blur
gradient capture. Three direct bounds cases and one bitmap-map case isolate the
mechanisms; existing filter ownership tests cover allocation and cleanup. Its
[evidence](Research/svgweb/filter-retained-source-stages.json) records the memory
tradeoff and retained bounds/map contracts. No API matrix is added.
The [nonzero-blur companion](Fixtures/original/filter-source-shader-reference.svg)
protects complete gradient drawing references during capture. Existing direct map
checks distinguish early device narrowing; [closure evidence](Research/svgweb/source-shader-reference.json)
maps the shared fix to native controls and the complete green-grapes original.

Migrated cases use consistent `Layer=Mechanism`, `Layer=Contract`,
`Layer=Rendering` and `Layer=Integrity` traits. Classification is incomplete;
a layer filter does not replace affected unclassified tests or full qualification.
For direct mask-bound vectors, use `FullyQualifiedName~SvgMaskPaintRegionTests&Layer=Mechanism`;
include its contract siblings for storage refusal, cancellation and replay.
The [supplied-font output map](Research/release-baselines/font-output-responsibilities.md)
keeps all 37 native rendering inputs, seven routine API matrices and 30 extended
matrices, with separate layout, acquisition and ownership regressions.
The [original shader hairlines](Research/shader-hairlines/README.md) retain 2,000
canonical native checks as optional research, separate from routine alpha/coverage,
ownership and selected rendering witnesses.
For pattern selection, `FullyQualifiedName~Pattern_child_content_selects_the_discovered_font`
selects direct discovery; include the raster and prepared siblings for integration.
`FullyQualifiedName~SvgPatternSelectionMechanismTests` selects the original resource
acceptance inputs without loading fonts. The [acceptance map](Research/release-baselines/resource-selection.md)
keeps their native, acquisition and explicit-exclusion responsibilities separate.
The [case map](Research/release-baselines/test-responsibilities.md#pattern-source-and-target-selection)
explains the routine prepared subset and extended `Category=Research` companions.
Use `FullyQualifiedName~OpenTypeFontTests&Layer=Mechanism&Category!=Research` for direct font work;
include contract/rendering siblings when changing shared font preparation or fixtures.
`FullyQualifiedName~LatinLightHintingTests` selects direct light-fitting topology,
alignment, rounding and scratch-lifetime checks. The
[alignment-zone cases](Research/svgweb/latin-blue-zone-fitting.json) isolate linked
anchors and shared edges; supplied-font rendering keeps its canonical path and
independent platform references.
`FullyQualifiedName~Windows_scan_` selects fitted/scan geometry, four native phase
vectors and preparation cleanup/retry. `FullyQualifiedName~Conditional_shift`
selects direct native function-recognition and ordinary-shift controls.
`FullyQualifiedName~SvgWindowsScanBoundaryTests` selects canonical
[turning-counter](Research/windows-scan-boundary/README.md) and
[conditional-function](Research/windows-conditional-shift/README.md) images, plus
the [stack-vector program](Research/windows-stack-vectors/README.md) and
[movement transitions](Research/windows-movement/README.md).
`FullyQualifiedName~stack_projection` selects Windows word-preservation and classic
normalization responsibilities, including the actual following movement.
`FullyQualifiedName~Windows_movement` selects eight direct compensation/copy
mechanisms and the canonical original-font rendering companion in their projects.
Existing half-unit fitted-point and complete native-texture regressions remain separate.

## Development checks

The pinned .NET 11 RC SDK is the default. Install both .NET 11 and .NET 10 runtimes;
no check relies on implicit major-version roll-forward. After a Release build:

```powershell
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgRootParserTests
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgAlphaBoundaryMechanismTests
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgInertPaintTests
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter '(Layer=Mechanism|Layer=Contract)&Category!=Research'
dotnet test tests/Lokad.Svg.VisualTests -c Release --no-build --filter 'FullyQualifiedName~SvgPaintInputReferenceTests&Layer=Rendering&Category!=Research'
```

The parser metadata selection excludes [rendering](Lokad.Svg.Tests/SvgRootRenderingTests.cs)
and [ownership](Lokad.Svg.Tests/SvgRootParserOwnershipTests.cs); include both for parser
integration. Start with the affected mechanism/class and follow with its rendering
bank and ownership paths. `Category!=Research` excludes only explicitly selected extended
matrices; other browser-backed research references remain routine.
For affected paint integration, omit `Layer=Rendering` to include the separate
font-capture recipe checks. Reference images share reads/decoding within the
fixture; every case still validates its source and declared reference hash.

Capture pixels are tightly packed sRGB straight-alpha RGBA8 after the production
unpremultiply. The comparer keeps its existing zero-alpha RGB convention;
[format/owner contracts](Lokad.Svg.VisualTests/RasterCaptureContractTests.cs)
check canonical PNG equivalence and retain the borrowed surface through use.
[PNG contracts](Lokad.Svg.Tests/PngEncoderTests.cs) retain exact bytes/chunks,
size boundaries, cancellation, backpressure and failed writes. Across runtimes,
compare decoded pixels; within a runtime, preserve claimed byte equivalence
between API forms. Do not recapture references for implementation changes.

## Corpus and offline execution

Visual tests read the committed `Research/` corpus in place instead of copying it
into each framework's output. `LOKAD_SVG_RESEARCH_ROOT` can select another corpus
containing `manifest.json`; invalid explicit roots fail. Tests only read it.
Small core `TestData` subsets and explicit fixture/reference links remain separate.

Use the [visual-suite export](../tools/README.md#export-the-visual-suite) for a
standalone offline directory containing binaries, references, font licenses and
the managed font host. Ordinary checks need no downloads, live browser, private
configuration or `external/` checkout.

Browser-tool qualification is separate from renderer acceptance. Its smoke keeps
fresh-process image/Blob captures, a viewport-versus-screenshot control and cleanup
contracts. The [SVGWeb capture record](Research/svgweb/capture-model.json) retains
individual platform contexts; ordinary tests do not fetch the hosted sample collection.
Root-image checks separate [layout mechanisms](Lokad.Svg.Tests/SvgRootImageMapTests.cs),
[representative ownership](Lokad.Svg.Tests/SvgRootImageBoundaryOwnershipTests.cs) and
[independent rendering](Lokad.Svg.VisualTests/SvgRootImageBoundaryReferenceTests.cs).
Fractional-root fixtures retain their original browser targets, including the
painted viewBox counterexample.
The [gradient-length record](Research/svgweb/gradient-lengths.json) separates direct
unit/rounding/ownership mechanisms from five canonical native rendering fixtures.

| Source of truth | Responsibility |
| --- | --- |
| [Fixture manifest](Fixtures/manifest.json) | Original CC0 inputs, independent support/fidelity statuses and oracle identities. |
| [Research index](Research/README.md) and [manifest](Research/manifest.json) | Feature collections, expected failures and measured differences; a reference alone does not imply support. |
| [Upstream documents](Research/upstream-static-documents/README.md) | Useful complete documents selected by feature, supplied fonts and separate platform references. |
| [Reference provenance](References/README.md) | Independent captures and retained export evidence with current consumers. |
| [Text fragments](Research/text-node-boundaries/README.md), [instance composition](Research/text-instance-composition/README.md) | Node/shaping/fitting witnesses and explicit R74/C04 nested-viewport exclusions. |
| [Public fonts](Research/public-font-qualification/README.md), [font-host tests](Lokad.Svg.FontProbe.Tests/) | Regular/bold/real-italic/fallback acquisition, independent metrics and actual host execution. Private deployment acceptance remains separate. |
| [Admission/cancellation map](Research/release-baselines/parser-validation.md) | Structural/numerical checks and real ownership contracts retained after fuel/private-cap removal. |

Collection manifests/READMEs own case identities, options, exclusions and licenses.
The [internal-entity checks](Research/xml-entities/README.md) separate XML and
ownership mechanisms, one prepared-font interaction and three canonical native
cases; numeric-ampersand browser differences remain explicit research evidence.
The [conic-center checks](Research/glyph-conic-centers/README.md) keep 21 direct
numerical cases separate from two canonical supplied-font rendering cases. The
font producer and complete native numerical sheet remain optional research tools.
The [mask text checks](Research/mask-text-recording/README.md) retain seven canonical
native cases for resource versus consumer font construction and one separate
scope/memory contract. The [text-clip checks](Research/text-clip/README.md) reuse
twelve of those independent references and add seven controls for inherited
typography, decorations, invisible layout bounds, instance opacity and group
isolation. They use one canonical conversion path; font selection and one
prepared ownership/refusal/cancellation/retry case remain separate contracts.
Historical document totals, percentage gates and exploratory quotas are not
completion criteria. Retained cases keep their own contracts regardless of the
collection name.

## Fixture admission

1. Record a stable ID, source/revision, creator, license/rights evidence, dimensions,
   feature purpose, viewport/scale/background and required fonts before screening
   renderer output. Preserve original bytes and notices.
2. Hash actual files. The original fixture catalog's `originalSha256` precedes the
   CC0 header; `materializedSha256` identifies rendered bytes. Declare capture-only
   root sizing/font bindings separately from the original drawing and font request.
3. Follow the [capture rules](../tools/README.md#capture-and-reference-rules), keeping
   source/resource/reference hashes, platform pin, recipe and capture options.
4. Require a reviewed reference and [exact comparison](../tools/README.md#pixel-comparison)
   before declaring fidelity exact. Unsupported cases retain their reason/provenance.

[Fixture audits](Lokad.Svg.VisualTests/FixtureManifestTests.cs) and collection tests
enforce admission. Preserve failing-before evidence; do not rewrite paths/text,
widen tolerances or replace references to accommodate the candidate. Changed
capture contracts need separate qualification while retaining old provenance.

## Milestone and release qualification

```powershell
dotnet restore --locked-mode
dotnet build Lokad.Svg.slnx -c Release --no-restore
dotnet test Lokad.Svg.slnx -c Release --no-build
```

The [CI workflow](../.github/workflows/ci.yml) runs the full unfiltered .NET 11 suite
on Windows/Linux, representative .NET 10 API/ownership/numerical/native checks,
resource/font-host tools and isolated installed-package consumers for both TFMs.
The .NET 10 development selection excludes `Category=Research`; full runs include it.
For a focused .NET 10 run, specify `--framework net10.0` and build that target.
At release boundaries or after relevant runtime/numerical changes, also run:

```powershell
dotnet build Lokad.Svg.slnx -c Release --framework net10.0 --no-restore
dotnet test Lokad.Svg.slnx -c Release --framework net10.0 --no-build
```

Package checks inspect DLL/XML/PDB assets, restore one isolated consumer and execute
each target on its installed runtime. Allocation gates retain the minimum of three fixed
warmed batches, with semantic/ownership checks outside measured intervals; changing
suite layout does not relax their thresholds. Frozen Windows/Linux references establish declared outputs,
without implying general browser completeness. Benchmark timing belongs on the
separate quiet VM; test/setup measurements are validation costs.
