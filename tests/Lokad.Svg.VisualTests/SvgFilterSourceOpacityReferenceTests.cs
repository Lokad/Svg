namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgFilterSourceOpacityReferenceTests
{
    [Fact]
    public void Enclosing_opacity_retains_native_thin_source_coverage()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "thin-curved-source", 9);

    [Fact]
    public void Downscaled_overlap_keeps_the_existing_group_alpha_path()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-group-opacity", "overlap-downscale", 2);

    [Fact]
    public void Upscaled_overlap_uses_the_native_affine_bitmap_restore()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-group-opacity", "overlap-upscale", 2);

    [Fact]
    public void Integer_viewport_keeps_its_source_group_bitmap_restore()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "thin-curved-source-integer", 9);

    [Fact]
    public void Filter_opacity_samples_in_its_enclosing_bitmap()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "coloured-stroke-opacity", 9);

    [Fact]
    public void Blurred_gradient_keeps_source_capture_separate_from_opacity_sampling()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "gradient-blur-opacity", 9);

    [Fact]
    public void Blurred_gradient_stroke_retains_parameter_visuals_for_its_source_bitmap()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "gradient-stroke-blur-opacity", 9);

    [Fact]
    public void Filter_source_device_is_created_in_its_enclosing_opacity_bitmap()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "opacity-parent-source-origin", 9);

    [Fact]
    public void Non_displayed_instances_do_not_move_the_visible_source_shader()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "hidden-instance-source-bounds", 9);

    [Fact]
    public void Rotated_stroke_alias_maps_fractional_layout_visuals_before_capture_enclosure()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("filter-source-opacity", "fractional-stroke-layout", 9);
}
