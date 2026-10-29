using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCubicSpanWalkerTests
{
    [Fact]
    public void Convex_cubic_reserves_both_live_monotonic_edges_before_allocation()
    {
        SvgDevicePoint[] points = [new(8, 48), new(32, 8), new(56, 48)];
        SvgCubicSpan[] spans = [new(0, 0, 2, new(8, 0), new(56, 0))];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [3], [], false, null, SvgPolygonMapping.FlattenedCurves, [], spans, 0, 64, 0, 64, 64, 64, budget, out bool eligible, CancellationToken.None);

        Assert.True(eligible);
        Assert.Equal(2, walker.EdgeCount);
        // Each kept edge and the two sentinels need at least the live-state
        // object footprint; the three row buffers need another nine bytes/pixel.
        Assert.True(budget.Snapshot().CurrentBytes >= (walker.EdgeCount + 2L) * 176 + 64 * 9);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Later_row_restores_live_curve_displacement_independently_of_previous_rows(bool convex, bool clipped)
    {
        byte[] input = convex
            ? "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,48 C8,0 56,0 56,48 Z' fill='blue'/></svg>"u8.ToArray()
            : "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,8 C52,4 56,60 8,56 C32,48 32,16 8,8 Z' fill='blue'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        var polygon = geometry.Polygon;
        Assert.Equal(convex ? 1 : 2, polygon.CubicSpans.Length);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        ReadOnlySpan<int> votes = polygon.Signs is int[] roles ? roles : Array.Empty<int>();
        SvgAnalyticClipContext? clip = clipped ? new(new(10, 0, 48, 64)) : null;
        using SvgAnalyticCoverage sequential = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, votes, polygon.IsEvenOdd, polygon.Signs, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 0, 64, 0, 64, 64, 64, new(0, 64), clip, budget, out bool sequentialEligible, CancellationToken.None);
        using SvgAnalyticCoverage skipped = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, votes, polygon.IsEvenOdd, polygon.Signs, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 0, 64, 0, 64, 64, 64, new(0, 64), clip, budget, out bool skippedEligible, CancellationToken.None);
        Assert.True(sequentialEligible);
        Assert.True(skippedEligible);

        sequential.PrepareRow(12, CancellationToken.None);
        sequential.PrepareRow(24, CancellationToken.None);
        skipped.PrepareRow(24, CancellationToken.None);

        byte[] sequentialPixels = new byte[64 * 4];
        byte[] skippedPixels = new byte[64 * 4];
        SvgAnalyticPaint paint = SvgAnalyticPaint.Solid(new(0, 0, 0, 255));
        for (int x = 0; x < 64; x++)
        {
            sequential.PaintPixel(x, sequentialPixels.AsSpan(x * 4, 4), paint, SvgAnalyticClipCoverage.Full);
            skipped.PaintPixel(x, skippedPixels.AsSpan(x * 4, 4), paint, SvgAnalyticClipCoverage.Full);
        }
        Assert.Equal(skippedPixels, sequentialPixels);
        sequential.PrepareRow(24, CancellationToken.None);
        byte[] repeatedPixels = new byte[64 * 4];
        for (int x = 0; x < 64; x++)
            sequential.PaintPixel(x, repeatedPixels.AsSpan(x * 4, 4), paint, SvgAnalyticClipCoverage.Full);
        Assert.Equal(skippedPixels, repeatedPixels);
        Assert.Contains(Enumerable.Range(0, 64).Select(x => sequentialPixels[x * 4 + 3]), alpha => alpha > 0 && alpha < 255);
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("cancel")]
    public void Convex_cubic_denial_releases_storage_and_allows_retry(string deniedPhase)
    {
        SvgDevicePoint[] points = [new(8, 48), new(32, 8), new(56, 48)];
        SvgCubicSpan[] spans = [new(0, 0, 2, new(8, 0), new(56, 0))];
        SvgAnalyticContourPreparation preparation = new(points, [3], [], false, null, SvgPolygonMapping.FlattenedCurves, [], spans, new(0, 0, 64, 64), 64, 64);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(deniedPhase == "storage" ? 1_000 : 1_000_000));
        using CancellationTokenSource cancellation = new();
        if (deniedPhase == "cancel") cancellation.Cancel();
        try
        {
            using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, budget, out bool _, cancellation.Token);
            Assert.Fail("Preparation must honor the denied resource.");
        }
        catch (SvgException exception)
        {
            Assert.NotEqual("cancel", deniedPhase);
            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        }
        catch (OperationCanceledException) { Assert.Equal("cancel", deniedPhase); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget retryBudget = new RenderBudget(new HostMemoryBudget(1_000_000));
        {
            using SvgAnalyticCoverage retry = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, retryBudget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(2, retry.EdgeCount);
            retry.PrepareRow(24, CancellationToken.None);
        }
        Assert.Equal(0, retryBudget.Snapshot().CurrentBytes);
    }

    private static (SvgDevicePoint[] Points, int[] Ends, int[]? Signs, SvgQuadSpan[] QuadSpans, SvgCubicSpan[] CubicSpans) LowerCubicArch(bool evenOdd)
    {
        string rule = evenOdd ? " fill-rule='evenodd'" : string.Empty;
        SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,48 C24,8 40,8 56,48 Z' fill='blue'" + rule + "/></svg>"), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        return (geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, geometry.Polygon.CubicSpans);
    }

    private static byte[] WalkRows(SvgDevicePoint[] points, int[] ends, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, bool isEvenOdd, int firstRow, int pastLastRow, out bool eligible)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(10_000_000));
        ReadOnlySpan<int> votes = signs is int[] roles ? roles : Array.Empty<int>();
        using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, ends, votes, isEvenOdd, signs, SvgPolygonMapping.FlattenedCurves, quadSpans, cubicSpans, 0, 64, 0, 64, 64, 64, budget, out eligible, CancellationToken.None);
        List<byte> rows = new();
        if (eligible)
        {
            for (int y = firstRow; y < pastLastRow; y++)
            {
                walker.PrepareRow(y, CancellationToken.None);
                byte[] pixels = new byte[64 * 4];
                for (int x = 0; x < 64; x++)
                {
                    walker.PaintPixel(x, pixels.AsSpan(x * 4, 4), SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
                    rows.Add(pixels[x * 4 + 3]);
                }
            }
        }
        return rows.ToArray();
    }

    private static long RowMass(byte[] rows)
    {
        long total = 0;
        foreach (byte value in rows) total += value;
        return total;
    }

    [Fact]
    public void Cubic_spans_substitute_analytic_edges_on_clean_arch()
    {
        (SvgDevicePoint[] points, int[] ends, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans) = LowerCubicArch(false);
        Assert.Empty(quadSpans);
        Assert.Single(cubicSpans);
        byte[] flat = WalkRows(points, ends, signs, quadSpans, Array.Empty<SvgCubicSpan>(), false, 8, 48, out bool flatEligible);
        byte[] curved = WalkRows(points, ends, signs, quadSpans, cubicSpans, false, 8, 48, out bool curvedEligible);
        Assert.True(flatEligible);
        Assert.True(curvedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.NotEqual(flat, curved);
    }

    [Fact]
    public void Overlapping_cubic_spans_decline_to_flattened_rows()
    {
        (SvgDevicePoint[] points, int[] ends, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans) = LowerCubicArch(false);
        SvgCubicSpan first = cubicSpans[0];
        SvgCubicSpan[] overlapping = [first, new SvgCubicSpan(first.Contour, first.StartIndex, first.EndIndex, first.FirstControl, first.SecondControl)];
        byte[] flat = WalkRows(points, ends, signs, quadSpans, Array.Empty<SvgCubicSpan>(), false, 8, 48, out bool _);
        byte[] declined = WalkRows(points, ends, signs, quadSpans, overlapping, false, 8, 48, out bool declinedEligible);
        Assert.True(declinedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.Equal(flat, declined);
    }

    [Fact]
    public void Evenodd_cubic_spans_decline_to_flattened_rows()
    {
        (SvgDevicePoint[] points, int[] ends, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans) = LowerCubicArch(true);
        Assert.Single(cubicSpans);
        byte[] flat = WalkRows(points, ends, signs, quadSpans, Array.Empty<SvgCubicSpan>(), true, 8, 48, out bool flatEligible);
        byte[] declined = WalkRows(points, ends, signs, quadSpans, cubicSpans, true, 8, 48, out bool declinedEligible);
        Assert.True(flatEligible);
        Assert.True(declinedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.Equal(flat, declined);
    }

    [Fact]
    public void Flat_cubic_span_falls_back_to_flattened_rows()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 C2,0 6,0 8,0 L8,8 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgCubicSpan span = Assert.Single(geometry.Polygon.CubicSpans);
        Assert.Equal(1, span.EndIndex - span.StartIndex);
        byte[] flat = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, Array.Empty<SvgCubicSpan>(), false, 0, 8, out bool flatEligible);
        byte[] declined = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, geometry.Polygon.CubicSpans, false, 0, 8, out bool declinedEligible);
        Assert.True(flatEligible);
        Assert.True(declinedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.Equal(flat, declined);
    }

    [Fact]
    public void Multi_quad_and_cubic_substitute_per_contour()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,48 Q32,8 56,48 Z M8,20 C12,12 20,12 24,20 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Single(geometry.Polygon.QuadSpans);
        SvgCubicSpan cubic = Assert.Single(geometry.Polygon.CubicSpans);
        Assert.Equal(1, cubic.Contour);
        byte[] flat = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), false, 8, 48, out bool flatEligible);
        byte[] quadOnly = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, Array.Empty<SvgCubicSpan>(), false, 8, 48, out bool quadEligible);
        byte[] curved = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, geometry.Polygon.CubicSpans, false, 8, 48, out bool curvedEligible);
        Assert.True(flatEligible);
        Assert.True(quadEligible);
        Assert.True(curvedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.NotEqual(flat, quadOnly);
        Assert.NotEqual(quadOnly, curved);
    }

    [Fact]
    public void Multi_invalid_spans_fall_back_per_contour()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,48 Q32,8 56,48 Z M8,20 C12,12 20,12 24,20 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Single(geometry.Polygon.QuadSpans);
        SvgCubicSpan cubic = Assert.Single(geometry.Polygon.CubicSpans);
        SvgCubicSpan[] overlapping = [cubic, new SvgCubicSpan(cubic.Contour, cubic.StartIndex, cubic.EndIndex, cubic.FirstControl, cubic.SecondControl)];
        byte[] flatLow = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), false, 0, 24, out bool _);
        byte[] declinedLow = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, overlapping, false, 0, 24, out bool declinedEligible);
        byte[] partialHigh = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, overlapping, false, 24, 64, out bool _);
        byte[] subsetHigh = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, [cubic], false, 24, 64, out bool _);
        byte[] flatHigh = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), false, 24, 64, out bool _);
        Assert.True(declinedEligible);
        Assert.True(RowMass(flatLow) + RowMass(flatHigh) > 0);
        Assert.Equal(flatLow, declinedLow);
        Assert.Equal(subsetHigh, partialHigh);
        Assert.NotEqual(flatHigh, partialHigh);
    }
    [Fact]
    public void Multi_cubic_spans_substitute_per_contour()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,30 C20,4 44,4 56,30 Z M8,60 C20,34 44,34 56,60 Z' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(2, geometry.Polygon.ContourEnds.Length);
        SvgCubicSpan[] spans = geometry.Polygon.CubicSpans;
        Assert.Equal(2, spans.Length);
        Assert.Equal(0, spans[0].Contour);
        Assert.Equal(1, spans[1].Contour);
        Assert.Empty(geometry.Polygon.QuadSpans);
        byte[] flat = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, Array.Empty<SvgCubicSpan>(), false, 4, 60, out bool flatEligible);
        byte[] curved = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, spans, false, 4, 60, out bool curvedEligible);
        Assert.True(flatEligible);
        Assert.True(curvedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.NotEqual(flat, curved);
    }
}
