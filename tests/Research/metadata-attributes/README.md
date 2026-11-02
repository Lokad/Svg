# Inert attributes

The original CC0 [source](descriptive-titles.svg) retains aliased XLink titles on
the root, definitions, a gradient, source geometry, group and local use. Some
titles resemble style declarations; they remain descriptions. The local reference
and paint still resolve normally.

The [manifest](capture.json) preserves three agreeing healthy native captures per
platform, source/browser/reference hashes and cleaned-profile receipts. Identical
platform PNGs share storage. The [rendering test](../../Lokad.Svg.VisualTests/SvgMetadataAttributeReferenceTests.cs)
uses one canonical conversion with exact pixels.

[Mechanism tests](../../Lokad.Svg.Tests/SvgForeignMetadataTests.cs) exercise the
common attribute policy and separate definitions validator directly, preserve the
reader position and keep other reserved attributes rejected. Existing traversal,
font preparation, structural accounting and ownership cases retain their separate
responsibilities. [Original outcomes and validation](../svgweb/metadata-attributes.json)
record the two hosted sources without retaining either SVG payload.

The CC0 [event control](inert-events.svg) retains static CSS, local references,
gradient paint, clipping, a nested viewport and a filter while carrying event
attributes on their validators. Its native image is frozen independently before
admission changes, with three healthy captures per host in the same manifest.
The event rendering test uses one canonical conversion. Direct
[validator cases](../../Lokad.Svg.Tests/SvgEventAttributeTests.cs) cover reader
position, literal names, reserved namespaces and filter/merge admission. One
[prepared-font contract](../../Lokad.Svg.Tests/OpenTypeFontTests.TraversalMetadata.cs)
protects span/path discovery, one font acquisition and replay. No per-fixture API
or ownership matrix is added. [Hosted-original validation](../svgweb/inert-events.json)
separates closed static event admission from remaining subtree and rendering gaps.
