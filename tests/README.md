# Tests and provenance

The [release baseline index](Research/release-baselines/README.md) identifies the
published source, package, reference trees and inherited qualification.

Run focused checks while editing, affected integration before promotion, and the
full offline suite at a coherent milestone. These responsibilities complement
each other:

| Layer | Purpose | Examples |
| --- | --- | --- |
| Mechanisms | Exercise parsing, numerical boundaries, geometry, shaping and rounding directly. Keep useful exhaustive boundaries here. | [Alpha boundaries](Lokad.Svg.Tests/SvgAlphaBoundaryMechanismTests.cs), parser and OpenType tests. |
| API and ownership | Cover distinct execution/allocation paths, destinations, prepared documents, cancellation, refusal, cleanup and retry with representative inputs. | [API forms](Lokad.Svg.Tests/SvgConversionApiContractTests.cs), [shared admission](Lokad.Svg.Tests/OpenTypeFontTests.SharedMemory.cs), [text replay/ownership](Lokad.Svg.Tests/SvgTextNodeMetricsTests.cs), public-contract and destination-overlap tests. |
| Rendering | Compare each input with its own independent browser reference through canonical owned synchronous conversion. Keep platform differences and specific replay interactions. | [Native paint inputs](Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs), fixture and research-reference tests. |

The pinned .NET 11 RC SDK is the development default. Install both .NET 11 and
.NET 10 runtimes; checks never require implicit major-version roll-forward.
For example, after a Release build, an alpha change can start with:

```powershell
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgAlphaBoundaryMechanismTests
```

Follow it with the affected rendering bank and contract classes. For a broader
development check excluding the extended color/filter matrix:

```powershell
dotnet test -c Release --no-build --filter 'Category!=Research'
```

Only the explicitly selected extended matrix has this category; it does not
exclude the other native research-reference tests. Migrated classes use consistent
`Layer=Mechanism`, `Layer=Contract`, `Layer=Rendering` and `Layer=Integrity` traits.
For their focused mechanism/contract check:

```powershell
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter 'Layer=Mechanism|Layer=Contract'
```

