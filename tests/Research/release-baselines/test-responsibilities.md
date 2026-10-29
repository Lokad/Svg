# Test responsibility map

Production admission changes retain their [contract map](parser-validation.md).
The suite uses complementary layers; class names alone do not classify every case.
Existing traits now consistently use `Mechanism`, `Contract`, `Rendering` and
`Integrity`; the former plural spellings are normalized without changing cases.
The explicit `Category=Research` selection remains independent of the layer.

| Responsibility | Retained coverage |
| --- | --- |
| Mechanisms | Parsing, geometry, shaping, filter stages and rounding test their actual numerical boundaries directly; alpha vectors remain exhaustive. |
| API and ownership | The 96-case conversion matrix covers eight representative inputs across twelve forms. Shared admission adds the distinct governed raw/prepared, destination and lifetime paths. Font, stream and scene ownership regressions remain separate. |
| Rendering | Each paint-bank input/options pair keeps its own independent reference and exact comparison. Platform images, former failing interactions and native source/provenance checks are retained. |
| Integrity | The paint partition check detects missing or duplicate identities; manifest loading validates historical filter resolution and research selections. Reference/source hashes and licensed fixtures remain authoritative. |
| Research | Extended numerical-context products and historical source-composition cases remain individually selectable and execute in full qualification. |

## Paint-bank discovery and loading

[The typed loader](../../Lokad.Svg.VisualTests/PaintReferenceBank.cs) discovers
identities and selections without retaining rendering JSON. It loads one immutable
metadata document per bank when rendering first needs it. Selection and loaded
metadata are checked for identity agreement; duplicate/unknown historical and
research IDs fail. Lazy initialization is synchronized for concurrent readers.
The original capture/status/selection manifests remain the only index. Rendering,
font ownership, source hashes, reference hashes and pixel assertions are unchanged.

The previous all-bank static constructor and per-case JSON clones mapped to these
same 41,334 individually discovered rendering cases. Both enumeration methods,
case names, source/options, order and routine/research membership are preserved.
This pass removes no case and introduces no batched rendering assertion.

| Bank | Routine | Research |
| --- | ---: | ---: |
| ordered-clip-coverage | 91 | 0 |
| clipped-stroke-preparation | 28 | 0 |
| marker-canvas-stages | 37 | 0 |
| filter-region-coordinates | 65 | 0 |
| point-list-source-maps | 90 | 0 |
| image-source-views | 120 | 0 |
| offset-merge-inputs | 201 | 0 |
| filter-merge-image-draw | 45 | 0 |
| filter-source-recording-map | 3 | 0 |
| filter-source-composition | 0 | 2,253 |
| linear-shape-source-maps | 119 | 0 |
| radial-axis-source-maps | 1,003 | 0 |
| gradient-spread-stages | 387 | 0 |
| gradient-paint-replay | 1,372 | 0 |
| gradient-factory-colors | 5,201 | 0 |
| linear-periodic-stops | 1,734 | 0 |
| gradient-stop-preparation | 7,242 | 0 |
| gradient-color-alpha | 8,760 | 1,300 |
| filter-uniform-precision | 4,534 | 3,716 |
| solid-paint-alpha | 2,361 | 0 |
| pattern-source-selection | 54 | 0 |
| text-node-boundaries | 594 | 0 |
| hairline-clip-rounding | 24 | 0 |
| **Total** | **34,065** | **7,269** |

The separate partition check adds one integrity case. The filter-uniform bank
includes 1,206 resolved historical cases, including its two routine shadow cases;
no historical status record or independent reference was rewritten.

## Local setup-cost check

Three fresh .NET 11 Windows processes enumerated `Cases()` and `ResearchCases()`
before and after this change, starting from commit `279039c1f`. Timing covered
those calls and row enumeration, excluding assembly load and rendering. Allocation
used `GC.GetTotalAllocatedBytes(true)`. Median results:

| Discovery | Before | After |
| --- | ---: | ---: |
| Time | 265.8 ms | 183.6 ms |
| Managed allocation | 177.1 MB | 99.7 MB |
| Process peak working set | 186.4 MB | 103.1 MB |

