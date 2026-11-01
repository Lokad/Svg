namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgNestedFilterPlacementReferenceTests
{
    [Theory]
    [InlineData("nested-opacity")]
    [InlineData("affine-nested-solid-srgb")]
    public void Nested_filters_size_and_paint_the_same_placed_source(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("nested-filter-placement", identity, 6);
}
