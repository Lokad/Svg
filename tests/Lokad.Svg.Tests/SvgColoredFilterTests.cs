using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgColoredFilterTests
{
    [Theory]
    [InlineData("flood-composite-in")]
    [InlineData("colored-blend-multiply")]
    [InlineData("small-anisotropic-fractional-offset")]
    [InlineData("arc-polyline-markers")]
    [InlineData("colored-signal-diagram")]
    [InlineData("fractional-offset-only")]
    [InlineData("offset-identity-result")]
    public void Colored_filter_source_capture_releases_owners_after_denial_cancellation_and_retry(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "colored-effects-markers", identity + ".svg"));
        byte[] unchanged = input.ToArray();
        RenderBudget measured = new(2_000_000);
        byte[] expected;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            expected = surface.Pixels.ToArray();
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);

        RenderBudget tight = new(Math.Max(1, measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, tight, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);

        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
        {
            long retained = measured.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, cancellation.Token));
            Assert.Equal(retained, measured.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Fact]
    public void Identity_windows_retain_source_image_bounds_before_fractional_displacement()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "colored-effects-markers", "fractional-offset-only.svg"));
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        string source = System.Text.Encoding.UTF8.GetString(input);
        foreach (string sigma in new[] { "0", ".4" })
        {
            byte[] identity = System.Text.Encoding.UTF8.GetBytes(source.Replace("<feOffset", "<feGaussianBlur stdDeviation='" + sigma + "'/><feOffset", StringComparison.Ordinal));
            Assert.Equal(expected, SvgConverter.ConvertToPng(identity, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        }
    }

    [Theory]
    [InlineData("sRGB")]
    [InlineData("linearRGB")]
    public void Binary_graph_inputs_and_color_conversions_obey_work_and_storage_limits(string colorSpace)
    {
        byte[] input = Document("<feFlood flood-color='#c89c35' flood-opacity='.5' result='tint'/>"
            + "<feComposite in='tint' in2='SourceAlpha' operator='in' result='cut'/>"
            + "<feBlend in='SourceGraphic' in2='cut' mode='multiply'/>", colorSpace);
        RenderBudget parser = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            byte[] source = [100, 40, 20, 128];
            byte[] unchanged = source.ToArray();
            RenderBudget budget = new(1_000_000);
            byte[] expected;
            long work;
            using (SvgFilterResult result = SvgFilterPlayback.Apply(source, 1, 1, resource, new(1, 1), budget, long.MaxValue, CancellationToken.None))
            { expected = result.Pixels.ToArray(); work = result.WorkUnits; }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgFilterPlayback.Apply(source, 1, 1, resource, new(1, 1), budget, work - 1, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            RenderBudget tight = new(Math.Max(1, budget.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgFilterPlayback.Apply(source, 1, 1, resource, new(1, 1), tight, work, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            using (SvgFilterResult retry = SvgFilterPlayback.Apply(source, 1, 1, resource, new(1, 1), budget, work, CancellationToken.None))
                Assert.Equal(expected, retry.Pixels);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, source);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<feComposite operator='arithmetic'/>")]
    [InlineData("<feBlend mode='darken'/>")]
    [InlineData("<feComposite in2='later'/><feFlood result='later'/>")]
    [InlineData("<feBlend in2='missing'/>")]
    [InlineData("<feFlood in='SourceGraphic'/>")]
    public void Unsupported_binary_graphs_publish_no_output_and_release_parser_owners(string graph)
    {
        byte[] input = Document(graph, "sRGB");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using MemoryStream output = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgConverter.ConvertToPng(input, output, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
    }

    private static byte[] Document(string graph, string colorSpace)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs><filter id='f' color-interpolation-filters='"
            + colorSpace + "'>" + graph + "</filter></defs><rect width='4' height='4' filter='url(#f)'/></svg>");
}
