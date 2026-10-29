using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgTransformedRectangleTests
{
    [Theory]
    [InlineData("rotate-solid")]
    [InlineData("translucent-overlap")]
    [InlineData("rendered-source-alias")]
    public void Transformed_fills_release_all_owners_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "transformed-rectangle-fills", identity + ".svg"));
        SvgRenderOptions options = SvgRenderOptions.Default;
        RenderBudget discovery = new RenderBudget(new HostMemoryBudget(8_000_000));
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, discovery, CancellationToken.None));
        Assert.Equal(0, discovery.Snapshot().CurrentBytes);
        // Source commands and contour storage transfer through the existing bounded owners.
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None, 1, 1, options.Viewport))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            {}
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new RenderBudget(new HostMemoryBudget(8_000_000));
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, cancellation.Token, 1, 1, options.Viewport)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, CancellationToken.None, 1, 1, options.Viewport))
        {
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, options, canceled, cancellation.Token)).CancellationToken);
            using SvgConverter.RasterizedSurface retried = SvgConverter.Rasterize(scene, options, canceled, CancellationToken.None);
        }
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None, 1, 1, options.Viewport);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Complete_heart_releases_partial_source_owners_at_late_denials_and_retries()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "illustrator-heart.svg"));
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
        {
            Assert.Equal(512, surface.Width);
            Assert.Equal(401, surface.Height);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget memory = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, memory, null, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, memory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, memory.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget retry = new RenderBudget(new HostMemoryBudget(8_000_000));
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, retry, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, retry.Snapshot().CurrentBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, retry, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
            Assert.Equal(512, surface.Width);
        Assert.Equal(0, retry.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Transformed_leaf_clip_boundary_renders_without_retaining_owners()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "transformed-rectangle-fills", "clip-crossing.svg"));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(64, surface.Width); Assert.Equal(48, surface.Height);
            Assert.Equal(new byte[] { 2, 91, 199, 255 }, surface.Pixels.AsSpan((24 * 64 + 30) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, surface.Pixels[..4]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

}
