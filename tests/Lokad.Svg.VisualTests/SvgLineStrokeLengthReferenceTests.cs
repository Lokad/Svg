namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgLineStrokeLengthReferenceTests
{
    [Fact]
    public void Fixed_widths_keep_axis_line_caps_dashes_and_zero_suppression()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("line-stroke-widths", "resolved-even-widths", 1);
}
