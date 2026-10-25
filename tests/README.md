# Tests and provenance

Run the offline suite from the repository root:

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

Use `--filter FullyQualifiedName~TestClassName` on a test project for focused
checks. The [CI workflow](../.github/workflows/ci.yml) runs Windows/Linux tests,
browser/resource/font-host smoke checks and package/installed-consumer checks.

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

[Affine flood tests](Lokad.Svg.Tests/SvgAffineFloodTests.cs) retain input-specific
Chrome RGBA hashes for mapped filter regions, colors, alpha and source forms,
plus work/memory denial, cancellation and retry checks. Fractional filter regions
can differ from unfiltered rectangles; those references describe the filtered input.
[Developer tools](../tools/README.md) documents capture and research procedures.

[Text paint box tests](Lokad.Svg.Tests/SvgTextPaintBoundsTests.cs) compare anchors,
positions, spacing, rotation and font fragments with [native numeric references](Research/text-paint-bounds/capture.json)
on each platform, and check metric reuse, denial, cancellation and scratch cleanup.
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
