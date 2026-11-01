namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgMetadataAttributeReferenceTests
{
    [Fact]
    public void Descriptive_titles_preserve_local_sources_and_paint_through_canonical_conversion()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("metadata-attributes", "descriptive-titles", 1);
}
