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
    public void Rasterization_rejects_an_unresolved_default_geometry_placeholder()
    {
        RenderBudget budget = new(8);
        SvgRoot root = new(1, 1, null, [default], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255)], false, 0, null);
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

    [Fact]
    public void Pattern_tile_blit_charges_content_pixels()
    {
        RenderBudget budget = new(1_000);

        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(PatternRoot(), new SvgRenderOptions(null, 1, SvgColor.Transparent, PatternLimits(20)), budget, CancellationToken.None);

        Assert.Equal(20, surface.WorkUnits);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels[0..4]);
        Assert.Equal(16, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Pattern_tile_blit_denies_before_content_work_exceeds_the_limit()
    {
        RenderBudget budget = new(1_000);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, PatternLimits(15));

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(PatternRoot(), options, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("Pattern tile", exception.Message);
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Pattern_tile_blit_observes_cancellation()
    {
        RenderBudget budget = new(1_000);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, PatternLimits(20));

        Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(PatternRoot(), options, budget, new CancellationToken(true)));

        AssertBudgetReleased(budget);
    }

    private static SvgRoot PatternRoot()
    {
        SvgRectangle content = new(0, 0, 4, 4, new SvgColor(255, 0, 0, 255), true, null);
        SvgRectangle fill = new(0, 0, 2, 2, default, true, null);
        SvgGeometry geometry = SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, 0, 0, 4, 4, fill, 255, null));
        return new SvgRoot(2, 2, null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255)], false, 0, null);
    }

    private static SvgRenderLimits PatternLimits(int maximumWorkUnits) => new(1_000, 1_000, 64, 4_096, 1_000, 64, 64, maximumWorkUnits);
    private static SvgRoot GlyphRoot()
    {
        SvgGlyphQuadraticContour contour = new([
            Line(0, 0, 1, 0), Line(1, 0, 1, 1), Line(1, 1, 0, 1), Line(0, 1, 0, 0)]);
        SvgGeometry geometry = SvgGeometry.FromGlyphContours(new SvgGlyphGeometry([contour], new SvgColor(0, 0, 0, 255)));
        return new SvgRoot(1, 1, null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255)], false, 0, null);
    }

    private static SvgGlyphQuadraticSegment Line(double startX, double startY, double endX, double endY)
        => new(new SvgGlyphOutlinePoint(startX, startY), new SvgGlyphOutlinePoint(endX, endY), new SvgGlyphOutlinePoint(endX, endY));

    [Fact]
    public void Scene_builder_transfers_one_reservation_per_distinct_gradient_stop_array()
    {
        RenderBudget budget = new(100_000);
        using SvgSceneBuilder scene = new(budget, new ParserWorkCounter(1_000_000), CancellationToken.None);
        SvgGradientStop[] sharedStops = [new SvgGradientStop(0, new SvgColor(255, 0, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))];
        SvgGradientStop[] otherStops = [new SvgGradientStop(0, new SvgColor(0, 0, 0, 255)), new SvgGradientStop(0.5, new SvgColor(0, 255, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))];
        scene.AddGeometry(GradientFill(sharedStops, 0));
        long afterFirst = SceneBytes(budget);
        Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(2) + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4) + ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(4), afterFirst);
        scene.AddGeometry(GradientFill(sharedStops, 2));
        Assert.Equal(afterFirst, SceneBytes(budget));
        scene.AddGeometry(GradientFill(otherStops, 4));
        long afterThird = SceneBytes(budget);
        Assert.Equal(afterFirst + ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(3), afterThird);
        scene.AddGeometry(EmptyGradientFill());
        Assert.Equal(afterThird, SceneBytes(budget));
        SvgSceneStorage storage = scene.Build();
        try
        {
            Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(2) + ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(3) + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4) + ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(4), SceneBytes(budget));
        }
        finally
        {
            storage.Dispose();
        }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Gradient_parse_retains_scene_owned_stop_storage_after_parser_state_releases()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(EightStopGradientSvg());
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, budget, CancellationToken.None);
        Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.GradientFill, root.Geometries[0].Kind);
        Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(1) + ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(1) + ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(8), budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Scene]);
    }

    [Fact]
    public void Fractional_straight_path_fits_small_scratch_budget()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0 L8,0 L8,8 Z' fill='red'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, budget, CancellationToken.None);
        Assert.Single(root.Geometries);
        Assert.True(budget.Snapshot().PeakBytes < 500_000, "Straight paths must not reserve curve-sized scratch.");
    }
    [Fact]
    public void Circle_fill_fits_small_scratch_budget()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='20' fill='red'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, budget, CancellationToken.None);
        Assert.Single(root.Geometries);
        Assert.True(budget.Snapshot().PeakBytes < 500_000, "Shape sampling must scale with loops and segments, not global limits.");
    }
    [Fact]
    public void Single_curve_path_fits_small_scratch_budget()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,4 C2,2 6,2 8,4' fill='red'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, budget, CancellationToken.None);
        Assert.Single(root.Geometries);
        Assert.True(budget.Snapshot().PeakBytes < 500_000, "Curve scratch must scale with commands, not global limits.");
    }

    [Fact]
    public void Curved_clip_bounds_fit_small_scratch_budget()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><path d='M0,8 Q4,-4 8,8' fill='red' clip-path='url(#window)'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, budget, CancellationToken.None);
        Assert.Single(root.Geometries);
        Assert.True(budget.Snapshot().PeakBytes < 500_000, "Clip-bounds flattening must scale with commands, not global limits.");
    }

    [Fact]
    public void Polygon_parse_denies_and_releases_when_points_exceed_the_remaining_budget()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,4 C2,2 6,2 8,4 Z' fill='red'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget probe = new(limits.MaximumAccountedBytes);
        using (SvgRootParser.Parse(svg, limits, probe, CancellationToken.None)) { }
        long peak = probe.Snapshot().PeakBytes;
        SvgRenderLimits deniedLimits = new(peak - 1, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, limits.MaximumWorkUnits, limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
        RenderBudget deniedBudget = new(deniedLimits.MaximumAccountedBytes);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, deniedLimits, deniedBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Polygon_rasterization_denies_and_releases_when_mapped_points_exceed_the_remaining_budget()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='red'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget parseBudget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, parseBudget, CancellationToken.None);
        RenderBudget rasterBudget = new(300);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Polygon_rasterization_charges_mapped_vertices_and_coverage_pixels()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8)];
        int[] ends = [3];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(8, 8, null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 195);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(195, surface.WorkUnits);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Polygon_rasterization_denies_before_vertex_weighted_coverage_exceeds_the_limit()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8)];
        int[] ends = [3];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(8, 8, null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100);
            SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            Assert.Contains("raster work", exception.Message);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Polygon_rasterization_denies_before_mapping_work_exceeds_the_limit()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8)];
        int[] ends = [3];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(8, 8, null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 2);
            SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            Assert.Contains("vertex mapping", exception.Message);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Gradient_parse_denies_and_releases_when_stops_exceed_the_remaining_budget()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(EightStopGradientSvg());
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget probe = new(limits.MaximumAccountedBytes);
        using (SvgRootParser.Parse(svg, limits, probe, CancellationToken.None)) { }
        long peak = probe.Snapshot().PeakBytes;
        SvgRenderLimits deniedLimits = new(peak - 1, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, limits.MaximumWorkUnits, limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
        RenderBudget deniedBudget = new(deniedLimits.MaximumAccountedBytes);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, deniedLimits, deniedBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Scene_builder_transfers_one_reservation_per_distinct_radial_stop_array()
    {
        RenderBudget budget = new(100_000);
        using SvgSceneBuilder scene = new(budget, new ParserWorkCounter(1_000_000), CancellationToken.None);
        SvgGradientStop[] sharedStops = [new SvgGradientStop(0, new SvgColor(255, 0, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))];
        SvgGradientStop[] otherStops = [new SvgGradientStop(0, new SvgColor(0, 0, 0, 255)), new SvgGradientStop(0.5, new SvgColor(0, 255, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))];
        scene.AddGeometry(RadialFill(sharedStops));
        long afterFirst = SceneBytes(budget);
        Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(2) + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4) + ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(4), afterFirst);
        scene.AddGeometry(RadialFill(sharedStops));
        Assert.Equal(afterFirst, SceneBytes(budget));
        scene.AddGeometry(RadialFill(otherStops));
        long afterThird = SceneBytes(budget);
        Assert.Equal(afterFirst + ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(3), afterThird);
        scene.AddGeometry(EmptyRadialFill());
        Assert.Equal(afterThird, SceneBytes(budget));
        SvgSceneStorage storage = scene.Build();
        try
        {
            Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(2) + ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(3) + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4) + ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(4), SceneBytes(budget));
        }
        finally
        {
            storage.Dispose();
        }
        AssertBudgetReleased(budget);
    }

    private static SvgGeometry GradientFill(SvgGradientStop[] stops, double x)
    {
        SvgLinearGradient gradient = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, stops, 1, 1);
        SvgRectangle box = new(x, 0, 2, 2, default, true, null);
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, box, 255, null));
    }

    private static SvgGeometry EmptyGradientFill()
    {
        SvgLinearGradient gradient = new(default, default, 0, 0, 1, 0, 0, 1, true, false, SvgGradientSpread.Pad, Array.Empty<SvgGradientStop>(), 1, 1);
        SvgRectangle box = new(8, 0, 2, 2, default, true, null);
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, box, 255, null));
    }

    private static long SceneBytes(RenderBudget budget) => budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Scene];

    private static SvgGeometry RadialFill(SvgGradientStop[] stops)
    {
        SvgRadialGradient gradient = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, stops, 1, 1);
        SvgRectangle box = new(0, 0, 2, 2, default, true, null);
        return SvgGeometry.FromRadialFill(new SvgRadialGeometry(gradient, box, box, 255, null));
    }

    private static SvgGeometry EmptyRadialFill()
    {
        SvgRadialGradient gradient = new(default, default, 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, true, false, SvgGradientSpread.Pad, Array.Empty<SvgGradientStop>(), 1, 1);
        SvgRectangle box = new(8, 0, 2, 2, default, true, null);
        return SvgGeometry.FromRadialFill(new SvgRadialGeometry(gradient, box, box, 255, null));
    }

    private static string EightStopGradientSvg() => "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"0.15\" stop-color=\"#ff8000\"/><stop offset=\"0.3\" stop-color=\"#ffff00\"/><stop offset=\"0.45\" stop-color=\"#00ff00\"/><stop offset=\"0.6\" stop-color=\"#00ffff\"/><stop offset=\"0.75\" stop-color=\"#0000ff\"/><stop offset=\"0.9\" stop-color=\"#8000ff\"/><stop offset=\"1\" stop-color=\"#ff00ff\"/></linearGradient></defs><rect x=\"0\" y=\"0\" width=\"16\" height=\"16\" fill=\"url(#g)\"/></svg>";

    private static SvgRenderLimits Limits() => Limits(8);

    private static SvgRenderLimits Limits(int maximumWorkUnits) => new(8, 8, 1, 1, 8, 8, 8, maximumWorkUnits);

    private static void AssertBudgetReleased(RenderBudget budget)
    {
        RenderBudgetSnapshot snapshot = budget.Snapshot();
        Assert.Equal(0, snapshot.CurrentBytes);
        Assert.Equal(0, snapshot.BytesByCategory[(int)SvgMemoryCategory.Surface]);
    }
}
