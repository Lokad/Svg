using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

using Lokad.Svg.Parsing;
using Lokad.Svg.Fonts;

public sealed class RenderBudgetTests
{
    [Fact]
    public void Parser_lists_charge_their_runtime_value_size_for_geometry_and_stylesheet_rules()
    {
        RenderBudget geometryBudget = new(1_000);
        using var geometries = new BudgetedParserList<SvgGeometry>(geometryBudget, SvgMemoryCategory.Geometry);
        geometries.Add(SvgGeometry.FromRectangle(new SvgRectangle(0, 0, 1, 1, SvgColor.Transparent, false, null)));
        RenderBudgetSnapshot geometrySnapshot = geometryBudget.Snapshot();

        RenderBudget stylesheetBudget = new(1_000);
        using var stylesheetRules = new BudgetedParserList<CssRule>(stylesheetBudget, SvgMemoryCategory.Parser);
        stylesheetRules.Add(default);
        RenderBudgetSnapshot stylesheetSnapshot = stylesheetBudget.Snapshot();

        Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4), geometrySnapshot.CurrentBytes);
        Assert.Equal(geometrySnapshot.CurrentBytes, geometrySnapshot.BytesByCategory[(int)SvgMemoryCategory.Geometry]);
        Assert.Equal(ParserCollectionMemory.GetArrayByteCount<CssRule>(4), stylesheetSnapshot.CurrentBytes);
        Assert.Equal(stylesheetSnapshot.CurrentBytes, stylesheetSnapshot.BytesByCategory[(int)SvgMemoryCategory.Parser]);
    }

    [Fact]
    public void Rasterization_releases_the_surface_reservation_when_background_cancellation_throws()
    {
        RenderBudget budget = new(4);
        using CancellationTokenSource cancellation = new();
        using IDisposable probe = BackgroundInitializationProbe.Arm(cancellation.Cancel, static () => { });
        SvgRoot root = new(1, 1, null, [], [], false, 0, null);
        var options = new SvgRenderOptions(null, 1, new SvgColor(1, 2, 3, 255), Limits());

        Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, options, budget, cancellation.Token));

        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Rasterization_releases_the_surface_reservation_when_scene_validation_throws()
    {
        RenderBudget budget = new(8);
        SvgRoot root = new(1, 1, null, [], [new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255)], false, 0, null);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, Limits());

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, options, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Glyph_rasterization_charges_bounds_all_samples_and_pixel_dispatch()
    {
        RenderBudget budget = new(8);

        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(GlyphRoot(), new SvgRenderOptions(null, 1, SvgColor.Transparent, Limits(517)), budget, CancellationToken.None);

        Assert.Equal(517, surface.WorkUnits);
        Assert.Equal(4, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Glyph_rasterization_denies_before_bounds_or_coverage_work_exceeds_the_limit()
    {
        RenderBudget boundsBudget = new(8);
        SvgException bounds = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(GlyphRoot(), new SvgRenderOptions(null, 1, SvgColor.Transparent, Limits(3)), boundsBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, bounds.ErrorCode);
        AssertBudgetReleased(boundsBudget);

        RenderBudget coverageBudget = new(8);
        SvgException coverage = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(GlyphRoot(), new SvgRenderOptions(null, 1, SvgColor.Transparent, Limits(516)), coverageBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, coverage.ErrorCode);
        AssertBudgetReleased(coverageBudget);
    }

    [Fact]
    public void Glyph_rasterization_observes_cancellation_at_coverage_entry()
    {
        RenderBudget budget = new(8);
        using CancellationTokenSource cancellation = new();
        bool coverageStarted = false;
        using IDisposable probe = GlyphCoverageProbe.Arm(() => { coverageStarted = true; cancellation.Cancel(); });

        Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(GlyphRoot(), new SvgRenderOptions(null, 1, SvgColor.Transparent, Limits(517)), budget, cancellation.Token));

        Assert.True(coverageStarted);
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Reservation_tracks_current_peak_and_category_bytes()
    {
        RenderBudget budget = new(100);

        using (budget.Reserve(30, SvgMemoryCategory.Parser))
        using (budget.Reserve(50, SvgMemoryCategory.Surface))
        {
            RenderBudgetSnapshot snapshot = budget.Snapshot();
            Assert.Equal(80, snapshot.CurrentBytes);
            Assert.Equal(80, snapshot.PeakBytes);
            Assert.Equal(30, snapshot.BytesByCategory[(int)SvgMemoryCategory.Parser]);
            Assert.Equal(50, snapshot.BytesByCategory[(int)SvgMemoryCategory.Surface]);
        }

        RenderBudgetSnapshot released = budget.Snapshot();
        Assert.Equal(0, released.CurrentBytes);
        Assert.Equal(80, released.PeakBytes);
    }

    [Fact]
    public void Over_budget_reservation_fails_without_changing_live_bytes()
    {
        RenderBudget budget = new(100);
        using RenderBudget.BudgetReservation reservation = budget.Reserve(80, SvgMemoryCategory.Scene);

        SvgException exception = Assert.Throws<SvgException>(() => budget.Reserve(21, SvgMemoryCategory.Geometry));
        RenderBudgetSnapshot snapshot = budget.Snapshot();

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(80, snapshot.CurrentBytes);
        Assert.Equal(80, snapshot.PeakBytes);
        Assert.Equal(1, snapshot.DeniedReservationCount);
        Assert.Equal(0, snapshot.BytesByCategory[(int)SvgMemoryCategory.Geometry]);
    }

    [Fact]
    public void Reservation_dispose_is_idempotent()
    {
        RenderBudget budget = new(10);
        RenderBudget.BudgetReservation reservation = budget.Reserve(10, SvgMemoryCategory.Output);

        reservation.Dispose();
        reservation.Dispose();

        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Arithmetic_does_not_overflow_at_large_limits()
    {
        RenderBudget budget = new(long.MaxValue);
        using RenderBudget.BudgetReservation reservation = budget.Reserve(long.MaxValue, SvgMemoryCategory.Temporary);

        SvgException exception = Assert.Throws<SvgException>(() => budget.Reserve(1, SvgMemoryCategory.Temporary));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(long.MaxValue, budget.Snapshot().CurrentBytes);
    }

    private static SvgRoot GlyphRoot()
    {
        SvgGlyphQuadraticContour contour = new([
            Line(0, 0, 1, 0), Line(1, 0, 1, 1), Line(1, 1, 0, 1), Line(0, 1, 0, 0)]);
        SvgGeometry geometry = SvgGeometry.FromGlyphContours(new SvgGlyphGeometry([contour], new SvgColor(0, 0, 0, 255)));
        return new SvgRoot(1, 1, null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255)], false, 0, null);
    }

    private static SvgGlyphQuadraticSegment Line(double startX, double startY, double endX, double endY)
        => new(new SvgGlyphOutlinePoint(startX, startY), new SvgGlyphOutlinePoint(endX, endY), new SvgGlyphOutlinePoint(endX, endY));

    private static SvgRenderLimits Limits() => Limits(8);

    private static SvgRenderLimits Limits(int maximumWorkUnits) => new(8, 8, 1, 1, 8, 8, 8, maximumWorkUnits);

    private static void AssertBudgetReleased(RenderBudget budget)
    {
        RenderBudgetSnapshot snapshot = budget.Snapshot();
        Assert.Equal(0, snapshot.CurrentBytes);
        Assert.Equal(0, snapshot.BytesByCategory[(int)SvgMemoryCategory.Surface]);
    }
}