Layer classification is still incomplete; this selection does not replace affected
unclassified tests or full qualification.
The [responsibility map](Research/release-baselines/test-responsibilities.md)
records the paint corpus's routine/research partition and lazy bank setup. Its
41,334 rendering cases retain their original identities, references and assertions;
case discovery no longer retains all rendering metadata. The measured setup
allocation fell from 177 MB to 100 MB; routine validation remained about 81 s.
The [point-list source maps](Research/point-list-source-maps/README.md) separate
90 independent native inputs, twelve viewport mechanisms and two selected ownership
cases; their numerical boundaries do not repeat the full API matrix.
The [preparation contracts](Lokad.Svg.Tests/OpenTypeFontTests.PreparationSharing.cs)
cover request-only and scalar-coverage acquisition with one stylesheet collection,
exact raw/prepared output, and shared-budget refusal, cleanup and reuse.
[Coverage ownership](Lokad.Svg.Tests/SvgFontCoverageDiscoveryTests.cs) keeps copied
scalar requirements charged through acquisition rather than refunding them when
the temporary collection finishes.
The [HTTP preparation contracts](Lokad.Svg.Tests/OpenTypeFontTests.HttpPrepared.cs)
cover failed initialization, independent font-free calls and result ownership.
Four [public font cases](Lokad.Svg.VisualTests/SvgPublicFontQualificationTests.cs)
compare selective HTTP acquisition with the explicit provider and unchanged native
references for regular, bold, real italic and fallback faces on both runtimes.
The [filter-region controls](Research/filter-region-coordinates/README.md) retain
65 independent native inputs, 27 direct mechanisms and one selected ownership
witness. Their source/image coordinate interactions retain the existing tests;
the rendering bank uses one canonical conversion path.
The [marker canvas controls](Research/marker-canvas-stages/README.md) retain 37
independent native inputs, five mechanisms and two selected opacity/shader
ownership paths. Existing culling and inverse-stroke resource regressions remain
independently asserted.
The [ordered clip controls](Research/ordered-clip-coverage/README.md) retain 91
unique input/options records covering 104 original and interaction roles, nine
direct mechanisms and five selected ownership/replay cases. Identical source
aliases share records; unequal graphs keep independent native references.
The [text/instance controls](Research/text-instance-composition/README.md) retain
154 source/options records, seventeen mechanisms and explicit scalar-mask
metadata contracts. The 768 symbol/viewport rendering pairs remain canonical;
six selected prepared-replay cases cover font, stroke and viewport interactions.
Existing 42/43-child structural-admission checks and the general API matrix remain separate.
The [fractional stroke controls](Research/fractional-text-strokes/README.md) retain
45 independent source/options for 51 roles, all exact on both hosts. Twenty-three
direct cases cover alignment and rounding; one dimension contract separates the
sample plane from its guards. Prepared replay and general ownership stay in their
representative contracts.
The [clipped stroke controls](Research/clipped-stroke-preparation/README.md)
retain 28 native inputs, fourteen direct mechanism cases and two selected
opacity/viewport ownership paths. Signed coordinate boundaries are tested
directly; each original path graph keeps its own canonical rendering regression.
The [embedded-image source views](Research/image-source-views/README.md) retain
120 canonical images for 126 original/acceptance roles, 39 direct mechanism cases
and three selected allocation paths with raw/prepared ownership checks. They
separate source crops, decoded-view reduction and picture mip interpolation from
destination coverage and from the general API matrix.
The [filter opacity and sampling controls](Research/filter-opacity-sampling/README.md)
retain canonical native images and two distinct allocation/ownership witnesses.
The [uniform-bank status](Research/filter-uniform-precision/resolved-known-differences.json)
adds exact assertions for historical differences without replacing captures:
two scaled shadows stay routine and the other 1,204 join the research matrix.
The [source/composition controls](Research/filter-merge-image-draw/README.md)
add 45 routine native inputs, direct image-bound admission checks and two selected
ownership inputs. The [historical corpus](Research/filter-source-composition/README.md)
keeps 2,253 individual checks in the research category. Its coverage index maps
every original role, consolidating only identical authored inputs/options with
matching native pixels on both hosts; references remain unchanged.
The [recording-coordinate controls](Research/filter-source-recording-map/README.md)
add three ellipse inputs and reuse the existing linear neutral reference. They
protect the shared source/layer map while the transformed-curve controls retain
their own native images.
The existing clipped-gradient control in the affine source sheet separately
protects clip-map ownership; include `SvgFontLayoutDiagramTests` when changing
source recording coordinates.
The full milestone command includes every extended case:

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

Routine .NET 10 compatibility uses API/ownership, HTTP fonts, numerical boundaries
and representative native rendering rather than repeating every research matrix.
The exact routine selections are in the [CI workflow](../.github/workflows/ci.yml).
For a targeted compatibility check, build the selected framework too:

```powershell
dotnet test tests/Lokad.Svg.Tests -c Release --framework net10.0 --no-restore --filter 'FullyQualifiedName~SvgConversionApiContractTests|FullyQualifiedName~OpenTypeFontTests'
```

At the release boundary, or after runtime/numerical changes, run the full suite on
both Windows and Linux for each framework. After the default .NET 11 run above:

```powershell
dotnet build Lokad.Svg.slnx -c Release --framework net10.0 --no-restore
dotnet test Lokad.Svg.slnx -c Release --framework net10.0 --no-build
```

Preserve decoded native pixels across runtimes and encoded output equivalence
between API forms within each runtime. Do not recapture references to accommodate
runtime or implementation differences. The package check inspects both DLL/XML/PDB
assets and restores one isolated consumer with both targets, executing each on its
own installed runtime.

Use `--filter FullyQualifiedName~TestClassName` on a test project for focused
checks. The [CI workflow](../.github/workflows/ci.yml) runs the full unfiltered .NET 11
Windows/Linux suite, routine .NET 10 compatibility,
browser/resource/font-host smoke checks and package/installed-consumer checks.

