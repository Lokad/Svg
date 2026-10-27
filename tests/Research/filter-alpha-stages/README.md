# Retained source-alpha stages

Fifteen inputs keep their own Windows and Linux browser images. Seven fail before
the retained source-alpha correction; eight protect independent positive paths.
[Capture metadata](capture.json) records source/reference hashes and three identical,
healthy fresh-process captures per input. See the [capture procedures](../../../tools/README.md).

Source alpha retains a color-filter shader through effective-zero blur, one-node
merge and zero-offset aliases. Its float sampling differs from a materialized
image, even when the stored pixels match. The materialized-blur control has 27
different native alpha pixels and retains its separate reference and packed path.
Consecutive fractional nearest transforms can also materialize an image; direct
mechanism checks retain that boundary through intervening aliases.
Other controls cover linear RGB, strokes, reflection, a backdrop, use and pattern
replay. These controls do not establish general affine crop or offset equivalence.

The [canonical rendering tests](../../Lokad.Svg.VisualTests/SvgFilterStageReferenceTests.cs)
compare every input with its own image. Direct stage-admission checks and one
selected pattern ownership/replay input protect their separate responsibilities;
the rendering cases do not repeat the API matrix.
