using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRectangleFrameTests
{
    [Theory]
    [InlineData("-.125")]
    [InlineData("2.5%")]
    [InlineData("NaN")]
    [InlineData("25")]
    public void Invalid_width_releases_the_preceding_rectangle_owner(string width)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><rect width='24' height='24' fill='blue'/><rect x='5' y='5' width='24' height='24' fill='none' stroke='red' stroke-width='" + width + "'/></svg>");
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><rect width='24' height='24' fill='none' stroke='red' stroke-width='2.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.Single(retry.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(24)]
    [InlineData(.375)]
    [InlineData(2.5)]
    [InlineData(19.5)]
    public void Frame_work_denial_releases_surface_owners(double strokeWidth)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='32'><rect x='4.125' y='3.375' width='36.5' height='24' fill='none' stroke='#bc9334' stroke-opacity='.375' stroke-width='" + strokeWidth.ToString(CultureInfo.InvariantCulture) + "'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget measured = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        long work;
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None)) work = surface.WorkUnits;
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(defaults.MaximumAccountedBytes, 1_000_000, 1000, 1_000_000, 1_000_000, 1000, 1000, checked((int)work - 1));
        RenderBudget denied = new(limits.MaximumAccountedBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, limits), denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }
}
