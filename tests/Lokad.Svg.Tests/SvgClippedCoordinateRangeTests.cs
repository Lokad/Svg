using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanisms")]
public sealed class SvgClippedCoordinateRangeTests
{
    [Fact]
    public void Retained_round_cap_controls_select_native_convex_coverage_after_clipping()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='translate(16 16) scale(8388607)'><path d='M0 0H1' fill='none' stroke='blue' stroke-width='8' stroke-linecap='round' vector-effect='non-scaling-stroke'/></g></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None))
        {
            SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            SvgAuthoredPath outline = Assert.IsType<SvgAuthoredPath>(path.StrokeOutline);
            using SvgPreparedPathContours source = outline.PrepareSource(SvgAffineTransform.Identity, false, budget, CancellationToken.None);
            SvgAnalyticContourPreparation preparation = new(source.Points, source.ContourEnds, [], false, source.Signs, SvgPolygonMapping.NativeFillEdges,
                source.QuadSpans, source.CubicSpans, new(12, 12, 8_388_627, 20), 32, 32, source, outline.SceneMap);
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 12, 32, 12, 20, new(0, 32), new(new(0, 0, 32, 32)), SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(12, out long _, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(1, pixel, SvgAnalyticPaint.Solid(new(0, 0, 255, 255)), SvgAnalyticClipCoverage.Full);
            // Premultiplied pixel (13,12) in the independent miter-bounds-00 capture.
            Assert.Equal(new byte[] { 0, 0, 76, 76 }, pixel);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(32_767)]
    [InlineData(32_768)]
    [InlineData(1_000_000)]
    [InlineData(-32_767)]
    [InlineData(-32_768)]
    [InlineData(-1_000_000)]
    public void Finite_source_extents_are_admitted_after_bitmap_clipping(double endpoint)
    {
        SvgDevicePoint[] points = [new(16, 4), new(endpoint, 16), new(16, 28)];
        SvgAnalyticContourPreparation preparation = new(points, [3], [], false, null, SvgPolygonMapping.NativeFillEdges, [], [], new(-1_000_000, 4, 1_000_000, 28), 32, 32);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 4, 28, new(0, 32), new(new(0, 0, 32, 32)), SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(16, out long _, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(endpoint > 0 ? 20 : 12, pixel, SvgAnalyticPaint.Solid(new(0, 0, 255, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal((byte)255, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Nonfinite_source_rejection_survives_cached_preparation(double endpoint)
    {
        SvgDevicePoint[] points = [new(4, 4), new(endpoint, 16), new(4, 28)];
        SvgAnalyticContourPreparation preparation = new(points, [3], [], false, null, SvgPolygonMapping.NativeFillEdges, [], [], new(0, 0, 32, 32), 32, 32);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage uncut = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 32), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.False(eligible);
        }
        {
            using SvgAnalyticCoverage clipped = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 32), new(new(0, 0, 32, 32)), SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.False(eligible);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(32_768)]
    [InlineData(-32_768)]
    public void Uncut_construction_keeps_its_fixed_range_restriction(double endpoint)
    {
        SvgDevicePoint[] points = [new(4, 4), new(endpoint, 16), new(4, 28)];
        SvgAnalyticContourPreparation preparation = new(points, [3], [], false, null, SvgPolygonMapping.NativeFillEdges, [], [], new(-32_768, 4, 32_768, 28), 40_000, 32);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 4, 28, new(0, 32), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.False(eligible);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40_000)]
    public void Clipped_edges_must_fit_both_local_and_canvas_fixed_coordinates(int origin)
    {
        SvgDevicePoint[] points = [new(40_004, 4), new(40_028, 16), new(40_004, 28)];
        SvgAnalyticContourPreparation preparation = new(points, [3], [], false, null, SvgPolygonMapping.NativeFillEdges, [], [], new(40_004, 4, 40_028, 28), 40_032, 32);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 40_000, 40_032, 4, 28, new(40_000, 40_032), new(origin, 0), new(new(40_000, 0, 40_032, 32), new(origin, 0)), SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.False(eligible);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }
}
