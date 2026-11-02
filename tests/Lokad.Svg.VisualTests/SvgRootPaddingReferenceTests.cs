namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgRootPaddingReferenceTests
{
    [Theory]
    [InlineData("padding-130")]
    [InlineData("padding-132")]
    public void Root_padding_retains_independent_css_subpixel_boundaries(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("root-box-layout", identity, 16);

    [Theory]
    [InlineData("padding-viewbox", 1)]
    [InlineData("padding-source-alpha", 1)]
    [InlineData("padding-clip-density", 1.5)]
    [InlineData("padding-percent", 1)]
    [InlineData("padding-empty", 1.5)]
    [InlineData("padding-priority", 1)]
    [InlineData("padding-coincident-clips", 1.5)]
    [InlineData("padding-quantized-zero", 1.5)]
    public void Root_padding_keeps_box_projection_clipping_and_source_composition_distinct(string identity, double scale)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("root-padding", identity, 8,
            new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default));
}
