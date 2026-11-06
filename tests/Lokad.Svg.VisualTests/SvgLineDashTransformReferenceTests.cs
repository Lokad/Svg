namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgLineDashTransformReferenceTests
{
    [Theory]
    [InlineData("gradient-line-nonuniform")]
    [InlineData("gradient-line-rotated")]
    [InlineData("gradient-line-sheared")]
    [InlineData("gradient-line-uniform")]
    [InlineData("empty-gradient-nonuniform")]
    [InlineData("empty-gradient-uniform")]
    [InlineData("empty-gradient-parent")]
    public void Line_strokes_preserve_affine_source_geometry_independently_of_gradient_availability(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("line-dash-transforms", identity, 7);
}
