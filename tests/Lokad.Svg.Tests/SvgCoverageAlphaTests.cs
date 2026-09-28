namespace Lokad.Svg.Tests;
public sealed class SvgCoverageAlphaTests
{
    [Theory]
    [InlineData(0.75, 192)]
    [InlineData(0.25, 64)]
    [InlineData(0.5, 128)]
    [InlineData(1.0, 255)]
    [InlineData(0.0, 0)]
    public void Opaque_coverage_truncates_times_256(double coverage, byte expected)
    {
        Assert.Equal(expected, SvgConverter.CoverageAlpha(255, coverage));
    }

    [Theory]
    [InlineData(128, 0.5, 64)]
    [InlineData(200, 0.5, 100)]
    [InlineData(127, 0.75, 95)]
    public void Translucent_coverage_keeps_rounding(byte alpha, double coverage, byte expected)
    {
        Assert.Equal(expected, SvgConverter.CoverageAlpha(alpha, coverage));
    }
}

