# Filter region coordinates

The [capture manifest](capture.json) retains unchanged Chromium 153 PNGs from
three healthy, stable processes per authored input and platform. All captures
use the authored 32-pixel viewport directly. Each of the 65 admitted inputs has
its own native reference and one canonical conversion test; authored bytes and
options are distinct even where the resulting PNGs share storage.

The [coverage index](coverage-index.json) accounts for all sixteen original
own-filter discrepancies and 49 additional admitted controls. Ten deliberately
restricted source inputs retain their numerical error codes and messages; their
native images do not imply admission. The [baseline](before-correction.json)
records 54 failing admitted inputs and eleven exact controls before correction.

Fixed filter lengths and translate arguments resolve within the native CSS
length range before placement. Matrix coefficients and percentages retain their
own domains. Authored object boxes survive scene placement, including nonzero
boxes whose placed float endpoints coincide. Primitive regions use the padded
filter bitmap's map; source drawings and sampled-image transforms keep their
own recording stages. Both filter and primitive unit systems are covered.

Source presence controls retain floods over transparent or remote input.
Wide, signed and near-boundary regions, explicit primitive subregions, matrix
versus translate placement, unfiltered geometry and normalized object regions
protect the shared conversion model. Existing parent-bitmap and viewport-offset
regressions remain independently asserted in the symbol-instance suite.

Direct [mechanisms](../../Lokad.Svg.Tests/SvgFilterLengthTests.cs) cover numeric
domains and authored object boxes. One selected
[ownership witness](../../Lokad.Svg.Tests/SvgFilterRegionOwnershipTests.cs)
covers retained bounds, padded storage, refusal, cancellation, cleanup and retry.
The general API matrix supplies destination and prepared-document contracts.
Marker placement has separate [canvas-stage controls](../marker-canvas-stages/README.md).
