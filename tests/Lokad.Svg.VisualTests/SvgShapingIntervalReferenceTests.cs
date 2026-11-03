namespace Lokad.Svg.VisualTests;

public sealed class SvgShapingIntervalReferenceTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Anchor_only_spans_keep_the_selected_fonts_ligature()
        => NativeFontFixture.AssertMatches("text-shaping-boundaries", "text-shaping-boundaries", "Example");
}
