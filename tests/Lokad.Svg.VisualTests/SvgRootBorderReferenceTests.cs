namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgRootBorderReferenceTests
{
    [Theory]
    [InlineData("meet", 1)]
    [InlineData("slice", 1)]
    [InlineData("none-alpha", 1)]
    [InlineData("overflow-default", 1)]
    [InlineData("overflow-hidden", 1)]
    [InlineData("asymmetric", 1)]
    [InlineData("box-sizing", 1)]
    [InlineData("hidden-border", 1)]
    [InlineData("none-display-border", 1)]
    [InlineData("fractional-1", 1)]
    [InlineData("fractional-0-75", .75)]
    [InlineData("fractional-1-5", 1.5)]
    [InlineData("fractional-2", 2)]
    [InlineData("empty-border", 1)]
    public void Root_border_preserves_frozen_box_content_and_composition_stages(string identity, double scale)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("root-box-layout", identity, 16,
            new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default));

    [Theory]
    [InlineData("mixed-alpha")]
    [InlineData("same-alpha")]
    [InlineData("partial")]
    [InlineData("transparent")]
    public void Border_edges_preserve_alpha_groups_current_color_priority_and_transparent_miters(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("root-border", identity, 4,
            new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default));
}
