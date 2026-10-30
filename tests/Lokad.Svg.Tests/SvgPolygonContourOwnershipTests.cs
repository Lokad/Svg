using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgPolygonContourOwnershipTests
{
    public enum Paint { Linear, Radial, Pattern, EmptyPattern }

    [Theory]
    [InlineData(Paint.Linear, false)]
    [InlineData(Paint.Radial, false)]
    [InlineData(Paint.Pattern, false)]
    [InlineData(Paint.EmptyPattern, false)]
    [InlineData(Paint.Linear, true)]
    [InlineData(Paint.Radial, true)]
    [InlineData(Paint.Pattern, true)]
    [InlineData(Paint.EmptyPattern, true)]
    public void Repaint_retains_contour_ownership_with_only_the_live_storage_allowance(Paint paint, bool roomy)
    {
        SvgDevicePoint[] points = [new(0, 0), new(2, 0), new(0, 2)];
        int[] ends = [3];
        int[] signs = [1];
        SvgQuadSpan[] quadratics = [new(0, 0, 1, 1, new(1, 0))];
        SvgCubicSpan[] cubics = [new(0, 1, 2, new(2, 1), new(1, 2))];
        long contourBytes = ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length)
            + ParserCollectionMemory.GetArrayByteCount<int>(ends.Length)
            + ParserCollectionMemory.GetArrayByteCount<int>(signs.Length)
            + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quadratics.Length)
            + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubics.Length);
        long paintBytes = paint == Paint.Radial ? SvgRadialGradient.RetainedStorageBytes : 0;
        RenderBudget budget = new(new HostMemoryBudget((roomy ? 2 : 1) * contourBytes + paintBytes));
        SvgClipBox clip = new(0, 0, 2, 2);
        using (SvgPolygonFill source = SvgPolygonFill.Create(points, ends, signs, quadratics, cubics,
            new(255, 0, 0, 255), true, true, SvgPolygonMapping.NativeFillEdges, clip, null, budget))
        {
            SvgGeometry result = Repaint(source, paint, budget);
            try
            {
                source.Dispose();
                Assert.Equal(contourBytes + paintBytes, budget.Snapshot().CurrentBytes);
                Assert.Equal(contourBytes + paintBytes, budget.Snapshot().PeakBytes);
                (SvgDevicePoint[] p, int[] e, int[]? s, SvgQuadSpan[] q, SvgCubicSpan[] c) = result.Kind switch
                {
                    SvgGeometryKind.GradientPolygon => (result.GradientPolygon.Points, result.GradientPolygon.ContourEnds, result.GradientPolygon.Signs, result.GradientPolygon.QuadSpans, result.GradientPolygon.CubicSpans),
                    SvgGeometryKind.RadialPolygon => (result.RadialPolygon.Points, result.RadialPolygon.ContourEnds, result.RadialPolygon.Signs, result.RadialPolygon.QuadSpans, result.RadialPolygon.CubicSpans),
                    SvgGeometryKind.PatternPolygon => (result.PatternPolygon.Points, result.PatternPolygon.ContourEnds, result.PatternPolygon.Signs, result.PatternPolygon.QuadSpans, result.PatternPolygon.CubicSpans),
                    SvgGeometryKind.PolygonFill => (result.Polygon.Points, result.Polygon.ContourEnds, result.Polygon.Signs, result.Polygon.QuadSpans, result.Polygon.CubicSpans),
                    _ => throw new InvalidOperationException("Unexpected repaint kind.")
                };
                Assert.Same(points, p);
                Assert.Same(ends, e);
                Assert.Same(signs, s);
                Assert.Same(quadratics, q);
                Assert.Same(cubics, c);
                if (paint == Paint.EmptyPattern) Assert.False(result.Polygon.HasFill);
            }
            finally { result.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);


    }

    [Fact]
    public void Denied_radial_paint_preserves_the_source_for_a_later_repaint()
    {
        SvgDevicePoint[] points = [new(0, 0), new(2, 0), new(0, 2)];
        int[] ends = [3];
        long contourBytes = ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length)
            + ParserCollectionMemory.GetArrayByteCount<int>(ends.Length);
        long paintBytes = SvgRadialGradient.RetainedStorageBytes;
        RenderBudget budget = new(new HostMemoryBudget(contourBytes + paintBytes));
        SvgRectangle box = new(0, 0, 2, 2, new(255, 0, 0, 255), true, null);
        using (SvgPolygonFill source = SvgPolygonFill.Create(points, ends, null,
            new(255, 0, 0, 255), true, false, SvgPolygonMapping.NativeFillEdges, null, null, budget))
        {
            using (RenderBudget.BudgetReservation blocker = budget.Reserve(paintBytes, SvgMemoryCategory.Scene))
            {
                SvgException denied = Assert.Throws<SvgException>(() => SvgRadialPolygon.RepaintOutline(source, RadialPaint(), box, 127, budget));
                Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
                Assert.Equal(contourBytes + paintBytes, budget.Snapshot().CurrentBytes);
                Assert.Same(points, source.Points);
            }
            Assert.Equal(contourBytes, budget.Snapshot().CurrentBytes);
            SvgGeometry result = SvgRadialPolygon.RepaintOutline(source, RadialPaint(), box, 127, budget);
            try
            {
                source.Dispose();
                Assert.Same(points, result.RadialPolygon.Points);
                Assert.Equal(contourBytes + paintBytes, budget.Snapshot().CurrentBytes);
            }
            finally { result.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(Paint.Linear)]
    [InlineData(Paint.Radial)]
    [InlineData(Paint.Pattern)]
    [InlineData(Paint.EmptyPattern)]
    public void Repeated_stamping_transfers_the_live_charge_without_refunding_borrowed_arrays(Paint paint)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgPolygonFill source = SvgPolygonFill.Create([new(0, 0), new(2, 0), new(0, 2)], [3], null,
            new(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, new(0, 0, 2, 2), null, budget))
        {
            SvgGeometry original = Repaint(source, paint, budget);
            try
            {
                long retained = budget.Snapshot().CurrentBytes;
                SvgGeometry first = Stamp(original);
                try
                {
                    SvgGeometry second = Stamp(first);
                    try
                    {
                        source.Dispose();
                        original.Dispose();
                        first.Dispose();
                        Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                        Assert.Equal(retained, budget.Snapshot().PeakBytes);
                    }
                    finally { second.Dispose(); second.Dispose(); }
                }
                finally { first.Dispose(); }
            }
            finally { original.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static SvgGeometry Stamp(SvgGeometry geometry)
        {
            SvgClipBox region = new(.5, .5, 1, 1);
            return geometry.Kind switch
            {
                SvgGeometryKind.PolygonFill => SvgGeometry.FromPolygonFill(geometry.Polygon.Stamped(127, region)),
                SvgGeometryKind.GradientPolygon => SvgGeometry.FromGradientPolygon(geometry.GradientPolygon.Stamped(127, region)),
                SvgGeometryKind.RadialPolygon => SvgGeometry.FromRadialPolygon(geometry.RadialPolygon.Stamped(127, region)),
                SvgGeometryKind.PatternPolygon => SvgGeometry.FromPatternPolygon(geometry.PatternPolygon.Stamped(127, region)),
                _ => throw new InvalidOperationException("Unexpected repaint kind.")
            };
        }
    }

    [Fact]
    public void Refused_vector_pattern_placement_preserves_source_and_borrowed_tile_for_retry()
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgVectorContent tileOwner = SvgVectorContent.CreatePending(budget))
        {
            long tileBytes = budget.Snapshot().CurrentBytes;
            SvgPatternTile tile = new(null, 0, 0, 2, 2, SvgAffineTransform.FromScale(2, 2)) { VectorContent = tileOwner };
            using (SvgPolygonFill source = SvgPolygonFill.Create([new(0, 0), new(2, 0), new(0, 2)], [3], null,
                new(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget))
            {
                long retained = budget.Snapshot().CurrentBytes;
                ArgumentOutOfRangeException rejected = Assert.Throws<ArgumentOutOfRangeException>(() => SvgPatternPolygon.RepaintOutline(source, tile, 127, SvgAffineTransform.FromScale(double.MaxValue, 1)));
                Assert.Equal("a", rejected.ParamName);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                SvgGeometry retry = SvgPatternPolygon.RepaintOutline(source, tile, 127, SvgAffineTransform.Identity);
                try
                {
                    source.Dispose();
                    Assert.Same(tileOwner, retry.PatternPolygon.VectorContent);
                    Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                    Assert.Equal(retained, budget.Snapshot().PeakBytes);
                }
                finally { retry.Dispose(); }
            }
            Assert.Equal(tileBytes, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgRadialGradient RadialPaint()
        => new(new(255, 0, 0, 255), new(0, 0, 255, 255), .5, .5, .5, .5, .5, 0, 0, 1, false, false, SvgGradientSpread.Pad, [], 1, 1, false, SvgGradientTransform.Identity, false);

    private static SvgGeometry Repaint(SvgPolygonFill source, Paint paint, RenderBudget budget)
    {
        SvgRectangle box = new(0, 0, 2, 2, new(255, 0, 0, 255), true, null);
        return paint switch
        {
            Paint.Linear => SvgGradientPolygon.RepaintOutline(source,
                new(new(255, 0, 0, 255), new(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, [], 1, 1, false), box, 127),
            Paint.Radial => SvgRadialPolygon.RepaintOutline(source,
                RadialPaint(), box, 127, budget),
            Paint.Pattern => SvgPatternPolygon.RepaintOutline(source,
                new(SvgGeometry.FromRectangle(box), 0, 0, 2, 2, SvgAffineTransform.Identity), 127, SvgAffineTransform.Identity),
            Paint.EmptyPattern => SvgPatternPolygon.RepaintOutline(source,
                new(null, 0, 0, 0, 0, SvgAffineTransform.Identity), 127, SvgAffineTransform.Identity),
            _ => throw new ArgumentOutOfRangeException(nameof(paint))
        };
    }
}
