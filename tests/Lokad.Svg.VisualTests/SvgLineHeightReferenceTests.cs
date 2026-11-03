namespace Lokad.Svg.VisualTests;

public sealed class SvgLineHeightReferenceTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Line_height_keeps_explicit_baselines_anchors_and_inherited_path_text()
        => NativeFontFixture.AssertMatches("text-line-layout", "text-line-layout", "Example");
}
