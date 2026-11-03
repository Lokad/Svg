# Image fitting and sampling

The qualified renderer matches all three native controls on both hosts and runtimes.
[Qualification](qualification.json) records full, compatibility and package checks;
[capture evidence](capture.json) retains the original inputs and three healthy,
identical native captures per case and host.

| Input | Responsibility |
| --- | --- |
| `meet-portrait.svg` | Non-integer root projection and image meet fitting; formerly two differing edge pixels per host. |
| `intrinsic.svg` | Same decoded source at intrinsic scale; keeps decoding/premultiplication separate from fitted sampling. |
| `wide.svg` | Previously exact wider placement across bordered compositor bitmaps and coordinate-buffer spans. |

Rectangle dispatch uses 100-pixel calls for fractional or single-row spans,
including coverage rounded to 255. Bitmap coordinate buffers independently hold
127 filtered or 254 unfiltered samples. [Direct mechanisms](../../Lokad.Svg.Tests/SvgRectangleBitmapSpanTests.cs)
check 85 compiled-native span boundaries. Three canonical rendering cases protect
the actual sampling interactions; existing image ownership tests retain cleanup,
refusal and retry without repeating those contracts for each fixture.

The 8×6 RGBA source is generated from the
[documented pixel formula](../svgweb/remaining-static-gates.json). These SVGs are
independently authored and use the repository license; neither contains hosted
SVG geometry or an external resource.

The [standalone native producer](native-spans.cpp) and [vectors](native-spans.json)
separate span construction from rendering and preserve the original references.
Follow the [capture rules](../../../tools/README.md#capture-and-reference-rules).
