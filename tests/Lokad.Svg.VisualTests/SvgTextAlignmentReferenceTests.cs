namespace Lokad.Svg.VisualTests;

public sealed class SvgTextAlignmentReferenceTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Preformatted_alignment_keeps_anchors_and_path_positioning()
        => NativeFontFixture.AssertMatches("preformatted-text-align", "preformatted-text-align", "Example");
}
