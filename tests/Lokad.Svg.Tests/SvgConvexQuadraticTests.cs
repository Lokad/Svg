using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgConvexQuadraticTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Convex_quadratic_rows_preserve_browser_coverage_after_out_of_order_replay(bool clipped)
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,12 Q8,2 14,12 Z' fill='black'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, [], false, null, SvgPolygonMapping.NativeFillEdges, polygon.QuadSpans, [], new(2, 7, 14, 12), 16, 16);
        SvgAnalyticClipContext? construction = clipped ? new(new(0, 0, 16, 16)) : null;
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 16, 0, 16, new(0, 16), construction, SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(true, preparation.Convex);
            foreach (int row in new[] { 8, 11, 7, 10, 9, 8 })
            {
                walker.PrepareRow(row, CancellationToken.None);
                if (row != 8) continue;
                // These alpha bytes come from the unchanged quadratic-solid-16x16
                // browser reference, including the formerly overpainted tip fringe.
                foreach ((int x, byte alpha) in new (int, byte)[] { (3, 0), (4, 71), (5, 243), (10, 243), (11, 71), (12, 0) })
                {
                    byte[] pixel = new byte[4];
                    walker.PaintPixel(x, pixel, SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
                    Assert.Equal(alpha, pixel[3]);
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
