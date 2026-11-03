# SVG image width and dashes

These authored inputs contain the same line, colour and dash pattern. Only the
outer width changes from 40 to 600. Both use a 384-pixel height at density 1.

The library preserves the shared 40×384 region exactly. Pinned native SVG images
change sixteen pixels there on both hosts. [Capture evidence](capture.json)
retains three healthy processes per input/host and the unchanged native PNGs.
These remain research controls; the width-dependent stage is unresolved.

Compare DOM geometry, SVG-as-image viewport mapping and compositor screenshots
before changing dash interpolation. The reference driver screenshots a block
`img`; canvas `drawImage` is a separate pipeline. Preserve both width contexts.

The [ny1 diagnostic](../svgweb/ny1-static-review.json) isolates its twelve residual
pixels to one line, after removing the other 99 graphic elements. These controls
contain no hosted geometry, fonts or external resources. Inputs use the repository
license. Follow the [capture procedure](../../../tools/README.md#capture-and-reference-rules)
with matching physical and image-viewport dimensions.
