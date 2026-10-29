using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientMaskTests
{
    [Theory]
    [InlineData("root-partial-gradient-mask", "gradient-masks")]
    [InlineData("shared-mask-twice", "gradient-masks")]
    [InlineData("explicit-region", "gradient-masks")]
    [InlineData("legacy-circle-on-leaf", "gradient-masks")]
    [InlineData("gradient-circle-mask", "gradient-masks")]
    [InlineData("mask-use-gradient-forward", "mask-local-references")]
    [InlineData("mask-clipped-use-forward", "mask-local-references")]
    [InlineData("mask-target-translation", "mask-local-references")]
    [InlineData("gradient-general-map", "mask-local-references")]
    [InlineData("clip-target-translation", "mask-local-references")]
    [InlineData("mask-use-target-reflection", "mask-local-references")]
    [InlineData("inverse-use-mask", "mask-content-order")]
    [InlineData("reversed-use-mask", "mask-content-order")]
    [InlineData("two-reference-mask-leaves", "mask-content-order")]
    [InlineData("alpha-mask-content-order", "mask-content-order")]
    [InlineData("gradient-mask-content-order", "mask-content-order")]
    [InlineData("masked-curved-reference-stroke", "mask-content-order")]
    [InlineData("shared-start-nested-sibling", "mask-group-scopes")]
    [InlineData("empty-inner-masked-sibling", "mask-group-scopes")]
    [InlineData("triple-nested-siblings", "mask-group-scopes")]
    [InlineData("inner-opacity-outer-mask", "mask-group-scopes")]
    [InlineData("shared-group-clip-and-mask", "mask-group-scopes")]
    [InlineData("uniform-inherited-fill-alpha", "mask-presentation")]
    [InlineData("use-source-alpha-override", "mask-presentation")]
    [InlineData("stylesheet-overrides-mask-presentation", "mask-presentation")]
    [InlineData("gradient-mask-inherited-alpha", "mask-presentation")]
    [InlineData("fractional-mask-shader-stroke", "mask-presentation")]
    [InlineData("pattern-ellipse-solid-stroke", "mask-presentation")]
    [InlineData("linear-ellipse-dashed-solid-stroke", "mask-presentation")]
    [InlineData("fractional-explicit-mask-boundary", "mask-presentation")]
    [InlineData("fractional-mask-nested-opacity-and-sibling", "mask-presentation")]
    public void Mask_scenes_release_owners_at_parser_memory_and_raster_work_boundaries(string identity, string family)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", family, identity + ".svg"));
        RenderBudget discovery = new RenderBudget(new HostMemoryBudget(8_000_000));
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, discovery, CancellationToken.None));
        Assert.Equal(0, discovery.Snapshot().CurrentBytes);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            {}
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);

        void DenyMemory(long limit)
        {
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("fill-opacity='NaN'", SvgErrorCode.MalformedInput)]
    [InlineData("style='fill-opacity:2'", SvgErrorCode.MalformedInput)]
    [InlineData("fill-rule='bogus'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("style='opacity:.5'", null)]
    [InlineData("style='bogus-property:1'", SvgErrorCode.UnsupportedFeature)]
    public void Mask_presentation_preserves_value_admission_and_cleanup(string attributes, SvgErrorCode? expected)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><mask id='m' " + attributes + "><rect width='8' height='8'/></mask><rect width='8' height='8' fill='red' mask='url(#m)'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        if (expected is null)
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
            using SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.All(output.Pixels, value => Assert.Equal((byte)0, value));
        }
        else
            Assert.Equal(expected.Value, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Canceled_rasterization_preserves_the_borrowed_mask_scene_and_allows_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "gradient-masks", "gradient-circle-mask.svg"));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
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
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        if (following.Length != 0 && id.Length != 0)
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        else if (paint.Contains("mask=", StringComparison.Ordinal))
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        else using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) Assert.Single(scene.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("gradient-mask-document.svg", 744, 1052)]
    [InlineData("gradient-mask-export.svg", 512, 880)]
    [InlineData("soccerball-mask-document.svg", 195, 195)]
    [InlineData("railway-tunnel-document.svg", 500, 500)]
    [InlineData("shadowed-soccerball-document.svg", 512, 512)]
    public void Complete_mask_document_releases_partial_owners_at_early_middle_and_late_denials(string inputName, int width, int height)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", inputName));
        SvgRenderOptions options = new(new(width, height), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(64_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, measured, CancellationToken.None))
            Assert.Equal(width, surface.Width);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget peakDenied = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, peakDenied, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, peakDenied, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, peakDenied.Snapshot().CurrentBytes);
    }
}
