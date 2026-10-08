using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternContentTests
{
    [Theory]
    [InlineData("circle-tile-rectangle")]
    [InlineData("curve-tile-circle")]
    [InlineData("two-vector-leaves")]
    [InlineData("rectangle-first-opacity")]
    [InlineData("forward-vector-pattern-use")]
    [InlineData("scaled-vector-pattern-symbol")]
    [InlineData("native-opacity")]
    public void Tile_scenes_release_owners_at_parser_memory_and_raster_work_boundaries(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "pattern-vector-content", identity + ".svg"));
        RenderBudget discovery = new(8_000_000);
        ParserWorkCounter discoveryWork = new(1_000_000);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, discoveryWork, discovery, CancellationToken.None));
        Assert.Equal(0, discovery.Snapshot().CurrentBytes);
        for (long limit = 0; limit < discoveryWork.Used; limit++)
        {
            RenderBudget denied = new(8_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(limit), denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget measured = new(8_000_000);
        ParserWorkCounter work = new(1_000_000);
        long rasterWork;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            rasterWork = surface.WorkUnits;
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget denied = new(8_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        foreach (long limit in new[] { rasterWork - 1, rasterWork, rasterWork + 1 })
        {
            SvgRenderLimits defaults = SvgRenderLimits.Default;
            SvgRenderLimits limits = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
                defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
                checked((int)limit), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
            RenderBudget budget = new(8_000_000);
            using (SvgRoot scene = SvgRootParser.Parse(input, limits, budget, null, new(1_000_000), CancellationToken.None))
            {
                long retained = budget.Snapshot().CurrentBytes;
                SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
                if (limit < rasterWork)
                {
                    Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, options, budget, CancellationToken.None)).ErrorCode);
                    Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                }
                else
                {
                    using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None);
                    Assert.Equal(rasterWork, surface.WorkUnits);
                }
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }

        void DenyMemory(long limit)
        {
            RenderBudget budget = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Cancellation_during_tile_replay_preserves_borrowed_scene_and_allows_retry()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256'><defs><pattern id='p' width='256' height='256' patternUnits='userSpaceOnUse'><circle cx='128' cy='128' r='100' fill='#025bc7'/></pattern></defs><rect width='256' height='256' fill='url(#p)'/></svg>"u8.ToArray();
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            int replayed = 0;
            using (SvgAnalyticPaintProbe.Arm((phase, _) =>
            {
                if (phase == SvgAnalyticPaintPhase.BeforeReplay && ++replayed == 1) cancellation.Cancel();
            }))
                Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token)).CancellationToken);
            Assert.Equal(1, replayed);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(256, retry.Width);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("id='p'")]
    public void Anonymous_and_unused_tiles_release_promoted_sources(string identifier)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern " + identifier + " width='16' height='16' patternUnits='userSpaceOnUse'><rect width='4' height='4'/><circle cx='8' cy='8' r='4'/></pattern></defs><rect width='8' height='8' fill='red'/></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels[..4]);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<pattern id='p' width='16' height='16' patternUnits='userSpaceOnUse'><circle cx='8' cy='8' r='4' fill='url(#p)'/></pattern>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<pattern id='p' width='16' height='16' patternUnits='userSpaceOnUse'><circle cx='8' cy='8' r='4' mask='url(#m)'/></pattern>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<pattern id='p' width='16' height='16' patternUnits='userSpaceOnUse'><circle cx='8' cy='8' r='4'/></pattern><pattern id='p' width='16' height='16' patternUnits='userSpaceOnUse'><circle cx='8' cy='8' r='4'/></pattern>", SvgErrorCode.MalformedInput)]
    public void Rejected_tile_content_and_duplicate_ids_release_partial_scenes(string content, SvgErrorCode expected)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs>" + content + "</defs><rect width='16' height='16' fill='url(#p)'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
