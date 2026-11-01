# Nested filter placement

Seven original CC0 controls retain three agreeing healthy, profile-cleaned browser
captures per host in the [manifest](capture.json). Identical platform references
share storage. No existing reference or tolerance changed.

The [canonical rendering tests](../../Lokad.Svg.VisualTests/SvgNestedFilterPlacementReferenceTests.cs)
cover five exact cases individually. Isolation bounds and replay share the placed
child filter. Each nested filter retains its own parameter-derived bitmap bounds
while borrowing its parent's affine placement; its source origin governs sampling.

| Controls | Current result on each host |
| --- | --- |
| Translation and affine solid colour | Exact |
| Affine gradient: linearRGB, sRGB, no source opacity | Exact; formerly 18, 26 and 19 differing pixels |
| Large-coordinate gradient | Open: one colour pixel; alpha matches |
| Fractional viewport with nested copying kernels | Open: 565 differing pixels, including 486 alpha differences |

[Six direct mechanisms](../../Lokad.Svg.Tests/SvgFilterSourceMapTests.cs) protect the
recorded map, translated-child coefficients and four one-ULP changes that must
retain general composition. They do not snap small rotations or shears. Existing
filter/opacity ownership tests cover cancellation, refusal, cleanup and retry;
the controls use one conversion path each.

The two open inputs keep independent references without exact admission. The
fractional case uses a 128.3×96.2 logical image viewport and a 128×96 screenshot.
[Placement evidence](../svgweb/nested-filter-placement.json) and
[source-scope evidence](../svgweb/filter-source-scope.json) retain failing-before
results, current original-scene counters and qualification limits.
