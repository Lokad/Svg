using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgRootPaddingOwnershipTests
{
    [Fact]
    public void Root_content_clip_borrows_rectangle_coverage_and_releases_allowance_for_retry()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='101' height='81' style='padding-right:3px'><rect x='-10' y='-10' width='150' height='120' fill='lime'/></svg>"u8.ToArray();
        RenderBudget sceneBudget = new(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None);
        RenderBudget rasterBudget = new(new HostMemoryBudget(1_000_000));
        SvgRenderOptions options = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] expected;
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, rasterBudget, CancellationToken.None))
        {
            Assert.Equal((152, 122), (surface.Width, surface.Height));
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, surface.Pixels.AsSpan(0, 4).ToArray());
            Assert.Equal(64, surface.Pixels[(10 * 152 + 151) * 4 + 3]);
            Assert.Equal(16, surface.Pixels[(121 * 152 + 151) * 4 + 3]);
            expected = surface.Pixels.ToArray();
        }
        long peak = rasterBudget.Snapshot().PeakBytes;
        Assert.InRange(peak, 152 * 122 * 4, 152 * 122 * 4 + 4_096);
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
        root.Dispose();
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }
}
