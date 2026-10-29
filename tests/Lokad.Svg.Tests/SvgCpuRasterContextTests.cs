using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCpuRasterContextTests
{
    private const string Curve = "M24,36 C190,-4 440,76 612,34 L612,270 C490,296 405,208 330,254 L300,164 L270,252 C180,212 92,304 24,270 Z";
    private const string Input = "<svg xmlns='http://www.w3.org/2000/svg' width='640' height='320'><path d='" + Curve + "' fill='#003478'/></svg>";

    [Theory]
    [InlineData(250, 100, 250, 100, 1)]
    [InlineData(500, 200, 500, 200, 1)]
    [InlineData(1000, 400, 256, 256, 8)]
    [InlineData(512, 255, 512, 255, 1)]
    [InlineData(513, 255, 512, 255, 2)]
    [InlineData(512, 256, 256, 256, 3)]
    [InlineData(513, 256, 256, 256, 3)]
    [InlineData(255, 513, 255, 512, 2)]
    [InlineData(256, 513, 256, 256, 3)]
    public void Pinned_cpu_policy_partitions_owned_pixels_at_dimension_thresholds(int width, int height, int firstWidth, int firstHeight, int count)
    {
        SvgCpuRasterGrid grid = new(width, height);
        int[] visits = new int[width * height];
        int contexts = 0;
        foreach (SvgCpuRasterContext context in grid.GetContexts(new(0, 0, width, height)))
        {
            if (contexts++ == 0) Assert.Equal(new SvgAnalyticClipBounds(0, 0, firstWidth, firstHeight), context.BitmapClip);
            Assert.True(context.BitmapClip.Left <= context.Interior.Left && context.BitmapClip.Top <= context.Interior.Top);
            Assert.True(context.BitmapClip.Right >= context.Interior.Right && context.BitmapClip.Bottom >= context.Interior.Bottom);
            for (int y = context.Interior.Top; y < context.Interior.Bottom; y++)
                for (int x = context.Interior.Left; x < context.Interior.Right; x++) visits[y * width + x]++;
        }
        Assert.Equal(count, contexts);
        Assert.All(visits, value => Assert.Equal(1, value));
    }

    [Fact]
    public void Small_drawing_crossing_a_border_visits_both_interiors_and_empty_drawings_visit_none()
    {
        SvgCpuRasterGrid grid = new(1000, 400);
        List<SvgCpuRasterContext> contexts = [];
        foreach (SvgCpuRasterContext context in grid.GetContexts(new(254, 10, 256, 11))) contexts.Add(context);
        Assert.Equal(2, contexts.Count);
        Assert.Equal(new SvgAnalyticClipBounds(0, 0, 256, 256), contexts[0].BitmapClip);
        Assert.Equal(new SvgAnalyticClipBounds(254, 0, 510, 256), contexts[1].BitmapClip);
        Assert.Equal(255, contexts[0].Interior.Right);
        Assert.Equal(255, contexts[1].Interior.Left);
        foreach (SvgCpuRasterContext _ in grid.GetContexts(new(254, 10, 254, 11))) Assert.Fail("Empty drawing must not visit a tile.");
        foreach (SvgCpuRasterContext _ in grid.GetContexts(new(254, 10, 256, 10))) Assert.Fail("Empty drawing must not visit a tile.");
    }

    [Fact]
    public void Last_pixel_on_a_large_canvas_needs_one_context_without_overflow_or_a_tile_tape()
    {
        SvgCpuRasterGrid grid = new(int.MaxValue, int.MaxValue);
        int count = 0;
        foreach (SvgCpuRasterContext context in grid.GetContexts(new(int.MaxValue - 1, int.MaxValue - 1, int.MaxValue, int.MaxValue)))
        {
            count++;
            Assert.Equal(int.MaxValue, context.Interior.Right);
            Assert.Equal(int.MaxValue, context.Interior.Bottom);
        }
        Assert.Equal(1, count);
    }

    [Fact]
    public void Cached_classification_and_storage_preserve_native_dispatch()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(Input), SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, new(24, 24, 612, 278), 640, 320);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 254, 510, 24, 278, new(254, 510), new(new(254, 0, 510, 256)), SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(256, walker.Row.Length);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 254, 510, 24, 278, new(254, 510), new(new(254, 0, 510, 256)), SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        // Reusing classification removes work once, independently of its internal algorithm.
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 254, 510, 24, 278, new(254, 510), new(new(254, 0, 510, 256)), SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        SvgAnalyticContourPreparation fresh = new(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, new(24, 24, 612, 278), 640, 320);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref fresh, 254, 510, 24, 278, new(254, 510), new(new(254, 0, 510, 256)), SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        // A narrow storage interval still uses the whole path's run dispatch.
        using SvgAnalyticCoverage narrow = SvgAnalyticCoverage.Prepare(ref preparation, 254, 270, 24, 278, new(254, 270), new(new(254, 0, 270, 256)), SvgAnalyticPaintClip.Binary, budget, out bool narrowEligible, CancellationToken.None);
        Assert.True(narrowEligible);
        Assert.True(preparation.UsesRunBlitter);
    }

    [Fact]
    public void Cached_context_memory_refusal_and_cancellation_release_every_owner()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(Input), SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, new(24, 24, 612, 278), 640, 320);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 254, 510, 24, 278, new(254, 510), new(new(254, 0, 510, 256)), SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        long storage;
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 254, 510, 24, 278, new(254, 510), new(new(254, 0, 510, 256)), SvgAnalyticPaintClip.Binary, budget, out bool _, CancellationToken.None);
            storage = budget.Snapshot().CurrentBytes;
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(storage - 1));
        try
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 254, 510, 24, 278, new(254, 510), new(new(254, 0, 510, 256)), SvgAnalyticPaintClip.Binary, denied, out bool _, CancellationToken.None);
            Assert.Fail("Edge and run-link reservations must fit together.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Equal(1, denied.Snapshot().DeniedReservationCount);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource source = new();
        source.Cancel();
        try
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 254, 510, 24, 278, new(254, 510), new(new(254, 0, 510, 256)), SvgAnalyticPaintClip.Binary, budget, out bool _, source.Token);
            Assert.Fail("Cached geometry must still observe cancellation.");
        }
        catch (OperationCanceledException exception) { Assert.Equal(source.Token, exception.CancellationToken); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public async Task Six_outputs_agree_across_bordered_contexts_and_four_paints(string material)
    {
        string definitions = "<defs><linearGradient id='linear'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='radial'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='pattern' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/></pattern></defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='640' height='320'>" + definitions + "<path d='" + Curve + "' fill='" + (material == "solid" ? "#003478" : "url(#" + material + ")") + "'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometryKind kind = material switch { "solid" => SvgGeometryKind.PolygonFill, "linear" => SvgGeometryKind.GradientPolygon, "radial" => SvgGeometryKind.RadialPolygon, "pattern" => SvgGeometryKind.PatternPolygon, _ => throw new ArgumentOutOfRangeException(nameof(material)) };
        Assert.Equal(kind, Assert.Single(root.Geometries).Kind);
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        byte[] asyncDestination = new byte[expected.Length];
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncDestination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncDestination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }
}
