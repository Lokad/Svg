# Shader hairlines

The [capture](capture.json) retains 40 independent browser references, including
24 wide-seam inputs, with three original captures per image. Existing rendering
tests preserve their source, placement and exact pixels.

The [combined review](../rectangle-shader-frames/admission-review.json) joins every
original wide-seam source and output scale to these unchanged references. Scale
1.5 uses an explicit root size and viewBox for browser capture; the original
conversion keeps its output-scale option. Both original outputs remain exact.

The earlier offscreen SVG-image/canvas targets differed on these inputs and were
rejected during the original qualification. Their hashes remain observations in
the review; they do not replace the independent browser references.
