# Pattern source selection

These 54 inputs retain three stable, independent Chrome captures per Windows and
Linux host. The [capture metadata](capture.json) binds each SVG, supplied
[Noto Sans font](../supplied-font-labels/README.md) and deduplicated PNG reference.

The controls cover dormant definitions, overridden channels, switches, zero-size
instances, masks and markers, inherited image paint, empty strokes and live
sources copied outside suppressed ancestors. Closed zero-length paths retain
their round-cap paint dependencies.

[Image tests](../../Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs) verify
native pixels and source/font hashes. [Dependency and ownership tests](../../Lokad.Svg.Tests/SvgPatternFontReachabilityTests.cs)
check provider counts, font-free rendering of dormant sources, prepared output,
denial, cancellation, cleanup and retry.
