namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterCaptureDemandReferenceTests
{
    [Theory]
    [InlineData("selected-matrix")]
    [InlineData("selected-blur")]
    public void Unread_halos_preserve_fractional_source_sampling(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-capture-demand", identity, 2);
}
