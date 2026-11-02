using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgCanvasBackgroundOwnershipTests
{
    [Fact]
    public void Canvas_paint_borrows_the_root_boundary_without_extra_storage_and_retries_after_refusal()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='17' height='11' style='background-color:rgba(176,64,32,.7);opacity:0;visibility:hidden'/>"u8.ToArray();
        RenderBudget sceneBudget = new(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None);
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget rasterBudget = new(new HostMemoryBudget(1_000_000));
        byte[] expected;
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, rasterBudget, CancellationToken.None))
        {
            Assert.Equal((26, 17), (surface.Width, surface.Height));
            Assert.Equal(179, surface.Pixels[3]);
            Assert.Equal(new byte[] { 176, 62, 28, 45 }, surface.Pixels.AsSpan((10 * 26 + 25) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 185, 46, 23, 11 }, surface.Pixels.AsSpan((16 * 26 + 25) * 4, 4).ToArray());
            expected = surface.Pixels.ToArray();
        }
        long peak = 26 * 17 * 4;
        Assert.Equal(peak, rasterBudget.Snapshot().PeakBytes);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
        HostMemoryBudget host = new(peak - 1);
        RenderBudget limited = new(host);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgConverter.Rasterize(root, options, limited, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, limited.Snapshot().CurrentBytes);
        host.Limit = peak;
        using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, options, limited, CancellationToken.None))
            Assert.Equal(expected, retry.Pixels);
        Assert.Equal(0, limited.Snapshot().CurrentBytes);
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }
}