[Hairline clip controls](Research/hairline-clip-rounding/README.md) retain short
rotated rectangle/path inputs through containing and intersecting clips. Direct
coverage checks guard binary/AA mask dispatch; each rendering uses its own native
image through the canonical conversion path.

The API matrix uses eight inputs across twelve raw/prepared destination forms.
Its shared-layout fixture adds the common glyph-owner allocation path once.
[PNG contracts](Lokad.Svg.Tests/PngEncoderTests.cs) retain exact output/chunk and
encoded-size boundaries, cancellation, backpressure and write failures. Shared-root
stream-growth refusal cases protect cleanup without a second admission during
compressor disposal; removed PNG fuel counters are not test contracts.
The [fuel-removal validation map](Research/release-baselines/parser-validation.md)
identifies the retained numerical, structural, cancellation and allocation paths
after removing operation fuel. Actual allocation-growth checks do not establish CPU
scaling; performance measurements belong in the benchmark harness.
Dedicated tests retain unusual stream failures, overlap, borrowed storage,
resource refusal, cancellation and disposal paths. Rendering fixtures do not
each need the whole matrix. The text-node corpus still has all 594 native image
cases; 24 replay cases and 14 lifetime cases protect reader growth, shaping,
rotation, fitting, gradients, scaling and use/symbol/pattern/mask owners.
The [text filter source controls](Research/text-paint-bounds/README.md) add 135
canonical image cases and 17 numeric owner-bound vectors. Their copied/borrowed
storage, cleanup and prepared replay contracts run separately from the images.
The [filter source coverage controls](Research/filter-source-coverage/README.md)
separate 20 native deviation values, 90 coverage vectors, bounds mechanisms and
two ownership contracts from 59 canonical browser-backed images per platform.
Their original Canvas captures are preserved and independently checked against
recipe-3 page captures, with explicit root-size materializations for scaled inputs.
The [raster device controls](Research/raster-capture-profiles/README.md) add ten
canonical images and two wide-pattern ownership contracts. They distinguish
pattern image devices from outer compositor tiles and retain naturally unequal
Canvas/page profiles.

The [filter clip ownership controls](Research/filter-clip-ownership/README.md)
keep fourteen canonical source/output clip images separate from sibling-work,
empty-intersection and ownership checks. Distinct lowering paths retain their
own references, including source-only clipping and pattern/retained replay.

The [filter restoration controls](Research/filter-restoration/README.md) add thirty
canonical images for operating color spaces, opacity, merge/offset aliases, pattern
destinations and backdrop preservation. Eleven positive controls and three naturally
unequal native color-space pairs protect the distinctions between those paths.

The [affine restoration controls](Research/filter-affine-restoration/README.md)
add 35 canonical images for per-bitmap source requests, nonzero crop bounds, logical
image extents, sprite ties and fractional graphs. Their naturally unequal crop and
color-space references remain separate. [Direct sprite mechanisms](Lokad.Svg.Tests/SvgFilterSpriteMechanismTests.cs)
cover adjacent floats and negative ties; [two representative ownership inputs](Lokad.Svg.Tests/SvgFilterBitmapOwnershipTests.cs)
cover clip-prepass and bitmap-graph storage, refusal, cancellation, retry and prepared
replay without multiplying every image across the API matrix.

The [shadow stage controls](Research/filter-shadow-stages/README.md) keep sixty
canonical browser-backed images separate from thirteen direct tint vectors.
They preserve ten failing-before cases and fifty positive controls, including
naturally unequal expanded graphs. One additional ownership case covers the
integer offset with a retained color filter; fractional blur ownership remains
covered by its existing case.

The [retained alpha controls](Research/filter-alpha-stages/README.md) add fifteen
canonical images, seventeen direct alias/materialization checks, and one selected
pattern input for ownership and prepared replay. Source-alpha stage metadata
survives image-free aliases; materialized images keep their separate native path.

