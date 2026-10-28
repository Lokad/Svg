# Marker canvas stages

The [capture manifest](capture.json) retains 37 independent inputs with unchanged
native PNGs from three healthy, stable processes on each platform. Each actual
uses its own reference and one canonical conversion. Source/options remain
distinct where PNG storage is shared. Output-scale captures keep the original
source viewport inside an outer viewport, preserving its percentage bases.

The [coverage index](coverage-index.json) retains all sixteen original point-list
marker failures, four direct/group/content/overflow diagnostics and seventeen
interactions. These cover signed large coordinates, root and nested viewports,
use/group replay, fractional reference points, marker viewBox, angle/auto/reverse
orientation, content/source transforms, inherited markers, gradient content,
source/instance opacity and output scaling. The [baseline](before-correction.json)
records 34 failing inputs and three exact controls before correction.

Native canvas stages belong to drawing, independently of clipping and paint kind.
Local rectangle contours survive placement; deferred shaders retain the same
construction contract. Rectangle coverage consumes the prepared canvas map.
Fill and stroke retention remain separate, preserving ordinary copied-shader
admission while marker fills retain their native source contours.
Allocation bounds enclose ink, while physical clips retain their own rounding.
Finite metadata maps preserve inverse stroke-unit intermediates; device admission
belongs to the completed draw map. Culled marker content drops inner draw scopes
so enclosing source-independent effects remain eligible.

Direct [mechanisms](../../Lokad.Svg.Tests/SvgMarkerCanvasTests.cs) cover native
concatenation and complete-map admission. Two selected
[ownership witnesses](../../Lokad.Svg.Tests/SvgMarkerCanvasOwnershipTests.cs)
cover source-opacity and deferred-shader allocation paths, refusal, cancellation,
cleanup and retry. Existing non-scaling-marker tests retain the tiny resource
and culled-flood interactions; the general API matrix covers destination and
prepared-document contracts. No reference is regenerated from library output.
