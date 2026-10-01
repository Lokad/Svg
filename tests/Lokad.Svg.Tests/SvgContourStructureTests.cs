using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgContourStructureTests
{
    [Fact]
    public void Overlapping_stub_reports_strict_overlap()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(4, 0), new SvgDevicePoint(12, 0), new SvgDevicePoint(12, 2), new SvgDevicePoint(4, 2)];

        Assert.True(SvgContourStructure.LoopsOverlapStrictly(points, 0, 4, 4, 4, CancellationToken.None));
        Assert.True(SvgContourStructure.LoopsOverlapStrictly(points, 4, 4, 0, 4, CancellationToken.None));
    }

    [Fact]
    public void Touching_twins_report_no_strict_overlap()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 8), new SvgDevicePoint(8, 8), new SvgDevicePoint(4, 14), new SvgDevicePoint(0, 8), new SvgDevicePoint(4, 2), new SvgDevicePoint(8, 8)];

        Assert.False(SvgContourStructure.LoopsOverlapStrictly(points, 0, 3, 3, 3, CancellationToken.None));
        Assert.False(SvgContourStructure.LoopsOverlapStrictly(points, 3, 3, 0, 3, CancellationToken.None));
    }

    [Fact]
    public void Nested_loops_report_strict_overlap()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(16, 0), new SvgDevicePoint(16, 16), new SvgDevicePoint(0, 16), new SvgDevicePoint(4, 4), new SvgDevicePoint(12, 4), new SvgDevicePoint(12, 12), new SvgDevicePoint(4, 12)];

        Assert.True(SvgContourStructure.LoopsOverlapStrictly(points, 0, 4, 4, 4, CancellationToken.None));
    }

    [Fact]
    public void Poking_vertex_reports_strict_overlap()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(4, 4), new SvgDevicePoint(12, 4), new SvgDevicePoint(4, 10)];

        Assert.True(SvgContourStructure.LoopsOverlapStrictly(points, 0, 4, 4, 3, CancellationToken.None));
    }

    [Fact]
    public void Simple_square_reports_none()
    {
        SvgDevicePoint[] square = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)];

        Assert.Equal(SvgContourFlags.None, SvgContourStructure.ClassifyContour(square, 0, square.Length, CancellationToken.None));
    }

    [Fact]
    public void Trailing_duplicate_reports_zero_length_and_touch()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(0, 8)];

        Assert.Equal(SvgContourFlags.ZeroLengthEdge | SvgContourFlags.SelfTouch, SvgContourStructure.ClassifyContour(points, 0, points.Length, CancellationToken.None));
    }

    [Fact]
    public void Bowtie_reports_proper_crossing_only()
    {
        SvgDevicePoint[] bowtie = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];

        Assert.Equal(SvgContourFlags.ProperCrossing, SvgContourStructure.ClassifyContour(bowtie, 0, bowtie.Length, CancellationToken.None));
    }

    [Fact]
    public void Figure_eight_reports_self_touch_only()
    {
        SvgDevicePoint[] eight = [new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(0, 8), new SvgDevicePoint(8, 8), new SvgDevicePoint(4, 4), new SvgDevicePoint(8, 0)];

        Assert.Equal(SvgContourFlags.SelfTouch, SvgContourStructure.ClassifyContour(eight, 0, eight.Length, CancellationToken.None));
    }

    [Fact]
    public void Retraced_square_reports_coincidence_and_touch()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(4, 0)];

        Assert.Equal(SvgContourFlags.CoincidentOverlap | SvgContourFlags.SelfTouch, SvgContourStructure.ClassifyContour(points, 0, points.Length, CancellationToken.None));
    }

    [Fact]
    public void Classify_supports_subslices_and_rejects_ranges()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8), new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)];

        Assert.Equal(SvgContourFlags.ProperCrossing, SvgContourStructure.ClassifyContour(points, 0, 4, CancellationToken.None));
        Assert.Equal(SvgContourFlags.None, SvgContourStructure.ClassifyContour(points, 4, 8, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourStructure.ClassifyContour(points, -1, 4, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourStructure.ClassifyContour(points, 0, 9, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourStructure.ClassifyContour(points, 5, 4, CancellationToken.None));
    }

    [Fact]
    public void Disjoint_set_reports_none()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(0, 4), new SvgDevicePoint(8, 8), new SvgDevicePoint(12, 8), new SvgDevicePoint(12, 12), new SvgDevicePoint(8, 12)];

        Assert.Equal(SvgContourFlags.None, SvgContourStructure.ClassifySet(points, [4, 8], CancellationToken.None));
    }

    [Fact]
    public void Nested_separated_set_reports_none()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];

        Assert.Equal(SvgContourFlags.None, SvgContourStructure.ClassifySet(points, [4, 8], CancellationToken.None));
    }

    [Fact]
    public void Shared_vertex_reports_other_touch_only()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(0, 0), new SvgDevicePoint(-8, 0), new SvgDevicePoint(-8, 8)];

        Assert.Equal(SvgContourFlags.TouchesOtherContour, SvgContourStructure.ClassifySet(points, [4, 7], CancellationToken.None));
    }

    [Fact]
    public void Crossing_contours_report_other_touch_only()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(4, -2), new SvgDevicePoint(4, 10)];

        Assert.Equal(SvgContourFlags.TouchesOtherContour, SvgContourStructure.ClassifySet(points, [4, 6], CancellationToken.None));
    }

    [Fact]
    public void Set_rejects_invalid_ends()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourStructure.ClassifySet(points, [2], CancellationToken.None));
    }

    [Fact]
    public void Structure_precancelled_token_throws()
    {
        SvgDevicePoint[] square = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)];
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgContourStructure.ClassifyContour(square, 0, square.Length, cancelled.Token));
    }

    [Fact]
    public void Bowtie_reports_first_crossing_with_interior_point()
    {
        SvgDevicePoint[] bowtie = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];

        Assert.True(SvgContourStructure.TryFindProperCrossing(bowtie, 0, bowtie.Length, CancellationToken.None, out int firstEdge, out int secondEdge, out SvgDevicePoint crossing));
        Assert.Equal(0, firstEdge);
        Assert.Equal(2, secondEdge);
        Assert.Equal(new SvgDevicePoint(4, 4), crossing);
    }

    [Fact]
    public void Simple_square_reports_no_crossing()
    {
        SvgDevicePoint[] square = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)];

        Assert.False(SvgContourStructure.TryFindProperCrossing(square, 0, square.Length, CancellationToken.None, out _, out _, out _));
    }

    [Fact]
    public void Single_crossing_reports_no_other_crossing()
    {
        SvgDevicePoint[] bowtie = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];

        Assert.True(SvgContourStructure.TryFindProperCrossing(bowtie, 0, bowtie.Length, CancellationToken.None, out int firstEdge, out int secondEdge, out _));
        Assert.False(SvgContourStructure.HasAnotherProperCrossing(bowtie, 0, bowtie.Length, firstEdge, secondEdge, CancellationToken.None));
    }

    [Fact]
    public void Double_crossing_reports_another_crossing()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(10, 10), new SvgDevicePoint(10, 0), new SvgDevicePoint(0, 10), new SvgDevicePoint(6, 10), new SvgDevicePoint(6, 4)];

        Assert.True(SvgContourStructure.TryFindProperCrossing(points, 0, points.Length, CancellationToken.None, out int firstEdge, out int secondEdge, out _));
        Assert.True(SvgContourStructure.HasAnotherProperCrossing(points, 0, points.Length, firstEdge, secondEdge, CancellationToken.None));
    }
    [Fact]
    public void Split_bowtie_loops_admit_union()
    {
        SvgDevicePoint[] loops = [new SvgDevicePoint(8, 8), new SvgDevicePoint(8, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(0, 8), new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 4)];

        Assert.True(SvgContourStructure.AreSplitLoopsAdmissible(loops, [3, 6], CancellationToken.None));
    }

    [Fact]
    public void Crossing_squares_report_inter_loop_crossing()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(4, 2), new SvgDevicePoint(12, 2), new SvgDevicePoint(12, 8), new SvgDevicePoint(4, 8)];

        Assert.True(SvgContourStructure.TryFindInterLoopProperCrossing(points, [4, 8], out int firstLoop, out int firstEdge, out int secondLoop, out int secondEdge, out SvgDevicePoint crossing, CancellationToken.None));
        Assert.Equal(0, firstLoop);
        Assert.Equal(1, firstEdge);
        Assert.Equal(1, secondLoop);
        Assert.Equal(0, secondEdge);
        Assert.Equal(new SvgDevicePoint(8, 2), crossing);
    }

    [Fact]
    public void Disjoint_squares_report_no_inter_loop_crossing()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(2, 0), new SvgDevicePoint(2, 2), new SvgDevicePoint(0, 2), new SvgDevicePoint(4, 4), new SvgDevicePoint(6, 4), new SvgDevicePoint(6, 6), new SvgDevicePoint(4, 6)];

        Assert.False(SvgContourStructure.TryFindInterLoopProperCrossing(points, [4, 8], out _, out _, out _, out _, out _, CancellationToken.None));
    }

    [Fact]
    public void Corner_touching_squares_report_no_inter_loop_crossing()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(0, 4), new SvgDevicePoint(4, 4), new SvgDevicePoint(8, 4), new SvgDevicePoint(8, 8), new SvgDevicePoint(4, 8)];

        Assert.False(SvgContourStructure.TryFindInterLoopProperCrossing(points, [4, 8], out _, out _, out _, out _, out _, CancellationToken.None));
    }

    [Fact]
    public void Inter_loop_crossing_rejects_bad_ranges()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8)];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourStructure.TryFindInterLoopProperCrossing(points, [2], out _, out _, out _, out _, out _, CancellationToken.None));
    }

    [Fact]
    public void Touching_loops_reject_union()
    {
        SvgDevicePoint[] loops = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(8, 4), new SvgDevicePoint(12, 0), new SvgDevicePoint(12, 8)];

        Assert.False(SvgContourStructure.AreSplitLoopsAdmissible(loops, [4, 7], CancellationToken.None));
    }

    [Fact]
    public void Overlapping_loops_reject_union()
    {
        SvgDevicePoint[] loops = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(4, 4), new SvgDevicePoint(12, 4), new SvgDevicePoint(12, 12), new SvgDevicePoint(4, 12)];

        Assert.False(SvgContourStructure.AreSplitLoopsAdmissible(loops, [4, 8], CancellationToken.None));
    }

    [Fact]
    public void Nested_loops_admit_union()
    {
        SvgDevicePoint[] loops = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];

        Assert.True(SvgContourStructure.AreSplitLoopsAdmissible(loops, [4, 8], CancellationToken.None));
    }

}