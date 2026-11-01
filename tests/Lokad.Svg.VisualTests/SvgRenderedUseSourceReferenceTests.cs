namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgRenderedUseSourceReferenceTests
{
    [Theory]
    [InlineData("root-forward")]
    [InlineData("group-local-presentation")]
    [InlineData("clipped-alias")]
    [InlineData("group-opacity")]
    [InlineData("filtered-group")]
    public void Retained_sources_preserve_native_presentation_and_effects(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("rendered-use-sources", identity, 5);
}
