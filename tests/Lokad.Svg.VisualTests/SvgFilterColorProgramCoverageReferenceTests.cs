namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterColorProgramCoverageReferenceTests
{
    [Theory]
    [InlineData("fractional-clip")]
    [InlineData("zero-alias")]
    [InlineData("integer-clip")]
    [InlineData("matrix-control")]
    [InlineData("white-fractional")]
    [InlineData("coloured-linear")]
    [InlineData("coloured-srgb")]
    [InlineData("offset-alias")]
    [InlineData("blur-alias")]
    [InlineData("merge-alias")]
    public void Terminal_colour_programs_remain_unquantized_through_coverage(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-color-program-coverage", identity, 10);
}
