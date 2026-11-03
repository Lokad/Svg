namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterColorMatrixReferenceTests
{
    [Theory]
    [InlineData("identity")]
    [InlineData("alpha-scale")]
    [InlineData("rgb-to-alpha")]
    [InlineData("alpha-to-rgb")]
    [InlineData("transparent-offset")]
    [InlineData("linear")]
    [InlineData("named-chain")]
    [InlineData("matrix-chain")]
    [InlineData("crop")]
    [InlineData("affine-offset")]
    [InlineData("uniform")]
    [InlineData("clamp")]
    [InlineData("saturate")]
    [InlineData("hue-rotate")]
    [InlineData("luminance-alpha")]
    [InlineData("affine-matrix")]
    [InlineData("affine-before")]
    [InlineData("affine-after")]
    [InlineData("flat-offset")]
    [InlineData("cropped-chain")]
    [InlineData("source-alpha")]
    [InlineData("matrix-blur")]
    [InlineData("matrix-merge")]
    [InlineData("matrix-composite")]
    [InlineData("matrix-zero-alias")]
    [InlineData("blur-matrix")]
    [InlineData("affine-uniform")]
    public void Matrix_stages_preserve_native_alpha_sampling_and_crop(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-color-matrix", identity, 27);
}
