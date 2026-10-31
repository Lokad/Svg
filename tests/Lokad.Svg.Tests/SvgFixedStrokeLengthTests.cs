using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgFixedStrokeLengthTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("0pt", 0)]
    [InlineData("-.75pt", -1)]
    [InlineData("6PT", 8)]
    [InlineData(".0625pc", 1)]
    [InlineData("1in", 96)]
    [InlineData("2.54cm", 96)]
    [InlineData("25.4mm", 96)]
    [InlineData("101.6q", 96)]
    [InlineData("1px", 1)]
    [InlineData("1610612735.25pt", int.MaxValue)]
    [InlineData("-1610612736pt", int.MinValue)]
    public void Fixed_dash_offsets_resolve_before_range_admission(string token, double expected)
        => Assert.Equal(expected, SvgStrokeStyleSyntax.ParseDashoffset(token));

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("0pt", 0)]
    [InlineData("2.25pt", 3)]
    [InlineData("1610612735.25pt", int.MaxValue)]
    public void Stroke_width_uses_fixed_length_conversion(string token, double expected)
        => Assert.Equal(expected, SvgStrokeStyleSyntax.ParseStrokeWidth(token, "path"));

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("0pt -0pc 0in", null)]
    [InlineData("1.5pt .25pc 2px", new double[] { 2, 4, 2, 2, 4, 2 })]
    [InlineData("1610612735.25pt 0", new double[] { int.MaxValue, 0 })]
    public void Dash_entries_share_conversion_and_preserve_zero_and_odd_patterns(string token, double[]? expected)
        => Assert.Equal(expected, SvgStrokeStyleSyntax.ParseDasharray(token));

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("1610612736pt")]
    [InlineData("-1610612736.75pt")]
    [InlineData("1 pt")]
    [InlineData("1mmpt")]
    [InlineData("1\u00a0pt")]
    [InlineData("0%")]
    [InlineData("0em")]
    [InlineData("0ex")]
    [InlineData("NaNpt")]
    [InlineData("Infinitypt")]
    [InlineData("1e309pt")]
    public void Unsupported_or_nonfinite_offsets_do_not_enter_stroke_construction(string token)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature,
            Assert.Throws<SvgException>(() => SvgStrokeStyleSyntax.ParseDashoffset(token)).ErrorCode);

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("-0.75pt")]
    [InlineData("1610612736pt")]
    public void Stroke_width_preserves_negative_and_converted_range_rejection(string token)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature,
            Assert.Throws<SvgException>(() => SvgStrokeStyleSyntax.ParseStrokeWidth(token, "path")).ErrorCode);

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("-0.75pt 1px", SvgErrorCode.MalformedInput)]
    [InlineData("1610612736pt 1px", SvgErrorCode.UnsupportedFeature)]
    public void Dash_entries_preserve_distinct_negative_and_converted_range_errors(string token, SvgErrorCode expected)
        => Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgStrokeStyleSyntax.ParseDasharray(token)).ErrorCode);

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("-0pt", true)]
    [InlineData("0q", true)]
    [InlineData("0%", false)]
    [InlineData("0em", false)]
    [InlineData("1 pt", false)]
    [InlineData("1e-3pc", false)]
    public void Zero_width_selection_uses_the_same_supported_length_domain(string token, bool expected)
        => Assert.Equal(expected, SvgStrokeStyleSyntax.IsZeroStrokeWidth(token));

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Root_lengths_accept_the_same_quarter_millimeter_unit()
        => Assert.Equal(96, SvgGeometrySyntax.ParseLength("101.6q"));
}
