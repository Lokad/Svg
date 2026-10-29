using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgConvexRectangleStripTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Clipped_triangle_tail_matches_native_rounded_coverage(bool antialiased)
    {
        SvgAnalyticPaintClip paintClip = antialiased ? SvgAnalyticPaintClip.Antialiased : SvgAnalyticPaintClip.Binary;
        // Original browser path/mapping in inv-027-native-route.json. The pinned
        // convex walker emits 71 + 25 at (457,258), using either supplied edge
        // states or the original path with native mapping and clipped construction.
        SvgDevicePoint[] points = [new(457.6119857788086, 571.416015625), new(754.5080047607422, 400.01598358154297), new(457.6119857788086, 228.6132049560547)];
        SvgAnalyticContourPreparation preparation = new(points, [3], [], false, null, SvgPolygonMapping.QuarterEdges, [], [], new(457, 228, 755, 572), 1144, 800);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 457, 510, 254, 510, new(254, 510), new(new(254, 254, 510, 510)), paintClip, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.False(preparation.DesnapBox);
            walker.PrepareRow(258, CancellationToken.None);
            Assert.Equal((byte)96, walker.Row[0]);
            byte[] pixel = [128, 128, 128, 255];
            walker.PaintPixel(0, pixel, SvgAnalyticPaint.Solid(new(255, 255, 255, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal(new byte[] { 176, 176, 176, 255 }, pixel);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Raw_fractional_box_preserves_its_separate_coverage_calibration()
    {
        SvgDevicePoint[] points = [new(3.609375, 2.25), new(28.5, 2.25), new(28.5, 30.25), new(3.609375, 30.25)];
        SvgAnalyticContourPreparation preparation = new(points, [4], [], false, null, SvgPolygonMapping.QuarterEdges, [], [], new(3, 2, 29, 31), 32, 32);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 3, 29, 2, 31, new(0, 32), null, SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.True(preparation.DesnapBox);
            Assert.False(preparation.UsesRunBlitter);
            walker.PrepareRow(10, CancellationToken.None);
            Assert.Equal((byte)99, walker.Row[0]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