The [image placement controls](Research/filter-image-placement/README.md) add twelve
canonical images for parameter-space source hints and actual image-clip context.
Eleven direct graph/bounds cases cover authored numerical fringes. One selected
near-integer input checks scratch ownership and prepared replay; the distinct
binary/antialiased parent-clip images retain their native two-pixel difference.

The [stage boundary controls](Research/filter-stage-boundaries/README.md) retain
seventeen canonical images, including fifteen failing-before inputs, separately
from 24 direct request/materialization/sampling mechanisms. One selected copy-blur
input checks metadata and resolved-image ownership, cancellation and prepared replay.
Image-free aliases and materializing stages preserve their distinct responsibilities;
these fixtures do not multiply numerical boundaries across all output APIs.

The [stored image controls](Research/filter-stored-images/README.md) add nineteen
canonical regressions for retained terminal offsets, offset/merge image extents
and source-alpha materialization. Seventeen direct cases cover bounds and numerical
store boundaries; one merged input checks ownership and prepared replay.

Color/filter numerical-context cross products remain available with
`--filter Category=Research`. Their [gradient](Research/gradient-color-alpha/research-cases.json)
and [filter](Research/filter-uniform-precision/research-cases.json) selections
move 3,812 repeated combinations out of the development loop, preserving all
sources, native references and individual assertions. Selection factors alpha
regime/grammar within the same XML structure, placement, output scale and bank;
it never uses equal images as evidence of redundancy. Gradient midpoint,
syntax and tiny-canvas banks remain routine. The direct alpha vectors retain
1,278 token pairs and native source/reference hashes. New captures stay routine
unless explicitly reviewed into the research lists; preserve a failing-before
interaction in routine coverage. Reference regeneration is not part of this split.

One sequential local Windows Release measurement (including test-host startup
and TRX output, excluding build) gave:

| Check | Before | After |
| --- | ---: | ---: |
| Text-node replay/lifetime | 19.2 s | 7.5 s |
| Full native paint corpus | 273.7 s | 283.3 s |
| Development native paint check | 273.7 s (formerly full) | 71.3 s (routine) |

These are edit-loop measurements, not performance guarantees or a claim that
full qualification became faster. That measurement covered 37,372 native
cases plus a corpus-partition check; the current partition is in the
[responsibility map](Research/release-baselines/test-responsibilities.md). The API matrix and direct numerical checks
add separate responsibilities; CI continues to run every research case.

[Paint input tests](Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs)
use [native controls](Research/offset-merge-inputs/README.md) for stored image
rounding, `SourceAlpha`, merge order, color spaces and single-input passthrough.
General-path controls also cover fractional offsets and unused graph primitives.
Alias controls cover offsets, offset chains and zero blur without collapsing moved inputs.
Affine controls cover image-edge coverage, transparent borders, fractional output
scales and native image bounds beyond compact storage.
[Linear shape controls](Research/linear-shape-source-maps/README.md) use the same
harness for circle, ellipse and rounded-rectangle gradient source maps, resource
and instance placement, independent strokes and empty dimensions.
[Radial axis controls](Research/radial-axis-source-maps/README.md) retain source
reflection through local and retained paint maps, with separate native images
for mirrored-coordinate spellings and focal-coordinate precision cases.
Inner-circle controls cover centered and offset conical coordinates, symbol
reference maps and empty viewports; ownership checks share the same replay path.
Local focal-circle maps stay separate from definition coordinates; collapsed
viewport controls include direct and inherited general paint maps.
Native conical masks also cover single-color paints and supplied glyphs; font
references retain Windows/Linux coverage profiles and verify the embedded bytes.
[Spread-stage controls](Research/gradient-spread-stages/README.md) cover native
periodic rounding, negative coordinates and period boundaries.
[Paint-replay controls](Research/gradient-paint-replay/README.md) cover single-stop
and zero-size paints, instance scopes and independent pattern-fill/stroke opacity.
[Factory-color controls](Research/gradient-factory-colors/README.md) cover float
thresholds, periodic mean colors, flat stops, opacity replay and viewport clipping
around filter output. Lifecycle checks cover integration and clip ownership.
[Periodic stop-range controls](Research/linear-periodic-stops/README.md) check
spreading before endpoint padding through shapes, glyphs and affine instances.
[Stop-preparation controls](Research/gradient-stop-preparation/README.md) cover
duplicate/flat stops, float collapse, uniform coefficients and glyph preblending.
[Color-alpha controls](Research/gradient-color-alpha/README.md) retain independent
percentage/numeric/encoded references, parser spellings, byte rounding boundaries
and gradient consumers; their deferred list preserves remaining filter cases.
[Uniform filter controls](Research/filter-uniform-precision/README.md) cover
source alpha, packed and floating graph arithmetic, sampled image bounds,
color transfer, crops and opacity through affine placement.
[Solid-paint controls](Research/solid-paint-alpha/README.md) cover inherited alpha,
mask and glyph paints, and text opacity across strokes, decorations and deferred runs.
[Pattern selection controls](Research/pattern-source-selection/README.md) cover
consumer dependencies, font requests, marker roles and empty fill/stroke sources.

