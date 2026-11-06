# Tests and provenance

Use focused checks while editing, affected integration before promotion, and full
Windows/Linux/package qualification at coherent milestones. The
[release baseline](Research/release-baselines/README.md) identifies retained source,
package and reference evidence; the [support matrix](../docs/SUPPORT_MATRIX.md)
defines supported behavior.

Changes to filter source capture or clip domains also require
`FullyQualifiedName~SvgFilterSourceContextReferenceTests`, including its Research
rows, alongside the affected paint fixtures. Its independently referenced
rectangle/path pairs exercise different source and layer routes. A
[withdrawn parent-domain candidate](Research/release-baselines/parent-picture-clip-domain.json)
passed the paint selection while regressing this bank; equal images or a routine-only
selection do not establish those routes are interchangeable.

The [claim trace](Research/release-baselines/support-claim-map.md) links current
claims to implementation, direct checks and native evidence, keeping unfinished
reviews and unresolved authority explicit.

[Native math profiles](Research/native-math-profiles/README.md) preserve exact
legacy and CORE-MATH references for the same affected cases. Independent native
functions and healthy browser triples establish each companion; a fixed BCL scalar
probe selects the host profile without inspecting renderer output. An unrecognized probe result
fails qualification. Existing sources, references and assertions stay intact.

## Responsibilities

Use three complementary validation layers, with separate integrity checks:

| Layer | Responsibility | Starting points |
| --- | --- | --- |
| Mechanism | Exercise parsing, numerical boundaries, geometry, shaping and rounding directly. Keep useful exhaustive boundaries without repeating them across unrelated rendering contexts. | [Alpha boundaries](Lokad.Svg.Tests/SvgAlphaBoundaryMechanismTests.cs), [parser metadata](Lokad.Svg.Tests/SvgRootParserTests.cs), OpenType and filter-stage tests. |
| Contract | Preserve distinct execution, allocation and ownership paths: sync/async, raw/prepared, owned/memory/stream output, cancellation, refusal, cleanup and retry. | [Representative API matrix](Lokad.Svg.Tests/SvgConversionApiContractTests.cs), [shared admission](Lokad.Svg.Tests/OpenTypeFontTests.SharedMemory.cs), [HTTP preparation](Lokad.Svg.Tests/OpenTypeFontTests.HttpPrepared.cs), [PNG sinks](Lokad.Svg.Tests/PngEncoderTests.cs). |
| Rendering | Compare each source/options/platform case against its independent native reference, usually through one canonical conversion. Add another API path when it protects a specific interaction. | [Paint inputs](Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs), [text references](Lokad.Svg.VisualTests/SvgTextPaintBoundsReferenceTests.cs), [source/clip contexts](Lokad.Svg.VisualTests/SvgFilterSourceContextReferenceTests.cs). |
| Integrity | Check source/reference hashes, capture context, licensing, corpus membership and provenance independently of renderer output. | [Fixture manifests](Lokad.Svg.VisualTests/FixtureManifestTests.cs), collection manifests and capture recipes. |

The 96-case API matrix uses representative inputs reaching different renderer and
preparation paths. Preserve its distinct routes and the separate governed-output
contracts; do not repeat the complete matrix for every native image. Equal output
images alone do not make two cases redundant. Preserve failing-before regressions,
source/control roles and known interactions.

`Category=Research` is independent of `Layer`. Broad exploratory products remain
individually selectable and execute in unfiltered qualification; keep known failing
interactions in routine coverage. Classification is incomplete, so layer-only
filters do not replace affected classes or full integration. Mixed classes can
contain mechanisms, contracts, renderings and integrity checks.

Use small shared asset/lifecycle helpers where they clarify ownership. Keep
shared versus separate allowances, partial-work cancellation, borrowed snapshots,
prepared documents and destination owners explicit. Preserve individual case
identities and assertions; batching checks is not a coverage simplification.

