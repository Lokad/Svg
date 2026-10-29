using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgZeroAreaStrokeContourTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Native_stroke_winding_keeps_zero_signed_area_contours(bool multiple, bool clipped)
    {
        // A zero signed sum does not make the bowtie's two lobes empty.
        SvgDevicePoint[] points = multiple
            ? [new(0, 0), new(8, 8), new(8, 0), new(0, 8), new(12, 1), new(16, 1), new(16, 5), new(12, 5)]
            : [new(0, 0), new(8, 8), new(8, 0), new(0, 8)];
        int[] ends = multiple ? [4, 8] : [4];
        RenderBudget budget = new(1_000_000);
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, ends, [], false, null, SvgPolygonMapping.NativeStrokeEdges,
            [], [], 0, 16, 0, 16, 16, 16, new(0, 16), clipped ? new(new(0, 0, 16, 16)) : null,
            budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            walker.PrepareRow(2, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(1, pixel, SvgAnalyticPaint.Solid(new(0, 0, 255, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal((byte)255, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
