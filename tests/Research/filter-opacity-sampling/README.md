# Filter opacity and sampling

These 53 actual inputs retain independent Windows/Linux browser images. The
[baseline comparison](before-correction.json) records 37 failing inputs and 16
positive controls before the shared correction. [Capture metadata](capture.json)
binds each source and reference to three identical healthy fresh-process captures
and cleaned profiles. Scaled inputs retain both authored and captured SVGs; page
background is a render option. See the [capture procedure](../../../tools/README.md).

The cases distinguish packed image alpha from shader alpha, logical opacity-layer
bounds from stored image bounds, and finite nearest-image coverage from packed
texels. They retain cropped/aliased affine offsets, explicit operating spaces,
functional versus hexadecimal alpha, sprite admission seams, local use, symbol
and pattern shadows. Every compact/expanded or filtered/unfiltered input keeps its
own native reference, including naturally unequal results.

[Rendering tests](../../Lokad.Svg.VisualTests/SvgFilterStageReferenceTests.cs) use
one canonical conversion. [Sprite mechanisms](../../Lokad.Svg.Tests/SvgFilterSpriteMechanismTests.cs)
retain adjacent-float and negative-tie boundaries directly. Two representative
[ownership inputs](../../Lokad.Svg.Tests/SvgFilterBitmapOwnershipTests.cs) exercise
packed coverage metadata and affine opacity/crop allocation, denial, cancellation,
cleanup, retry and prepared replay. The fixture collection does not repeat the API
matrix. The broader historical [uniform bank](../filter-uniform-precision/README.md)
keeps its original captures and individually tested research cases.
