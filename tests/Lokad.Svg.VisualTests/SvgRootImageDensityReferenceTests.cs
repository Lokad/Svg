namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgRootImageDensityReferenceTests
{
    [Theory]
    [InlineData("boundary-0-75", .75)]
    [InlineData("boundary-1", 1)]
    [InlineData("boundary-1-5", 1.5)]
    [InlineData("boundary-2", 2)]
    public void Output_density_preserves_image_layout_and_independent_fractional_edges(string identity, double scale)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("root-image-density", identity, 4,
            new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default));
}
