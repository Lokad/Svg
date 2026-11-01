namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterSourceOpacityReferenceTests
{
    [Fact]
    public void Enclosing_opacity_retains_native_thin_source_coverage()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "thin-curved-source", 2);
}
