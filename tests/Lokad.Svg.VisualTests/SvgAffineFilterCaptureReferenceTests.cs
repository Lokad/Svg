namespace Lokad.Svg.VisualTests;

public sealed class SvgAffineFilterCaptureReferenceTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public void Affine_filter_edges_retain_coverage_beside_the_bitmap_border()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("affine-filter-capture", "stroke", 1);
}
