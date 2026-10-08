using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientMaskTests
{
    [Theory]
    [InlineData("root-partial-gradient-mask")]
    [InlineData("shared-mask-twice")]
    [InlineData("explicit-region")]
    [InlineData("legacy-circle-on-leaf")]
    [InlineData("gradient-circle-mask")]
    public void Mask_scenes_release_owners_at_parser_memory_and_raster_work_boundaries(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "gradient-masks", identity + ".svg"));
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
    public void Canceled_rasterization_preserves_the_borrowed_mask_scene_and_allows_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "gradient-masks", "gradient-circle-mask.svg"));
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token)).CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(40, retry.Width);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("circle", "fill='#808080'", "", "", "")]
    [InlineData("circle", "fill='#808080'", "", "", "<mask id='m'/>")]
    [InlineData("circle", "fill='#808080'", "id='m'", "", "<mask id='m'/>")]
    [InlineData("rect", "fill='url(#a)'", "id='m'", "", "<mask id='m'/>")]
    [InlineData("rect", "fill='url(#a)'", "id='m'", "", "<mask id='m'><rect width='8' height='8'/></mask>")]
    [InlineData("circle", "fill='#ff0000'", "id='m'", "", "")]
    [InlineData("rect", "fill='url(#a)'", "id='m'", "maskContentUnits='objectBoundingBox'", "")]
    [InlineData("rect", "fill='url(#a)' mask='url(#m)'", "id='m'", "", "")]
    public void Unused_anonymous_or_rejected_masks_release_partial_owners(string shape, string paint, string id, string units, string following)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='a'><stop/><stop offset='1' stop-color='white'/></linearGradient><mask " + id + " " + units + "><" + shape + " cx='4' cy='4' r='3' width='8' height='8' " + paint + "/></mask>" + following + "</defs><rect width='8' height='8' fill='red'/></svg>";
        // Circle-specific attributes are not valid on rectangles, and vice versa.
        svg = shape == "circle" ? svg.Replace(" width='8' height='8' " + paint, " " + paint, StringComparison.Ordinal)
            : svg.Replace(" cx='4' cy='4' r='3'", "", StringComparison.Ordinal);
        RenderBudget budget = new(1_000_000);
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        if (following.Length != 0 && id.Length != 0)
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        else if (paint.Contains("#ff0000", StringComparison.Ordinal) || units.Length != 0 || paint.Contains("mask=", StringComparison.Ordinal))
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        else using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) Assert.Single(scene.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Complete_mask_document_releases_partial_owners_at_early_middle_and_late_denials()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "gradient-mask-document.svg"));
        RenderBudget measured = new(64_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
            Assert.Equal(744, surface.Width);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 0L, work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(64_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget peakDenied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, peakDenied, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, peakDenied, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, peakDenied.Snapshot().CurrentBytes);
    }
}
