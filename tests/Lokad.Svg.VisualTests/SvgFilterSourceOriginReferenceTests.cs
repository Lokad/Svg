namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterSourceOriginReferenceTests
{
    [Theory]
    [InlineData("compact-gradient")]
    [InlineData("wide-solid")]
    [InlineData("wide-gradient")]
    [InlineData("cross-tile")]
    public void Compact_filter_sources_enter_the_owning_bitmap_before_sampling(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-origins", identity, 4);
}
