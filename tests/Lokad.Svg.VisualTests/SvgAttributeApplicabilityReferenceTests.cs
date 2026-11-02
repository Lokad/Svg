namespace Lokad.Svg.VisualTests;

public sealed class SvgAttributeApplicabilityReferenceTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public void Attribute_applicability_preserves_geometry_gradient_fallback_and_filter_paint()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("attribute-applicability", "attributes", 1);
}
