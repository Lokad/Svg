# Current text paint precision review

All [39 retained originals](../text-paint-bounds/precision-residuals.json) now match
both hosts exactly. [Current results](precision-current.csv), the
[preceding results](precision-before-source.csv) and
[qualification](source-paint-qualification.json) preserve their original sources,
fonts, options and platform targets. No reference or tolerance changed. This closes
the retained precision cohort; broader node/anchor/phase, shader and decoration
review remains open.

Text paints keep reference, physical font and fragment drawing stages separate.
Source boxes form physical float rectangles before rotating their corners. Pattern
tile origins join the local transform before float narrowing; picture-image scale
is appended afterward. These shared stages serve fills and strokes without changing
layout, fitting, contour ownership, sampling or final alpha rounding.

The final correction closes Windows 224-control and Linux 232-control while
preserving the original 233 pair at output scale 1.5. The source box is independent
of that output density. One direct numerical mechanism and five original image
cases protect the box, radial/pattern consumers, density and opacity interactions.
Both failing images and the mechanism fail against the preceding qualified library.
The four canonical image banks together cover every original. Earlier failing-before
cases and independent font-free drawing controls remain individually selectable.

The original [234 native comparisons](precision-native-verification.json) and
[physical-box triples](fragment-drawing-qualification.json) remain the authority.
The two-style font graph, platform differences and all previous test IDs are retained.
Drawing metadata still uses the existing pre-allocation admission/transfer/refusal/
cleanup/retry contract; this correction adds no retained storage or owner.

Focused checks pass 178 core and 582 rendering/provenance cases on both runtimes
and hosts. Full .NET 11 passes 47,550 core cases per host and all 46,706 Linux
rendering cases. Windows passes 46,705 and retains its unchanged DejaVu conflict.
All 204 default outcomes/diagnostics and 168 rendered PNGs are unchanged;
22 supplied-font contexts and 13 declared static diagnostics remain exact.
Matching committed-source runtime/symbol packages reproduce byte-for-byte and pass
installed consumers on both runtimes and hosts. Matching CI passes the full Ubuntu
workflow. Windows retains only DejaVu; its later steps are skipped. The run is
terminal and closed.
Validation timings in qualification are functional costs, not renderer benchmarks.
