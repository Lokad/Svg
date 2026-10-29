using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Xunit;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterClipConsumerTests
{
    [Theory]
    [InlineData("rectangle-offset", 191)]
    [InlineData("painted-rectangle", 192)]
    [InlineData("painted-path", 191)]
    public void Filter_capture_retains_clip_and_authored_paint_coverage_as_distinct_consumers(string identity, int nativeAlpha)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-instance-composition", identity + ".svg"));
        RenderBudget sceneBudget = new RenderBudget(new HostMemoryBudget(2_000_000)), rasterBudget = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None))
        {
            int offset = (18 * 32 + 5) * 4;
            // Capture returns straight RGBA, matching the independent PNG references.
            Assert.Equal(255, surface.Pixels[offset]);
            Assert.Equal(255, surface.Pixels[offset + 1]);
            Assert.Equal(255, surface.Pixels[offset + 2]);
            Assert.Equal(nativeAlpha, surface.Pixels[offset + 3]);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }
}
