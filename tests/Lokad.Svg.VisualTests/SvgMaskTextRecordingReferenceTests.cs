namespace Lokad.Svg.VisualTests;

public sealed class SvgMaskTextRecordingReferenceTests
{
    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("identity")]
    [InlineData("consumer-scale")]
    [InlineData("consumer-affine")]
    [InlineData("child-affine")]
    [InlineData("object-box")]
    [InlineData("consumer-affine-stroke")]
    [InlineData("consumer-affine-path")]
    public Task Mask_text_font_construction_keeps_resource_and_consumer_maps_distinct(string identity)
        => NativeFontFixture.AssertCaseMatches("mask-text-recording", identity, 7, new("DiagnosticNoto", 400, false));
}
