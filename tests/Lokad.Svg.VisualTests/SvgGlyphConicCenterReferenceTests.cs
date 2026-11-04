namespace Lokad.Svg.VisualTests;

public sealed class SvgGlyphConicCenterReferenceTests
{
    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("black")]
    [InlineData("blue")]
    public Task Supplied_glyph_paint_preserves_native_conic_center_coverage(string identity)
        => NativeFontFixture.AssertCaseMatches("glyph-conic-centers", identity, 2, new("DiagnosticNoto", 400, false));
}
