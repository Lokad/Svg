using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgContourNestingTests
{
    [Fact]
    public void Nested_hole_alternates()
    {
        Assert.Equal([1, -1], ParitySigns(OuterWithSameWoundHole()));
    }

    [Fact]
    public void Sibling_contours_stay_positive()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(0, 4), new SvgDevicePoint(8, 8), new SvgDevicePoint(12, 8), new SvgDevicePoint(12, 12), new SvgDevicePoint(8, 12)];

        Assert.Equal([1, 1], ParitySigns(points, [4, 8]));
    }

    [Fact]
    public void Triple_nesting_alternates()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6), new SvgDevicePoint(3, 3), new SvgDevicePoint(5, 3), new SvgDevicePoint(5, 5), new SvgDevicePoint(3, 5)];

        Assert.Equal([1, -1, 1], ParitySigns(points, [4, 8, 12]));
    }

    [Fact]
    public void Signs_follow_geometry_not_order()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6), new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)];

        Assert.Equal([-1, 1], ParitySigns(points, [4, 8]));
    }

    [Fact]
    public void Degenerate_contour_never_contains()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(4, 4), new SvgDevicePoint(5, 5), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];

        Assert.Equal([1, 1, -1], ParitySigns(points, [4, 6, 10]));
    }

    [Fact]
    public void Nesting_rejects_mismatched_shapes()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        int[] signs = new int[1];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourNesting.ComputeParitySigns(points, [2], signs, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourNesting.ComputeParitySigns(points, [3], new int[2], CancellationToken.None));
    }

    [Fact]
    public void Empty_set_is_noop()
    {
        SvgContourNesting.ComputeParitySigns([], [], [], CancellationToken.None);
    }

    [Fact]
    public void Nesting_precancelled_token_throws()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgContourNesting.ComputeParitySigns(points, [3], new int[1], cancelled.Token));
    }

    private static (SvgDevicePoint[] Points, int[] Ends) OuterWithSameWoundHole()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];
        return (points, [4, 8]);
    }

    private static int[] ParitySigns((SvgDevicePoint[] Points, int[] Ends) contours)
    {
        return ParitySigns(contours.Points, contours.Ends);
    }

    private static int[] ParitySigns(SvgDevicePoint[] points, int[] ends)
    {
        int[] signs = new int[ends.Length];
        SvgContourNesting.ComputeParitySigns(points, ends, signs, CancellationToken.None);
        return signs;
    }
}