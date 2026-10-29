using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgClippedPreparationTests
{
    private const string Curve = "M44.5,8 C46.9,7.2 43.1,5.3 41.7,6.5 L41.7,43 C44,55 50,73 52,51 L56,8 Z";
    private const string ScaledInput = "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><g transform='scale(2)'><path d='" + Curve + "' fill='#003478'/></g></svg>";

    [Theory]
    [InlineData("solid")]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public void Escaping_controls_preserve_native_run_fringe_across_four_paints(string material)
    {
        string definitions = "<defs><linearGradient id='linear'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='radial'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='pattern' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='red'/></pattern></defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + definitions + "<path d='" + Curve + "' fill='" + (material == "solid" ? "#003478" : "url(#" + material + ")") + "'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface image = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            // The retained control-bounds browser references have these coverage
            // bytes. Clamping the escaped control to height 64 chooses a mask
            // and loses the opaque snap at (42,6) and zero snap at (45,6).
            Assert.Equal((byte)255, image.Pixels[(6 * image.Width + 42) * 4 + 3]);
            Assert.Equal((byte)0, image.Pixels[(6 * image.Width + 45) * 4 + 3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Wide_dispatch_bounds_keep_run_storage_canvas_bounded()
    {
        SvgDevicePoint[] points = [new(2, 2), new(12, 2), new(6, 12)];
        SvgAnalyticContourPreparation preparation = new(points, [3], [], false, null, SvgPolygonMapping.QuarterEdges, [], [], new(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue), 16, 16);
        Assert.True(preparation.UsesRunBlitter);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 16, 0, 16, new(0, 16), null, SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            walker.PrepareRow(6, CancellationToken.None);
            Assert.Equal(16, walker.Row.Length);
            byte[] pixel = new byte[4];
            walker.PaintPixel(6, pixel, SvgAnalyticPaint.Solid(new(255, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal((byte)255, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Opposite_projected_verticals_cancel_without_flattened_fallback()
    {
        SvgDevicePoint[] points = [new(-4, 4), new(-2, 4), new(-2, 12), new(-4, 12)];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [4], [], false, null, SvgPolygonMapping.FiniteEdges, [], [], 0, 16, 0, 16, 16, 16, new(0, 16), new(new(8, 0, 16, 16)), budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(8, CancellationToken.None);
            // The closed box lies wholly to the left of this clip. Its projected
            // sides have opposite winding and must leave no coverage interval.
            Assert.All(walker.Row.ToArray(), value => Assert.Equal((byte)0, value));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Escaping_control_selects_native_construction_even_when_geometry_fits()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(ScaledInput), SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Contains(polygon.CubicSpans, span => span.SecondControl.Y > 128);
        Assert.All(polygon.Points, point => Assert.InRange(point.Y, 0, 128));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 0, 128, 0, 128, 128, 128, new(0, 128), new(new(0, 0, 128, 128)), budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(12, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(86, pixel, SvgAnalyticPaint.Solid(polygon.Color), SvgAnalyticClipCoverage.Full);
            // The independently frozen scaled control is alpha 168 here;
            // uncut construction produces 192 from the identical retained verb.
            Assert.Equal((byte)168, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Run_link_memory_denial_releases_reserved_clipped_edges()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(ScaledInput), SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        RenderBudget success = new RenderBudget(new HostMemoryBudget(1_000_000));
        long storage;
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 0, 128, 0, 128, 128, 128, new(0, 128), new(new(0, 0, 128, 128)), success, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            storage = success.Snapshot().CurrentBytes;
        }
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(storage - 1));
        SvgException exception = Assert.Throws<SvgException>(() =>
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 0, 128, 0, 128, 128, 128, new(0, 128), new(new(0, 0, 128, 128)), denied, out bool _, CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.True(denied.Snapshot().PeakBytes > 0);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(1, denied.Snapshot().DeniedReservationCount);
    }

    [Fact]
    public void Clipped_preparation_preserves_cancellation_token_before_storage()
    {
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(ScaledInput), SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using CancellationTokenSource source = new();
        source.Cancel();
        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() =>
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 0, 128, 0, 128, 128, 128, new(0, 128), new(new(0, 0, 128, 128)), budget, out bool _, source.Token);
        });
        Assert.Equal(source.Token, exception.CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public async Task Six_outputs_preserve_clipped_preparation_across_four_paints(string material)
    {
        string definitions = "<defs><linearGradient id='linear'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='radial'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='pattern' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='red'/></pattern></defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + definitions + "<path d='" + Curve + "' fill='" + (material == "solid" ? "#003478" : "url(#" + material + ")") + "'/></svg>");
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