[Affine flood tests](Lokad.Svg.Tests/SvgAffineFloodTests.cs) retain input-specific
Chrome RGBA hashes for mapped filter regions, colors, alpha and source forms,
plus work/memory denial, cancellation and retry checks. Fractional filter regions
can differ from unfiltered rectangles; those references describe the filtered input.
[Developer tools](../tools/README.md) documents capture and research procedures.

[Text paint box tests](Lokad.Svg.Tests/SvgTextPaintBoundsTests.cs) compare anchors,
positions, spacing, rotation and font fragments with [native numeric references](Research/text-paint-bounds/capture.json)
on each platform, and check metric reuse, denial, cancellation and scratch cleanup.
[Shared-layout controls](Research/text-paint-bounds/README.md) add exact whole-text
box vectors and canonical mixed direct/path images. Their separate contracts
cover borrowed views and source-local own-filter remapping, including cancellation
during glyph transformation; rendering fixtures do not repeat the destination matrix.
[Image controls](Lokad.Svg.VisualTests/SvgTextPaintBoundsReferenceTests.cs) retain
[separate platform references](Research/text-paint-bounds/image-capture.json) for
linear, radial and pattern fills/strokes in object-box and user-space units.
Partial-opacity gradient references also check drawing-rectangle rounding before
output scaling; cache/record tests guard repeated-run work and scene storage.
[Path source-box vectors](Research/text-paint-bounds/path-capture.json) cover
anchors, rotation, offsets, mixed faces/sizes and baseline transforms independently
of the glyph paint origin and output scale.
[Character-width captures](Research/text-paint-bounds/inline-capture.json) check
fractional letter/word spacing and preserved edge spaces;
[spaced-path controls](Research/text-paint-bounds/spaced-path-capture.json) combine
these widths with anchors, font changes, `textLength` and negative advances.
[General path-pose tests](Lokad.Svg.Tests/OpenTypeFontTests.TextPathPlacement.cs)
cover horizontal tangents under scaled/affine placement and cleanup on denial.
Recorded-strike image controls vary local/output scales and mapped viewport replay before matrix rounding.
Horizontal fragment image controls cover fractional/negative origins, zero-angle segments and both font programs before scaled/affine placement.
Decoration drawing image controls separate native opacity isolation from opaque text for all three line types.
Uniform-mask frame controls cover overlines through direct text, instances and
symbols with separate native images for opaque and partial-opacity paints.
Clip frame controls retain decoration ink outside the recorded text drawing box.
[Deferred filter tests](Lokad.Svg.Tests/OpenTypeFontTests.FilteredDeferredText.cs)
cover text continuations and replay; [source bounds tests](Lokad.Svg.Tests/SvgDeferredFilterSourceTests.cs)
keep nonpainting layout bounds separate from painted filter bounds.

