using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgInheritedContourOwnershipTests
{
    public enum FillMode { Authored, Override, RoomyOverride }

    [Theory]
    [InlineData(false, FillMode.Authored)]
    [InlineData(false, FillMode.Override)]
    [InlineData(false, FillMode.RoomyOverride)]
    [InlineData(true, FillMode.Authored)]
    [InlineData(true, FillMode.Override)]
    [InlineData(true, FillMode.RoomyOverride)]
    public void Temporary_fill_borrows_the_definition_until_mapping_finishes(bool symbol, FillMode mode)
    {
        SvgDevicePoint[] points = new SvgDevicePoint[16_384];
        for (int index = 0; index < points.Length; index++) points[index] = new(index % 2, index % 3);
        long contourBytes = ContourBytes(points.Length);
        long allowance = 2 * contourBytes + 65_536 + (mode == FillMode.RoomyOverride ? contourBytes : 0);
        RenderBudget budget = new(new HostMemoryBudget(allowance));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        using (SvgPolygonFill source = Source(points, budget))
        {
            SvgDefinition definition = Definition(source, symbol);
            SvgUseReference reference = new("shape", 2, 3, 2, 2, SvgAffineTransform.Identity, SvgAffineTransform.Identity,
                255, true, mode == FillMode.Authored ? null : SvgPaint.Default, null, null);
            SvgGeometry[] mapped = SvgReferenceResolver.Apply(definition, reference, null, state, CancellationToken.None);
            try
            {
                SvgPolygonFill result = Assert.Single(mapped).Polygon;
                Assert.NotSame(points, result.Points);
                Assert.Same(source.ContourEnds, result.ContourEnds);
                Assert.Null(result.Signs);
                Assert.Equal(new SvgDevicePoint(2, 3), result.Points[0]);
                Assert.Equal(mode == FillMode.Authored ? new SvgColor(255, 0, 0, 255) : new SvgColor(0, 0, 0, 255), result.Color);
                Assert.Equal(new SvgColor(255, 0, 0, 255), source.Color);
                Assert.Same(points, source.Points);
                long retained = budget.Snapshot().CurrentBytes;
                source.Dispose();
                Assert.Equal(retained - contourBytes, budget.Snapshot().CurrentBytes);
                Assert.Equal(new SvgDevicePoint(2, 3), result.Points[0]);
                result.Dispose();
                result.Dispose();
                Assert.Equal(retained - 2 * contourBytes, budget.Snapshot().CurrentBytes);
            }
            finally { foreach (SvgGeometry geometry in mapped) geometry.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_recolored_mapping_keeps_the_definition_owner_for_retry(bool symbol)
    {
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            long stateBytes = budget.Snapshot().CurrentBytes;
            SvgDevicePoint[] points = [new(double.MaxValue, 0), new(1, 0), new(0, 1)];
            using SvgPolygonFill source = Source(points, budget);
            SvgDefinition definition = Definition(source, symbol);
            long retained = budget.Snapshot().CurrentBytes;
            Assert.Equal(ContourBytes(points.Length), retained - stateBytes);
            SvgUseReference reference = new("shape", 0, 0, 2, 2, SvgAffineTransform.FromScale(2, 2),
                SvgAffineTransform.Identity, 255, true, SvgPaint.Default, null, null);
            SvgException failure = Assert.Throws<SvgException>(() => SvgReferenceResolver.Apply(definition, reference, null, state, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, failure.ErrorCode);
            Assert.Contains("numeric range", failure.Message, StringComparison.Ordinal);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.Same(points, source.Points);
            Assert.Equal(new SvgColor(255, 0, 0, 255), source.Color);

            SvgGeometry[] retry = SvgReferenceResolver.Apply(definition, reference with { Transform = SvgAffineTransform.Identity }, null, state, CancellationToken.None);
            try
            {
                SvgPolygonFill result = Assert.Single(retry).Polygon;
                Assert.Equal(new SvgColor(0, 0, 0, 255), result.Color);
                Assert.Equal(points[0], result.Points[0]);
                long beforeSourceDisposal = budget.Snapshot().CurrentBytes;
                source.Dispose();
                Assert.Equal(beforeSourceDisposal - ContourBytes(points.Length), budget.Snapshot().CurrentBytes);
            }
            finally { foreach (SvgGeometry geometry in retry) geometry.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Partial_fill_preparation_preserves_recolored_and_authored_channels_for_retry()
    {
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        using (SvgPolygonFill first = SvgPolygonFill.Create([new(0, 0), new(2, 0), new(0, 2)], [3], [1],
            [new(0, 0, 1, 1, new(1, 0))], [new(0, 1, 2, new(2, 1), new(1, 2))],
            new(255, 0, 0, 255), true, true, SvgPolygonMapping.FiniteEdges, null, null, budget))
        using (SvgPolygonFill authored = Source([new(0, 0), new(1, 0), new(0, 1)], budget))
        using (SvgRadialPolygon unsupported = SvgRadialPolygon.Create([new(0, 0), new(1, 0), new(0, 1)], [3], null,
            new(new(255, 0, 0, 255), new(0, 0, 255, 255), .5, .5, .5, .5, .5, 0, 0, 1, false, false, SvgGradientSpread.Pad, [], 1, 1, false, SvgGradientTransform.Identity, false),
            new(0, 0, 2, 2, default, true, null), true, false, 255, SvgPolygonMapping.FiniteEdges, null, null, budget))
        {
            SvgDefinition definition = new([SvgGeometry.FromPolygonFill(first), SvgGeometry.FromPolygonFill(authored), SvgGeometry.FromRadialPolygon(unsupported)], false, null, null)
            {
                SymbolFillBindings = [new(true, null, null), new(false, null, null), new(true, null, null)]
            };
            SvgUseReference reference = new("shape", 0, 0, 2, 2, SvgAffineTransform.Identity, SvgAffineTransform.Identity, 255, true, SvgPaint.Default, null, null);
            long retained = budget.Snapshot().CurrentBytes;
            SvgException failure = Assert.Throws<SvgException>(() => SvgReferenceResolver.Apply(definition, reference, null, state, CancellationToken.None));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
            Assert.Contains("Inherited fill", failure.Message, StringComparison.Ordinal);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.Equal(new SvgColor(255, 0, 0, 255), first.Color);
            Assert.Equal(new SvgColor(255, 0, 0, 255), authored.Color);
            SvgDefinition retryDefinition = definition with
            {
                Geometries = [SvgGeometry.FromPolygonFill(first), SvgGeometry.FromPolygonFill(authored)],
                SymbolFillBindings = [new(true, null, null), new(false, null, null)]
            };
            SvgGeometry[] retry = SvgReferenceResolver.Apply(retryDefinition, reference, null, state, CancellationToken.None);
            try
            {
                Assert.Equal(2, retry.Length);
                Assert.Equal(new SvgColor(0, 0, 0, 255), retry[0].Polygon.Color);
                Assert.Equal(authored.Color, retry[1].Polygon.Color);
                Assert.Same(first.ContourEnds, retry[0].Polygon.ContourEnds);
                Assert.Same(first.Signs, retry[0].Polygon.Signs);
                Assert.Equal(first.QuadSpans, retry[0].Polygon.QuadSpans);
                Assert.Equal(first.CubicSpans, retry[0].Polygon.CubicSpans);
            }
            finally { foreach (SvgGeometry geometry in retry) geometry.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static long ContourBytes(int count)
        => ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(count) + ParserCollectionMemory.GetArrayByteCount<int>(1);

    private static SvgPolygonFill Source(SvgDevicePoint[] points, RenderBudget budget)
        => SvgPolygonFill.Create(points, [points.Length], null, new(255, 0, 0, 255), true, false,
            SvgPolygonMapping.FiniteEdges, null, null, budget);

    private static SvgDefinition Definition(SvgPolygonFill source, bool symbol)
        => new([SvgGeometry.FromPolygonFill(source)], true, null,
            symbol ? new(0, 0, 2, 2, SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet) : null);
}
