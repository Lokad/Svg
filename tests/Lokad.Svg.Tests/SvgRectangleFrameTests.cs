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
    public void Width_admission_releases_the_preceding_rectangle_owner(string width)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><rect width='24' height='24' fill='blue'/><rect x='5' y='5' width='24' height='24' fill='none' stroke='red' stroke-width='" + width + "'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        if (width == "25")
        {
            using SvgRoot admitted = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
            using SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(admitted, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Contains(output.Pixels, value => value != 0);
        }
        else
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><rect width='24' height='24' fill='none' stroke='red' stroke-width='2.5'/></svg>"u8.ToArray(), SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.Single(retry.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
