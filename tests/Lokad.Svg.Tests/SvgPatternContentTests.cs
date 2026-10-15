using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternContentTests
{
    [Fact]
    public void Tile_filter_references_bind_forward_without_copying_resource_owners()
    {
        const string filter = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><feGaussianBlur stdDeviation='.7'/></filter>";
        const string pattern = "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect x='2' y='2' width='4' height='4' fill='red' filter='url(#f)'/></pattern>";
        byte[] before = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs>" + filter + pattern + "</defs><rect width='16' height='16' fill='url(#p)'/></svg>");
        byte[] after = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs>" + pattern + filter + "</defs><rect width='16' height='16' fill='url(#p)'/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(before, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(after, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("<image width='4' height='4' href='data:image/png;base64,!!!'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<rect width='4' height='4' filter='url(#missing)'/>", SvgErrorCode.UnsupportedFeature)]
    public void Extended_tile_failures_release_partial_scenes(string leaf, SvgErrorCode expected)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><circle cx='4' cy='4' r='3'/>" + leaf + "</pattern></defs><rect width='16' height='16' fill='url(#p)'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Extended_pattern_leaves_preserve_unsupported_child_admission()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><filter id='f'><feGaussianBlur stdDeviation='1'/></filter><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' filter='url(#f)'><animate attributeName='x'/></rect></pattern></defs><rect width='16' height='16' fill='url(#p)'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.True(root.HasUnsupportedChild);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Nested_tile_opacity_ends_before_siblings_and_empty_groups()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><g opacity='.5'><g opacity='.5'><rect width='4' height='4' fill='red'/></g><rect x='4' width='4' height='4' fill='blue'/></g><g opacity='0'/><rect y='4' width='8' height='4' fill='#00ff00'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 64 }, surface.Pixels.AsSpan((2 * 8 + 1) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 255, 128 }, surface.Pixels.AsSpan((2 * 8 + 6) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, surface.Pixels.AsSpan((6 * 8 + 1) * 4, 4).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

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

    [Theory]
    [InlineData("", false, false)]
    [InlineData("scale(1.5 2)", true, false)]
    [InlineData("rotate(-45)", true, false)]
    [InlineData("", false, true)]
    [InlineData("rotate(-45)", true, true)]
    public void Cancellation_during_tile_replay_preserves_borrowed_scene_and_allows_retry(string transform, bool rootLocal, bool grouped)
    {
        string content = "<circle cx='128' cy='128' r='100' fill='#025bc7'/>";
        if (grouped) content = "<g transform='translate(2 -3)'><g transform='scale(0.75)'>" + content + "</g></g>";
        string definition = "<pattern id='p' width='256' height='256' patternUnits='userSpaceOnUse' patternTransform='" + transform + "'>" + content + "</pattern>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256'>" + (rootLocal ? definition : "<defs>" + definition + "</defs>") + "<rect width='256' height='256' fill='url(#p)'/></svg>");
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
    [InlineData("group-map-origins")]
    [InlineData("group-inherited-paint")]
    [InlineData("empty-group-legacy")]
    public void Grouped_tiles_release_material_storage_at_tight_limits(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "pattern-vector-content", identity + ".svg"));
        RenderBudget measured = new(8_000_000);
        ParserWorkCounter work = new(1_000_000);
        long rasterWork;
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
            rasterWork = surface.WorkUnits;
        Assert.Equal(0, measured.Snapshot().CurrentBytes);

        RenderBudget parserBudget = new(8_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, parserBudget, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, parserBudget.Snapshot().CurrentBytes);

        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, tight, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);

        SvgRenderLimits defaults = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
            defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
            checked((int)rasterWork - 1), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgConverter.Rasterize(root, options, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(rasterWork, retry.WorkUnits);
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
    [InlineData("<pattern id='p' width='16' height='16' patternUnits='userSpaceOnUse'><g mask='url(#m)'><circle cx='8' cy='8' r='4'/></g></pattern>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<pattern id='p' width='16' height='16' patternUnits='userSpaceOnUse'><g><circle cx='8' cy='8' r='4' fill='url(#p)'/></g></pattern>", SvgErrorCode.UnsupportedFeature)]
    public void Rejected_tile_content_and_duplicate_ids_release_partial_scenes(string content, SvgErrorCode expected)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs>" + content + "</defs><rect width='16' height='16' fill='url(#p)'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Original_group_opacity_admission_releases_the_replayed_scene()
    {
        const string content = "<pattern id='p' width='16' height='16' patternUnits='userSpaceOnUse'><rect width='16' height='16'/><g opacity='0.6'><circle cx='8' cy='8' r='4'/></g></pattern>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs>" + content + "</defs><rect width='16' height='16' fill='url(#p)'/></svg>");
        byte[] unchanged = input.ToArray();
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal((byte)255, surface.Pixels[3]);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
