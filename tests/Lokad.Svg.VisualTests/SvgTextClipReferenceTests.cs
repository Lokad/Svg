namespace Lokad.Svg.VisualTests;

public sealed class SvgTextClipReferenceTests
{
    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("identity")]
    [InlineData("fractional-origin")]
    [InlineData("consumer-scale")]
    [InlineData("consumer-affine")]
    [InlineData("child-affine")]
    [InlineData("object-box")]
    [InlineData("nonzero-opacity")]
    [InlineData("span-paint")]
    [InlineData("positioned-span")]
    [InlineData("length-adjust")]
    [InlineData("mixed-children")]
    [InlineData("child-intersection")]
    [InlineData("inherited-font")]
    [InlineData("decorations")]
    [InlineData("referenced-opacity")]
    [InlineData("mixed-opacity")]
    [InlineData("mixed-opacity-affine")]
    [InlineData("group-consumer")]
    [InlineData("referenced-geometry-opacity")]
    public Task Text_clip_sources_preserve_native_operation_and_placement(string identity)
        => NativeFontFixture.AssertCaseMatches("text-clip", identity, 19, new("DiagnosticNoto", 400, false));
}
