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

The [original-matrix qualification](../release-baselines/r86-hairline-qualification.json)
closes the original 800 geometry and 200 alpha pairs: all 2,000 inputs are exact
through the packaged renderer and three fresh healthy browser processes per host.
The compact recipe regenerates every original source; frozen
[native vectors](original-native.csv.gz) retain independent Windows/Linux pixels.

`SvgShaderHairlineOriginalTests` exposes all 2,000 inputs individually as
`Layer=Rendering, Category=Research`, with one canonical conversion and exact
dimensions/pixels/source immutability per input. Routine checks retain 200 direct
alpha/coverage mechanisms, 12 ownership cases and the existing 40 browser images.
The separate integrity check validates the vector file and capture provenance.

For development, select `SvgShaderHairlineTests` in the core project and
`SvgShaderHairlineReferenceTests` in the visual project. Select
`FullyQualifiedName~SvgShaderHairlineOriginalTests&Category=Research` for the optional matrix;
`Category!=Research` excludes its rendering product. Full qualification includes it.
