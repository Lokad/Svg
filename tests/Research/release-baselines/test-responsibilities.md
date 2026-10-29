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

P2 still needs suite-wide classification, separation of bulk raster
comparison from PNG transport, and review of remaining repeated fixture work.
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
