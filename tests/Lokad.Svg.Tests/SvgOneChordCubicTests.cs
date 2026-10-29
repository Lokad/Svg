using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOneChordCubicTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Retained_cubic_keeps_native_coverage_when_its_flattening_has_one_chord(bool pair)
    {
        string path = "M8,8 C10.5,8.25 13.5,8 16,8.125 L16,24 L8,24 Z";
        if (pair) path += " M40,8 C42.5,8.25 45.5,8 48,8.125 L48,24 L40,24 Z";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='" + path + "' fill='#003478'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Equal(pair ? 2 : 1, polygon.CubicSpans.Length);
        foreach (SvgCubicSpan span in polygon.CubicSpans) Assert.Equal(1, span.EndIndex - span.StartIndex);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 8, pair ? 48 : 16, 8, 24, 64, 64, new(0, 64), budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(8, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(1, pixel, SvgAnalyticPaint.Solid(polygon.Color), SvgAnalyticClipCoverage.Full);
            // Pinned native shoulder capture is fully covered at (9,8).
            Assert.Equal((byte)255, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
