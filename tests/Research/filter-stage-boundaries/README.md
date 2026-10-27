# Filter stage boundaries

These seventeen inputs retain their own Windows/Linux browser images. Fifteen
fail before the shared-stage correction; two preserve existing sRGB behavior.
[Capture metadata](capture.json) records source/reference hashes, three identical
healthy fresh-process captures, profile cleanup and platform pins. The
[baseline comparison](before-correction.json) identifies the earlier renderer
and each failing input. See the [capture procedure](../../../tools/README.md).

The cases separate requested output from scratch storage, cropped intermediate
images, pending versus materialized uniform color, and shader sampling from image
drawing. They cover zero-blur/merge/identity aliases, small-blur materialization,
cropped aliases, chained transforms and both operating color spaces. Compact and
expanded affine shadows retain independent, naturally unequal references. The
byte-identical expanded input shared by two investigations appears only once;
equal images from distinct inputs do not establish redundancy.

The [rendering tests](../../Lokad.Svg.VisualTests/SvgFilterStageReferenceTests.cs)
use canonical synchronous conversion. Twenty-four
[direct mechanisms](../../Lokad.Svg.Tests/SvgFilterOutputRequestTests.cs) cover
requested subsets, materialization and nearest-sampling/coverage boundaries.
One selected copy-blur input covers metadata and resolved-image allocation,
refusal, cancellation, cleanup, retry and prepared replay through the existing
[ownership contracts](../../Lokad.Svg.Tests/SvgFilterBitmapOwnershipTests.cs).
The fixtures do not repeat the output API matrix.
