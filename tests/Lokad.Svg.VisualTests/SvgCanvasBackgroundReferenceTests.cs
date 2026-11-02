namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgCanvasBackgroundReferenceTests
{
    [Theory]
    [InlineData("canvas-source-opacity")]
    [InlineData("canvas-cascade-applicability")]
    [InlineData("canvas-empty")]
    [InlineData("canvas-root-display")]
    [InlineData("canvas-source-alpha")]
    public void Canvas_background_matches_native_independently_of_element_paint(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("root-css-box", identity, 5);
}
