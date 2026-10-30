# Polygon contour ownership

[The contour payload](../../../src/Lokad.Svg/Parsing/SvgPolygonContours.cs) owns
points, contour ends, winding signs and quadratic/cubic spans under one scene
charge. Solid, linear, radial and pattern bindings retain their own paint, mapping
and clip state. Repaint and stamping transfer the payload after construction;
radial paint retains a separate reservation. Empty pattern paint disables filling
while keeping the geometry payload intact. No reference counting or additional
heap owner is introduced.

On baseline `174f0135c`, four repaint routes reject an allowance sufficient for
their live arrays and radial paint, although no second contour allocation occurs.
The same four inputs pass with extra allowance. A fifth witness first refuses
radial paint without losing the source, releases the blocking allowance, then
fails on retry at the duplicate contour charge.

[The fourteen ownership regressions](../../Lokad.Svg.Tests/SvgPolygonContourOwnershipTests.cs)
retain those nine inputs and checks. They also assert one retained/peak charge,
array identity including analytic spans, empty-paint behavior, repeated stamping,
idempotent disposal and release after the final owner. Failed vector placement
preserves the existing argument-error contract, source storage and borrowed tile;
a valid retry succeeds without consuming the tile owner.

Existing radial factory tests retain separate paint/array refusal, caller-owned
reservation survival, partial lowering and publication cleanup. The general API
matrix and independent native rendering fixtures remain separate responsibilities;
this change adds no rendering/API cross product. Original references, numerical
tolerances, authored-path ownership and text-layout code are unchanged.

Qualification passes on Windows and Linux under both supported runtimes: 35,178
core cases, 45,988 visual cases and ten font-host cases per combination. The focused
ownership/budget selection passes 94 cases. Both solution builds are warning-free.
