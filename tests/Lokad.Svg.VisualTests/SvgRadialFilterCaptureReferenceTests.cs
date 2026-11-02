namespace Lokad.Svg.VisualTests;

public sealed class SvgRadialFilterCaptureReferenceTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public void Radial_filter_capture_preserves_the_parent_bitmap_drawing_boundary()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("radial-filter-capture", "capture", 1);
}
