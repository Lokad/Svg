namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgTextPathStyleReferenceTests
{
    [Theory]
    [InlineData("basic")]
    [InlineData("cascade")]
    [InlineData("nested")]
    [InlineData("mixed")]
    public Task Path_child_styles_keep_native_cascade_and_placement(string identity)
        => NativeFontFixture.AssertMatches("text-path-styles/" + identity, "text-path-" + identity, "Example");
}
