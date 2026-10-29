using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRenderedGroupStyleTests
{
    [Theory]
    [InlineData("current-color-fill-transparent")]
    [InlineData("nested-groups-transparent")]
    [InlineData("forward-chain-transparent")]
    [InlineData("dashed-path-transparent")]
    [InlineData("masked-leaf-style-transparent")]
    [InlineData("masked-use-style-transparent")]
    [InlineData("empty-forward-transparent")]
    [InlineData("thin-scaled-ellipse-transparent")]
    [InlineData("paint-server-ancestor-transparent")]
    [InlineData("css-source-transparent")]
    [InlineData("fill-rule-transparent")]
    public void Rendered_group_source_replay_releases_all_owners_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "rendered-group-styles", identity + ".svg"));
        // Round dashed curves retain native outlines and replay scratch above one MiB.
        RenderBudget measured = new(8_000_000);
        byte[] expected;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            expected = surface.Pixels.ToArray();
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new(8_000_000);
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, CancellationToken.None))
        {
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, canceled, cancellation.Token)).CancellationToken);
            using SvgConverter.RasterizedSurface retried = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, canceled, CancellationToken.None);
            Assert.Equal(expected, retried.Pixels);
        }
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget budget = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }
}