[Marker paint tests](Lokad.Svg.Tests/SvgMarkerPaintBindingTests.cs) cover forward
resources, inherited fills, aliases, tile paint ownership, limits and cancellation.
[Browser checks](Lokad.Svg.VisualTests/SvgMarkerPaintBindingReferenceTests.cs) use
[independently captured inputs and hashes](Research/marker-paint-binding/capture.json).

[Line-marker tests](Lokad.Svg.Tests/SvgLineMarkerTests.cs) cover unstroked hosts,
forward markers, affine replay and owner cleanup. The
[font cases](Lokad.Svg.Tests/OpenTypeFontTests.LineMarkers.cs) check ex metric
preparation and suppression; [browser evidence](Research/line-marker-instances/capture.json)
retains source orders, local fragment spellings and resource placements.

[Fractional mask tests](Lokad.Svg.Tests/SvgFractionalMaskRegionTests.cs) check source
paint clips, placed uniform regions and raster ownership against native RGBA hashes.
[Captured cases](Research/fractional-mask-regions/capture.json) retain unequal
masked controls as separate browser images.

[Instance paint order tests](Lokad.Svg.Tests/OpenTypeFontTests.InstancePaintOrder.cs)
cover source/use/resource ordering, inherited paints, replay, limits and ownership.
The [browser reference tests](Lokad.Svg.VisualTests/SvgInstancePaintOrderReferenceTests.cs)
check each order against its own committed image; capture hashes are retained
with the [font-free inputs](Research/forward-instance-paints/capture.json).

