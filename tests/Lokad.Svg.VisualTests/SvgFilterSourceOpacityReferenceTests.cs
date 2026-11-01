namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterSourceOpacityReferenceTests
{
    [Fact]
    public void Enclosing_opacity_retains_native_thin_source_coverage()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "thin-curved-source", 6);

    [Fact]
    public void Downscaled_overlap_keeps_the_existing_group_alpha_path()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-group-opacity", "overlap-downscale", 2);

    [Fact]
    public void Upscaled_overlap_uses_the_native_affine_bitmap_restore()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-group-opacity", "overlap-upscale", 2);

    [Fact]
    public void Integer_viewport_keeps_its_source_group_bitmap_restore()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "thin-curved-source-integer", 6);

    [Fact]
    public void Filter_opacity_samples_in_its_enclosing_bitmap()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "coloured-stroke-opacity", 6);

    [Fact]
    public void Blurred_gradient_keeps_source_capture_separate_from_opacity_sampling()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "gradient-blur-opacity", 6);

    [Fact]
    public void Blurred_gradient_stroke_retains_parameter_visuals_for_its_source_bitmap()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "gradient-stroke-blur-opacity", 6);
}
