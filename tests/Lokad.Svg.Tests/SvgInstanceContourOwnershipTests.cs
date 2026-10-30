using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgInstanceContourOwnershipTests
{
    public enum Paint { Solid, Linear, Radial, Pattern }

    public static IEnumerable<object[]> Placements()
    {
        foreach (Paint paint in Enum.GetValues<Paint>())
            foreach (bool symbol in new[] { false, true })
                foreach (bool roomy in new[] { false, true })
                    yield return [paint, symbol, roomy];
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void Mapped_arrays_use_one_charge_and_outlive_the_definition(Paint paint, bool symbol, bool roomy)
    {
        SvgDevicePoint[] points = new SvgDevicePoint[16_384];
        for (int index = 0; index < points.Length; index++) points[index] = new(index % 2, index % 3);
        int[] ends = [points.Length];
        int[] signs = [1];
        SvgQuadSpan[] quadratics = [new(0, 0, 1, 1, new(.5, 0))];
        SvgCubicSpan[] cubics = [new(0, 1, 2, new(1, 1), new(.5, 2))];
        long sourceBytes = ContourBytes(points.Length, quadratics.Length, cubics.Length);
        bool retainsCurves = !symbol || paint == Paint.Solid;
        long instanceBytes = ContourBytes(points.Length, retainsCurves ? quadratics.Length : 0, retainsCurves ? cubics.Length : 0);
        long paintBytes = paint == Paint.Radial ? SvgRadialGradient.RetainedStorageBytes : 0;
        // The bounded batch/paint bookkeeping is independent of contour length.
        // Roomy controls show that the same input is valid with surplus allowance.
        long allowance = sourceBytes + instanceBytes + 2 * paintBytes + 65_536;
        RenderBudget budget = new(new HostMemoryBudget(allowance + (roomy ? sourceBytes : 0)));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            SvgGeometry source = CreateSource(paint, points, ends, signs, quadratics, cubics, budget);
            SvgDefinition definition = new([source], false, null,
                symbol ? new(0, 0, 2, 2, SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet) : null);
            try { Assert.True(state.Definitions.TryAdd("shape", definition)); }
            catch { source.Dispose(); throw; }
            SvgUseReference reference = new("shape", 2, 3, 2, 2, SvgAffineTransform.Identity, SvgAffineTransform.Identity, 127, true, null, null, null);
            SvgGeometry[] applied = SvgReferenceResolver.Apply(definition, reference, null, state, CancellationToken.None);
            try
            {
                SvgGeometry result = Assert.Single(applied);
                (SvgDevicePoint[] p, int[] e, int[]? s, SvgQuadSpan[] q, SvgCubicSpan[] c) = result.Kind switch
                {
                    SvgGeometryKind.PolygonFill => (result.Polygon.Points, result.Polygon.ContourEnds, result.Polygon.Signs, result.Polygon.QuadSpans, result.Polygon.CubicSpans),
                    SvgGeometryKind.GradientPolygon => (result.GradientPolygon.Points, result.GradientPolygon.ContourEnds, result.GradientPolygon.Signs, result.GradientPolygon.QuadSpans, result.GradientPolygon.CubicSpans),
                    SvgGeometryKind.RadialPolygon => (result.RadialPolygon.Points, result.RadialPolygon.ContourEnds, result.RadialPolygon.Signs, result.RadialPolygon.QuadSpans, result.RadialPolygon.CubicSpans),
                    SvgGeometryKind.PatternPolygon => (result.PatternPolygon.Points, result.PatternPolygon.ContourEnds, result.PatternPolygon.Signs, result.PatternPolygon.QuadSpans, result.PatternPolygon.CubicSpans),
                    _ => throw new InvalidOperationException("Unexpected instance paint.")
                };
                Assert.NotSame(points, p);
                Assert.Equal(new SvgDevicePoint(2, 3), p[0]);
                Assert.Equal(points.Length, p.Length);
                Assert.Same(ends, e);
                Assert.Same(signs, s);
                if (retainsCurves)
                {
                    Assert.NotSame(quadratics, q);
                    Assert.NotSame(cubics, c);
                    Assert.Equal(new SvgDevicePoint(2.5, 3), Assert.Single(q).Control);
                    Assert.Equal(new SvgDevicePoint(3, 4), Assert.Single(c).FirstControl);
                }
                else { Assert.Empty(q); Assert.Empty(c); }
                long beforeRelease = budget.Snapshot().CurrentBytes;
                state.ReleaseDefinitions();
                Assert.Equal(beforeRelease - sourceBytes - paintBytes, budget.Snapshot().CurrentBytes);
                Assert.Equal(new SvgDevicePoint(2, 3), p[0]);
                long retained = budget.Snapshot().CurrentBytes;
                result.Dispose();
                result.Dispose();
                Assert.Equal(retained - instanceBytes - paintBytes, budget.Snapshot().CurrentBytes);
            }
            finally { foreach (SvgGeometry geometry in applied) geometry.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(Paint.Solid, false)]
    [InlineData(Paint.Linear, false)]
    [InlineData(Paint.Radial, false)]
    [InlineData(Paint.Pattern, false)]
    [InlineData(Paint.Solid, true)]
    [InlineData(Paint.Linear, true)]
    [InlineData(Paint.Radial, true)]
    [InlineData(Paint.Pattern, true)]
    public void Failed_mapping_releases_its_reservation_and_preserves_the_source_for_retry(Paint paint, bool symbol)
    {
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            SvgDevicePoint[] points = [new(double.MaxValue, 0), new(1, 0), new(0, 1)];
            long stateBytes = budget.Snapshot().CurrentBytes;
            SvgGeometry source = CreateSource(paint, points, [3], [1], [], [], budget);
            try
            {
                SvgDefinition definition = new([source], false, null,
                    symbol ? new(0, 0, 2, 2, SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet) : null);
                long retained = budget.Snapshot().CurrentBytes;
                Assert.Equal(ContourBytes(points.Length, 0, 0) + (paint == Paint.Radial ? SvgRadialGradient.RetainedStorageBytes : 0), retained - stateBytes);
                SvgUseReference reference = new("shape", 0, 0, 2, 2, SvgAffineTransform.FromScale(2, 2), SvgAffineTransform.Identity, 255, true, null, null, null);
                SvgException failure = Assert.Throws<SvgException>(() => SvgReferenceResolver.Apply(definition, reference, null, state, CancellationToken.None));
                Assert.Equal(SvgErrorCode.LimitExceeded, failure.ErrorCode);
                Assert.Contains("numeric range", failure.Message, StringComparison.Ordinal);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                SvgGeometry[] retry = SvgReferenceResolver.Apply(definition, reference with { Transform = SvgAffineTransform.Identity }, null, state, CancellationToken.None);
                try { Assert.Equal(source.Kind, Assert.Single(retry).Kind); }
                finally { foreach (SvgGeometry geometry in retry) geometry.Dispose(); }
            }
            finally { source.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static long ContourBytes(int pointCount, int quadraticCount, int cubicCount)
        => ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCount)
            + 2 * ParserCollectionMemory.GetArrayByteCount<int>(1)
            + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quadraticCount)
            + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubicCount);

    private static SvgGeometry CreateSource(Paint paint, SvgDevicePoint[] points, int[] ends, int[] signs,
        SvgQuadSpan[] quadratics, SvgCubicSpan[] cubics, RenderBudget budget)
    {
        SvgColor red = new(255, 0, 0, 255), blue = new(0, 0, 255, 255);
        SvgRectangle box = new(0, 0, 2, 2, red, true, null);
        return paint switch
        {
            Paint.Solid => SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(points, ends, signs, quadratics, cubics, red, true, true, SvgPolygonMapping.FiniteEdges, null, null, budget)),
            Paint.Linear => SvgGeometry.FromGradientPolygon(SvgGradientPolygon.Create(points, ends, signs, quadratics, cubics,
                new(red, blue, 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, [], 1, 1, false), box, true, true, 255, SvgPolygonMapping.FiniteEdges, null, null, budget)),
            Paint.Radial => SvgGeometry.FromRadialPolygon(SvgRadialPolygon.Create(points, ends, signs, quadratics, cubics,
                new(red, blue, .5, .5, .5, .5, .5, 0, 0, 1, false, false, SvgGradientSpread.Pad, [], 1, 1, false, SvgGradientTransform.Identity, false), box, true, true, 255, SvgPolygonMapping.FiniteEdges, null, null, budget)),
            Paint.Pattern => Pattern(),
            _ => throw new ArgumentOutOfRangeException(nameof(paint))
        };

        SvgGeometry Pattern()
        {
            using SvgPolygonFill solid = SvgPolygonFill.Create(points, ends, signs, quadratics, cubics, red, true, true, SvgPolygonMapping.FiniteEdges, null, null, budget);
            return SvgPatternPolygon.RepaintOutline(solid, new(SvgGeometry.FromRectangle(box), 0, 0, 2, 2, SvgAffineTransform.Identity), 255, SvgAffineTransform.Identity);
        }
    }
}