[Empty contour tests](Lokad.Svg.Tests/SvgEmptyContourMappingTests.cs) exercise
solid/gradient/pattern instances and curve scratch ownership. The
[empty-target cases](Lokad.Svg.Tests/OpenTypeFontTests.EmptyPatternTargets.cs) cover
tile-font selection; [browser checks](Lokad.Svg.VisualTests/SvgEmptyContourReferenceTests.cs)
retain independently captured inputs and hashes in
[their evidence record](Research/empty-contour-sources/capture.json).
The [inactive-channel cases](Lokad.Svg.Tests/OpenTypeFontTests.InactivePatternChannels.cs)
cover zero fill/stroke opacity and stroke width, CSS precedence, instances,
masks/filters and independent live channels.
The [primitive cases](Lokad.Svg.Tests/OpenTypeFontTests.InactivePatternPrimitives.cs)
also cover intrinsic visibility, fill-area eligibility, inherited activity and
scratch ownership. [Source visibility tests](Lokad.Svg.Tests/SvgSourceVisibilityTests.cs)
cover cached shader replay; [browser checks](Lokad.Svg.VisualTests/SvgSourceVisibilityReferenceTests.cs)
use each input's own [captured image](Research/source-visibility/capture.json).
The [rectangle shader cases](Lokad.Svg.Tests/SvgRectangleShaderInstanceTests.cs)
cover native frame coverage, empty/oversized strokes and clip-source ordering;
[context cases](Lokad.Svg.Tests/SvgRectangleShaderContextsTests.cs) compare direct
and retained paint coordinates through effects. [Ownership tests](Lokad.Svg.Tests/SvgRectangleShaderOwnershipTests.cs)
cover denial/cancellation/retry; [browser checks](Lokad.Svg.VisualTests/SvgRectangleShaderFrameReferenceTests.cs)
use [independently captured inputs](Research/rectangle-shader-frames/capture.json).
The [overlap cases](Lokad.Svg.Tests/SvgRectanglePaintOverlapTests.cs) compare
combined fill/stroke paints with ordered draws, including opacity layers and
masks. Their [browser checks](Lokad.Svg.VisualTests/SvgRectanglePaintOverlapReferenceTests.cs)
retain each input's [native image](Research/rectangle-paint-overlap/capture.json).
The [zero-radius cases](Lokad.Svg.Tests/SvgZeroRadiusPaintTests.cs) cover contour
order and symbol linear-paint maps; [ownership cases](Lokad.Svg.Tests/SvgZeroRadiusSymbolOwnershipTests.cs)
exercise denial, cancellation and retry. Their [browser checks](Lokad.Svg.VisualTests/SvgZeroRadiusPaintReferenceTests.cs)
retain [separate references](Research/zero-radius-paints/capture.json) for naturally unequal inputs.
The [shader hairline cases](Lokad.Svg.Tests/SvgShaderHairlineTests.cs) retain
independently measured width/alpha equivalences;
[ownership checks](Lokad.Svg.Tests/SvgShaderHairlineOwnershipTests.cs) cover
material lifetime and denial/cancellation/retry. Their
[browser checks](Lokad.Svg.VisualTests/SvgShaderHairlineReferenceTests.cs) use
[fresh-process references](Research/shader-hairlines/capture.json), including
wide surfaces whose offscreen SVG-image rasterization differs from browser capture.
The [inherited clip cases](Lokad.Svg.Tests/SvgInheritedClipIsolationTests.cs)
check native edge bytes, forward references, linear command-rebuild work and
ownership under denial/cancellation/retry. Their
[browser checks](Lokad.Svg.VisualTests/SvgInheritedClipReferenceTests.cs) retain
[independent images](Research/inherited-clip-isolation/capture.json) for outer
and inner clip placements, including naturally different outputs.
The [containment cases](Lokad.Svg.Tests/SvgContainedClipIsolationTests.cs)
retain native mask/shader grids through direct and referenced paints, placement
and output scaling, with [independent references](Research/contained-mask-clips/capture.json).
The [own-clip cases](Lokad.Svg.Tests/SvgOwnClipIsolationTests.cs) cover the owner's
opacity boundary, both reference orders, native pixels for large axis extents and
linear sibling work. Their [browser checks](Lokad.Svg.VisualTests/SvgOwnClipReferenceTests.cs)
use [separate images](Research/own-clip-isolation/capture.json), including masks and instances.
The [mask clip cases](Lokad.Svg.Tests/SvgMaskClipIsolationTests.cs) compare own
and inherited clips in both reference orders, retaining literal native edge
colors and shader grids. Their [browser checks](Lokad.Svg.VisualTests/SvgMaskClipReferenceTests.cs)
use [fresh native images](Research/mask-clip-isolation/capture.json).
The [pattern recording cases](Lokad.Svg.Tests/SvgPatternRecordingTransformTests.cs)
cover source-order pixels, deep-chain storage, cancellation/retry and independent
draw matrices. Their [browser checks](Lokad.Svg.VisualTests/SvgPatternRecordingReferenceTests.cs)
use [deduplicated fresh references](Research/pattern-recording/capture.json) for
authored viewports and external scaling, including instances and supplied-font text.
The [viewport recording cases](Lokad.Svg.Tests/SvgViewportRecordingScopeTests.cs)
check native shader colors for outer, own and inner opacity, plus denial and retry
ownership. Their [browser checks](Lokad.Svg.VisualTests/SvgViewportRecordingReferenceTests.cs)
use [fresh viewport references](Research/viewport-recording/capture.json).
The [viewport storage checks](Lokad.Svg.Tests/SvgViewportContextStorageTests.cs)
compare the charged allowance with actual context allocations and verify failed
registration, cancellation and disposal.

[Display inheritance tests](Lokad.Svg.Tests/SvgDisplayInheritanceTests.cs) cover
logical parents, retained instances, viewports and resource children across
attributes, inline styles and stylesheets. The
[prepared text cases](Lokad.Svg.Tests/OpenTypeFontTests.DisplayInheritance.cs)
also check font discovery and suppression without font acquisition.

[Definition display tests](Lokad.Svg.Tests/SvgDefinitionDisplayTests.cs) distinguish
hidden resources from hidden outer groups and ordinary use sources. They cover
template replacements, single-stop gradients, filter suppression and resource
cleanup after limits or cancellation. The
[font cases](Lokad.Svg.Tests/OpenTypeFontTests.DefinitionDisplay.cs) verify disabled
resource acquisition and visible local replacements.

## Collections

