using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternTemplateTests
{
    [Theory]
    [InlineData("forward-circle-chain")]
    [InlineData("legacy-rectangle-alias")]
    [InlineData("overridden-dimensions")]
    [InlineData("fractional-grid-origin")]
    [InlineData("scaled-fractional-grid")]
    [InlineData("alias-paint-in-retained-leaf")]
    public void Template_scenes_release_owners_at_parser_memory_and_raster_work_boundaries(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "pattern-inheritance", identity + ".svg"));
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
    [InlineData("<pattern id='p' href='#missing'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<pattern id='p' href='#p'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<pattern id='p' href='#q'/><pattern id='q' href='#p'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<pattern id='p' href='#q' width='-1'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<pattern id='p' href='#q'/><pattern id='p' href='#q'/>", SvgErrorCode.MalformedInput)]
    public void Rejected_template_graphs_release_owners(string definitions, SvgErrorCode expected)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>" + definitions + "</defs><rect width='8' height='8' fill='url(#p)'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Deep_forward_template_chain_resolves_without_recursive_traversal()
    {
        StringBuilder text = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>");
        for (int index = 0; index < 1024; index++) text.Append("<pattern id='p").Append(index).Append("' href='#p").Append(index + 1).Append("'/>");
        text.Append("<pattern id='p1024' width='8' height='8' patternUnits='userSpaceOnUse'><circle cx='4' cy='4' r='2' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p0)'/></svg>");
        RenderBudget budget = new(8_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(text.ToString()), SvgRenderLimits.Default, budget, null, work, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
        Assert.True(work.Used < 100_000);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Aliases_borrow_one_promoted_rectangle_scene_with_independent_grids()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='8'><defs><pattern id='base' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern><pattern id='p' href='#base'/><pattern id='q' href='#base' width='12'/></defs><rect width='8' height='8' fill='url(#p)'/><rect x='12' width='12' height='8' fill='url(#q)'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgPatternGeometry[] paints = root.Geometries.Where(g => g.Kind == SvgGeometryKind.PatternFill).Select(g => g.Pattern).ToArray();
            Assert.Equal(2, paints.Length);
            Assert.NotNull(paints[0].VectorContent);
            Assert.Same(paints[0].VectorContent, paints[1].VectorContent);
            Assert.Equal(8, paints[0].TileWidth);
            Assert.Equal(12, paints[1].TileWidth);
            long retained = budget.Snapshot().CurrentBytes;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan(12 * 4, 4).ToArray());
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Full_inkscape_document_releases_partial_owners_at_work_and_memory_boundaries()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "inkscape-pattern.svg"));
        RenderBudget measured = new(8_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
            Assert.Equal(178, surface.Width);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 0, work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(8_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget deniedMemory = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedMemory, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, deniedMemory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, deniedMemory.Snapshot().CurrentBytes);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Oversized_tile_mapping_releases_scratch_without_disposing_borrowed_content()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='50000.5' height='8' patternUnits='userSpaceOnUse'><circle cx='4' cy='4' r='2'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
