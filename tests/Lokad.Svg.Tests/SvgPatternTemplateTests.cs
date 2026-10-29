using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternTemplateTests
{
    [Theory]
    [InlineData("pattern-inheritance", "forward-circle-chain")]
    [InlineData("pattern-inheritance", "legacy-rectangle-alias")]
    [InlineData("pattern-inheritance", "overridden-dimensions")]
    [InlineData("pattern-inheritance", "fractional-grid-origin")]
    [InlineData("pattern-inheritance", "scaled-fractional-grid")]
    [InlineData("pattern-inheritance", "alias-paint-in-retained-leaf")]
    [InlineData("pattern-affine-placement", "root-path-tile")]
    [InlineData("pattern-affine-placement", "scale-nonuniform")]
    [InlineData("pattern-affine-placement", "curved-content-scale")]
    [InlineData("pattern-affine-placement", "inherited-scale")]
    [InlineData("pattern-affine-placement", "tiny-fractional-grid")]
    [InlineData("pattern-general-placement", "hatch-rotate")]
    [InlineData("pattern-general-placement", "hatch-reflected")]
    [InlineData("pattern-general-placement", "hatch-fractional-output")]
    [InlineData("pattern-general-placement", "shear-single-rectangle")]
    [InlineData("pattern-general-placement", "inherited-rotated-grid")]
    [InlineData("pattern-general-placement", "hatch-tiled-curved-target")]
    [InlineData("paint-order", "path-opacity")]
    [InlineData("paint-order", "linear-stroked-rectangle")]
    [InlineData("paint-order", "symbol-container-inheritance")]
    [InlineData("paint-order", "symbol-nested-use")]
    [InlineData("paint-order", "pattern-content-inheritance")]
    [InlineData("paint-order", "pattern-presentation-alias")]
    public void Template_scenes_release_owners_at_parser_memory_and_raster_work_boundaries(string family, string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", family, identity + ".svg"));
        RenderBudget discovery = new(8_000_000);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, discovery, CancellationToken.None));
        Assert.Equal(0, discovery.Snapshot().CurrentBytes);
        RenderBudget measured = new(8_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            {}
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);

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
        // The retained contexts now include independently resolved element opacity.
        RenderBudget budget = new(10_000_000);
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(text.ToString()), SvgRenderLimits.Default, budget, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((4 * 8 + 4) * 4, 4).ToArray());
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

    [Theory]
    [InlineData("inkscape-pattern", 178, 0)]
    [InlineData("striped-pattern", 374, 0)]
    [InlineData("set-screw", 512, 538)]
    public void Full_pattern_document_releases_partial_owners_at_work_and_memory_boundaries(string identity, int width, int explicitHeight)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", identity + ".svg"));
        SvgRenderOptions options = explicitHeight == 0 ? SvgRenderOptions.Default : new(new(width, explicitHeight), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget measured = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, measured, CancellationToken.None))
            Assert.Equal(width, surface.Width);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget deniedMemory = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedMemory, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, deniedMemory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, deniedMemory.Snapshot().CurrentBytes);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
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

    [Theory]
    [InlineData("matrix(1 1 1 1 0 0)")]
    [InlineData("matrix(0 0 0 1 0 0)")]
    public void Singular_pattern_maps_reject_and_release_partial_owners(string transform)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><pattern id='prior' width='4' height='4' patternUnits='userSpaceOnUse'><circle cx='2' cy='2' r='1'/></pattern><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse' patternTransform='" + transform + "'><circle cx='2' cy='2' r='1'/></pattern><rect width='8' height='8' fill='url(#p)'/></svg>");
        RenderBudget budget = new(1_000_000);
        SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal("Only nonsingular finite matrix() transforms are implemented.", error.Message);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("scale(10000 1)")]
    [InlineData("rotate(-45) scale(10000 1)")]
    [InlineData("rotate(23) scale(-10000 1)")]
    public void Oversized_pattern_transform_denies_and_allows_retry_with_the_same_borrowed_scene(string transform)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse' patternTransform='" + transform + "'><circle cx='2' cy='2' r='1' fill='red'/></pattern><rect width='8' height='8' fill='url(#p)'/></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            SvgRenderOptions smaller = new(null, 0.125, SvgColor.Transparent, SvgRenderLimits.Default);
            byte[] first;
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, smaller, budget, CancellationToken.None))
                first = retry.Pixels.ToArray();
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, smaller, budget, CancellationToken.None))
                Assert.Equal(first, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
