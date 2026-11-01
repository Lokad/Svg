namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterSourceOpacityReferenceTests
{
    [Fact]
    public void Enclosing_opacity_retains_native_thin_source_coverage()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "thin-curved-source", 3);

    [Fact]
    public void Downscaled_overlap_keeps_the_existing_group_alpha_path()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-group-opacity", "overlap-downscale", 2);

    [Fact]
    public void Upscaled_overlap_uses_the_native_affine_bitmap_restore()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-group-opacity", "overlap-upscale", 2);

    [Fact]
    public void Integer_viewport_keeps_its_source_group_bitmap_restore()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "thin-curved-source-integer", 3);
}
