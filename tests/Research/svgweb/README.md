# Hosted SVGWeb review

[Originals](originals.json) accounts for all 204 direct `.svg` entries in the
[hosted directory](https://dev.w3.org/SVG/tools/svgweb/samples/svg-files/): URL,
source hash/size, root declarations, lexical font/resource use and platform outcomes.
Bodies are fetched into bounded memory and discarded. Ordinary tests are offline;
this directory contains metadata and original orthogonal controls, not the hosted
SVG collection.

The current default-path review has 148 rendered inputs per host: 132 exact and
16 different, with 56 rejected. The latest black-filter stage replay preserves every
prior exact image and 146 PNGs byte-for-byte. Car's total channel error falls from
30,023 to 13,924; Gallardo's falls from 15,845 to 359 (204 differing pixels).
Outcomes and source/font diagnostics remain unchanged. These transparent,
unsupplied-font/resource observations are exploratory, not whole-corpus admission
or throughput measurements.

| Initial first blocker | Originals | Current responsibility |
| --- | ---: | --- |
| Gradient-stop `id` | 13 | Admission fixed, with direct identifier boundaries and an exact linear/radial/inherited-stop fixture. Eight originals render inexactly; five expose filter, pattern or color gates. |
| Fixed units in stroke offsets | 3 | Share a length model; keep units separate from dash geometry and rounding. |
| Other gradient declarations | 5 | Partial radial inheritance, percentages, defaults, unit overrides and explicit paint fallback are fixed. `twitter.svg` is exact on both hosts; invalid attributes/units remain individually classified. |
| Required fonts | 9 | Supply programs explicitly; distinguish SVG-font previews from font-free geometry. |
| Root events | 10 | Qualify static state and document actual interaction limits. |
| Unsupported children | 11 | Inspect parent/traversal state before attributing the failure to the reported child. |
| External raster content | 3 | Bind actual resources explicitly and qualify host materialization. |
| Other boundaries | 14 | Retain CSS, clip composition, authoring attributes, XML and duplicate-ID diagnostics. |

Counts classify the initial first blocker. Removing one can reveal another.
Parsing success, ambient font substitution and unexplained pixel differences do
not close a gap. Font declarations are lexical inventory, not resolved requests;
resource roles distinguish navigation from image/CSS/media dependencies.

The [filter-image model](filter-image-model.json) closes retained path boxes,
materialized-image crop/padding and alias sampling through affine restoration.
Seven CC0 references have independent platform triples. Empty affine destination
tiles skip capture and edge preparation while intersecting tiles retain painting.
The [bitmap restoration record](filter-bitmap-restoration.json) retains the original
opacity and unit-blur targets, now canonical regressions, plus independent opaque
and enclosing-group controls. The full filter/root milestone remains open;
rendering successfully does not admit an original as exact.

The [source-capture record](filter-source-capture.json) retains three CC0 native
platform triples for copying/three-box blur and untiled image padding, plus a
translucent-flood mechanism for exclusive bitmap interiors. Construction padding
is separate from captured content and nested requests; seven existing crop,
rescaling and nested-effect targets remain exact. Full core/native qualification
includes the broad symbol/reference bank on both hosts and supported runtimes.

The [referenced-clip record](clip-use-construction.json) closes substitution of
flattened chords for copied authored clip paths. Three failing-before mechanisms
protect quadratic/cubic/arc verbs and fractional placement; two canonical native
platform triples protect plain clipping and a colored filter interaction. All 35
Gallardo filter consumers are mapped individually. Their isolated results and
the complete original remain separate acceptance responsibilities.

The [black-filter stage record](black-linear-stages.json) closes the content-dependent
shortcut that discarded linearRGB shader rounding for black input. One canonical
96x64 native platform triple protects backdrop composition; a retained independent
1x1 triple supplies the direct numerical expectation. One black case extends the
existing ownership checks. The redundant RGB scan is removed; color, alpha and
transport coverage retain their separate responsibilities.

The [radial source-binding record](radial-source-binding.json) closes loss of local
gradient transforms when axis-resolved paints bind to skewed shapes. Four canonical
controls preserve direct, clipped, inherited-stop and translated path placement with
native platform triples. Axis and skewed paths share source binding; the former
translated-axis research witness and target moved unchanged into canonical coverage.
Mechanisms protect definition coordinates, inverse mapping and local/reference separation.

The [paint-reference record](paint-references.json) maps four CC0 controls to native
platform triples. Paint fallbacks share lexical parsing and completed resource
availability across lowering, retained content and font selection. Empty content
and an unavailable server remain distinct. The
[filter-source review](filter-source-review.json) closes the apparent blank-text
witness as missing bounds in its synthetic test font. Eight valid-font controls
are independently exact on each host. These single-process exploratory captures
preserve platform differences and do not constitute new reference admission.

The [gradient-length record](gradient-lengths.json) maps five original CC0 fixtures
to independent native platform references. Typed lengths retain number/percentage
intent and missing/default state through inheritance. Definitions and nested aliases
resolve in their own viewports before placement; unit overrides reinterpret the
inherited lengths, while cross-kind templates use their own coordinate defaults.
Mechanisms protect all coordinates, float boundaries, ownership and invalid focal
circles; most rendering uses the canonical conversion path. A native DOM probe
pins an intermediate radius that equal small images alone could not distinguish.

The earlier [radial inheritance fixture](../../Fixtures/original/radial-coordinate-inheritance.svg)
protects per-field overrides and changed-source ownership; `intertwingly.svg` is
exact against three fresh healthy captures on each host. The
[stop-identity fixture](../../Fixtures/original/gradient-stop-identities.svg) protects
stop metadata independently. Every existing target remains unchanged.

[Capture calibration](capture-model.json) retains independent image/Blob,
background and viewport contexts. Match the logical image viewport separately
from screenshot dimensions, preserve original bytes/options, and label any static
state or resource materialization. Scripts are inactive in image documents;
animation, ambient fonts and unresolved resources still need explicit qualification.

The [root viewport investigation](root-viewport-review.json) remains research.
An output-only projection improved exploratory matches but conflicts with uniform
fitting and retains a painted viewBox counterexample. Preserve layout, fitting and
all targets while reviewing container layout and image projection as shared stages.

Group confirmed gaps by mechanism and retain original-to-fixture mappings,
licenses and failing-before evidence. Reference admission requires three fresh
healthy captures per platform and exact established comparisons. Recheck all
originals after shared fixes. The
[tool procedure](../../../tools/README.md#hosted-svgweb-inventory) reproduces the
inventory without retaining SVG files.
