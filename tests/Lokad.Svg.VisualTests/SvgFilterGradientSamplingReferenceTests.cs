namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterGradientSamplingReferenceTests
{
    [Theory]
    [InlineData("identity-gradient")]
    [InlineData("matrix-gradient")]
    public void Source_image_bounds_preserve_native_gradient_phase(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-gradient-sampling", identity, 2);
}
