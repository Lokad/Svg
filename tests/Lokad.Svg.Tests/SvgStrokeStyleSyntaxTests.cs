using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgStrokeStyleSyntaxTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(" nOnE ", null)]
    [InlineData("2px, 3", new double[] { 2, 3 })]
    [InlineData(".5 1e1 2.25px", new double[] { .5, 10, 2.25, .5, 10, 2.25 })]
    [InlineData("0", null)]
    [InlineData("0 0", null)]
    [InlineData("0px -0px 0e0", null)]
    [InlineData("0 4", new double[] { 0, 4 })]
    [InlineData("1 2 3 4 5 6 7 8", new double[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
    public void Dash_materialization_preserves_values_and_odd_pattern_repetition(string? value, double[]? expected)
    {
        Assert.Equal(expected, SvgStrokeStyleSyntax.ParseDasharray(value));
        if (value is not null)
            SvgStrokeStyleSyntax.ValidateAttribute("stroke-dasharray", value, true, true, true, true, true);
    }

    [Theory]
    [InlineData("", SvgErrorCode.UnsupportedFeature)]
    [InlineData(" ", SvgErrorCode.UnsupportedFeature)]
    [InlineData("-1 2", SvgErrorCode.MalformedInput)]
    [InlineData("1 2 3 4 5 6 7 8 9", SvgErrorCode.UnsupportedFeature)]
    [InlineData("NaN", SvgErrorCode.UnsupportedFeature)]
    [InlineData("1px 2%", SvgErrorCode.UnsupportedFeature)]
    [InlineData("1 2147483648", SvgErrorCode.UnsupportedFeature)]
    public void Dash_validation_and_materialization_preserve_error_categories(string value, SvgErrorCode expected)
    {
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgStrokeStyleSyntax.ParseDasharray(value)).ErrorCode);
        Assert.Equal(expected, Assert.Throws<SvgException>(() =>
            SvgStrokeStyleSyntax.ValidateAttribute("stroke-dasharray", value, true, true, true, true, true)).ErrorCode);
    }

    [Theory]
    [InlineData("2 3")]
    [InlineData("1 2 3 4 5 6 7 8")]
    public void Stroke_validation_and_presence_checks_do_not_allocate_dash_arrays(string dasharray)
    {
        SvgInheritedStrokeStyle style = SvgInheritedStrokeStyle.Default with { Stroke = "red", Width = "2", Dasharray = dasharray };
        SvgTextGlyphPaint paint = new(new(SvgColor.Transparent, 0), false) { Stroke = new(style, SvgPaint.Default, SvgPaintOpacity.Opaque) };
        for (int index = 0; index < 256; index++)
        {
            _ = paint.HasStroke;
            SvgStrokeStyleSyntax.ValidateAttribute("stroke-dasharray", dasharray, true, true, true, true, true);
        }
        bool hasStroke = true;
        long allocated = long.MaxValue;
        // Fixed batches measure steady per-call storage, excluding one-time runtime warmup.
        for (int sample = 0; sample < 3; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 4_096; index++)
            {
                hasStroke &= paint.HasStroke;
                SvgStrokeStyleSyntax.ValidateAttribute("stroke-dasharray", dasharray, true, true, true, true, true);
            }
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.True(hasStroke);
        Assert.InRange(allocated, 0, 1_024);
    }
}