Ordinally sorted `method:folder:id` rows, joined with LF and SHA-256 hashed, match
before/after: `a8fe323578a74003d9d498db59b7fba8d9d2c831c8f7509e6bc60b1f26200572`.
The complete routine paint selection passed 34,066 checks in 81.32 s before and
81.68 s after, including test-host/discovery/TRX costs and excluding build.
There is no measured improvement in total rendering-validation time from this pass.
These local setup measurements are not renderer performance or quiet-VM results.

P2 still needs suite-wide classification and review of remaining repeated fixture
work.
This loader change does not establish that those tasks are closed.

Validation: all 41,335 paint/integrity checks pass on Windows and Linux under
.NET 11. The normalized mechanism/contract selection passes 1,990 checks on each
runtime/host combination. A .NET 10 rendering selection retains 619 text, clip and
partition checks on both hosts; both framework builds are warning-free. No runtime
code, fixture inputs, native images, capture metadata or licenses changed.

## Shared corpus and explicit export

Visual tests resolve the committed research corpus once per process and read it
in place. They prefer the checkout to stale copies from previous builds. An
explicit `LOKAD_SVG_RESEARCH_ROOT` overrides discovery; missing explicit roots fail.
Outside a checkout, the standalone suite uses its adjacent `Research` directory.
Five selection regressions cover explicit roots, checkout freshness, standalone
fallback and invalid/missing configuration. Public font qualification also locates
its bundled FontProbe host before trying checkout tool outputs.

