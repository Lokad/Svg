using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgViewportPercentageTests
{
    [Theory]
    [InlineData("width='0' height='12'")]
    [InlineData("width='12' height='0'")]
    public void Empty_affine_viewports_paint_nothing_and_restore_the_sibling(string dimensions)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
            + "<g transform='skewX(20)'><svg x='4' y='4' " + dimensions + "><rect width='12' height='12' fill='blue'/></svg></g>"
            + "<rect x='24' y='24' width='4' height='4' fill='red'/></svg>");
        RenderBudget sceneBudget = new(1_000_000), rasterBudget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, rasterBudget, CancellationToken.None))
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                int offset = (y * 32 + x) * 4;
                byte red = x is >= 24 and < 28 && y is >= 24 and < 28 ? (byte)255 : (byte)0;
                Assert.Equal(red, raster.Pixels[offset]); Assert.Equal(0, raster.Pixels[offset + 1]);
                Assert.Equal(0, raster.Pixels[offset + 2]); Assert.Equal(red, raster.Pixels[offset + 3]);
            }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes); Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("translate(18 12)", 30, 24, 18, 14, 22, 58, 80, 20)]
    [InlineData("translate(6 8) scale(2 1)", 30, 20, 36, 14, 14, 54, 160, 20)]
    [InlineData("translate(230 10) scale(-1 1)", 200, 22, 18, 14, 146, 56, 80, 20)]
    public void Outer_axis_maps_stay_outside_nested_viewBox_scaling_and_clipping(string ancestor,
        int x, int y, int width, int height, int stripeX, int stripeY, int stripeWidth, int stripeHeight)
    {
        string nested = "<g transform='" + ancestor + "'><svg x='4' y='6' width='80' height='60' viewBox='0 0 40 30'>"
            + "<g transform='translate(3 2)'><rect x='1' y='1' width='9' height='7' fill='#285a93'/></g>"
            + "<rect x='-4' y='20' width='48' height='16' fill='#a44f32'/></svg></g>";
        string explicitShapes = FormattableString.Invariant($"<rect x='{x}' y='{y}' width='{width}' height='{height}' fill='#285a93'/><rect x='{stripeX}' y='{stripeY}' width='{stripeWidth}' height='{stripeHeight}' fill='#a44f32'/>");
        Assert.Equal(Render(explicitShapes), Render(nested));

        static byte[] Render(string body)
            => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='160'>" + body
                + "<rect x='240' y='140' width='10' height='10' fill='#338855'/></svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
    }

    [Fact]
    public void Root_percentages_use_the_viewBox_extent()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='96' viewBox='0 0 64 48'><svg x='25%' y='25%' width='50%' height='50%'><rect width='8' height='8' fill='red'/></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new SvgRectangle(16, 12, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Nested_percentages_use_the_local_viewBox_extent()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='96'><svg x='8' y='6' width='64' height='48' viewBox='0 0 32 24'><svg x='25%' y='25%' width='50%' height='50%'><rect width='8' height='8' fill='red'/></svg></svg></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new SvgRectangle(24, 18, 16, 16, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Percentage_root_use_positions_resolve_before_instance_mapping()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='100%'><defs><rect id='r' width='8' height='8' fill='red'/></defs><use href='#r' x='25%' y='25%'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new SvgRectangle(75, 37.5, 8, 8, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
    }

    [Fact]
    public void Discovery_keeps_later_font_requests_after_a_percentage_viewport()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 48'><svg width='50%' height='50%'><rect width='8' height='8'/></svg><text font-family='After' font-size='12' y='12'>A</text></svg>"u8.ToArray();
        RenderBudget budget = new(8_000_000);
        Assert.Equal("After", Assert.Single(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(1_000_000), budget, CancellationToken.None)).Family);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("root-match-transparent")]
    [InlineData("root-symbol-default-transparent")]
    [InlineData("root-native-percent-transparent")]
    [InlineData("nested-match-transparent")]
    [InlineData("nested-use-scaled-transparent")]
    [InlineData("deep-nested-percent-transparent")]
    [InlineData("root-percent-no-viewbox-transparent")]
    [InlineData("nested-image-percent-transparent")]
    [InlineData("root-viewbox-only-transparent")]
    public void Viewport_percentage_bases_release_all_owners_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "viewport-percentage-bases", identity + ".svg"));
        SvgRenderOptions options = identity.StartsWith("root-viewbox-only-", StringComparison.Ordinal)
            ? new(new SvgViewport(128, 96), 1, SvgColor.Transparent, SvgRenderLimits.Default) : SvgRenderOptions.Default;
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
        // Percentage resolution adds no owners; native instance replay and image storage use the existing budget.
        RenderBudget measured = new(8_000_000);
        ParserWorkCounter work = new(1_000_000);
        long rasterWork;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None, 1, 1, options.Viewport))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            rasterWork = surface.WorkUnits;
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget budget = new(8_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(limit), CancellationToken.None, 1, 1, options.Viewport)).ErrorCode);
            Assert.True(budget.Snapshot().CurrentBytes == 0, identity + " retained storage at parser work limit " + limit + ".");
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
            using (SvgRoot scene = SvgRootParser.Parse(input, limits, budget, null, new(1_000_000), CancellationToken.None, 1, 1, options.Viewport))
            {
                SvgRenderOptions limited = new(options.Viewport, 1, SvgColor.Transparent, limits);
                if (limit < rasterWork) Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, limited, budget, CancellationToken.None)).ErrorCode);
                else
                {
                    using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, limited, budget, CancellationToken.None);
                    Assert.Equal(rasterWork, surface.WorkUnits);
                }
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new(8_000_000);
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, new(1_000_000), cancellation.Token, 1, 1, options.Viewport)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, new(1_000_000), CancellationToken.None, 1, 1, options.Viewport))
        {
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, options, canceled, cancellation.Token)).CancellationToken);
            using SvgConverter.RasterizedSurface retried = SvgConverter.Rasterize(scene, options, canceled, CancellationToken.None);
            Assert.Equal(rasterWork, retried.WorkUnits);
        }
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget budget = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None, 1, 1, options.Viewport);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }
}
