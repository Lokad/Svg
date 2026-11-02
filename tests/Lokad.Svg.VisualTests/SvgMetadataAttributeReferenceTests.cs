namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgMetadataAttributeReferenceTests
{
    [Fact]
    public void Descriptive_titles_preserve_local_sources_and_paint_through_canonical_conversion()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("metadata-attributes", "descriptive-titles", 2);

    [Fact]
    public void Inert_events_preserve_paint_references_filters_and_nested_viewports()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("metadata-attributes", "inert-events", 2);
}
