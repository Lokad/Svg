# Static image admission

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

The CC0 [script control](inert-scripts.svg) protects non-executing script bodies
across source definitions, filters, geometry, nested viewports, switches and pattern
inheritance. It uses one canonical conversion against six frozen native captures.
[Direct script mechanisms](../../Lokad.Svg.Tests/SvgStaticScriptTests.cs) keep
absolute depth, attributes, node counts, namespaces, reader position and malformed
XML separate from pixels. Representative live/retained/instanced text contracts
retain font acquisition, ignored-character accounting and the original node charge;
the existing cancellation test also covers script consumption. Element children
remain unsupported. [Original outcomes and qualification](../svgweb/inert-scripts.json)
record the unmodified hosted source without retaining its payload.

The CC0 [CSS control](static-css-state.svg) retains inactive hover/active/focus
rules, a mixed selector list, an unused external URL and root/group float. One
canonical conversion matches six native captures frozen before implementation.
[Direct mechanisms](../../Lokad.Svg.Tests/SvgStaticCssTests.cs) separate selector
syntax/specificity, bounds, inactive storage and style-error locations. The
representative font contract preserves one acquisition and prepared replay;
snapshot admission retains its own acceptance and rejection-before-acquisition
cases. [Native outcomes and shared-model review](../svgweb/static-css.json)
close two hosted float cases and keep geometry lengths/root CSS box paint open.
