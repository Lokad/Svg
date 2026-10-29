using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAnalyticPaintTests
{
    [Fact]
    public void Opaque_color_pair_keeps_its_distinct_interpolation_scale()
    {
        SvgAnalyticPaint paint = SvgAnalyticPaint.Solid(new(0, 52, 120, 255));
        byte[] direct = new byte[4];
        byte[] pair = new byte[4];
        paint.Blend(direct, 100, SvgAnalyticPaintKind.Direct);
        paint.Blend(pair, 100, SvgAnalyticPaintKind.Pair);
        Assert.Equal(new byte[] { 0, 20, 47, 100 }, direct);
        Assert.Equal(new byte[] { 0, 20, 46, 99 }, pair);
    }

    [Fact]
    public void Opaque_black_pair_preserves_byte_coverage()
    {
        byte[] pixel = new byte[4];
        SvgAnalyticPaint.Solid(new(0, 0, 0, 255)).Blend(pixel, 116, SvgAnalyticPaintKind.Pair);
        Assert.Equal(new byte[] { 0, 0, 0, 116 }, pixel);
    }

    [Fact]
    public void Translucent_material_quantizes_before_coverage()
    {
        byte[] pixel = new byte[4];
        SvgAnalyticPaint.Solid(new(195, 78, 151, 64)).Blend(pixel, 240, SvgAnalyticPaintKind.Mask);
        // Independently captured quarter-opacity fringe has this premultiplied
        // pixel; capture conversion produces straight (195,76,149,60).
        Assert.Equal(new byte[] { 46, 18, 35, 60 }, pixel);
    }

    [Fact]
    public void Full_span_keeps_one_descriptor_and_preserves_indexed_paint()
    {
        const int width = 4096;
        long preparedBytes = SvgAnalyticCoverage.ScratchByteCount(4, width, 0) + 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintLink>(width);
        long spanBufferBytes = 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintSpan>(16);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(preparedBytes + spanBufferBytes));
        SvgDevicePoint[] points = [new(0, 0), new(width, 0), new(width, 4), new(0, 4)];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [4], [], false, null, SvgPolygonMapping.QuarterEdges, [], [], 0, width, 0, 4, width, 4, budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            walker.PrepareRow(0, CancellationToken.None);
            Assert.Equal(preparedBytes + spanBufferBytes, budget.Snapshot().CurrentBytes);
            byte[] destination = new byte[4];
            walker.PaintPixel(width / 2, destination, SvgAnalyticPaint.Solid(new(195, 78, 151, 64)), SvgAnalyticClipCoverage.Full);
            Assert.Equal(new byte[] { 49, 20, 38, 64 }, destination);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancellation_before_span_growth_preserves_token_and_releases_owners()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using CancellationTokenSource source = new();
        using IDisposable probe = SvgAnalyticPaintProbe.Arm((phase, _) =>
        {
            if (phase == SvgAnalyticPaintPhase.BeforeSpanBufferGrowth) source.Cancel();
        });
        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() => PrepareOneRow(budget, source.Token));
        Assert.Equal(source.Token, exception.CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Span_storage_denial_releases_preparation_and_pixel_links()
    {
        long preparedBytes = SvgAnalyticCoverage.ScratchByteCount(4, 64, 0) + 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintLink>(64);
        long spanBufferBytes = 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintSpan>(16);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(preparedBytes + spanBufferBytes - 1));
        int growthCalls = 0;
        using IDisposable probe = SvgAnalyticPaintProbe.Arm((phase, _) =>
        {
            if (phase == SvgAnalyticPaintPhase.BeforeSpanBufferGrowth) growthCalls++;
        });
        SvgException exception = Assert.Throws<SvgException>(() => PrepareOneRow(budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(1, growthCalls);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Operation_growth_reserves_old_and_replacement_storage_together()
    {
        long preparedBytes = SvgAnalyticCoverage.ScratchByteCount(4, 64, 0) + 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintLink>(64);
        long firstBuffer = 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintOperation>(16);
        long replacementBuffer = 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintOperation>(32);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(preparedBytes + firstBuffer + replacementBuffer - 1));
        int deniedGrowthCount = -1;
        using IDisposable probe = SvgAnalyticPaintProbe.Arm((phase, count) =>
        {
            if (phase == SvgAnalyticPaintPhase.BeforeBufferGrowth) deniedGrowthCount = count;
        });
        SvgException exception = Assert.Throws<SvgException>(() => PrepareOneRow(budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(16, deniedGrowthCount);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancellation_during_operation_growth_preserves_token_and_releases_owners()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using CancellationTokenSource source = new();
        int lastGrowth = -1;
        using IDisposable probe = SvgAnalyticPaintProbe.Arm((phase, count) =>
        {
            if (phase != SvgAnalyticPaintPhase.BeforeBufferGrowth) return;
            lastGrowth = count;
            if (count == 16) source.Cancel();
        });
        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() => PrepareOneRow(budget, source.Token));
        Assert.Equal(source.Token, exception.CancellationToken);
        Assert.Equal(16, lastGrowth);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancellation_during_operation_replay_preserves_token_and_releases_raster_storage()
    {
        using CancellationTokenSource source = new();
        int replayCalls = 0;
        using IDisposable probe = SvgAnalyticPaintProbe.Arm((phase, _) =>
        {
            if (phase != SvgAnalyticPaintPhase.BeforeReplay) return;
            replayCalls++;
            source.Cancel();
        });
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='4'><polygon points='0,0 64,0 0,4' fill='#003478'/></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, budget, null, CancellationToken.None);
        long preparedBytes = budget.Snapshot().CurrentBytes;
        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, source.Token));
        Assert.Equal(source.Token, exception.CancellationToken);
        Assert.Equal(1, replayCalls);
        Assert.Equal(preparedBytes, budget.Snapshot().CurrentBytes);
    }

    private static void PrepareOneRow(RenderBudget budget, CancellationToken cancellationToken)
    {
        SvgDevicePoint[] points = [new(0, 0), new(64, 0), new(32, 1), new(0, 2)];
        using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [4], [], false, null, SvgPolygonMapping.QuarterEdges, [], [], 0, 64, 0, 4, 64, 4, budget, out bool eligible, cancellationToken);
        Assert.True(eligible);
        walker.PrepareRow(0, cancellationToken);
    }
}
