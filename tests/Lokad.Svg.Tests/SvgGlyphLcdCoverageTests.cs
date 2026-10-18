using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(22, 0, 11, 15)]
    [InlineData(23, 15, 5, 0)]
    public void Instruction_free_simple_dropouts_keep_the_native_left_tip(int x, byte red, byte green, byte blue)
    {
        // Independent DirectWrite texture of the original CC0 Primary.ttf:
        // em 32, origin (16,166), metric origin 80, no instructions.
        OpenTypeOutlinePoint[] points = [new(16, 166), new(16 + 450d / 64, 166 - 1434d / 64), new(16 + 901d / 64, 166)];
        OpenTypeQuadraticSegment[] segments = Enumerable.Range(0, points.Length).Select(i => new OpenTypeQuadraticSegment(points[i], points[(i + 1) % points.Length], points[(i + 1) % points.Length])).ToArray();
        var work = new ParserWorkCounter(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, false, true, work, CancellationToken.None);
        Assert.Equal(new SvgGlyphRgbCoverage(red, green, blue), SvgGlyphLcdCoverage.GetCoverage(segments, x, 143, new SvgGlyphLcdCoverage.Crossing[6], bounds, work, CancellationToken.None));
    }

    [Theory]
    [InlineData(62, 186, 71, 89, 58)]
    [InlineData(63, 186, 27, 0, 0)]
    [InlineData(57, 192, 0, 48, 124)]
    [InlineData(57, 193, 5, 5, 5)]
    public void Simple_second_pass_observes_first_pass_cells_across_pixel_rows(int x, int y, byte red, byte green, byte blue)
    {
        // Independent same-byte DirectWrite texture of Secondary.ttf at the
        // retained text-path transform. Explicit SCANTYPE 0 reproduces it;
        // SCANTYPE 2 and 4 distinguish missing and duplicate tip cells.
        OpenTypeOutlinePoint[][] polygons =
        [
            [new(58, 210.6), new(54.578125, 195.63125), new(64.875, 193.271875), new(68.296875, 208.240625)],
            [new(57.578125, 192.975), new(60.5, 187.053125), new(63, 186.490625), new(60.078125, 192.396875)]
        ];
        OpenTypeQuadraticSegment[] segments = polygons.SelectMany(points => Enumerable.Range(0, points.Length).Select(i => new OpenTypeQuadraticSegment(points[i], points[(i + 1) % points.Length], points[(i + 1) % points.Length]))).ToArray();
        var work = new ParserWorkCounter(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, false, true, work, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        SvgGlyphRgbCoverage expected = new(red, green, blue);
        Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, work, CancellationToken.None));
        RenderBudget budget = new(long.MaxValue);
        using (SvgGlyphLcdCoverage.ScanStorage rows = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, budget, work, CancellationToken.None) ?? throw new InvalidOperationException("The native control must fit."))
        {
            Span<int> counts = stackalloc int[10];
            Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(rows.PrepareRow(segments, y, counts, work, CancellationToken.None), x, scratch, bounds, work, CancellationToken.None));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 5, 24)]
    [InlineData(0, 0, 0, 1, 4, 30)]
    [InlineData(0, 0, 0, 2, 6, 50)]
    [InlineData(0, 0, 0, 6, 1, 68)]
    [InlineData(0, 0, 1, 1, 0, 22)]
    [InlineData(0, 0, 1, 3, 1, 48)]
    [InlineData(0, 0, 1, 4, 0, 54)]
    [InlineData(0, 0, 1, 4, 3, 68)]
    public void Vertical_filter_matches_independent_native_cell_masks(int first, int second, int third, int fourth, int fifth, byte expected)
    {
        // Original CC0 grid fonts: UPEM 960, em 48, integer contours. Values were
        // measured independently from DirectWrite, before managed implementation.
        int[] counts = [first, second, third, fourth, fifth];
        List<OpenTypeQuadraticSegment> segments = [];
        for (int row = 0; row < counts.Length; row++)
            if (counts[row] != 0) segments.AddRange(Rectangle(0, row / 5d, Math.Round(counts[row] * 20d / 6) / 20, (row + 1) / 5d));
        SvgGlyphRgbCoverage coverage = Coverage(segments.ToArray(), 0, 0);
        Assert.Equal(expected, coverage.Green);
    }

    [Theory]
    [InlineData(-1, 0, 0, 85)]
    [InlineData(0, 170, 255, 255)]
    [InlineData(1, 255, 255, 255)]
    [InlineData(5, 255, 255, 170)]
    [InlineData(6, 85, 0, 0)]
    public void Displaced_channels_match_independent_six_pixel_square(int x, byte red, byte green, byte blue)
        => Assert.Equal(new SvgGlyphRgbCoverage(red, green, blue), Coverage(Rectangle(0, 0, 6, 6), x, 0));

    [Fact]
    public void Opposite_winding_cancels_counters_and_same_winding_keeps_union()
    {
        OpenTypeQuadraticSegment[] outer = Rectangle(-1, -1, 2, 2), inner = Rectangle(0, 0, 1, 1);
        OpenTypeQuadraticSegment[] reversed = inner.Reverse().Select(segment => new OpenTypeQuadraticSegment(segment.End, segment.Start, segment.Start)).ToArray();
        Assert.Equal((byte)255, Coverage([.. outer, .. inner], 0, 0).Green);
        Assert.Equal((byte)0, Coverage([.. outer, .. reversed], 0, 0).Green);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Quadratic_endpoint_on_sample_row_keeps_closed_winding(bool reverse, bool reflect)
    {
        OpenTypeQuadraticSegment[] segments =
        [
            new(new(0, .5), new(1, 0), new(2, 0)),
            new(new(2, 0), new(2, 1), new(2, 1)),
            new(new(2, 1), new(0, 1), new(0, 1)),
            new(new(0, 1), new(0, .5), new(0, .5))
        ];
        if (reflect) segments = segments.Select(s => new OpenTypeQuadraticSegment(Mirror(s.Start), Mirror(s.Control), Mirror(s.End))).ToArray();
        if (reverse) segments = segments.Reverse().Select(s => new OpenTypeQuadraticSegment(s.End, s.Control == s.End ? s.Start : s.Control, s.Start)).ToArray();
        Assert.Equal(default(SvgGlyphRgbCoverage), Coverage(segments, 3, 0));
        Assert.NotEqual((byte)0, Coverage(segments, 1, 0).Green);

        static OpenTypeOutlinePoint Mirror(OpenTypeOutlinePoint point) => new(point.X, 1 - point.Y);
    }

    [Fact]
    public void Coverage_observes_cancellation_and_bounds_segment_work()
    {
        OpenTypeQuadraticSegment[] segments = Rectangle(0, 0, 1, 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphLcdCoverage.GetCoverage(segments, 0, 0, new SvgGlyphLcdCoverage.Crossing[8], false, new(2), CancellationToken.None)).ErrorCode);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphLcdCoverage.GetCoverage([], 0, 0, [], false, new(long.MaxValue), cancelled.Token));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 28)]
    public void Native_thin_horizontal_stroke_requires_enabled_smart_dropouts(bool enabled, byte expected)
    {
        // Original CC0 font: UPEM 1024, em 16, rectangle (0,0)-(384,1),
        // prep SCANCTRL 511, SCANTYPE 2 or 4. Native symmetric LCD texture
        // at an interior pixel is respectively (0,0,0) or (28,28,28).
        OpenTypeQuadraticSegment[] segments = Rectangle(0, 0, 6, 1d / 64);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[8];
        Assert.Equal(new SvgGlyphRgbCoverage(expected, expected, expected), SvgGlyphLcdCoverage.GetCoverage(segments, 2, 0, scratch, enabled, new(long.MaxValue), CancellationToken.None));
    }

    [Fact]
    public void Native_slanted_stroke_preserves_last_row_dropout()
    {
        // Independent original CC0 trapezoid: points (0,0),(604,0),
        // (401,768),(0,768), UPEM 1024, em 16. Baseline is at device Y12.
        OpenTypeOutlinePoint[] points = [new(0, 12), new(604d / 64, 12), new(401d / 64, 0), new(0, 0)];
        OpenTypeQuadraticSegment[] segments = Enumerable.Range(0, 4).Select(i => new OpenTypeQuadraticSegment(points[i], points[(i + 1) % 4], points[(i + 1) % 4])).ToArray();
        SvgGlyphRgbCoverage actual = SvgGlyphLcdCoverage.GetCoverage(segments, 9, 11, new SvgGlyphLcdCoverage.Crossing[8], true, new(long.MaxValue), CancellationToken.None);
        Assert.Equal(new SvgGlyphRgbCoverage(170, 85, 5), actual);
    }

    [Fact]
    public void Smart_dropout_scratch_and_crossing_work_remain_bounded()
    {
        OpenTypeQuadraticSegment[] segments = Rectangle(0, -1d / 64, 6, 0);
        Assert.Throws<ArgumentException>(() => SvgGlyphLcdCoverage.GetCoverage(segments, 2, 0, new SvgGlyphLcdCoverage.Crossing[7], true, new(long.MaxValue), CancellationToken.None));
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[8];
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphLcdCoverage.GetCoverage(segments, 2, 0, scratch, true, new(15), CancellationToken.None)).ErrorCode);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphLcdCoverage.GetCoverage(segments, 2, 0, scratch, true, new(long.MaxValue), cancelled.Token));
        OpenTypeQuadraticSegment[] reversed = segments.Reverse().Select(s => new OpenTypeQuadraticSegment(s.End, s.Start, s.Start)).ToArray();
        // Independent complete native textures in windows-smart-stubs.json:
        // opposite thin contours cancel normal winding, but smart dropouts
        // still retain their cells. SCANTYPE 2 disables that coverage.
        Assert.Equal(default(SvgGlyphRgbCoverage), SvgGlyphLcdCoverage.GetCoverage([.. segments, .. reversed], 2, -1, new SvgGlyphLcdCoverage.Crossing[16], false, new(long.MaxValue), CancellationToken.None));
        Assert.Equal(new SvgGlyphRgbCoverage(28, 28, 28), SvgGlyphLcdCoverage.GetCoverage([.. segments, .. reversed], 2, -1, new SvgGlyphLcdCoverage.Crossing[16], true, new(long.MaxValue), CancellationToken.None));
    }

    [Theory]
    [InlineData(false, 0, 0, 85, 170)]
    [InlineData(false, 6, 255, 213, 128)]
    [InlineData(true, 0, 0, 43, 128)]
    [InlineData(true, 6, 255, 170, 85)]
    public void Native_exact_vertical_centers_follow_directed_contour_ownership(bool reverse, int x, byte red, byte green, byte blue)
    {
        // Independently captured original CC0 rectangles: reversing the
        // identical outline includes/excludes both exact sample-center borders.
        OpenTypeQuadraticSegment[] segments = Rectangle(.75, -6, 6.75, 0);
        if (reverse) segments = segments.Reverse().Select(s => new OpenTypeQuadraticSegment(s.End, s.Start, s.Start)).ToArray();
        Assert.Equal(new SvgGlyphRgbCoverage(red, green, blue), Coverage(segments, x, -3));
    }

    [Theory]
    [InlineData(false, 163)]
    [InlineData(true, 92)]
    public void Native_exact_horizontal_center_follows_directed_contour_ownership(bool reverse, byte expected)
    {
        OpenTypeQuadraticSegment[] segments = Rectangle(0, -.5, 6, 0);
        if (reverse) segments = segments.Reverse().Select(s => new OpenTypeQuadraticSegment(s.End, s.Start, s.Start)).ToArray();
        Assert.Equal(new SvgGlyphRgbCoverage(expected, expected, expected), Coverage(segments, 2, -1));
    }

    private static SvgGlyphRgbCoverage Coverage(OpenTypeQuadraticSegment[] segments, int x, int y)
        => SvgGlyphLcdCoverage.GetCoverage(segments, x, y, new SvgGlyphLcdCoverage.Crossing[segments.Length * 2], false, new(long.MaxValue), CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepared_rows_preserve_curve_endpoints_and_reduce_actual_gathering_work(bool dropouts)
    {
        OpenTypeQuadraticSegment[] segments =
        [
            new(new(0, .5), new(1, 0), new(2, 0)),
            new(new(2, 0), new(2, 1), new(2, 1)),
            new(new(2, 1), new(0, 1), new(0, 1)),
            new(new(0, 1), new(0, .5), new(0, .5)),
            .. Rectangle(3, -1d / 64, 6, 0)
        ];
        ParserWorkCounter originalWork = new(long.MaxValue), preparedWork = new(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, dropouts, preparedWork, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] originalScratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        SvgGlyphLcdCoverage.Crossing[] preparedScratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        using SvgGlyphLcdCoverage.ScanStorage rowStorage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, new RenderBudget(long.MaxValue), preparedWork, CancellationToken.None) ?? throw new InvalidOperationException("Row preparation should fit.");
        int[] counts = new int[10];
        for (int y = -1; y <= 1; y++)
        {
            SvgGlyphLcdCoverage.ScanRow row = rowStorage.PrepareRow(segments, y, counts, preparedWork, CancellationToken.None);
            for (int x = -1; x <= 7; x++)
                Assert.Equal(
                    SvgGlyphLcdCoverage.GetCoverage(segments, x, y, originalScratch, dropouts, originalWork, CancellationToken.None),
                    SvgGlyphLcdCoverage.GetCoverage(row, x, preparedScratch, bounds, preparedWork, CancellationToken.None));
        }
        Assert.True(preparedWork.Used < originalWork.Used);
    }

    [Fact]
    public void Row_preparation_checks_borrowed_storage_work_boundary_and_cancellation()
    {
        OpenTypeQuadraticSegment[] segments = Rectangle(0, 0, 6, 6);
        SvgGlyphLcdCoverage.Crossing[] storage = new SvgGlyphLcdCoverage.Crossing[80];
        int[] counts = new int[10];
        Assert.Throws<ArgumentException>(() => { SvgGlyphLcdCoverage.PrepareRow(segments, 0, storage.AsSpan(0, 79), counts, new(long.MaxValue), CancellationToken.None); });
        Assert.Throws<ArgumentException>(() => { SvgGlyphLcdCoverage.PrepareRow(segments, 0, storage, counts.AsSpan(0, 9), new(long.MaxValue), CancellationToken.None); });
        ParserWorkCounter measured = new(long.MaxValue);
        SvgGlyphLcdCoverage.PrepareRow(segments, 0, storage, counts, measured, CancellationToken.None);
        SvgGlyphLcdCoverage.PrepareRow(segments, 0, storage, counts, new(measured.Used), CancellationToken.None);
        SvgException error = Assert.Throws<SvgException>(() => { SvgGlyphLcdCoverage.PrepareRow(segments, 0, storage, counts, new(measured.Used - 1), CancellationToken.None); });
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        using CancellationTokenSource cancelled = new(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => { SvgGlyphLcdCoverage.PrepareRow(segments, 0, storage, counts, new(long.MaxValue), cancelled.Token); });
    }

    [Fact]
    public void Optional_row_storage_yields_to_memory_pressure_and_releases_its_aggregate_allowance()
    {
        RenderBudget budget = new(10_000);
        OpenTypeQuadraticSegment[] segments = Rectangle(0, 0, 6, 6);
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, budget, new(long.MaxValue), CancellationToken.None) ?? throw new InvalidOperationException("Row preparation should fit."))
        {
            Assert.Equal(80, storage.Crossings.Length);
            Assert.True(budget.Snapshot().CurrentBytes > 0);
            Assert.Null(SvgGlyphLcdCoverage.ScanStorage.TryCreate(new OpenTypeQuadraticSegment[100], budget, new(long.MaxValue), CancellationToken.None));
            Assert.Equal(0, budget.Snapshot().DeniedReservationCount);
            storage.Dispose();
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Throws<ObjectDisposedException>(() => { _ = storage.Crossings.Length; });
            Assert.Throws<ObjectDisposedException>(() => { _ = storage.MappedSegments.Length; });
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Null(SvgGlyphLcdCoverage.ScanStorage.TryCreate([], budget, new(long.MaxValue), CancellationToken.None));
        SvgException error = Assert.Throws<SvgException>(() => SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, budget, new(0), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancelled = new(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, budget, new(long.MaxValue), cancelled.Token));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Unprepared_pressure_route_keeps_independent_native_dropout_coverage()
    {
        RenderBudget budget = new(1_000);
        OpenTypeQuadraticSegment[] segments = Rectangle(0, 0, 6, 1d / 64);
        Assert.Null(SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, budget, new(long.MaxValue), CancellationToken.None));
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, true, new(long.MaxValue), CancellationToken.None);
        // Same unchanged independent native thin-stroke result as above.
        Assert.Equal(new SvgGlyphRgbCoverage(28, 28, 28), SvgGlyphLcdCoverage.GetCoverage(segments, 2, 0, new SvgGlyphLcdCoverage.Crossing[8], bounds, new(long.MaxValue), CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(0, budget.Snapshot().DeniedReservationCount);
    }

    private static OpenTypeQuadraticSegment[] Rectangle(double left, double top, double right, double bottom)
    {
        OpenTypeOutlinePoint[] points = [new(left, top), new(right, top), new(right, bottom), new(left, bottom)];
        return Enumerable.Range(0, 4).Select(i => new OpenTypeQuadraticSegment(points[i], points[(i + 1) % 4], points[(i + 1) % 4])).ToArray();
    }
}
