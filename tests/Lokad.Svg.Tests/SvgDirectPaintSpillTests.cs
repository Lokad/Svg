using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDirectPaintSpillTests
{
    // The independently declared run-width-solid control retains this circle.
    private static readonly byte[] Source = "<svg xmlns='http://www.w3.org/2000/svg' width='85' height='79'><circle cx='42.375' cy='38.125' r='23.625' fill='#3c71b7'/></svg>"u8.ToArray();
    private static readonly byte[] CompletedStripSource = "<svg xmlns='http://www.w3.org/2000/svg' width='300' height='150'><circle cx='150' cy='52.5' r='48' fill='black'/></svg>"u8.ToArray();

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public void Completed_strip_uses_the_paint_clip_instead_of_mask_row_storage(bool clipConstruction)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(CompletedStripSource, SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null,
            polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 102, 198, 4, 101, 300, 150,
            clipConstruction ? new(102, 198) : new(0, 300), budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            walker.PrepareRow(52, CancellationToken.None);
            Assert.Equal(96, walker.Row.Length);
            Assert.Equal(new(clipConstruction ? 102 : 101, 198), walker.GetPaintWindow(new(102, 198)));
            byte[] pixel = new byte[4];
            walker.PaintPixel(-1, pixel, SvgAnalyticPaint.Solid(polygon.Color), SvgAnalyticClipCoverage.Full);
            Assert.Equal(new byte[] { 0, 0, 0, clipConstruction ? (byte)0 : (byte)8 }, pixel);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Completed_strip_growth_refusal_releases_owners_and_allows_explicit_retry()
    {
        const long allowance = 1_000_000;
        RenderBudget budget = new(new HostMemoryBudget(allowance));
        long liveAtGrowth = 0;
        using (SvgAnalyticPaintProbe.Arm((phase, _) =>
        {
            if (phase == SvgAnalyticPaintPhase.BeforeLinkBufferGrowth) liveAtGrowth = budget.Snapshot().CurrentBytes;
        })) PrepareStripRow(budget);
        Assert.True(liveAtGrowth > 0);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        long replacement = 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintLink>(192);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(allowance - liveAtGrowth - replacement + 1, SvgMemoryCategory.Scene))
        {
            long borrowedBytes = budget.Snapshot().CurrentBytes;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => PrepareStripRow(budget)).ErrorCode);
            Assert.Equal(borrowedBytes, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        PrepareStripRow(budget);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static void PrepareStripRow(RenderBudget budget)
        {
            using SvgRoot root = SvgRootParser.Parse(CompletedStripSource, SvgRenderLimits.Default, CancellationToken.None);
            SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null,
                polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 102, 198, 4, 101, 300, 150, new(0, 300), budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(52, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(-1, pixel, SvgAnalyticPaint.Solid(polygon.Color), SvgAnalyticClipCoverage.Full);
            Assert.Equal(new byte[] { 0, 0, 0, 8 }, pixel);
        }
    }

    [Fact]
    public void Direct_fringe_paints_beyond_path_storage_without_expanding_coverage_rows()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(Source, SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans,
            18, 66, 14, 62, 85, 79, new(0, 85), budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            walker.PrepareRow(38, CancellationToken.None);
            Assert.Equal(48, walker.Row.Length);
            Assert.Equal(new(18, 67), walker.GetPaintWindow(new(18, 66)));
            byte[] pixel = new byte[4];
            walker.PaintPixel(48, pixel, SvgAnalyticPaint.Solid(polygon.Color), SvgAnalyticClipCoverage.Full);
            // Stable browser capture has straight RGBA (0,85,170,3).
            Assert.Equal(new byte[] { 0, 1, 2, 3 }, pixel);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_at_link_growth_or_subsequent_replay_releases_all_owners(bool afterGrowth)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using CancellationTokenSource cancellation = new();
        bool observedGrowth = false;
        using IDisposable probe = SvgAnalyticPaintProbe.Arm((phase, _) =>
        {
            if (phase == SvgAnalyticPaintPhase.BeforeLinkBufferGrowth)
            {
                observedGrowth = true;
                if (!afterGrowth) cancellation.Cancel();
            }
            if (afterGrowth && observedGrowth && phase == SvgAnalyticPaintPhase.BeforeReplay) cancellation.Cancel();
        });
        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(Source, SvgRenderLimits.Default, budget, null, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token);
        });
        Assert.True(observedGrowth);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Link_growth_reserves_old_and_replacement_storage_together()
    {
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        long liveAtGrowth = 0;
        using (SvgAnalyticPaintProbe.Arm((phase, _) =>
        {
            if (phase == SvgAnalyticPaintPhase.BeforeLinkBufferGrowth) liveAtGrowth = measured.Snapshot().CurrentBytes;
        })) PrepareSpillRow(measured);
        Assert.True(liveAtGrowth > 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long replacementBytes = 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintLink>(85);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(liveAtGrowth + replacementBytes - 1));
        int growthCalls = 0;
        using IDisposable probe = SvgAnalyticPaintProbe.Arm((phase, _) =>
        {
            if (phase == SvgAnalyticPaintPhase.BeforeLinkBufferGrowth) growthCalls++;
        });
        SvgException exception = Assert.Throws<SvgException>(() => PrepareSpillRow(denied));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(1, growthCalls);
        Assert.Equal(1, denied.Snapshot().DeniedReservationCount);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    private static void PrepareSpillRow(RenderBudget budget)
    {
        using SvgRoot root = SvgRootParser.Parse(Source, SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans,
            18, 66, 14, 62, 85, 79, new(0, 85), budget, out bool eligible, CancellationToken.None);
        Assert.True(eligible);
        walker.PrepareRow(38, CancellationToken.None);
    }
}
