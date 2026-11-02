namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgCanvasDensityReferenceTests
{
    [Theory]
    [InlineData("transparent-edge", false)]
    [InlineData("white-edge", true)]
    [InlineData("fractional-edge", false)]
    public void Canvas_image_edges_keep_native_packed_coverage_with_the_host_background(string identity, bool white)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("root-canvas-density", identity, 3,
            new(null, 1.5, white ? new SvgColor(255, 255, 255, 255) : SvgColor.Transparent, SvgRenderLimits.Default));
}