The ordinary project no longer copies the full research tree: 47,581 tracked
files / 100,548,974 blob bytes at the preceding checkpoint. Its two explicit
fixture-oracle links remain. Small core `TestData` subsets remain separate.
The [export procedure](../../../tools/README.md#export-the-visual-suite) copies
the full corpus and notices only when an archive is requested. Old output copies
were preserved for this migration; avoided future copies are not reclaimed space.
No input, native reference, capture record or license was rewritten.

Shared-corpus qualification passes all 45,917 visual checks per host under .NET 11
and 1,393 selected checks per host under .NET 10. An exported suite outside the
checkout passes 40 integrity/rendering/font-host checks on each OS, including
actual FontProbe execution from its bundled directory. Both normal builds were
checked without an output Research tree, and neither recreated it.

## Raster comparisons and PNG transport

All 41,334 original paint-case identities now compare the production capture
surface with their same native image. The existing parse/raster owner is the
internal seam; no second scene parser, renderer, pixel API or ownership wrapper
was added. It retains scene, surface and host-scope charges through comparison.
Capture pixels are sRGB straight-alpha RGBA8 after the production's final
unpremultiply. RGB at zero alpha is unspecified before encoding and ignored by
the existing comparer on both paths; all other channels keep exact comparisons.
The previous 16,384-pixel actual-image bound remains explicitly asserted.

Ten complete PNG representatives retain clips, embedded-image cropping, scaled
linear/radial paints, filter composition, patterns, fractional font placement,
alpha midpoints and both original scaled shadow graphs. Other native PNG test
classes, the 96-case API matrix, distinct ownership paths and codec/stream tests
remain unchanged. Six direct seam cases compare every canonical RGBA byte with
decoded PNG (normalizing only zero-alpha RGB), retain the borrowed root through
access and cover cancellation, refused preparation, cleanup and immediate retry.
No native image, case, capture metadata or tolerance is removed or rewritten.

One sequential Windows .NET 11 edit-loop measurement, including discovery, host
startup and TRX output but excluding build, took 82.53 s before (34,066 checks)
and 75.14 s after (34,076 checks). The ten extra checks are the complete PNG
representatives; original rendering identities and the research partition retain
their same SHA-256 digest. This measures validation cost, not renderer throughput
or a quiet-VM performance result.

The affected milestone passes 45,933 visual checks per host on .NET 11, 783 selected
visual checks per host on .NET 10 and 294 API/budget/codec checks on each runtime/host
combination. Both solution builds are warning-free. The main test guide now links
these responsibility/provenance records instead of repeating collection narratives.

## Background initialization

[Background contracts](../../Lokad.Svg.VisualTests/SvgBackgroundColorTests.cs)
retain seven public PNG cases. Five fail before the shared initialization fix:
partial-alpha backgrounds are brightened, zero-alpha RGB tints later source-over,
and background options disagree with the same painted underlay. Transparent and
opaque controls remain exact. Empty RGBA(10,20,30,128) now captures as that color,
with the existing RGBA8 premultiply/unpremultiply quantization at very low alpha.
The fix computes three premultiplied background lanes once before initialization,
using the existing alpha multiply. No new rendering branch or reference is added.

Background qualification passes 45,940 visual checks per .NET 11 host and the
expanded 817-case .NET 10 native/format/background selection per host. The 294
API/budget/codec checks pass on all runtime/host combinations. Both solution
builds are warning-free; unchanged inputs/references and opaque/transparent
controls preserve the existing rendering baseline.

## Core pixel helpers and allocation contracts

The source before this pass is `e13932150`. The eight core clip/curve/mask/polygon/
shape/stroke/style/instance classes retain every original declaration, SVG input,
assertion and data case. Their shared [pixel helper](../../Lokad.Svg.Tests/SvgRasterTestHelper.cs)
uses the existing production capture owner and returns a test-owned copy before
releasing it. Only zero-alpha RGB is normalized, matching prior PNG output.
The eight private PNG decoders are removed; six explicit PNG geometry paths and
all encoded-byte equality checks still use PNG. Those paths reuse the existing
independent VisualDiff decoder, including its CRC and bounded-format checks.
The [codec tests](../../Lokad.Svg.Tests/PngEncoderTests.cs) separately retain
unfiltered multirow layout, straight RGBA lanes and transparent-RGB clearing.
The new codec case makes that responsibility explicit instead of testing filter
choice incidentally throughout geometry fixtures.

| Previous responsibility | Current owner and preserved behavior |
| --- | --- |
| Nine contour eligibility methods in `RenderBudgetTests` | [SvgContourEligibilityTests](../../Lokad.Svg.Tests/SvgContourEligibilityTests.cs), with identical method names/bodies and original cleanup assertions; classified as mechanisms. |
| Remaining budget tests | [RenderBudgetTests](../../Lokad.Svg.Tests/RenderBudgetTests.cs), classified as contracts; the curve storage/command-bound assertion stays here. |
| Unclassified public API checks | [PublicContractTests](../../Lokad.Svg.Tests/PublicContractTests.cs), classified as contracts. |
| Mixed PNG codec and stream checks | [PngEncoderTests](../../Lokad.Svg.Tests/PngEncoderTests.cs) uses method-level Mechanism/Contract traits for all seventeen methods. |
| Font read in every API case | [API matrix](../../Lokad.Svg.Tests/SvgConversionApiContractTests.cs) still has 96 cases and per-invocation provider/snapshot owners. Only its 36 text cases read the font; unexpected font-free acquisition fails. |

On every runtime/host, all 1,146 unmoved TRX case IDs match; nine relocated facts
match their original method names/bodies. Truncated display names retain distinct
case IDs. The normalized display-name multiset for all 1,155 original cases has
SHA-256
`e7d83654c6001ab0faebcc1447248e8b0036e459421be2a6ba65ef445041bffe`.
No original case is batched or removed; one focused multirow codec case is added.
The .NET 10 CI selection includes the relocated mechanisms.

Three fresh Windows .NET 11 processes executed all 96 API cases sequentially.
Median managed allocation fell from 84.5 MB to 50.3 MB; median execution stayed
about 1.02 seconds. Input font bytes and per-case lifetimes remain independent;
there is no shared mutable provider or snapshot. One sequential focused VSTest
comparison cost 10.64 seconds before and 5.67 seconds after, with approximately
one second of reported test execution in both. That startup-dominated sample does
not establish a timing improvement; allocation and responsibility separation are
the concrete results. No renderer or quiet-VM performance claim follows.

Validation for this pass: the complete .NET 11 core suite passes 27,753 cases per
host after helper migration and method relocation. The final test-only multirow
codec addition passes with the affected 1,156-case selection on each runtime/host;
all original cases remain, plus the named codec vector. Both solution builds and
locked restore pass. Production source, independent references, authored inputs,
tolerances and licenses are unchanged.

## Inert-paint rendering and prepared contracts

The original regressions, introduced in `2a75d0b96`, compare an inert paint server
with explicit `none` while retaining the independent paint, bounds and effects.
They are metamorphic rendering checks, supplementary to the unchanged independent
browser references. Their former source at `58ddcf2f0` mixes canonical rendering,
prepared rendering and font-request counts inside two looped theories.

| Original inputs | Canonical rendering | Routine prepared contract | Extended prepared contract |
| --- | ---: | ---: | ---: |
| 540 paint rows, with 3 or 4 companion paints | 2,120 graphs | 302 | 1,818 |
| 80 template rows, with 18 order/map/effect combinations | 1,440 graphs | 600 | 840 |

[Canonical cases](../../Lokad.Svg.Tests/SvgInertPaintTests.cs) now discover each
graph separately and compare exact production capture pixels with its original
control. Dimensions and every nonzero-alpha lane remain exact; only zero-alpha
RGB is normalized to the former PNG convention. No input or control is removed.
Both factories preserve all 7,120 generated SVG byte arrays, including unused
definitions, forward ordering and the original nested-pattern exclusions.
Sorted parameter/control-or-input/hash rows have SHA-256:

- Paint: `dc88127a9b815df045a5944e31a46981e2a6e55f5770e98e52a61f467a6586bd`.
- Template: `fc3c38772e310475fd78617e62d604f4b9466084841554788ab9182d634dccaa`.

[Prepared contracts](../../Lokad.Svg.Tests/OpenTypeFontTests.InertPaintServers.cs)
keep complete PNG equality and zero acquisition with fresh per-case providers.
Routine paint cases retain every server, paint and consumer on the curve route,
plus every primitive lowering route with direct solid paint. Routine templates
retain every server/paint-role/units/consumer/order/effect combination with a
rotated map, plus every map/effect on the direct object-box route. Thus shared
preparation and resource replay stay covered without requiring the full product
for every development check. Remaining combinations are ordinary individually
discovered `Category=Research` cases; unfiltered qualification includes all of them.
The .NET 10 CI compatibility selection keeps the routine contracts, while full
.NET 10 release checks also include research.

The nine invalid-value cases still check all three resource kinds through raw
conversion and asynchronous preparation, retaining their exact error categories.
The mask partial-owner fact still checks borrowed storage, refusal, cancellation,
cleanup, unchanged input and immediate retry. Their bodies and case IDs are
unchanged. Supplied-text/native-layer regressions remain in their independent
[visual class](../../Lokad.Svg.VisualTests/SvgSuppliedFontLabelTests.cs); this pass
does not generalize away font, glyph-bound or prepared-specific interactions.

Three sequential fresh Windows .NET 11 processes measured the same affected
method selection before and its separated routine responsibilities afterward,
including 135 unchanged inert-input sentinels. Median wall time, including host,
discovery and TRX writing but excluding build, is **14.30 s before / 15.02 s after**.
Canonical rendering alone takes **14.00 s**. Individually exposing the formerly
looped graphs increases discovery/reporting cost; this pass establishes **no
end-to-end validation speedup**. The benefit is selectable responsibilities and
less repeated encoding/preparation during rendering investigation. Counts are
765 old rows versus 4,607 routine rows; both cover the same 3,560 target graphs.
No cases are hidden by batching, and these are not renderer/quiet-VM timings.

The complete .NET 11 core suite, including every extended prepared case, passes
34,254 checks on each host. The affected .NET 10 rendering/contract/API/codec
selection passes 4,726 on each host. The original 145 retained case IDs match,
including all invalid-value/ownership cases and the unchanged sentinels.

## Shared native images and font-capture integrity

All original paint cases, options, platform companions and exactness rules remain.
The fixture now shares each declared reference path's hash and decoded pixels
through [NativeReferenceImages](../../Lokad.Svg.VisualTests/NativeReferenceImages.cs).
Every row still checks its own expected hash, including cache hits. Two negative
integrity cases cover mismatched metadata on cold/warm paths and valid reuse
after rejection. Pixels are borrowed read-only; the existing comparer does not
mutate them. The class fixture clears images after every borrower has finished.
The 16,384-pixel per-image decode bound remains. Caching follows file identity;
no source graphs or distinct native companions are combined because they look alike.

Across the 23 Windows banks, 41,334 rendering inputs reference 9,493 paths, with
31,841 repeat decodes. The complete decoded corpus is 59,729,000 bytes; selected
runs load only requested images, retained for their fixture lifetime. This is test
fixture memory, not an allocation or performance claim about library conversions.

The same 10,633 font-bearing graph recipes now have separate per-bank Integrity
checks in their original routine/research partition. Source/font/capture hashes,
family replacement and exact embedded CSS bytes remain pinned. SHA-256 consumes
the same UTF-8 prefix, cached CSS and suffix without materializing a font-sized
inserted string and byte array per graph. The ten complete PNG representatives
remain rendering checks; their shared recipe is verified by the integrity layer.
No reference, metadata, input or license is regenerated or changed.

| Sequential Windows .NET 11 validation cost | Before | After |
| --- | ---: | ---: |
| Routine paint, including discovery/host/TRX, median of three fresh processes | 81.95 s /34,076 cases | 66.11 s /34,099 cases |
| Temporary managed allocation for 100 existing font-capture hashes | 227,795,824 bytes | 58,192 bytes |

Every original routine case ID remains; the additional 23 cases are the separate
routine integrity gates. Before wall times were 179.49/79.60/81.95 seconds; after
68.36/66.11/65.69. The first baseline was much slower, and these sequential local
samples are validation costs, not renderer throughput or quiet-VM measurements.
The untimed allocation probe checks both algorithms against every selected
committed capture hash; the table uses three-process medians (the first baseline
allocated 2,320 extra bytes, while both other baselines and all after runs agree). It excludes shared CSS and
input setup from both intervals and measures the recipe hashing work alone.

Validation: all 45,988 visual checks pass on Windows/Linux under .NET 11,
including every research recipe and original image comparison. The affected
59 integrity/partition/complete-PNG checks also pass on .NET 10 on both hosts.
Case IDs agree across hosts; the original rendering-case declarations, data
selection and bank registry are unchanged. Both solution builds are warning-free.

## Core shader, overlap and owning-clip responsibilities

At `493bd2485`, 216 shader-context rows use `consumer=direct`. Their actual and
control SVG bytes are identical; the retained `use` and `group-use` rows already
render those same control bytes with identical options. Only these self-comparisons
are removed. Their input/options membership SHA-256 is
`5a3e6a7a8248394a2d306f2a73cab249013ce9555d67f379193a3bc21ec32b83`.
This decision uses source/execution identity, not equal resulting images.

| Original responsibility | Retained owner and scope |
| --- | --- |
| Shader-context equivalence, 1,728 rows | [Shader contexts](../../Lokad.Svg.Tests/SvgRectangleShaderContextsTests.cs): 1,512 meaningful rows for the seven distinct consumers, with original paint/map/scale/opacity combinations and all direct control inputs. |
| Combined paints and ordered draws | [Rectangle overlap](../../Lokad.Svg.Tests/SvgRectanglePaintOverlapTests.cs): all 1,464 paint comparisons and 144 isolated-layer comparisons, with original exclusions. Constant-color paints retain their distinct resource graphs. |
| Owning-use clip placement | [Clip placement](../../Lokad.Svg.Tests/SvgUseClipPlacementTests.cs): all 864 placement comparisons, five direct coverage/rounding mechanism cases, nine ownership cases and four destination-rejection cases. |

All 3,984 retained rendering pairs and their serialized options match the original
compiled builders byte-for-byte. The raster helper now accepts explicit options
and compares dimensions as well as exact canonical RGBA bytes. Its owner, final
unpremultiply and zero-alpha RGB convention are unchanged. Only the bulk checks'
incidental encoding is removed. Eleven complete PNG representatives retain shader
maps through masks/clips/filters, pattern/symbol frames, independent paint overlap,
fractional layer strokes and owning clips with forward references and effects.
Every representative belongs to the original source/options matrix.

Rendering cases have Rendering traits; direct clip coverage/rounding cases have
Mechanism traits; refusal/cancellation/cleanup/retry and untouched-destination
checks have Contract traits. The existing independent
[shader-frame](../../Lokad.Svg.VisualTests/SvgRectangleShaderFrameReferenceTests.cs)
and [overlap](../../Lokad.Svg.VisualTests/SvgRectanglePaintOverlapReferenceTests.cs)
browser captures remain unchanged, including their prepared companions.

Thirty-two adjacent duplicate `Snapshot().CurrentBytes` assertions are removed.
The conversion-confined snapshot only reads counters and clones category data;
there is no operation between either repeated read. All checks around distinct
parse/render/refusal/cancellation/retry operations remain. Twenty-seven other
files match their original source after deleting only those duplicate lines;
the mixed clip class's mechanism/contract bodies retain every other statement.
No ownership test or allocation path is removed.

Three fresh sequential Windows .NET 11 processes measured 13.39 seconds median
before and 13.04 seconds after, including host/discovery/TRX and excluding build.
That small change does not establish a material timing improvement. The original
4,002 meaningful case IDs remain; 216 self-comparisons are removed and eleven PNG
cases added, yielding 4,013 focused cases. Production code, source inputs, native
references, tolerances and licenses are unchanged.

The complete .NET 11 core suite passes 34,049 cases on each host. The affected
.NET 10 selection passes 4,100 per host, and 128 native-reference/layout integration
cases pass on every host/runtime pair. Case IDs agree across hosts and the .NET 10
affected cases remain in full .NET 11 coverage. Both solution builds are warning-free.
The core inventory now has 20,716 unclassified cases; this slice does not close
the remaining suite-wide classification work.

## Degenerate geometry and flood ownership

The source before this pass is `4d9a4bd4a`. The
[zero-radius tests](../../Lokad.Svg.Tests/SvgZeroRadiusPaintTests.cs) retain all
576 solid-path and 216 symbol-map comparisons, including their original
nonuniform-map exclusions. Compiled old/new builders produce identical input
pairs and options. Bulk comparisons use exact capture pixels and dimensions;
five original source/options combinations also retain complete PNG equality.
The independent [browser references](../../Lokad.Svg.VisualTests/SvgZeroRadiusPaintReferenceTests.cs)
and their prepared companions are unchanged.

The 576 [empty-source cases](../../Lokad.Svg.Tests/SvgEmptyContourMappingTests.cs)
are named for the blank pixels they actually assert. The former name claimed
allocation coverage without measuring allocation. Their source construction,
paint/instance matrix and expected output remain; only incidental PNG encoding
is removed. The eight curve-mapping ownership cases retain their original refusal
sweep, borrowed storage, cancellation, source-command preservation and retry.
Two direct parser-boundary facts retain input-byte/node and authored-command
limits; the whitespace fact keeps its complete PNG check and loses the stale
"work" label.

The [flood rendering bank](../../Lokad.Svg.Tests/SvgAffineFloodTests.cs) retains
all 540 mapped-region and twelve pattern/mask cases, with every original Chrome
hash and rendering statement. The three restoration contracts move unchanged to
[SvgAffineFloodOwnershipTests](../../Lokad.Svg.Tests/SvgAffineFloodOwnershipTests.cs).
A small [shared input factory](../../Lokad.Svg.Tests/SvgAffineFloodInput.cs)
retains the exact original source builder, avoiding duplicated SVG construction.
The ownership cases now also match the existing .NET 10 CI ownership selection.

No original case is removed. The case map accounts for 576 rendering-name
changes, one whitespace-name change and three relocated ownership cases; all
1,353 other original identities remain. Five complete PNG cases make 1,938
focused cases versus 1,933 before. The old/new Windows .NET 11 validation medians
are 8.54/8.75 seconds across three fresh processes, including discovery/host/TRX;
there is no measured speedup. This pass separates responsibilities and corrects
claims, without changing production, inputs, native hashes/references or limits.

Validation: the complete .NET 11 core suite passes 34,054 cases per host, the
affected .NET 10 selection passes 1,938 per host, and all eight independent
zero-radius reference cases pass on every runtime/host pair. Explicit identity
mapping accounts for every original case; both solution builds are warning-free.

## Parsed-scene lifetimes in characterization tests

Before this pass (`becf16041`), 343 successful parse-result declarations lacked
`using`. Syntax and caller review distinguish their ownership:

- 338 test-local declarations now retain and dispose the scene at scope exit,
  after geometry assertions, coverage walkers and other borrowers finish.
- Three source-copy/transfer tests retain their exact deliberate early disposal
  and `finally` cleanup; they are not converted mechanically.
- The cubic/quadratic walker helpers return an owned scene instead of borrowed
  arrays from an undisposed scene. All six callers scope that owner. Their input
  expressions and assertions are preserved, with no array copies added.

The [scene contract](../../../src/Lokad.Svg/Parsing/SvgRoot.cs) now describes the
retained scene and viewport, shared ownership across copies, borrowed inputs,
conversion-thread use and uncancelable disposal of storage/reservations.
The obsolete viewport-only summary is removed; production behavior is unchanged.

A broader syntax audit accounts for 1,586 direct test `Parse`/`ParsePrepared`
invocations: 1,132 scoped forms, 443 expected failures, seven owner-returning
factories with scoped callers, the three intentional early-disposal declarations
and one successful assignment followed by an explicit `using` scope. This audit
does not equate every missing keyword with a leak or assume pooled storage.

All original test identities, inputs, assertions and references remain. The 943
focused cases pass before/after. Three sequential fresh Windows .NET 11 processes
measure 7.88/8.71 seconds median, including discovery/host/TRX and excluding build;
this adds required lifetime cleanup and establishes no validation speedup.
The full .NET 11 core suite passes 34,054 cases per host; 970 affected .NET 10
checks pass per host. Five visual diagnostics pass on each runtime/host pair,
including their unchanged coverage-stage evidence. Both builds are warning-free.


## Point-list boundaries, instances and parser ownership

The source before this pass is `8f1220541`. All 2,317 original focused case IDs
remain; twelve complete PNG representatives are added. Source audits retain every
original data enumeration, document builder and shader input expression.

- [Point-list numbers](../../Lokad.Svg.Tests/SvgPointListNumberTests.cs) keep all
  360 padded-coordinate and 112 signed/exponent/fraction combinations, including
  source eligibility through markers, filters, instances, patterns and masks.
  Three malformed-tail cases now exercise the parser directly. The four
  enclosing-versus-own flood cases retain their exact alpha assertions.
- [Point-list coverage](../../Lokad.Svg.Tests/SvgPointListCoverageTests.cs) keeps all
  448 placement rows, including the sixteen unsupported nested-pattern pairs,
  and the independent bilinear stripe-alpha assertion.
- [Shader instances](../../Lokad.Svg.Tests/SvgRectangleShaderInstanceTests.cs) keep
  all 360 frame rows and both definition orders. The nine clip and 36 degenerate
  or oversized-stroke cases retain their complete PNG assertions.

The 1,624 bulk rendering pairs compare production capture dimensions and exact
canonical RGBA bytes through the existing raster seam. Six numerical, three
point-list placement and three shader-frame PNG representatives retain encoding
interactions; each belongs to the original matrix. Independent browser references,
prepared companions, tolerances and input restrictions are unchanged.

The [parser lifecycle helper](../../Lokad.Svg.Tests/SvgParseOwnershipAssertions.cs)
extracts one byte-identical sequence from three callers: twenty cases retain
success, borrowed storage, caller cancellation, retry, peak-minus-one refusal and
cleanup. Expanding the helper restores each original method body exactly. The
[culled-bound](../../Lokad.Svg.Tests/SvgPointListDrawingBoundsTests.cs) sequence
and marker tests have different operations and remain separate. Mixed classes use
method traits; 1,432 original cases gain a responsibility without changing identity.

Three fresh sequential Windows .NET 11 processes measure 8.46/8.50 seconds median,
including discovery/host/TRX and excluding build. There is no material validation
speedup. The benefit is separated rendering/transport responsibilities and shared
ownership assertions. All 2,329 affected cases and 100 point-list/shader native/provenance
integration checks pass on Windows/Linux under both supported runtimes. Both affected
builds are warning-free. Production and the text-layout implementation are unchanged.

## Root parser responsibilities

The source before this pass is `e975ca152`. The former parser class mixed scene
metadata, rendering and memory admission. All 752 original cases remain:

| Responsibility | Owner | Methods / cases |
| --- | --- | ---: |
| Parsing, structure, numerical limits and retained geometry/paint metadata | [SvgRootParserTests](../../Lokad.Svg.Tests/SvgRootParserTests.cs) | 520 /541 |
| Exact pixels, complete PNG equivalence and known rendering interactions | [SvgRootRenderingTests](../../Lokad.Svg.Tests/SvgRootRenderingTests.cs) | 184 /198 |
| Distinct parser allocations, transfer, growth, refusal and release | [SvgRootParserOwnershipTests](../../Lokad.Svg.Tests/SvgRootParserOwnershipTests.cs) | 13 /13 |

Every one of the original 725 method/helper bodies, parameter lists and test
attribute lists has an identical source fingerprint after relocation. No test
assertion, input expression, data row, limit, PNG check or cleanup is removed.
The nine callers of `SvgPathFillAssertions.Equivalent` belong to rendering;
mask/recording command assertions inspect metadata and remain mechanisms.
Cleanup surrounding a pixel check does not by itself make it a general contract.
The thirteen ownership cases now also match the existing .NET 10 CI selection.

Seven methods across the mechanism/rendering classes share one small
[native-pattern assertion](../../Lokad.Svg.Tests/SvgPatternContentAssertions.cs).
It borrows the scene only during the assertion. Other helpers stay with their
consumers. Classes have one layer each, with no inheritance or new test framework.
Two names now describe decimal dimensions and uniform-image color/placement;
their old allocation claims had no allocation assertion. Their bodies are unchanged.
An explicit case map accounts for all class/name changes and retains all 540
unaffected IDs, including distinct cases with truncated equal display names.

Three fresh sequential Windows .NET 11 processes measured 7.95 seconds median
before and 7.59 after for all 752 cases. The separate 541-case mechanism selection
measured 7.01 seconds. These modest, host-dominated validation costs include
discovery/host/TRX and exclude build; they are not renderer throughput measurements.
All 752 affected cases, 71 native-image checks and two provenance checks pass on
each Windows/Linux/runtime combination. Both affected builds are warning-free.
Production, browser references, tolerances and the text-layout implementation are
unchanged. This closes the root-parser split, not suite-wide classification.

## Definition display and font acquisition

The source before this pass is `3997d21b3`. All 390 original
[definition-display cases](../../Lokad.Svg.Tests/SvgDefinitionDisplayTests.cs)
and 73 [sibling font cases](../../Lokad.Svg.Tests/OpenTypeFontTests.DefinitionDisplay.cs)
retain their original identities. Both shared data builders, including the text
branch consumed through `MemberData`, have identical bodies and constants.

The 389 rendering rows retain every resource kind, display state, selector,
definition order, template replacement and filter transform. Their 391 comparison
pairs use exact production capture dimensions and canonical RGBA. Seven complete
PNG representatives are selected directly from the original resource/template
rows. The borrowed-parser/raster refusal, cancellation and retry body is unchanged.
Rendering and contract traits separate these responsibilities without removing
inputs or changing references, tolerances, numerical boundaries or production.

The font cases keep their conversions, exact pixels, fallback controls and zero
request assertions. The positive request check now measures the actual input's
increment after control preparation; previously the control's requests alone
could satisfy it. No font scenario is moved to research or dropped.

Two string overloads forward UTF-8 input to the existing raster/PNG helpers;
all six original helper implementations are unchanged. All 463 original IDs
remain, with seven PNG cases added, yielding 396 Rendering and 74 Contract cases.
Three fresh sequential Windows .NET 11 processes measure 7.35/7.13 seconds median
for the core selection, including discovery/host/TRX and excluding build. This is
a small validation-cost difference, not a material renderer speedup.
All 470 affected cases, 71 native-image checks and two provenance checks pass on
Windows/Linux under both runtimes. Both affected builds are warning-free.
