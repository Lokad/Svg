namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterBitmapRestoreReferenceTests
{
    [Theory]
    [InlineData("wide-linear-colour")]
    [InlineData("wide-srgb-colour")]
    [InlineData("compact-linear-colour")]
    [InlineData("opacity-linear")]
    [InlineData("clip-cross-tile")]
    public void Filter_bitmaps_compose_directly_in_the_destination_context(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-bitmap-restoration", identity, 5);
}
