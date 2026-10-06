using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

public sealed class SvgRadialPaintStorageTests
{
    [Fact]
    public void Radial_fill_denies_before_retaining_paint_and_transfers_ownership_through_clip_and_mask()
    {
        RenderBudget deniedBudget = new(SvgRadialGradient.RetainedStorageBytes - 1);
        SvgException denied = Assert.Throws<SvgException>(() => Fill(deniedBudget));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
        RenderBudget budget = new(SvgRadialGradient.RetainedStorageBytes);
        SvgRadialGeometry source = Fill(budget);
        SvgRadialGeometry clipped = source.ClipTo(new(0, 0, 2, 2));
        SvgRadialGeometry masked = clipped.Stamped(127);
        source.Dispose();
        clipped.Dispose();
        Assert.Equal(SvgRadialGradient.RetainedStorageBytes, budget.Snapshot().CurrentBytes);
        Assert.Equal(127, masked.Opacity);
        masked.Dispose();
        masked.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Radial_polygon_releases_partial_paint_reservation_and_keeps_caller_array_ownership_on_denial()
    {
        SvgDevicePoint[] points = [new(0, 0), new(2, 0), new(0, 2)];
        int[] ends = [3];
        long arrays = ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length) + ParserCollectionMemory.GetArrayByteCount<int>(ends.Length);
        RenderBudget deniedArrays = new(SvgRadialGradient.RetainedStorageBytes + arrays - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Polygon(deniedArrays, null)).ErrorCode);
        Assert.Equal(0, deniedArrays.Snapshot().CurrentBytes);
        RenderBudget deniedPaint = new(arrays + SvgRadialGradient.RetainedStorageBytes - 1);
        using (RenderBudget.BudgetReservation callerArrays = deniedPaint.Reserve(arrays, SvgMemoryCategory.Scene))
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Polygon(deniedPaint, callerArrays)).ErrorCode);
            Assert.Equal(arrays, deniedPaint.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, deniedPaint.Snapshot().CurrentBytes);
        RenderBudget budget = new(arrays + SvgRadialGradient.RetainedStorageBytes);
        SvgRadialPolygon original = Polygon(budget, null);
        SvgRadialPolygon masked = original.Stamped(127, new(0, 0, 2, 2));
        original.Dispose();
        Assert.Equal(arrays + SvgRadialGradient.RetainedStorageBytes, budget.Snapshot().CurrentBytes);
        masked.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        SvgRadialPolygon Polygon(RenderBudget target, RenderBudget.BudgetReservation? reservation)
            => SvgRadialPolygon.Create(points, ends, null, Gradient(), default, true, false, 255, SvgPolygonMapping.QuarterEdges, null, reservation, target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Geometry_builder_releases_paint_when_array_growth_or_detach_is_denied(bool detach)
    {
        long paintBytes = SvgRadialGradient.RetainedStorageBytes;
        long arrayBytes = ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4);
        RenderBudget budget = new(detach ? paintBytes + arrayBytes : paintBytes);
        using (BudgetedGeometryBuilder builder = new(budget, SvgMemoryCategory.Geometry, new(1000), CancellationToken.None))
        {
            if (detach)
            {
                builder.Add(SvgGeometry.FromRadialFill(Fill(budget)));
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => builder.Detach()).ErrorCode);
            }
            else
            {
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => builder.Add(SvgGeometry.FromRadialFill(Fill(budget)))).ErrorCode);
                Assert.Equal(0, budget.Snapshot().CurrentBytes);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Rejected_stroked_radial_mask_keeps_the_source_reservation_for_cleanup()
    {
        RenderBudget budget = new(SvgRadialGradient.RetainedStorageBytes);
        SvgRectangle box = new(0, 0, 2, 2, default, true, null);
        using (SvgRadialGeometry fill = SvgRadialGeometry.Create(Gradient(), box, box, 255, new(new SvgColor(0, 0, 0, 255), 2), budget))
        {
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgMaskSyntax.ApplyMask(SvgGeometry.FromRadialFill(fill), 127, new(0, 0, 2, 2))).ErrorCode);
            Assert.Equal(SvgRadialGradient.RetainedStorageBytes, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<rect x='0' y='0' width='32' height='32' fill='url(#g)'/>")]
    [InlineData("<path d='M0 0 H32 V32 H0 Z' fill='url(#g)'/>")]
    [InlineData("<rect x='2' y='2' width='28' height='28' fill='url(#g)' stroke='url(#g)' stroke-width='2'/>")]
    [InlineData("<line x1='2' y1='16' x2='30' y2='16' stroke='url(#g)' stroke-width='2' stroke-dasharray='2 2'/>")]
    public void Partial_radial_lowering_releases_every_reservation_when_remaining_memory_is_denied(string shape)
    {
        byte[] svg = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='20' gradientTransform='matrix(1.173 .227 -.313 .897 3.7 9.3)'><stop stop-color='#656f71'/><stop offset='1' stop-color='#d3d4cf'/></radialGradient></defs>" + shape + "</svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget roomy = new(limits.MaximumAccountedBytes);
        using (SvgRootParser.Parse(svg, limits, roomy, CancellationToken.None)) { }
        long peak = roomy.Snapshot().PeakBytes;
        Assert.Equal(0, roomy.Snapshot().CurrentBytes);
        for (long maximum = peak - 1; maximum > peak - 4096; maximum -= 128)
        {
            RenderBudget budget = new(maximum);
            try { using SvgRoot parsed = SvgRootParser.Parse(svg, limits, budget, CancellationToken.None); }
            catch (SvgException denied) { Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode); }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    private static SvgRadialGeometry Fill(RenderBudget budget)
    {
        SvgRectangle box = new(0, 0, 2, 2, default, true, null);
        return SvgRadialGeometry.Create(Gradient(), box, box, 255, null, budget);
    }

    private static SvgRadialGradient Gradient()
        => new(default, default, .5, .5, .5, .5, .5, 0, 0, 1, false, false, SvgGradientSpread.Pad, Array.Empty<SvgGradientStop>(), 1, 1, false, SvgGradientTransform.Identity, false);

    [Fact]
    public void Group_use_snapshot_borrows_source_geometry_until_scene_disposal()
    {
        const string path = "<path fill='red' d='M1 1 Q7 1 7 7 L1 7 Z'/>";
        byte[] shared = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><g id='g'>" + path + "</g><use href='#g' x='8'/></svg>");
        byte[] independent = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'>" + path + "<g transform='translate(8 0)'>" + path + "</g></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(independent, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(shared, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }
}