| Collection | Role and source of truth |
| --- | --- |
| [Upstream static documents](Research/upstream-static-documents/README.md) | 26 documents selected by feature from pinned upstream inventories, each with one recipe, supplied fonts where needed and separate Windows/Linux references. |
| [Original fixtures](Fixtures/manifest.json) | Small CC0 regressions with independent semantic-support and fidelity statuses; oracle metadata under `References/`. |
| [Research probes](Research/manifest.json) | Categorized rejections or measured renders, including known differences. A reference alone does not imply support. |
| Focused banks under [Research/](Research/README.md) | Feature-specific manifests/READMEs, including [native paths](Research/native-path-replay/manifest.json), [bounded sizing](Research/bounded-raster-sizing/README.md) and [static TrueType](Research/static-truetype-fonts/README.md). |
| [Core tests](Lokad.Svg.Tests/) and [visual tests](Lokad.Svg.VisualTests/) | Geometry, numerical, API, ownership, resource and rendering contracts. |
| [Font-host tests](Lokad.Svg.FontProbe.Tests/) | Explicit font acquisition and host integration. |

Manifests own asset identities, options and expected outcomes. Offline tests use
committed assets without downloads, private configuration or `external/`. Focused
banks complement whole documents; document totals and exhaustive combination
quotas are not completion criteria.

## Fixture admission

1. Declare a stable ID, source/revision, creator, license and rights evidence,
   dimensions, feature purpose, viewport/scale/background and required font programs
   before screening renderer output. Keep original bytes and notices.
2. Compute lowercase SHA-256 hashes from actual files. In the original fixture
   catalog, `originalSha256` preserves bytes before the CC0 SPDX header was added;
   `materializedSha256` identifies the rendered asset. Declare capture-only root
   sizing/font bindings separately, preserving the original drawing and requested
   font identity. Keep original and materialized identities distinct.
3. Follow the [capture rules](../tools/README.md#capture-and-reference-rules).
   Retain input/resource/reference hashes, platform browser pin, recipe revision
   and capture options in the collection's manifest/reference metadata.
4. Require a reviewed reference and an [exact independent comparison](../tools/README.md#pixel-comparison)
   before declaring fidelity `exact`. Unsupported entries retain their reason and
   provenance and stay outside exact comparison until admitted.

[FixtureManifestTests](Lokad.Svg.VisualTests/FixtureManifestTests.cs) audits and
renders every exact fixture. Research and feature-bank tests enforce their own
schemas/outcomes. Do not rewrite paths/text, add tolerance or replace references
to accommodate the candidate. A changed capture contract needs separate
qualification while preserving the old provenance.

## Qualification

Dash-validation and denied-scratch allocation gates take the minimum of three fixed
warmed batches. Keep semantic assertions outside measured intervals and validate
every sample's error and ownership results. Limits remain unchanged; per-call
array/storage regressions must still fail.

Windows and Linux use separate frozen Chrome references. Passing cases establish
the declared outputs, not general SVG completeness. Assess qualification through
the committed manifests/tests and CI results for the relevant revision; historical
test totals are not ongoing gates.

[Public font qualification](Research/public-font-qualification/README.md) covers
regular, bold, real italic and intentional fallback: acquisition, intended-face
selection, independent metrics and exact platform rendering. Standalone
qualification is complete. Four private deployed-manifest requests remain pending
before production adoption; public success makes no claim about that deployment.

[Text-node fragment controls](Research/text-node-boundaries/README.md) retain
native boundary, shaping, fitting and resource cases with ownership/retry checks.

## Retired collections

The upstream selection replaced full logo/export/icon/authored-sheet membership.
Old document-count and percentage gates are retired. Local manifests identify
surviving focused cases; assets needed for font provenance, API/resource contracts,
benchmarks remain under those contracts without counting as active full documents.
The corporate capture/measurement workflow and its aggregate reports are retired
to Git history. [Export provenance](References/README.md#retained-export-evidence)
and focused references remain with their current consumers.
[SvgCubicRouteReferenceTests](Lokad.Svg.VisualTests/SvgCubicRouteReferenceTests.cs)
retains the intended-size `inv-027` cubic-routing regression and native-picture evidence.
