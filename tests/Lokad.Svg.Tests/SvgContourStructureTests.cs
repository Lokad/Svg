using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgContourStructureTests
{
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
}