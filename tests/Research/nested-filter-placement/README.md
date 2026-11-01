# Nested filter placement

Seven original CC0 controls retain three agreeing healthy, profile-cleaned browser
captures per host in the [manifest](capture.json). Identical platform references
share storage. No existing reference or tolerance changed.

The [canonical rendering tests](../../Lokad.Svg.VisualTests/SvgNestedFilterPlacementReferenceTests.cs)
cover seven exact cases individually. Isolation bounds and replay share the placed
child filter. Each nested filter retains its own parameter-derived bitmap bounds
while borrowing its parent's affine placement; its source origin governs sampling.
Original drawing preparation precedes the native capture transform, including
recursive captures and shader recording boundaries.

| Controls | Current result on each host |
| --- | --- |
| Translation and affine solid colour | Exact |
| Affine gradient: linearRGB, sRGB, no source opacity | Exact; formerly 18, 26 and 19 differing pixels |
| Large-coordinate gradient | Exact; formerly one differing colour pixel |
| Fractional viewport with nested copying kernels | Exact; formerly 565 differing pixels, including 486 alpha differences |

[Six direct map mechanisms](../../Lokad.Svg.Tests/SvgFilterSourceMapTests.cs) follow
[native arithmetic vectors](../../Lokad.Svg.Tests/TestData/native-source-stages.json),
including four one-ULP inputs. Two
[shader cases](../../Lokad.Svg.Tests/SvgRecordedShaderCoordinatesTests.cs) separate
64/65-stage storage and bitmap-origin cache changes. One
[recursive-capture ownership case](../../Lokad.Svg.Tests/SvgUniformFilterOwnershipTests.cs)
covers the additional replay allocation through refusal, cancellation and retry.
The images use one conversion path each.

The fractional case uses a 128.3×96.2 logical image viewport and a 128×96 screenshot.
[Placement evidence](../svgweb/nested-filter-placement.json) and
[source-scope evidence](../svgweb/filter-source-scope.json) retain earlier bounds
observations; [prepared drawing evidence](../svgweb/prepared-source-drawing.json)
records the drawing-stage fix. [Source-opacity evidence](../svgweb/filter-source-opacity.json)
governs current counters and qualification limits; the complete hosted original
and exploratory integer-viewport source variants remain open.
