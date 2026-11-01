# Filter source opacity

These own CC0 scenes isolate source layout, recording and bitmap restoration;
they contain no hosted SVG content. The [manifest](capture.json) records source and
PNG hashes, viewports, and three agreeing healthy, profile-cleaned captures per host.
The [canonical tests](../../Lokad.Svg.VisualTests/SvgFilterSourceOpacityReferenceTests.cs)
compare exact pixels through one conversion path per case.

| Controls | Responsibility and evidence |
| --- | --- |
| Fractional and integer thin curves | [Source bounds](../svgweb/filter-source-opacity.json) and [native device-axis consumer selection](../svgweb/group-opacity-stages.json). |
| Coloured stroke and blurred gradient | Separate source recording from the [enclosing opacity bitmap](../svgweb/filter-opacity-device.json), including copying and nontrivial kernels. |
| Gradient-stroke blur | Reuse [retained parameter visuals](../svgweb/filter-stroke-source-bounds.json) when preparing the source bitmap. |
| Opacity parent | Construct the filter source in its [enclosing opacity bitmap](../svgweb/filter-opacity-source-origin.json); source preparation and restoration must use the same parent. |
| Hidden instance | [Display contracts](../svgweb/filter-instance-display-bounds.json) distinguish suppression from zero opacity and hidden visibility. |
| Fractional stroke layout | Map [fractional layout visuals](../svgweb/filter-fractional-stroke-layout.json) through a rotated alias while retaining per-draw recording enclosure. |

The [wide control](inputs/thin-curved-source-wide.svg) remains **open** at four
one-level alpha differences on both hosts. Its large viewport and outer placement
expose a boundary absent from the small cases; direct-source painting is exact.
Its admitted reference supports investigation, with a passing rendering assertion
to follow closure. The complete hosted original also remains open.

Use the [capture procedure](../../../tools/README.md#hosted-svgweb-inventory).
Preserve native reference bytes and exact comparisons.
