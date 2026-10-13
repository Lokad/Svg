using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPreparedGlyphCoverageTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void Prepared_curves_preserve_fractional_sampling_and_opposite_winding(int samples)
    {
        SvgGlyphQuadraticContour[] contours =
        [
            new([
                new(new(0, 0), new(1, -1), new(2, 0)),
                new(new(2, 0), new(3, 1), new(2, 2)),
                new(new(2, 2), new(1, 3), new(0, 2)),
                new(new(0, 2), new(-1, 1), new(0, 0))]),
            new([Line(.75, .75, .75, 1.25), Line(.75, 1.25, 1.25, 1.25), Line(1.25, 1.25, 1.25, .75), Line(1.25, .75, .75, .75)])
        ];
        SvgGlyphQuadraticSegment[][] original = contours.Select(c => c.Segments.ToArray()).ToArray();
        RenderBudget budget = new(10_000);
        using (SvgGlyphCoverage.PreparedContours prepared = SvgGlyphCoverage.PreparedContours.TryCreate(contours, budget, new(long.MaxValue), CancellationToken.None) ?? throw new InvalidOperationException("Preparation should fit."))
        {
            for (int y = -2; y <= 3; y++)
            for (int x = -2; x <= 3; x++)
            {
                double left = x * .625 + .125, top = y * 1.125 - .25;
                Assert.Equal(
                    SvgGlyphCoverage.GetCoverage(contours, left, top, .875, .625, samples, CancellationToken.None),
                    SvgGlyphCoverage.GetCoverage(prepared, left, top, .875, .625, samples, CancellationToken.None));
            }
            for (int index = 0; index < contours.Length; index++) Assert.Equal(original[index], contours[index].Segments);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Preparation_declines_memory_pressure_and_keeps_the_original_coverage_route()
    {
        SvgGlyphQuadraticContour[] contours = [new([Line(0, 0, 2, 0), Line(2, 0, 2, 2), Line(2, 2, 0, 2), Line(0, 2, 0, 0)])];
        RenderBudget budget = new(500);
        Assert.Null(SvgGlyphCoverage.PreparedContours.TryCreate(contours, budget, new(long.MaxValue), CancellationToken.None));
        Assert.Equal(255, SvgGlyphCoverage.GetCoverage(contours, 0, 0, 4, CancellationToken.None));
        Assert.Equal(0, SvgGlyphCoverage.GetCoverage(contours, 2, 0, 4, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(0, budget.Snapshot().DeniedReservationCount);
        Assert.Null(SvgGlyphCoverage.PreparedContours.TryCreate([], budget, new(0), CancellationToken.None));
    }

    [Fact]
    public void Preparation_work_boundary_cancellation_and_disposal_release_the_owner()
    {
        SvgGlyphQuadraticContour[] contours = [new([Line(0, 0, 2, 0), Line(2, 0, 2, 2), Line(2, 2, 0, 2), Line(0, 2, 0, 0)])];
        RenderBudget budget = new(10_000);
        ParserWorkCounter work = new(long.MaxValue);
        using (SvgGlyphCoverage.PreparedContours prepared = SvgGlyphCoverage.PreparedContours.TryCreate(contours, budget, work, CancellationToken.None) ?? throw new InvalidOperationException("Preparation should fit."))
        {
            Assert.True(budget.Snapshot().CurrentBytes > 0);
            Assert.Equal(255, SvgGlyphCoverage.GetCoverage(prepared, 0, 0, 1, 1, 4, CancellationToken.None));
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphCoverage.GetCoverage(prepared, 0, 0, 1, 1, 4, cancelled.Token));
            prepared.Dispose();
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Throws<ObjectDisposedException>(() => { _ = prepared.Points.Length; });
        }
        using (SvgGlyphCoverage.PreparedContours exact = SvgGlyphCoverage.PreparedContours.TryCreate(contours, budget, new(work.Used), CancellationToken.None) ?? throw new InvalidOperationException("Exact work boundary should fit.")) { }
        SvgException denied = Assert.Throws<SvgException>(() => SvgGlyphCoverage.PreparedContours.TryCreate(contours, budget, new(work.Used - 1), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphCoverage.PreparedContours.TryCreate(contours, budget, new(long.MaxValue), cancellation.Token));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Prepared_extreme_curves_keep_numeric_rejection_and_cleanup()
    {
        RenderBudget budget = new(10_000);
        SvgGlyphQuadraticContour[] overflow = [new([new(new(0, 0), new(double.PositiveInfinity, 1), new(2, 0))])];
        SvgException preparation = Assert.Throws<SvgException>(() => SvgGlyphCoverage.PreparedContours.TryCreate(overflow, budget, new(long.MaxValue), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, preparation.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgGlyphQuadraticContour[] extreme = [new([Line(-double.MaxValue, -double.MaxValue, double.MaxValue, double.MaxValue)])];
        using (SvgGlyphCoverage.PreparedContours prepared = SvgGlyphCoverage.PreparedContours.TryCreate(extreme, budget, new(long.MaxValue), CancellationToken.None) ?? throw new InvalidOperationException("Preparation should fit."))
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphCoverage.GetCoverage(prepared, 0, 0, 1, 1, 1, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphCoverage.GetCoverage(extreme, 0, 0, 1, CancellationToken.None)).ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgGlyphQuadraticSegment Line(double x1, double y1, double x2, double y2) => new(new(x1, y1), new(x2, y2), new(x2, y2));
}
