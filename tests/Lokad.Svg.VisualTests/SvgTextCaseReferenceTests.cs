namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgTextCaseReferenceTests
{
    [Theory]
    [InlineData("basic")]
    [InlineData("path")]
    [InlineData("indexed")]
    public Task Uppercase_uses_emitted_text_and_original_svg_position_addresses(string identity)
        => NativeFontFixture.AssertMatches("text-case-mapping/" + identity, "text-case-" + identity, "Example");
}
