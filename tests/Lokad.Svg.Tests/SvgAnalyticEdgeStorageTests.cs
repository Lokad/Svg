using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgAnalyticEdgeStorageTests
{
    [Theory]
    [InlineData("single")]
    [InlineData("compound")]
    [InlineData("clipped")]
    public void Completed_edge_storage_survives_collection_and_exact_row_rewind(string route)
    {
        string path = route switch
        {
            "single" => "M2 8Q8 0 14 8Q8 16 2 8Z",
            "compound" => "M1 4Q4 0 7 4Q4 8 1 4Z M9 12Q12 8 15 12Q12 16 9 12Z",
            "clipped" => "M-5 8Q8 -8 21 8Q8 24 -5 8Z",
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='" + path + "'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, [], false, null,
            polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans,
            route == "clipped" ? new(-5, -8, 21, 24) : new(0, 0, 16, 16), 16, 16);
        SvgAnalyticCoverage coverage = SvgAnalyticCoverage.Prepare(ref preparation, 0, 16, 0, 16, new(0, 16),
            route == "clipped" ? new SvgAnalyticClipContext(new(0, 0, 16, 16)) : null,
            SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
        try
        {
            Assert.True(eligible);
            Assert.True(coverage.EdgeCount > 0);
            byte[] expected = Paint(ref coverage);
            Assert.Contains(expected, value => value != 0);
            long retained = budget.Snapshot().CurrentBytes;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            Assert.Equal(expected, Paint(ref coverage));
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        finally { coverage.Dispose(); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static byte[] Paint(ref SvgAnalyticCoverage walker)
        {
            byte[] pixels = new byte[16 * 16 * 4];
            SvgAnalyticPaint paint = SvgAnalyticPaint.Solid(new(27, 103, 181, 211));
            for (int y = 0; y < 16; y++)
            {
                walker.PrepareRow(y, CancellationToken.None);
                for (int x = 0; x < 16; x++)
                    walker.PaintPixel(x, pixels.AsSpan((y * 16 + x) * 4, 4), paint, SvgAnalyticClipCoverage.Full);
            }
            return pixels;
        }
    }
}
