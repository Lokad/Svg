# Hosted SVGWeb review

[Originals](originals.json) accounts for all 204 direct `.svg` entries in the
[hosted directory](https://dev.w3.org/SVG/tools/svgweb/samples/svg-files/): URL,
source hash/size, root declarations, lexical font/resource use and platform outcomes.
Bodies are fetched into bounded memory and discarded. Ordinary tests are offline;
this directory contains metadata and original orthogonal controls, not the hosted
SVG collection.

The current default-path review has 147 rendered inputs per host: 132 exact and
15 different, with 57 rejected. The shared filter-image replay took 59.66 s;
all prior exact images remain exact. Of 146 prior renders, 145 PNGs are unchanged
and `car.svg` improves. `scimitar.svg` newly renders but remains inexact.
These observations use transparent output without supplied fonts/resources; they
are neither whole-corpus reference admission nor throughput measurements. The
ledger retains earlier passes and every follow-up outcome.

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
Six CC0 references have independent platform triples. The full filter/root
milestone remains open; exploratory original outcomes are recorded separately.

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
