using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed class SvgGlyphDeviceMetricsTests
{
    // Independent canvas measurements at the document's two actual device sizes,
    // using only the pinned supplied Noto Sans Mono Bold face. Path widths are
    // independently witnessed by the original SVG's character positions.
    [Theory]
    [InlineData("windows", 15.36, false, 9.215988159179688)]
    [InlineData("windows", 30.72, false, 18.431991577148438)]
    [InlineData("linux", 15.36, false, 9.21563720703125)]
    [InlineData("linux", 30.72, false, 18.431289672851562)]
    [InlineData("windows", 15.36, true, 9.21875)]
    [InlineData("windows", 30.72, true, 18.4375)]
    [InlineData("linux", 15.36, true, 9.21875)]
    [InlineData("linux", 30.72, true, 18.4375)]
    public void Platform_advances_match_independent_font_and_fragment_measurements(string target, double size, bool textPath, double expected)
    {
        SvgGlyphDeviceMetrics metrics = new(1, target == "windows" ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, textPath);
        Assert.Equal(expected, metrics.GetAdvance(600, 1000, size));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void Invalid_output_scale_reports_a_bounded_device_failure(double scale)
    {
        SvgGlyphDeviceMetrics metrics = new(scale, SvgGlyphMetricTarget.Linux, false);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => metrics.GetAdvance(600, 1000, 18)).ErrorCode);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("linux")]
    public void Excessive_device_advance_reports_the_existing_numeric_limit(string target)
    {
        SvgGlyphDeviceMetrics metrics = new(1, target == "windows" ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, false);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => metrics.GetAdvance(600, 1000, 100_000)).ErrorCode);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("linux")]
    public void Positive_sizes_below_scalar_precision_retain_zero_advance(string target)
    {
        SvgGlyphDeviceMetrics metrics = new(1, target == "windows" ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, false);
        Assert.Equal(0, metrics.GetAdvance(600, 1000, 1e-300));
    }
}
