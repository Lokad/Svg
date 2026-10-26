# Windows font-unit scaling

Two original CC0 static TrueType fonts isolate initial Y points, initial CVT
values, WCVTF writes and original MD distances. Fractional.ttf uses UPEM 1000
and coordinates ±714 at 11.5 px; Half.ttf uses UPEM 1024 and ±1 at 8 px.
Each includes positive/negative programs and two identity composite controls.

The glyph programs read the selected value, amplify each 26.6 coordinate unit
to a whole pixel, and draw a four-pixel band. The twenty
[independent vectors](native-vectors.json) come from DirectWrite with explicit
font files, enabled fitting and symmetric ClearType. Native nonzero bitmap
bounds encode the value without needing internal native outline access.

Windows points, distances and vertical-dominant control tables retain
floating-point precision: 714 at 11.5 px becomes 526 coordinate units;
premature 16.16 scaling produces 525. Signed half units round toward positive
infinity. The offline tests check both band edges, unchanged source bytes and
released storage.

A larger horizontal strike initializes its control table with a 16.16 scale
and rounds signed halves away from zero. Reads quantize the projection-to-table
ratio to 16.16; writes divide by that same ratio. WCVTF uses the table's axis
independently of the current projection. The [control-value tests](../../Lokad.Svg.Tests/TrueTypeHintingTests.WindowsControlValues.cs)
retain native band observations for both axes, compression and signed values.

These fixtures protect shared fitting semantics. Complete font sheets retain
their separate selection, independent metric and exact image acceptance gates.
All fixture assets and vectors are [CC0](LICENSE.txt).
