using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAnalyticScanWindowTests
{
    private const string FirstCircle = "M16,50C16,41.163444002707301,23.163444002707301,34,32,34C40.836555997292699,34,48,41.163444002707301,48,50C48,58.836555997292699,40.836555997292699,66,32,66C23.163444002707301,66,16,58.836555997292699,16,50Z";
    private const string SecondCircle = "M52,50C52,41.163444002707301,59.163444002707301,34,68,34C76.836555997292692,34,84,41.163444002707301,84,50C84,58.836555997292699,76.836555997292692,66,68,66C59.163444002707301,66,52,58.836555997292699,52,50Z";

    [Theory]
    [InlineData(100)]
    [InlineData(70_000)]
    public void Run_walk_uses_clip_window_without_expanding_path_storage(int canvasWidth)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><path d='" + FirstCircle + " " + SecondCircle + "' fill='#003478'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        RenderBudget canvasBudget = new(1_000_000);
        RenderBudget tightBudget = new(1_000_000);
        using SvgAnalyticCoverage canvas = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 16, 84, 34, 66, canvasWidth, 100, new(0, canvasWidth), canvasBudget, out long canvasSetup, out bool canvasEligible, CancellationToken.None);
        using SvgAnalyticCoverage tight = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 16, 84, 34, 66, 100, 100, new(16, 84), tightBudget, out long tightSetup, out bool tightEligible, CancellationToken.None);
        Assert.True(canvasEligible && tightEligible);
        Assert.Equal(canvasSetup, tightSetup);
        Assert.Equal(canvasBudget.Snapshot().CurrentBytes, tightBudget.Snapshot().CurrentBytes);
        Assert.Equal(68, canvas.Row.Length);
        canvas.PrepareRow(49, out long _, CancellationToken.None);
        tight.PrepareRow(49, out long _, CancellationToken.None);
        byte[] canvasPixel = new byte[4];
        byte[] tightPixel = new byte[4];
        SvgAnalyticPaint paint = SvgAnalyticPaint.Solid(polygon.Color);
        canvas.PaintPixel(0, canvasPixel, paint, SvgAnalyticClipCoverage.Full);
        tight.PaintPixel(0, tightPixel, paint, SvgAnalyticClipCoverage.Full);
        // Native unclipped and tight-clip captures differ at this fringe.
        Assert.Equal((byte)250, canvasPixel[3]);
        Assert.Equal((byte)245, tightPixel[3]);
    }

    [Fact]
    public void Mask_walk_keeps_its_path_window_inside_a_large_canvas()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><path d='" + FirstCircle + "' fill='#003478'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        RenderBudget budget = new(1_000_000);
        using SvgAnalyticCoverage canvas = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 16, 48, 34, 66, 100, 100, new(0, 100), budget, out long _, out bool canvasEligible, CancellationToken.None);
        using SvgAnalyticCoverage tight = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 16, 48, 34, 66, 100, 100, new(16, 48), budget, out long _, out bool tightEligible, CancellationToken.None);
        Assert.True(canvasEligible && tightEligible);
        canvas.PrepareRow(49, out long _, CancellationToken.None);
        tight.PrepareRow(49, out long _, CancellationToken.None);
        byte[] canvasPixels = new byte[32 * 4];
        byte[] tightPixels = new byte[32 * 4];
        SvgAnalyticPaint paint = SvgAnalyticPaint.Solid(polygon.Color);
        for (int x = 0; x < 32; x++)
        {
            canvas.PaintPixel(x, canvasPixels.AsSpan(x * 4, 4), paint, SvgAnalyticClipCoverage.Full);
            tight.PaintPixel(x, tightPixels.AsSpan(x * 4, 4), paint, SvgAnalyticClipCoverage.Full);
        }
        Assert.Equal(canvasPixels, tightPixels);
    }
}
