# Tests and provenance

Use focused checks while editing, affected integration before promotion, and full
Windows/Linux/package qualification at coherent milestones. The
[release baseline](Research/release-baselines/README.md) identifies retained source,
package and reference evidence; the [support matrix](../docs/SUPPORT_MATRIX.md)
defines supported behavior.

## Responsibilities

| Layer | Purpose and representative tests |
| --- | --- |
| Mechanism | Parsing, numerical boundaries, geometry, shaping and rounding directly. Keep useful exhaustive boundaries here: [alpha](Lokad.Svg.Tests/SvgAlphaBoundaryMechanismTests.cs), parser, OpenType and filter-stage tests. |
| Contract | Distinct execution, allocation and ownership paths: the [96-case API matrix](Lokad.Svg.Tests/SvgConversionApiContractTests.cs), [shared admission](Lokad.Svg.Tests/OpenTypeFontTests.SharedMemory.cs), retained font/scene data, cancellation, refusal, cleanup and retry. |
| Rendering | Each source/options/platform case keeps its independent native image. The [paint bank](Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs) compares production capture pixels directly; selected complete PNG cases retain transport interactions. |
| Integrity | Manifest identities, hashes, capture context, licensing, corpus partition and provenance. Existing combined fixtures retain these assertions until their responsibilities are separated. |
| Research | Extended numerical-context products remain individually selectable with `Category=Research` and run in full qualification. Former failing interactions remain routine. |

The [responsibility map](Research/release-baselines/test-responsibilities.md)
records paint-bank membership, lazy loading, raster/PNG separation and measured
validation costs, including the core pixel helpers, API fixture allocation and
the [inert-paint split](Research/release-baselines/test-responsibilities.md#inert-paint-rendering-and-prepared-contracts).
Equal images alone never establish redundancy. Preserve distinct
source graphs, allocation paths, native companions, platform differences and
exclusions. Rendering fixtures do not each need all twelve API forms.

Migrated cases use consistent `Layer=Mechanism`, `Layer=Contract`,
`Layer=Rendering` and `Layer=Integrity` traits. Classification is incomplete;
a layer filter does not replace affected unclassified tests or full qualification.

## Development checks

The pinned .NET 11 RC SDK is the default. Install both .NET 11 and .NET 10 runtimes;
no check relies on implicit major-version roll-forward. After a Release build:

```powershell
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgAlphaBoundaryMechanismTests
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgInertPaintTests
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter 'Layer=Mechanism|Layer=Contract'
dotnet test tests/Lokad.Svg.VisualTests -c Release --no-build --filter 'FullyQualifiedName~SvgPaintInputReferenceTests&Category!=Research'
```

Start with the affected mechanism/class and follow with its rendering bank and
ownership paths. `Category!=Research` excludes only explicitly selected extended
matrices; other browser-backed research references remain routine.

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
