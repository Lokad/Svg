# Image fitting and sampling

`meet-portrait.svg` reproduces two differing edge pixels on Windows and Linux.
`intrinsic.svg` paints the same authored PNG at its intrinsic size and is exact.
[Capture evidence](capture.json) freezes three healthy, identical native captures
per case and host. This is open failing-before research, not a supported baseline.

The 8×6 RGBA source is generated from the
[documented pixel formula](../svgweb/remaining-static-gates.json). Both SVGs are
independently authored and use the repository license; neither contains hosted
SVG geometry or an external resource.

Next isolate composed image coordinates, scanline seeding and fractional-edge
coverage in direct mechanisms. Keep the intrinsic control, reference bytes and
exactness unchanged; use canonical rendering rather than an API cross product.
Follow the [capture rules](../../../tools/README.md#capture-and-reference-rules).
