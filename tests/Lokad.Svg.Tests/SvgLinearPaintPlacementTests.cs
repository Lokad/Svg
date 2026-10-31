using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLinearPaintPlacementTests
{
    public enum Consumer { Rectangle, Path, Polygon, RectangleStroke, PathStroke, UseRectangle, UsePath, UsePolygon, UseFill, SymbolRectangle, SymbolStroke }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(Consumer.Rectangle)]
    [InlineData(Consumer.Path)]
    [InlineData(Consumer.Polygon)]
    [InlineData(Consumer.RectangleStroke)]
    [InlineData(Consumer.PathStroke)]
    [InlineData(Consumer.UseRectangle)]
    [InlineData(Consumer.UsePath)]
    [InlineData(Consumer.UsePolygon)]
    [InlineData(Consumer.UseFill)]
    [InlineData(Consumer.SymbolRectangle)]
    [InlineData(Consumer.SymbolStroke)]
    public void Declaration_local_transform_and_reference_placement_remain_separate(Consumer consumer)
    {
        using SvgRoot root = SvgRootParser.Parse(Document(consumer), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(consumer == Consumer.SymbolStroke ? 2 : 1, root.Geometries.Length);
        SvgGeometry geometry = root.Geometries[^1];
        SvgLinearGradient gradient = geometry.Kind switch
        {
            SvgGeometryKind.GradientFill => geometry.Gradient.Gradient,
            SvgGeometryKind.GradientPolygon => geometry.GradientPolygon.Gradient,
            SvgGeometryKind.GradientStroke => geometry.GradientStroke.Gradient,
            _ => throw new InvalidDataException("Expected one linear-painted consumer.")
        };
        SvgLinearPaintMap paint = Assert.IsType<SvgLinearPaintMap>(gradient.SourcePaint);
        Assert.True(paint.Data.IsMapped);
        Assert.Equal((.125, .375, 11.25, 13.5), (paint.Data.X1, paint.Data.Y1, paint.Data.X2, paint.Data.Y2));
        Assert.Equal(new(1, 0, 0, 1, .25, .5), paint.Data.Local);
        SvgAffineTransform expected = consumer switch
        {
            Consumer.SymbolRectangle or Consumer.SymbolStroke => new(2, 0, 0, 2, 5, 8),
            Consumer.UseRectangle or Consumer.UsePath or Consumer.UsePolygon or Consumer.UseFill => SvgAffineTransform.FromTranslation(5, 8),
            _ => SvgAffineTransform.FromTranslation(4, 6)
        };
        Assert.Equal(expected, paint.Data.Referencing);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData(Consumer.UseRectangle)]
    [InlineData(Consumer.SymbolStroke)]
    public void Parser_releases_bound_and_instanced_maps_on_failure_and_retry(Consumer consumer)
        => SvgParseOwnershipAssertions.PreservesBorrowedStorageOnDenialCancellationAndRetry(Document(consumer));

    [Fact]
    [Trait("Layer", "Contract")]
    public void Map_placement_admits_storage_before_allocation_and_identity_reuses_it()
    {
        const long allowance = 1_000_000;
        RenderBudget budget = new(new HostMemoryBudget(allowance));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            SvgLinearGradient source = new(default, default, 0, 0, 8, 0, 0, 1, false, true, SvgGradientSpread.Pad, [], 1, 1, false)
            {
                SourcePaint = state.Scene.RetainLinearPaintMap(new(0, 0, 8, 0, SvgGradientTransform.Identity, SvgAffineTransform.Identity, false))
            };
            long retained = budget.Snapshot().CurrentBytes;
            SvgAffineTransform placement = SvgAffineTransform.FromTranslation(3, 5);
            using (RenderBudget.BudgetReservation borrowed = budget.Reserve(allowance - retained - SvgLinearPaintMap.RetainedStorageBytes + 1, SvgMemoryCategory.Scene))
            {
                long deniedBytes = budget.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgGradientSyntax.MapUserSpaceVector(placement, source, null, state, CancellationToken.None)).ErrorCode);
                Assert.Equal(deniedBytes, budget.Snapshot().CurrentBytes);
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() =>
                SvgGradientSyntax.MapUserSpaceVector(placement, source, null, state, cancellation.Token)).CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            SvgLinearGradient retry = SvgGradientSyntax.MapUserSpaceVector(placement, source, null, state, CancellationToken.None);
            Assert.Equal(placement, Assert.IsType<SvgLinearPaintMap>(retry.SourcePaint).Data.Referencing);
            long placedBytes = budget.Snapshot().CurrentBytes;
            SvgLinearGradient identity = SvgGradientSyntax.MapUserSpaceVector(SvgAffineTransform.Identity, retry, null, state, CancellationToken.None);
            Assert.Same(retry.SourcePaint, identity.SourcePaint);
            Assert.Equal(placedBytes, budget.Snapshot().CurrentBytes);
            Assert.False(Assert.IsType<SvgLinearPaintMap>(source.SourcePaint).Data.IsMapped);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Document(Consumer consumer)
    {
        const string fill = "fill='url(#g)'";
        const string stroke = "fill='none' stroke='url(#g)' stroke-width='2'";
        string shape = consumer switch
        {
            Consumer.Path or Consumer.UsePath => "<path id='shape' d='M2 2H10V10H2Z' " + fill + "/>",
            Consumer.Polygon or Consumer.UsePolygon => "<polygon id='shape' points='2,2 10,2 10,10 2,10' " + fill + "/>",
            Consumer.PathStroke => "<path d='M2 2H10V10H2Z' " + stroke + "/>",
            Consumer.RectangleStroke or Consumer.SymbolStroke => "<rect x='2' y='2' width='8' height='8' " + stroke + "/>",
            Consumer.UseFill => "<rect id='shape' x='2' y='2' width='8' height='8'/>",
            _ => "<rect id='shape' x='2' y='2' width='8' height='8' " + fill + "/>"
        };
        string definitions = "<linearGradient id='g' gradientUnits='userSpaceOnUse' x1='.125' y1='.375' x2='11.25' y2='13.5' gradientTransform='translate(.25 .5)'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        string body;
        if (consumer is Consumer.SymbolRectangle or Consumer.SymbolStroke)
        {
            definitions += "<symbol id='symbol' viewBox='0 0 16 16'>" + shape + "</symbol>";
            body = "<use href='#symbol' x='1' y='2' width='32' height='32'/>";
        }
        else if (consumer is Consumer.UseRectangle or Consumer.UsePath or Consumer.UsePolygon or Consumer.UseFill)
        {
            definitions += shape;
            body = "<use href='#shape' x='1' y='2'" + (consumer == Consumer.UseFill ? " " + fill : "") + "/>";
        }
        else body = shape;
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs>" + definitions
            + "</defs><g transform='translate(4 6)'>" + body + "</g></svg>");
    }
}
