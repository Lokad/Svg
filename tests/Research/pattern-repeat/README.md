# Picture repeat boundaries

The own CC0 [control](inputs/repeat-edge.svg) uses one rectangle and one horizontal
stroke in a rotated child pattern. Fractional tile dimensions expose a floating
repeat boundary. The [manifest](capture.json) retains three agreeing healthy,
profile-cleaned native captures per host, frozen before candidate comparison.

The [canonical rendering test](../../Lokad.Svg.VisualTests/SvgPatternRepeatReferenceTests.cs)
compares exact pixels through one conversion. Fifteen
[mechanism cases](../../Lokad.Svg.Tests/SvgPatternRepeatAddressTests.cs) exercise
positive/negative coordinates, half-pixel neighbors, independent taps and gather
clamping directly against [compiled native vectors](../svgweb/pattern-repeat-native.csv).
Existing glyph-pattern cases protect the separate fragment drawing transform;
their source, platform references and assertions remain intact.

[Closure evidence](../svgweb/pattern-repeat-addressing.json) records the complete
metal-effect original separately. No hosted SVG geometry is retained. Use the
[capture and native-vector procedures](../../../tools/README.md).
