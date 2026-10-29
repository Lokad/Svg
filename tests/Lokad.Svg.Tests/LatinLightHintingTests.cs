using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class LatinLightHintingTests
{
    [Fact]
    public void Pending_edges_bound_fitted_stems_before_serif_interpolation()
    {
        // Original boxes and a triangular spur keep a pending edge between two
        // stems. Native light fitting bounds the middle stem against its original
        // 136-unit position before resolving the unlinked spur.
        OpenTypeGlyphPoint[] points = [.. Box(0, 300, 0, 200), .. Box(600, 900, 677, 904),
            .. Box(350, 1200, 1140, 1349), new(100, 586, true), new(250, 1000, true), new(400, 586, true)];
        LatinAlignmentZone[] zones = [new(0, -20, 1370, -20, false, false), new(1349, 1370, 1370, -20, true, false),
            new(1082, 1102, 1102, -20, true, true)];
        RenderBudget budget = new(long.MaxValue);

        LatinLightHinting.Hint(points, [3, 7, 11, 14], zones, 2048, 8, new(0, 0), budget, CancellationToken.None);

        Assert.Equal(136, points[4].Y);
        Assert.Equal(186, points[5].Y);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Unlinked_serifs_keep_their_offset_when_they_cross_an_earlier_stem()
    {
        OpenTypeGlyphPoint[] points = [.. Box(0, 300, 0, 180), .. Box(500, 750, 499, 651),
            .. Box(300, 900, 920, 1102), new(200, 774, true), new(350, 1050, true), new(500, 774, true)];
        LatinAlignmentZone[] zones = [new(0, -20, 1370, -20, false, false), new(1349, 1370, 1370, -20, true, false),
            new(1082, 1102, 1102, -20, true, true)];
        RenderBudget budget = new(long.MaxValue);

        LatinLightHinting.Hint(points, [3, 7, 11, 14], zones, 2048, 4, new(0, 0), budget, CancellationToken.None);

        Assert.Equal(90, points[12].Y);
        Assert.Equal(104, points[5].Y);
        Assert.True(points[12].Y < points[5].Y);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(352, 88)]
    [InlineData(356, 87)]
    public void Stem_bounds_preserve_the_native_quarter_pixel_collapse_boundary(int spurY, int expectedStemY)
    {
        // The linked upper edge fits to 105. Pending bounds at 88/89 would leave
        // 17/16 units: the native collapse guard permits only the first bound.
        OpenTypeGlyphPoint[] points = [.. Box(0, 300, 0, 200), .. Box(500, 800, 416, 488),
            .. Box(250, 1000, 800, 1000), new(100, spurY, true), new(200, 700, true), new(300, spurY, true)];
        LatinAlignmentZone[] zones = [new(0, 0, 1000, 0, false, false), new(1000, 1000, 1000, 0, true, false)];
        RenderBudget budget = new(long.MaxValue);

        LatinLightHinting.Hint(points, [3, 7, 11, 14], zones, 2048, 8, new(0, 0), budget, CancellationToken.None);

        Assert.Equal(expectedStemY, points[4].Y);
        Assert.Equal(105, points[5].Y);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static OpenTypeGlyphPoint[] Box(int left, int right, int bottom, int top)
        => [new(left, bottom, true), new(left, top, true), new(right, top, true), new(right, bottom, true)];

    [Fact]
    public void Standard_stem_bounds_edge_merging_independently_of_the_largest_linked_stem()
    {
        // Two original clockwise boxes have separate 20- and 80-unit horizontal
        // stems. The pinned hinter uses the first width / 5 for edge proximity.
        OpenTypeGlyphPoint[] points = [new(0, 0, true), new(0, 20, true), new(300, 20, true), new(300, 0, true),
            new(500, 0, true), new(500, 80, true), new(800, 80, true), new(800, 0, true)];
        OpenTypeGlyphPoint[] unchanged = points.ToArray();
        RenderBudget budget = new(long.MaxValue);

        LatinStemWidths widths = LatinLightHinting.MeasureStemWidths(points, [3, 7], 2048, budget, CancellationToken.None);

        Assert.Equal(80, widths.MaximumWidth);
        Assert.Equal(4, widths.EdgeDistanceThreshold);
        Assert.Equal(unchanged, points);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(32)]
    public void Keeps_coincident_near_only_contours_bounded(int count)
    {
        OpenTypeGlyphPoint[] points = Enumerable.Repeat(new OpenTypeGlyphPoint(3, 7, true), count).ToArray();
        RenderBudget budget = new(long.MaxValue);

        LatinLightHinting.Hint(points, [(ushort)(count - 1)], [], 1000, 16, budget, CancellationToken.None);

        Assert.All(points, point => Assert.Equal(new OpenTypeGlyphPoint(3, 7, true), point));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Near_only_contours_keep_scaled_geometry_without_spurious_edges()
    {
        OpenTypeGlyphPoint[] points = [new(0, 300, true), new(0, 303, true), new(3, 303, true), new(3, 300, true)];
        OpenTypeGlyphPoint[] expected = [new(0, 307, true), new(0, 310, true), new(3, 310, true), new(3, 307, true)];
        LatinAlignmentZone[] zones = [new(0, 0, 700, 0, false, false), new(700, 700, 700, 0, true, false)];
        RenderBudget budget = new(long.MaxValue);

        LatinLightHinting.Hint(points, [3], zones, 1000, 16, budget, CancellationToken.None);

        Assert.Equal(expected, points);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Fits_opposite_windings_to_the_same_alignment_zones()
    {
        OpenTypeGlyphPoint[] forward = [new(0, 0, true), new(0, 700, true), new(300, 700, true), new(300, 0, true)];
        OpenTypeGlyphPoint[] backward = forward.Reverse().ToArray();
        LatinAlignmentZone[] zones = [new(0, 0, 700, 0, false, false), new(700, 700, 700, 0, true, false)];
        var budget = new RenderBudget(long.MaxValue);
        LatinLightHinting.Hint(forward, [3], zones, 1000, 16, budget, CancellationToken.None);
        LatinLightHinting.Hint(backward, [3], zones, 1000, 16, budget, CancellationToken.None);

        Assert.Equal(forward, backward.Reverse());
        Assert.Equal(new int[] { 0, 704, 704, 0 }, forward.Select(point => point.Y));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Scales_without_edges_when_the_face_has_no_Latin_alignment_samples()
    {
        OpenTypeGlyphPoint[] points = [new(-100, 0, true), new(0, 700, true), new(100, 0, true)];
        var budget = new RenderBudget(long.MaxValue);

        LatinLightHinting.Hint(points, [2], [], 1000, 16, budget, CancellationToken.None);

        Assert.Equal(new OpenTypeGlyphPoint[] { new(-102, 0, true), new(0, 717, true), new(102, 0, true) }, points);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Keeps_zero_scaled_subpixel_outlines_bounded()
    {
        OpenTypeGlyphPoint[] points = [new(0, 0, true), new(0, 700, true), new(300, 700, true), new(300, 0, true)];
        LatinAlignmentZone[] zones = [new(700, 700, 700, 0, true, true)];
        var budget = new RenderBudget(long.MaxValue);

        LatinLightHinting.Hint(points, [3], zones, 1000, 0.001, budget, CancellationToken.None);

        Assert.All(points, point => Assert.Equal(new OpenTypeGlyphPoint(0, 0, true), point));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(32768)]
    public void Rejects_out_of_range_device_sizes_before_allocation(double size)
    {
        var budget = new RenderBudget(long.MaxValue);
        SvgException exception = Assert.Throws<SvgException>(() => LatinLightHinting.Hint([], [], [], 1000, size, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(287)]
    [InlineData(288)]
    [InlineData(500)]
    public void Releases_point_and_topology_scratch_after_memory_denial(long maximumBytes)
    {
        OpenTypeGlyphPoint[] points = [new(0, 0, true), new(0, 700, true), new(300, 700, true), new(300, 0, true)];
        LatinAlignmentZone[] zones = [new(0, 0, 700, 0, false, false), new(700, 700, 700, 0, true, false)];
        var budget = new RenderBudget(maximumBytes);

        SvgException exception = Assert.Throws<SvgException>(() => LatinLightHinting.Hint(points, [3], zones, 1000, 16, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
    }

    [Fact]
    public void Observes_cancellation_before_allocating_outline_scratch()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var budget = new RenderBudget(long.MaxValue);

        Assert.ThrowsAny<OperationCanceledException>(() => LatinLightHinting.Hint([new(0, 0, true)], [0], [new(0, 0, 0, 0, false, false)], 1000, 16, budget, cancellation.Token));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
