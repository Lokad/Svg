using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGroupMaskCropTests
{
    [Theory]
    [InlineData("fractional-luminance-crop")]
    [InlineData("nested-crop")]
    [InlineData("rotated-elliptical-stroke")]
    public void Spatial_fallback_and_arc_strokes_release_owners_on_sampled_denial(string identity)
    {
        byte[] input = Read(identity);
        RenderBudget measured = new(8_000_000);
        long rasterWork;
        byte[] expected;
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
        {
            expected = surface.Pixels.ToArray();
            rasterWork = surface.WorkUnits;
            SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
            Assert.All(storage.MaskContents, mask => Assert.Null(mask.UniformSource));
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget memory = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, memory, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, memory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, memory.Snapshot().CurrentBytes);
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
            defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
            checked((int)(rasterWork - 1)), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
        RenderBudget raster = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, limits, raster, null, CancellationToken.None))
        {
            long retained = raster.Snapshot().CurrentBytes;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, limits), raster, CancellationToken.None)).ErrorCode);
            Assert.Equal(retained, raster.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, raster.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Covering_uniform_mask_keeps_the_scalar_route()
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(Read("covering-uniform-preserved"), SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
            Assert.Empty(storage.MaskContents);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Contains(surface.Pixels, alpha => alpha != 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("isolation", "auto")]
    [InlineData("mix-blend-mode", "normal")]
    [InlineData("color-rendering", "auto")]
    [InlineData("image-rendering", "auto")]
    [InlineData("shape-rendering", "auto")]
    [InlineData("text-rendering", "auto")]
    [InlineData("color-interpolation-filters", "linearRGB")]
    [InlineData("solid-color", "#123456")]
    [InlineData("solid-opacity", ".4")]
    public void Neutral_exporter_declarations_preserve_geometry_and_paint(string property, string value)
    {
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='red'";
        byte[] expected = SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes(prefix + "/></svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] actual = SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes(prefix + " style='" + property + ":" + value + "!important'/></svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("isolation:isolate", SvgErrorCode.UnsupportedFeature)]
    [InlineData("mix-blend-mode:multiply", SvgErrorCode.UnsupportedFeature)]
    [InlineData("shape-rendering:crispEdges", SvgErrorCode.UnsupportedFeature)]
    [InlineData("solid-opacity:NaN", SvgErrorCode.MalformedInput)]
    [InlineData("solid-color:bogus", SvgErrorCode.UnsupportedFeature)]
    public void Meaningful_or_invalid_exporter_values_reject_without_owners(string declaration, SvgErrorCode expected)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' style='" + declaration + "'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<g transform='translate(1)' mask='url(#m)'><rect width='8' height='8'/></g>")]
    [InlineData("<use href='#r' mask='url(#m)'/>")]
    [InlineData("<path d='M0 0A4 2 0 0 1 8 4' fill='none' stroke='red' transform='matrix(1 .2 .1 1 0 0)'/>")]
    public void Instance_and_width_admission_preserves_output_and_cleanup(string geometry)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect width='4' height='8' fill='white'/></mask><rect id='r' width='8' height='8'/></defs>" + geometry + "</svg>");
        RenderBudget budget = new(1_000_000);
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
            using SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Contains(output.Pixels, value => value != 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancellation_preserves_the_borrowed_spatial_scene_and_retry_pixels()
    {
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(Read("nested-crop"), SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                expected = surface.Pixels.ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            int seen = 0;
            using (SvgAnalyticPaintProbe.Arm((phase, _) =>
            {
                if (phase == SvgAnalyticPaintPhase.BeforeReplay && ++seen == 3) cancellation.Cancel();
            }))
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                {
                    using SvgConverter.RasterizedSurface canceled = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token);
                }).CancellationToken);
            Assert.Equal(3, seen);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Read(string identity)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "group-mask-crop", identity + ".svg"));
}
