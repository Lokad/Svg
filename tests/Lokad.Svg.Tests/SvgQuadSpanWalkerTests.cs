using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgQuadSpanWalkerTests
{
    private static (SvgDevicePoint[] Points, int[] Ends, int[]? Signs, SvgQuadSpan[] Spans) LowerArch(bool evenOdd)
    {
        string rule = evenOdd ? " fill-rule='evenodd'" : string.Empty;
        SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,48 Q32,8 56,48 Z' fill='blue'" + rule + "/></svg>"), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        return (geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans);
    }

    private static byte[] WalkRows(SvgDevicePoint[] points, int[] ends, int[]? signs, SvgQuadSpan[] spans, bool isEvenOdd, int firstRow, int pastLastRow, out bool eligible)
    {
        RenderBudget budget = new(10_000_000);
        ReadOnlySpan<int> votes = signs is int[] roles ? roles : Array.Empty<int>();
        using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, ends, votes, isEvenOdd, signs, SvgPolygonMapping.FlattenedCurves, spans, 0, 64, 0, 64, 64, 64, budget, out long _, out eligible, CancellationToken.None);
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
    public void Quad_spans_substitute_analytic_edges_on_clean_arch()
    {
        (SvgDevicePoint[] points, int[] ends, int[]? signs, SvgQuadSpan[] spans) = LowerArch(false);
        Assert.Single(spans);
        byte[] flat = WalkRows(points, ends, signs, Array.Empty<SvgQuadSpan>(), false, 8, 48, out bool flatEligible);
        byte[] curved = WalkRows(points, ends, signs, spans, false, 8, 48, out bool curvedEligible);
        Assert.True(flatEligible);
        Assert.True(curvedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.NotEqual(flat, curved);
    }

    [Fact]
    public void Overlapping_spans_decline_to_flattened_rows()
    {
        (SvgDevicePoint[] points, int[] ends, int[]? signs, SvgQuadSpan[] spans) = LowerArch(false);
        SvgQuadSpan first = spans[0];
        SvgQuadSpan[] overlapping = [first, new SvgQuadSpan(0, first.StartIndex, first.EndIndex, first.Control)];
        byte[] flat = WalkRows(points, ends, signs, Array.Empty<SvgQuadSpan>(), false, 8, 48, out bool _);
        byte[] declined = WalkRows(points, ends, signs, overlapping, false, 8, 48, out bool declinedEligible);
        Assert.True(declinedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.Equal(flat, declined);
    }

    [Fact]
    public void Evenodd_spans_decline_to_flattened_rows()
    {
        (SvgDevicePoint[] points, int[] ends, int[]? signs, SvgQuadSpan[] spans) = LowerArch(true);
        Assert.Single(spans);
        byte[] flat = WalkRows(points, ends, signs, Array.Empty<SvgQuadSpan>(), true, 8, 48, out bool flatEligible);
        byte[] declined = WalkRows(points, ends, signs, spans, true, 8, 48, out bool declinedEligible);
        Assert.True(flatEligible);
        Assert.True(declinedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.Equal(flat, declined);
    }
    [Fact]
    public void Single_chord_span_falls_back_to_flattened_rows()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 Q4,0 8,0 L8,8 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgQuadSpan span = Assert.Single(geometry.Polygon.QuadSpans);
        Assert.Equal(1, span.EndIndex - span.StartIndex);
        byte[] flat = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, Array.Empty<SvgQuadSpan>(), false, 0, 8, out bool flatEligible);
        byte[] declined = WalkRows(geometry.Polygon.Points, geometry.Polygon.ContourEnds, geometry.Polygon.Signs, geometry.Polygon.QuadSpans, false, 0, 8, out bool declinedEligible);
        Assert.True(flatEligible);
        Assert.True(declinedEligible);
        Assert.True(RowMass(flat) > 0);
        Assert.Equal(flat, declined);
    }
}
