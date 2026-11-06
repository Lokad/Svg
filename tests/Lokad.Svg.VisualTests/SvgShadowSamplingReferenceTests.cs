namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgShadowSamplingReferenceTests
{
    [Theory]
    [InlineData("panel-controls.json", "panel-shadow", 5)]
    [InlineData("panel-controls.json", "panel-source", 5)]
    [InlineData("panel-controls.json", "sharp-shadow", 5)]
    [InlineData("panel-controls.json", "origin-shadow", 5)]
    [InlineData("panel-controls.json", "zero-shadow", 5)]
    [InlineData("stage-controls.json", "transparent-shadow", 7)]
    [InlineData("stage-controls.json", "blur-alpha", 7)]
    [InlineData("stage-controls.json", "expanded-shadow", 7)]
    [InlineData("color-materialization.json", "color-image", 1)]
    public void Source_capture_respects_shadow_sampling_without_changing_expanded_graphs(
        string manifest, string identity, int expectedCases)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("benchmark-workloads", manifest, identity,
            expectedCases, SvgRenderOptions.Default);
}