| Work area | Coverage to retain |
| --- | --- |
| Color/filter changes | Direct numerical/admission tests, affected native banks and ownership paths. The [edit-loop profiles](Research/release-baselines/test-responsibilities.md#color-and-filter-edit-loops) select useful development subsets; complete bank integration precedes promotion. |
| Filter dependency/capture changes | [Graph demand](Lokad.Svg.Tests/SvgFilterGraphDemandTests.cs), [input storage](Lokad.Svg.Tests/SvgFilterInputStorageTests.cs) and source-context references keep metadata validation, pixel demand, source bounds and storage separate. |
| Text changes | Direct Unicode/font/layout mechanisms, representative acquisition/storage contracts and canonical native documents. Preserve source addresses, node/paint/font boundaries, complete mark clusters and path/suffix interactions. [Text acceptance](Research/release-baselines/text-layout-acceptance.json) and the [claim trace](Research/release-baselines/support-claim-map.md) identify their evidence. |
| Scene/resource changes | Keep declaration, live instance, dormant source and resource-context roles distinct. Native comparisons do not replace refusal/cancellation/cleanup contracts. |
| Harness/corpus changes | Preserve discovered case membership, source/options/reference identities, platform differences and fresh-output dependencies. Measure discovery/copy/execution costs separately from renderer performance. |

The [responsibility map](Research/release-baselines/test-responsibilities.md) owns
detailed partitions and measured validation costs. The [evidence index](Research/release-baselines/README.md)
owns completed implementation/qualification records; individual collection
READMEs and manifests own fixture purposes and exclusions.

## Development checks

The pinned .NET 11 RC SDK is the default. Install both .NET 11 and .NET 10 runtimes;
no check relies on implicit major-version roll-forward. After a Release build:

```powershell
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgRootParserTests
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgAlphaBoundaryMechanismTests
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgInertPaintTests
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter '(Layer=Mechanism|Layer=Contract)&Category!=Research'
dotnet test tests/Lokad.Svg.VisualTests -c Release --no-build --filter 'FullyQualifiedName~PixelComparerTests&Layer=Mechanism'
dotnet test tests/Lokad.Svg.VisualTests -c Release --no-build --filter 'FullyQualifiedName~SvgPaintInputReferenceTests&Layer=Rendering&Category!=Research'
```

The parser metadata selection excludes [rendering](Lokad.Svg.Tests/SvgRootRenderingTests.cs)
and [ownership](Lokad.Svg.Tests/SvgRootParserOwnershipTests.cs); include both for parser
integration. Start with the affected mechanism/class and follow with its rendering
bank and ownership paths. `Category!=Research` excludes only explicitly selected extended
matrices; other browser-backed research references remain routine.
The [color/filter profiles](Research/release-baselines/test-responsibilities.md#color-and-filter-edit-loops)
provide narrower edit loops while preserving full bank and ownership qualification.
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

Visual tests read the committed `Research/` corpus in place. Set
`LOKAD_SVG_RESEARCH_ROOT` to another corpus containing `manifest.json` when needed;
invalid explicit roots fail, and tests only read the selected corpus. Small core
`TestData` subsets and explicit fixture/reference links remain separate.

When changing asset-copy rules, verify fresh output directories: incremental
files can hide missing dependencies. The [core asset audit](Research/release-baselines/core-output-assets.json)
and [text-paint output audit](Research/release-baselines/text-paint-output-assets.json)
record the current consumer boundaries. Keep font licenses and shared inputs.

Use the [visual-suite export](../tools/README.md#export-the-visual-suite) for a
standalone offline directory containing binaries, references, font licenses and
the managed font host. Ordinary checks use committed assets without downloads
or a live browser. Browser-tool smoke/cleanup qualification is separate from
renderer acceptance.

| Source of truth | Responsibility |
| --- | --- |
| [Fixture manifest](Fixtures/manifest.json) | Original CC0 inputs, independent support/fidelity statuses and reference identities. |
| [Research index](Research/README.md) and [manifest](Research/manifest.json) | Feature collections, expected failures and measured differences; a reference alone does not imply support. |
| [Upstream documents](Research/upstream-static-documents/README.md) | Complete-document qualification with explicit fonts and platform references. |
| [Reference provenance](References/README.md) | Independent captures and retained export evidence with current consumers. |
| [Text fragments](Research/text-node-boundaries/README.md), [instance composition](Research/text-instance-composition/README.md) | Node/shaping/fitting witnesses and explicit nested-viewport exclusions. |
| [Public fonts](Research/public-font-qualification/README.md), [font-host tests](Lokad.Svg.FontProbe.Tests/) | Supplied-program acquisition, independent metrics and actual host execution. Private deployment acceptance remains separate. |
| [Admission/cancellation map](Research/release-baselines/parser-validation.md) | Structural/numerical boundaries and real ownership contracts. |

Collection manifests/READMEs own case identities, options, capture roles, exclusions
and licenses. Historical totals, percentage gates and exploratory quotas are not
completion criteria. Keep source-unqualified observations distinct from fidelity.

The [hosted SVGWeb procedure](../tools/README.md#hosted-svgweb-inventory) and
[original ledger](Research/svgweb/originals.json) retain each actual context.
Ordinary tests do not fetch that collection. Keep hosted bodies/fragments in
bounded RAM; retain hashes, results and authored orthogonal fixtures rather than
vendoring the original corpus.

## Retired collections

The [upstream static documents](Research/upstream-static-documents/README.md) own
complete-document qualification. Other collections retain mechanisms, interaction
regressions and font/API/ownership contracts under their current manifests.
Retained SVGs and PNGs do not confer full-document membership; keep retired
document gates and historical totals out of current acceptance criteria.

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

The frozen Windows DejaVu assertion remains an open reference-authority conflict;
the [current source-bound qualification](Research/release-baselines/text-feature-dispatch-contexts.json)
records it separately. Keep the target and assertion until that authority is
resolved; a retained known failure does not make a qualification run green.
