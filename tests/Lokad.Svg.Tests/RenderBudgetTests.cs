using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

using Lokad.Svg.Parsing;
using Lokad.Svg.Fonts;

public sealed class RenderBudgetTests
{
    [Fact]
    public void Analytic_scratch_denies_a_budget_that_cannot_hold_retained_edge_objects()
    {
        // Three 176-byte edges plus two sentinels, rows and reference arrays
        // cannot fit 1,000 bytes. The pre-live-state 80-byte estimate admitted it.
        RenderBudget budget = new(1_000);
        SvgException denied = Assert.Throws<SvgException>(() => PrepareTriangle(budget));

        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
        AssertBudgetReleased(budget);

        static void PrepareTriangle(RenderBudget budget)
        {
            SvgDevicePoint[] points = [new(0, 0), new(16, 0), new(0, 16)];
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [3], [], false, null, SvgPolygonMapping.QuarterEdges, 0, 16, 0, 16, 16, 16, budget, out long _, out bool _, CancellationToken.None);
        }
    }

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
        SvgRoot root = new(new SvgRootLength(1, false), new SvgRootLength(1, false), null, [], [], false, 0, null);
        var options = new SvgRenderOptions(null, 1, new SvgColor(1, 2, 3, 255), Limits());

        Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, options, budget, cancellation.Token));

        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Rasterization_releases_the_surface_reservation_when_scene_validation_throws()
    {
        RenderBudget budget = new(8);
        SvgRoot root = new(new SvgRootLength(1, false), new SvgRootLength(1, false), null, [], [new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, null, -1)], false, 0, null);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, Limits());

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, options, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Rasterization_rejects_an_unresolved_default_geometry_placeholder()
    {
        RenderBudget budget = new(8);
        SvgRoot root = new(new SvgRootLength(1, false), new SvgRootLength(1, false), null, [default], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
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
        return new SvgRoot(new SvgRootLength(2, false), new SvgRootLength(2, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
    }

    private static SvgRenderLimits PatternLimits(int maximumWorkUnits) => new(1_000, 1_000, 64, 4_096, 1_000, 64, 64, maximumWorkUnits);
    private static SvgRoot GlyphRoot()
    {
        SvgGlyphQuadraticContour contour = new([
            Line(0, 0, 1, 0), Line(1, 0, 1, 1), Line(1, 1, 0, 1), Line(0, 1, 0, 0)]);
        SvgGeometry geometry = SvgGeometry.FromGlyphContours(new SvgGlyphGeometry([contour], new SvgColor(0, 0, 0, 255)));
        return new SvgRoot(new SvgRootLength(1, false), new SvgRootLength(1, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
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
    public void Many_curves_fit_linear_scratch_budget()
    {
        string curve = "<path d='M0,4 C2,2 6,2 8,4 Z' fill='red'/>";
        System.Text.StringBuilder manyDocument = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>");
        for (int index = 0; index < 20; index++) manyDocument.Append(curve);
        manyDocument.Append("</svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget singleBudget = new(limits.MaximumAccountedBytes);
        using (SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + curve + "</svg>"), limits, singleBudget, CancellationToken.None)) { }
        long singlePeak = singleBudget.Snapshot().PeakBytes;
        RenderBudget manyBudget = new(limits.MaximumAccountedBytes);
        using (SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(manyDocument.ToString()), limits, manyBudget, CancellationToken.None)) { }
        long manyPeak = manyBudget.Snapshot().PeakBytes;
        Assert.True(manyPeak < 500_000, "Twenty curves must not reserve global-maximum scratch.");
        Assert.True(manyPeak - singlePeak < 100_000, "Per-curve incremental cost must stay flat; scratch must be released between lowerings.");
    }

    [Fact]
    public void Outlined_wordmark_fits_linear_scratch_budget()
    {
        // R38 remainder: an outlined wordmark (outer/inner counter pairs per glyph)
        // must scale scratch with curve commands, not global limits.
        string glyph = "<path d='M1,0 C4,0 7,1 7,4 C7,7 4,8 1,8 C0,8 0,7 0,4 C0,1 0,0 1,0 Z M3,2 C4,2 5,3 5,4 C5,5 4,6 3,6 C2,6 2,5 2,4 C2,3 2,2 3,2 Z' fill='red' fill-rule='evenodd'/>";
        System.Text.StringBuilder wordmark = new("<svg xmlns='http://www.w3.org/2000/svg' width='84' height='8'>");
        for (int index = 0; index < 8; index++) wordmark.Append("<g transform='translate(").Append(index * 10).Append(",0)'>").Append(glyph).Append("</g>");
        wordmark.Append("</svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget singleBudget = new(limits.MaximumAccountedBytes);
        using (SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12' height='8'>" + glyph + "</svg>"), limits, singleBudget, CancellationToken.None)) { }
        long singlePeak = singleBudget.Snapshot().PeakBytes;
        RenderBudget manyBudget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(wordmark.ToString()), limits, manyBudget, CancellationToken.None);
        Assert.Equal(8, root.Geometries.Length);
        long manyPeak = manyBudget.Snapshot().PeakBytes;
        Assert.True(manyPeak < 500_000, "Eight outlined glyphs must not reserve global-maximum scratch.");
        Assert.True(manyPeak - singlePeak < 100_000, "Per-glyph incremental cost must stay flat; scratch must be released between lowerings.");
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
    public void Use_instance_mapping_charges_one_work_unit_per_mapped_point()
    {
        // R39: mapping work is charged incrementally per mapped polygon point
        // inside the Apply dispatch rather than post-hoc at entry points.
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8)];
        int[] ends = [3];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgDefinition definition = new([SvgGeometry.FromPolygonFill(fill), SvgGeometry.FromRectangle(new SvgRectangle(0, 0, 1, 1, SvgColor.Transparent, false, null))], false, null, null);
            using SvgDocumentState state = new(new RenderBudget(100_000), new ParserWorkCounter(100_000), CancellationToken.None);
            ParserWorkCounter workCounter = new(100_000);
            SvgGeometry[] applied = SvgReferenceResolver.Apply(definition, new SvgUseReference("tile", 0, 0, 0, 0, SvgAffineTransform.Identity, SvgAffineTransform.Identity, 255, true, null, null, null), null, state, workCounter, CancellationToken.None);
            Assert.Equal(3, workCounter.Used);
            Assert.Equal(2, applied.Length);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Use_instance_mapping_observes_cancellation_during_point_mapping()
    {
        // R39: per-loop charging observes cancellation inside the mapping loop,
        // so a cancelled resolve stops before scene transfer.
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8)];
        int[] ends = [3];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgDefinition definition = new([SvgGeometry.FromPolygonFill(fill)], false, null, null);
            using SvgDocumentState state = new(new RenderBudget(100_000), new ParserWorkCounter(100_000), CancellationToken.None);
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => SvgReferenceResolver.Apply(definition, new SvgUseReference("tile", 0, 0, 0, 0, SvgAffineTransform.Identity, SvgAffineTransform.Identity, 255, true, null, null, null), null, state, new ParserWorkCounter(100_000), cancelled.Token));
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Use_instance_mapping_denies_when_work_runs_out()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget probe = new(limits.MaximumAccountedBytes);
        long used;
        using (SvgRoot root = SvgRootParser.Parse(svg, limits, probe, CancellationToken.None)) { used = root.ParserWorkUnits; }
        SvgRenderLimits deniedLimits = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)(used - 1)), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
        RenderBudget deniedBudget = new(deniedLimits.MaximumAccountedBytes);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, deniedLimits, deniedBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Use_instance_mapping_denies_and_releases_when_points_exceed_the_remaining_budget()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p'/></svg>");
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
    public void Successful_use_parse_releases_definition_storage_after_root_disposal()
    {
        byte[] probeSvg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p'/></svg>");
        SvgRenderLimits probeLimits = SvgRenderLimits.Default;
        RenderBudget probeBudget = new(probeLimits.MaximumAccountedBytes);
        using (SvgRootParser.Parse(probeSvg, probeLimits, probeBudget, CancellationToken.None)) { }
        Assert.Equal(0, probeBudget.Snapshot().CurrentBytes);
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
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(8, false), new SvgRootLength(8, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 375);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(250, surface.WorkUnits);
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
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(8, false), new SvgRootLength(8, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 80);
            SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            Assert.Contains("raster work", exception.Message);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Polygon_rasterization_denies_mapping_work_before_mapped_reservation()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='red'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget parseBudget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, parseBudget, CancellationToken.None);
        SvgRenderLimits deniedLimits = new(300, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, 2, limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
        RenderBudget rasterBudget = new(deniedLimits.MaximumAccountedBytes);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, deniedLimits), rasterBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("vertex mapping", exception.Message);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Radial_polygon_rasterization_denies_and_releases_under_tight_budget()
    {
        // R39 remainder: denial coverage grows with new geometry kinds. Radial
        // fills share the polygon mapping path and must deny before work all
        // the same.
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget parseBudget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, parseBudget, CancellationToken.None);
        RenderBudget rasterBudget = new(300);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }
    [Fact]
    public void Gradient_polygon_rasterization_denies_and_releases_under_tight_budget()
    {
        // R39 remainder: denial coverage grows with new geometry kinds. Gradient fills
        // share the polygon mapping path and must deny before work all the same.
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><polygon points='0,0 8,0 0,8' fill='url(#g)'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget parseBudget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, parseBudget, CancellationToken.None);
        RenderBudget rasterBudget = new(300);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }
    [Fact]
    public void Pattern_polygon_rasterization_denies_and_releases_under_tight_budget()
    {
        // R39 remainder: denial coverage grows with new geometry kinds. Pattern
        // fills share the polygon mapping path and must deny before work all
        // the same.
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><polygon points='0,0 8,0 0,8' fill='url(#p)'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget parseBudget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, parseBudget, CancellationToken.None);
        RenderBudget rasterBudget = new(300);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }
    [Fact]
    public void Curved_solid_fill_denies_and_releases_under_tight_raster_budget()
    {
        // R39 remainder: denial coverage grows with new geometry kinds. Solid
        // curved fills flatten through budgeted scratch before painting.
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 Q8,0 8,8 L0,8 Z' fill='red'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget parseBudget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, parseBudget, CancellationToken.None);
        RenderBudget rasterBudget = new(300);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Curved_gradient_fill_denies_and_releases_under_tight_raster_budget()
    {
        // R39 remainder: denial coverage grows with new geometry kinds. Curved
        // gradient fills flatten through budgeted scratch before repainting.
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient></defs><path d=\"M0,0 Q8,0 8,8 L0,8 Z\" fill=\"url(#g)\"/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget parseBudget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, parseBudget, CancellationToken.None);
        RenderBudget rasterBudget = new(300);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }
    [Fact]
    public void Curved_gradient_fill_denies_and_releases_under_tight_parse_budget()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient></defs><path d=\"M0,0 Q8,0 8,8 L0,8 Z\" fill=\"url(#g)\"/></svg>");
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
    public void Polygon_rasterization_observes_cancellation_during_vertex_mapping()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(100, 100), new(108, 100), new(100, 108)];
        int[] ends = [3];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(8, false), new SvgRootLength(8, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100_000);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, cancellation.Token));
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Polygon_rasterization_emits_analytic_rows_without_fan_per_contour()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8)];
        int[] ends = [3];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(8, false), new SvgRootLength(8, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100_000);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(0, fanEvaluations);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Polygon_rasterization_emits_analytic_rows_without_fan_under_evenodd_roles()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8)];
        int[] ends = [3];
        int[] signs = [1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, signs, new SvgColor(255, 0, 0, 255), true, true, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(8, false), new SvgRootLength(8, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100_000);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(0, fanEvaluations);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Polygon_rasterization_emits_analytic_rows_without_fan_under_nonzero_roles()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8)];
        int[] ends = [3];
        int[] roles = [1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, roles, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(8, false), new SvgRootLength(8, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100_000);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(0, fanEvaluations);
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
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(8, false), new SvgRootLength(8, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
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
    public void Radial_parse_denies_and_releases_when_stops_exceed_the_remaining_budget()
    {
        // R39 remainder: denial coverage grows with new geometry kinds. Radial
        // stop arrays are scene-owned like linear ones and must deny before
        // exceeding the remaining budget with nothing retained.
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(EightStopRadialGradientSvg());
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
        SvgLinearGradient gradient = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 0, 0, 1, false, false, SvgGradientSpread.Pad, stops, 1, 1, false);
        SvgRectangle box = new(x, 0, 2, 2, default, true, null);
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, box, 255, null));
    }

    private static SvgGeometry EmptyGradientFill()
    {
        SvgLinearGradient gradient = new(default, default, 0, 0, 1, 0, 0, 1, true, false, SvgGradientSpread.Pad, Array.Empty<SvgGradientStop>(), 1, 1, false);
        SvgRectangle box = new(8, 0, 2, 2, default, true, null);
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, box, 255, null));
    }

    private static long SceneBytes(RenderBudget budget) => budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Scene];

    private static SvgGeometry RadialFill(SvgGradientStop[] stops)
    {
        SvgRadialGradient gradient = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, stops, 1, 1, false, SvgGradientTransform.Identity, false);
        SvgRectangle box = new(0, 0, 2, 2, default, true, null);
        return SvgGeometry.FromRadialFill(new SvgRadialGeometry(gradient, box, box, 255, null));
    }

    private static SvgGeometry EmptyRadialFill()
    {
        SvgRadialGradient gradient = new(default, default, 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, true, false, SvgGradientSpread.Pad, Array.Empty<SvgGradientStop>(), 1, 1, false, SvgGradientTransform.Identity, false);
        SvgRectangle box = new(8, 0, 2, 2, default, true, null);
        return SvgGeometry.FromRadialFill(new SvgRadialGeometry(gradient, box, box, 255, null));
    }

    private static string EightStopRadialGradientSvg() => "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><radialGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"0.15\" stop-color=\"#ff8000\"/><stop offset=\"0.3\" stop-color=\"#ffff00\"/><stop offset=\"0.45\" stop-color=\"#00ff00\"/><stop offset=\"0.6\" stop-color=\"#00ffff\"/><stop offset=\"0.75\" stop-color=\"#0000ff\"/><stop offset=\"0.9\" stop-color=\"#8000ff\"/><stop offset=\"1\" stop-color=\"#ff00ff\"/></radialGradient></defs><rect x=\"0\" y=\"0\" width=\"16\" height=\"16\" fill=\"url(#g)\"/></svg>";

    private static string EightStopGradientSvg() => "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"0.15\" stop-color=\"#ff8000\"/><stop offset=\"0.3\" stop-color=\"#ffff00\"/><stop offset=\"0.45\" stop-color=\"#00ff00\"/><stop offset=\"0.6\" stop-color=\"#00ffff\"/><stop offset=\"0.75\" stop-color=\"#0000ff\"/><stop offset=\"0.9\" stop-color=\"#8000ff\"/><stop offset=\"1\" stop-color=\"#ff00ff\"/></linearGradient></defs><rect x=\"0\" y=\"0\" width=\"16\" height=\"16\" fill=\"url(#g)\"/></svg>";

    private static SvgRenderLimits Limits() => Limits(8);

    private static SvgRenderLimits Limits(int maximumWorkUnits) => new(8, 8, 1, 1, 8, 8, 8, maximumWorkUnits);

    private static void AssertBudgetReleased(RenderBudget budget)
    {
        RenderBudgetSnapshot snapshot = budget.Snapshot();
        Assert.Equal(0, snapshot.CurrentBytes);
        Assert.Equal(0, snapshot.BytesByCategory[(int)SvgMemoryCategory.Surface]);
    }

    [Fact]
    public void Crossing_split_denies_when_budget_covers_mapping_but_not_the_split()
    {
        // R38/R39: the scene pre-reservation precedes retained copies and stays
        // held while the split lists grow, so a budget sized for mapping,
        // retention and split points but not split ends fails at the split.
        RenderBudget roomyBudget = new(100_000);
        ParserWorkCounter roomyCounter = new(100_000);
        using BudgetedParserList<SvgPoint> points = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        points.Add(new SvgPoint(0, 0));
        points.Add(new SvgPoint(8, 8));
        points.Add(new SvgPoint(8, 0));
        points.Add(new SvgPoint(0, 8));
        using BudgetedParserList<int> ends = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        ends.Add(4);
        RenderBudget tightBudget = new(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) + ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) + ParserCollectionMemory.GetArrayByteCount<int>(1) + ParserCollectionMemory.GetArrayByteCount<int>(1) + ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) + ParserCollectionMemory.GetArrayByteCount<int>(4) - 1);

        SvgException denied = Assert.Throws<SvgException>(() =>
        {
            using SvgOrthogonalGeometryLowering lowering = new(points, ends, "polygon", false, false, 100_000, roomyBudget, roomyCounter, CancellationToken.None);
            return lowering.LowerPolygonFill(new SvgColor(255, 0, 0, 255), true, SvgAffineTransform.Identity, null, tightBudget);
        });

        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        AssertBudgetReleased(tightBudget);
    }
    [Fact]
    public void Crossing_split_denies_multi_crossing_when_scene_pre_reserve_exhausted()
    {
        // R39: the scene pre-reservation precedes retained copies, so a budget
        // sized near the mapped input fails at the pre-reserve with full release.
        RenderBudget roomyBudget = new(100_000);
        ParserWorkCounter roomyCounter = new(100_000);
        using BudgetedParserList<SvgPoint> points = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        points.Add(new SvgPoint(0, 0));
        points.Add(new SvgPoint(6, 6));
        points.Add(new SvgPoint(6, 2));
        points.Add(new SvgPoint(0, 8));
        points.Add(new SvgPoint(6, 8));
        using BudgetedParserList<int> ends = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        ends.Add(5);
        RenderBudget tightBudget = new(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(6) + ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(6) + ParserCollectionMemory.GetArrayByteCount<int>(6) - 1);

        SvgException denied = Assert.Throws<SvgException>(() =>
        {
            using SvgOrthogonalGeometryLowering lowering = new(points, ends, "polygon", false, false, 100_000, roomyBudget, roomyCounter, CancellationToken.None);
            return lowering.LowerPolygonFill(new SvgColor(255, 0, 0, 255), true, SvgAffineTransform.Identity, null, tightBudget);
        });

        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        AssertBudgetReleased(tightBudget);
    }

    [Fact]
    public void Polygon_retention_denies_and_releases_when_scene_pre_reserve_exceeds_budget()
    {
        // R39: the scene pre-reservation precedes retained copies, so a budget
        // sized for mapping but not retention fails before allocating copies.
        RenderBudget roomyBudget = new(100_000);
        ParserWorkCounter roomyCounter = new(100_000);
        using BudgetedParserList<SvgPoint> points = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        points.Add(new SvgPoint(0, 0));
        points.Add(new SvgPoint(8, 0));
        points.Add(new SvgPoint(0, 8));
        using BudgetedParserList<int> ends = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        ends.Add(3);
        RenderBudget tightBudget = new(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) + ParserCollectionMemory.GetArrayByteCount<int>(1) + ParserCollectionMemory.GetArrayByteCount<int>(1) - 1);

        SvgException denied = Assert.Throws<SvgException>(() =>
        {
            using SvgOrthogonalGeometryLowering lowering = new(points, ends, "polygon", false, false, 100_000, roomyBudget, roomyCounter, CancellationToken.None);
            return lowering.LowerPolygonFill(new SvgColor(255, 0, 0, 255), true, SvgAffineTransform.Identity, null, tightBudget);
        });

        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        AssertBudgetReleased(tightBudget);
    }

    [Fact]
    public void Gradient_polygon_retention_denies_and_releases_when_scene_pre_reserve_exceeds_budget()
    {
        // R39: gradient retention shares the pre-reserved contour boundary, so
        // the same mapping-sized budget fails before allocating copies.
        RenderBudget roomyBudget = new(100_000);
        ParserWorkCounter roomyCounter = new(100_000);
        using BudgetedParserList<SvgPoint> points = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        points.Add(new SvgPoint(0, 0));
        points.Add(new SvgPoint(8, 0));
        points.Add(new SvgPoint(0, 8));
        using BudgetedParserList<int> ends = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        ends.Add(3);
        RenderBudget tightBudget = new(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) + ParserCollectionMemory.GetArrayByteCount<int>(1) + ParserCollectionMemory.GetArrayByteCount<int>(1) - 1);
        SvgLinearGradient gradient = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 1, 0, 1, false, false, SvgGradientSpread.Pad, [new SvgGradientStop(0, new SvgColor(255, 0, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))], 1, 1, false);
        SvgRectangle box = new(0, 0, 8, 8, default, true, null);

        SvgException denied = Assert.Throws<SvgException>(() =>
        {
            using SvgOrthogonalGeometryLowering lowering = new(points, ends, "polygon", false, false, 100_000, roomyBudget, roomyCounter, CancellationToken.None);
            return lowering.LowerGradientPolygonFill(gradient, box, true, 255, SvgAffineTransform.Identity, null, tightBudget);
        });

        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        AssertBudgetReleased(tightBudget);
    }

    [Fact]
    public void Radial_polygon_retention_denies_and_releases_when_scene_pre_reserve_exceeds_budget()
    {
        // R39: radial retention shares the pre-reserved contour boundary, so
        // the same mapping-sized budget fails before allocating copies.
        RenderBudget roomyBudget = new(100_000);
        ParserWorkCounter roomyCounter = new(100_000);
        using BudgetedParserList<SvgPoint> points = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        points.Add(new SvgPoint(0, 0));
        points.Add(new SvgPoint(8, 0));
        points.Add(new SvgPoint(0, 8));
        using BudgetedParserList<int> ends = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        ends.Add(3);
        RenderBudget tightBudget = new(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) + ParserCollectionMemory.GetArrayByteCount<int>(1) + ParserCollectionMemory.GetArrayByteCount<int>(1) - 1);
        SvgRadialGradient circle = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, [new SvgGradientStop(0, new SvgColor(255, 0, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))], 1, 1, false, SvgGradientTransform.Identity, false);
        SvgRectangle box = new(0, 0, 8, 8, default, true, null);

        SvgException denied = Assert.Throws<SvgException>(() =>
        {
            using SvgOrthogonalGeometryLowering lowering = new(points, ends, "polygon", false, false, 100_000, roomyBudget, roomyCounter, CancellationToken.None);
            return lowering.LowerRadialPolygonFill(circle, box, true, 255, SvgAffineTransform.Identity, null, tightBudget);
        });

        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        AssertBudgetReleased(tightBudget);
    }

    [Fact]
    public void Polygon_retention_transfers_exact_scene_bytes_to_the_fill()
    {
        // R39: the adopted pre-reservation single-counts retained arrays, so a
        // successful lowering holds exactly points, ends and signs in Scene.
        RenderBudget budget = new(100_000);
        ParserWorkCounter counter = new(100_000);
        using (BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, counter, CancellationToken.None))
        using (BudgetedParserList<int> ends = new(budget, SvgMemoryCategory.Geometry, counter, CancellationToken.None))
        {
        points.Add(new SvgPoint(0, 0));
        points.Add(new SvgPoint(8, 0));
        points.Add(new SvgPoint(0, 8));
        ends.Add(3);
        using (SvgOrthogonalGeometryLowering lowering = new(points, ends, "polygon", false, false, 100_000, budget, counter, CancellationToken.None))
        using (SvgParsedGeometry parsed = lowering.LowerPolygonFill(new SvgColor(255, 0, 0, 255), true, SvgAffineTransform.Identity, null, budget))
        {
            Assert.Equal(1, parsed.Geometries.Length);
            Assert.Equal(SvgGeometryKind.PolygonFill, parsed.Geometries[0].Kind);
            Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(3) + ParserCollectionMemory.GetArrayByteCount<int>(1) + ParserCollectionMemory.GetArrayByteCount<int>(1), SceneBytes(budget));
            foreach (SvgGeometry geometry in parsed.Geometries) geometry.Dispose();
        }
        }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Stroke_retention_denies_and_releases_when_scene_pre_reserve_exceeds_budget()
    {
        // R39: stroke retention shares the pre-reserved boundary, so a budget
        // sized for mapping and outlining but not retention fails before copying.
        RenderBudget roomyBudget = new(100_000);
        ParserWorkCounter roomyCounter = new(100_000);
        using BudgetedParserList<SvgPoint> points = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        points.Add(new SvgPoint(0, 0));
        points.Add(new SvgPoint(8, 0));
        using BudgetedParserList<int> ends = new(roomyBudget, SvgMemoryCategory.Geometry, roomyCounter, CancellationToken.None);
        ends.Add(2);
        RenderBudget tightBudget = new(
            ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) +
            ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(16) +
            2 * ParserCollectionMemory.GetArrayByteCount<int>(1) +
            ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) +
            ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) +
            ParserCollectionMemory.GetArrayByteCount<int>(1) +
            ParserCollectionMemory.GetArrayByteCount<int>(1) - 1);
        SvgException denied = Assert.Throws<SvgException>(() =>
        {
            using SvgOrthogonalGeometryLowering lowering = new(points, ends, "polyline", false, false, 100_000, roomyBudget, roomyCounter, CancellationToken.None);
            return lowering.LowerStrokePolyline(points, false, SvgAffineTransform.Identity, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, null, 0, null, false, new SvgColor(255, 0, 0, 255), false, null, tightBudget);
        });
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        AssertBudgetReleased(tightBudget);
    }

    [Fact]
    public void Disjoint_twin_triangles_take_analytic_rows_without_fan()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8), new(8, 8), new(16, 8), new(8, 16)];
        int[] ends = [3, 6];
        int[] roles = [1, 1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, roles, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100_000);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(0, fanEvaluations);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Nested_counter_takes_analytic_rows_without_fan()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(16, 0), new(16, 16), new(0, 16), new(12, 4), new(12, 12), new(4, 12), new(4, 4)];
        int[] ends = [4, 8];
        int[] roles = [1, -1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, roles, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100_000);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(0, fanEvaluations);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Evenodd_twin_takes_analytic_rows_without_fan()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8), new(8, 8), new(16, 8), new(8, 16)];
        int[] ends = [3, 6];
        int[] signs = [1, 1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, signs, new SvgColor(255, 0, 0, 255), true, true, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100_000);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(0, fanEvaluations);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Edge_sharing_multi_keeps_fan_path()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8), new(8, 0), new(8, 8), new(0, 8)];
        int[] ends = [3, 6];
        int[] roles = [1, 1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, roles, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100_000);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.True(fanEvaluations > 0);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Overlapping_multi_keeps_fan_path()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8), new(4, 0), new(12, 0), new(4, 8)];
        int[] ends = [3, 6];
        int[] roles = [1, 1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, roles, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1)], false, 0, null);
            SvgRenderLimits limits = new(100_000, 8_192, 16, 4_096, 4_096, 16, 16, 100_000);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.True(fanEvaluations > 0);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Multi_contour_preparation_charges_set_scan_and_reports_eligibility()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] twin = [new(0, 0), new(8, 0), new(0, 8), new(8, 8), new(16, 8), new(8, 16)];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(twin, [3, 6], [], false, [1, 1], SvgPolygonMapping.QuarterEdges, 0, 16, 0, 16, 16, 16, budget, out long twinSetup, out bool twinEligible, CancellationToken.None))
        {
            Assert.True(twinEligible);
            Assert.Equal(104, twinSetup);
        }
        SvgDevicePoint[] touching = [new(0, 0), new(8, 0), new(0, 8), new(8, 0), new(8, 8), new(0, 8)];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(touching, [3, 6], [], false, [1, 1], SvgPolygonMapping.QuarterEdges, 0, 16, 0, 16, 16, 16, budget, out long touchingSetup, out bool touchingEligible, CancellationToken.None))
        {
            Assert.False(touchingEligible);
        }
        SvgDevicePoint[] degenerate = [new(0, 0), new(0, 0), new(0, 0)];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(degenerate, [3], [], false, [0], SvgPolygonMapping.QuarterEdges, 0, 16, 0, 16, 16, 16, budget, out long degenerateSetup, out bool degenerateEligible, CancellationToken.None))
        {
            Assert.False(degenerateEligible);
        }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Shared_vertex_multi_keeps_fan_path()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] lobes = [new(2, 2), new(4, 3), new(8, 4), new(8, 8), new(2, 8), new(4, 4), new(4, 2), new(4, 0), new(2, 2), new(0, 4), new(0, 0), new(2, 2)];
        int[] ends = [5, 9, 12];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(lobes, ends, [], false, null, SvgPolygonMapping.QuarterEdges, 0, 16, 0, 16, 16, 16, budget, out long _, out bool eligible, CancellationToken.None))
        {
            Assert.False(eligible);
        }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Self_touching_curve_multi_takes_analytic_path()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] loops = [new(0, 0), new(8, 0), new(0, 8), new(16, 0), new(24, 8), new(24, 0), new(20, 4)];
        int[] ends = [3, 7];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(loops, ends, [], false, null, SvgPolygonMapping.FlattenedCurves, 0, 32, 0, 16, 32, 16, budget, out long _, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
        }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Transversal_crossing_straight_multi_takes_analytic_path()
    {
        // Row 4: straight-edge loops meeting only in transversal crossings union through
        // signed-winding intervals on any mapping; touches and coincident spans keep the fan path.
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] bands = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(2, 2), new(6, 2), new(6, 6), new(2, 6)];
        int[] ends = [4, 8];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(bands, ends, [], false, [1, 1], SvgPolygonMapping.FiniteEdges, 0, 8, 0, 8, 8, 8, budget, out long _, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
        }
        AssertBudgetReleased(budget);
    }

    [Fact]

    public void Curve_contour_arrays_fit_command_bound()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M1,0 C4,0 7,1 7,4 C7,7 4,8 1,8 C0,8 0,7 0,4 C0,1 0,0 1,0 Z' fill='red'/></svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        using SvgRoot root = SvgRootParser.Parse(svg, limits, budget, CancellationToken.None);
        Assert.True(budget.Snapshot().PeakBytes < 260_000, "Curve contour arrays must scale with commands, not curve expansion.");
    }
}
