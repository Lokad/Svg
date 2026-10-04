using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCubicSpanWalkerTests
{
    [Fact]
    public void Retained_cubic_expansion_reserves_all_kept_edges_before_allocation()
    {
        SvgDevicePoint[] points = [new(8, 48), new(32, 8), new(56, 48)];
        SvgCubicSpan[] spans = [new(0, 0, 2, new(8, 0), new(56, 0))];
        RenderBudget budget = new(1_000_000);
        using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [3], [], false, null, SvgPolygonMapping.FlattenedCurves, [], spans, 0, 64, 0, 64, 64, 64, budget, out long _, out bool eligible, CancellationToken.None);

        Assert.True(eligible);
        Assert.True(walker.EdgeCount > points.Length);
        // Each kept edge and the two sentinels need at least the live-state
        // object footprint; the three row buffers need another nine bytes/pixel.
        Assert.True(budget.Snapshot().CurrentBytes >= (walker.EdgeCount + 2L) * 176 + 64 * 9);
    }

    [Fact]
    public void Later_row_restores_live_curve_displacement_independently_of_previous_rows()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,8 C52,4 56,60 8,56 C32,48 32,16 8,8 Z' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        var polygon = geometry.Polygon;
        Assert.Equal(2, polygon.CubicSpans.Length);
        RenderBudget budget = new(1_000_000);
        ReadOnlySpan<int> votes = polygon.Signs is int[] roles ? roles : Array.Empty<int>();
        using SvgAnalyticCoverage sequential = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, votes, polygon.IsEvenOdd, polygon.Signs, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 0, 64, 0, 64, 64, 64, budget, out long _, out bool sequentialEligible, CancellationToken.None);
        using SvgAnalyticCoverage skipped = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, votes, polygon.IsEvenOdd, polygon.Signs, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 0, 64, 0, 64, 64, 64, budget, out long _, out bool skippedEligible, CancellationToken.None);
        Assert.True(sequentialEligible);
        Assert.True(skippedEligible);

        sequential.PrepareRow(12, out long _, CancellationToken.None);
        sequential.PrepareRow(24, out long sequentialWork, CancellationToken.None);
        skipped.PrepareRow(24, out long skippedWork, CancellationToken.None);

        Assert.Equal(skipped.Row.ToArray(), sequential.Row.ToArray());
        Assert.Equal(skippedWork, sequentialWork);
        Assert.Contains(sequential.Row.ToArray(), alpha => alpha > 0 && alpha < 255);
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
        RenderBudget budget = new(10_000_000);
        ReadOnlySpan<int> votes = signs is int[] roles ? roles : Array.Empty<int>();
        using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, ends, votes, isEvenOdd, signs, SvgPolygonMapping.FlattenedCurves, quadSpans, cubicSpans, 0, 64, 0, 64, 64, 64, budget, out long _, out eligible, CancellationToken.None);
        List<byte> rows = new();
        if (eligible)
        {
            for (int y = firstRow; y < pastLastRow; y++)
            {
                walker.PrepareRow(y, out long _, CancellationToken.None);
                rows.AddRange(walker.Row.ToArray());
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
