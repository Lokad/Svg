using Lokad.Svg.Parsing;
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

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourNesting.ComputeParitySigns(points, [2], signs, false, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourNesting.ComputeParitySigns(points, [3], new int[2], false, CancellationToken.None));
    }

    [Fact]
    public void Empty_set_is_noop()
    {
        SvgContourNesting.ComputeParitySigns([], [], [], false, CancellationToken.None);
    }

    [Fact]
    public void Nesting_precancelled_token_throws()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgContourNesting.ComputeParitySigns(points, [3], new int[1], false, cancelled.Token));
    }

    private static (SvgDevicePoint[] Points, int[] Ends) OuterWithSameWoundHole()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];
        return (points, [4, 8]);
    }

    [Fact]
    public void Identical_twins_merge_to_first()
    {
        SvgDevicePoint[] square = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)];
        SvgDevicePoint[] points = [.. square, .. square];
        (bool merged, SvgDevicePoint[] kept, int[] ends) = Merge(points, [4, 8]);

        Assert.True(merged);
        Assert.Equal([4], ends);
        Assert.Equal(square, kept);
    }

    [Fact]
    public void Distinct_siblings_pass_through_untouched()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(0, 4)];
        (bool merged, SvgDevicePoint[] kept, int[] ends) = Merge(points, [4, 8]);

        Assert.False(merged);
        Assert.Same(points, kept);
        Assert.Equal([4, 8], ends);
    }

    [Fact]
    public void Short_duplicates_stay()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 8)];
        (bool merged, _, _) = Merge(points, [2, 4]);

        Assert.False(merged);
    }

    [Fact]
    public void Rotated_identical_stays_for_spatial_identity()
    {
        SvgDevicePoint a = new(0, 0);
        SvgDevicePoint b = new(8, 0);
        SvgDevicePoint c = new(8, 8);
        SvgDevicePoint[] points = [a, b, c, b, c, a];
        (bool merged, _, _) = Merge(points, [3, 6]);

        Assert.False(merged);
    }

    [Fact]
    public void Merge_rejects_mismatched_shapes()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];

        Assert.Throws<ArgumentOutOfRangeException>(() => Merge(points, [2]));
    }

    private static (bool Merged, SvgDevicePoint[] Points, int[] Ends) Merge(SvgDevicePoint[] points, int[] ends)
    {
        bool merged = SvgContourNesting.MergeIdenticalContours(points, ends, new ParserWorkCounter(1000), out SvgDevicePoint[] kept, out int[] keptEnds, CancellationToken.None);
        return (merged, kept, keptEnds);
    }

    private static int[] ParitySigns((SvgDevicePoint[] Points, int[] Ends) contours)
    {
        return ParitySigns(contours.Points, contours.Ends);
    }

    [Fact]
    public void Disjoint_opposite_contours_share_positive_roles()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0.1, 0.1), new SvgDevicePoint(0.4, 0.1), new SvgDevicePoint(0.1, 0.4), new SvgDevicePoint(0.6, 0.6), new SvgDevicePoint(0.6, 0.9), new SvgDevicePoint(0.9, 0.6)];

        Assert.Equal([1, 1], WindingRoles(points, [3, 6]));
    }

    [Fact]
    public void Contour_reversal_keeps_roles()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0.1, 0.1), new SvgDevicePoint(0.4, 0.1), new SvgDevicePoint(0.1, 0.4), new SvgDevicePoint(0.6, 0.6), new SvgDevicePoint(0.9, 0.6), new SvgDevicePoint(0.6, 0.9)];

        Assert.Equal([1, 1], WindingRoles(points, [3, 6]));
    }

    [Fact]
    public void Nested_opposite_hole_subtracts()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(2, 6), new SvgDevicePoint(6, 6), new SvgDevicePoint(6, 2)];

        Assert.Equal([1, -1], WindingRoles(points, [4, 8]));
    }

    [Fact]
    public void Same_wound_nesting_adds_nothing()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];

        Assert.Equal([1, 0], WindingRoles(points, [4, 8]));
    }

    [Fact]
    public void Island_hole_triple_resolves_through_winding()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(1, 1), new SvgDevicePoint(7, 1), new SvgDevicePoint(7, 7), new SvgDevicePoint(1, 7), new SvgDevicePoint(2, 2), new SvgDevicePoint(2, 6), new SvgDevicePoint(6, 6), new SvgDevicePoint(6, 2)];

        Assert.Equal([1, 0, 0], WindingRoles(points, [4, 8, 12]));
    }

    [Fact]
    public void Hole_island_triple_repaints()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(2, 6), new SvgDevicePoint(6, 6), new SvgDevicePoint(6, 2), new SvgDevicePoint(3, 3), new SvgDevicePoint(7, 3), new SvgDevicePoint(7, 7), new SvgDevicePoint(3, 7)];

        Assert.Equal([1, -1, 1], WindingRoles(points, [4, 8, 12]));
    }

    [Fact]
    public void Single_clockwise_contour_paints()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(0, 8), new SvgDevicePoint(8, 0)];

        Assert.Equal([1], WindingRoles(points, [3]));
    }

    [Fact]
    public void Degenerate_contour_scores_zero_role()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(4, 4), new SvgDevicePoint(5, 5)];

        Assert.Equal([1, 0], WindingRoles(points, [4, 6]));
    }

    [Fact]
    public void Winding_roles_reject_mismatched_shapes()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        int[] roles = new int[1];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourNesting.ComputeWindingRoles(points, [2], roles, false, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgContourNesting.ComputeWindingRoles(points, [3], new int[2], false, CancellationToken.None));
    }

    [Fact]
    public void Winding_precancelled_token_throws()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgContourNesting.ComputeWindingRoles(points, [3], new int[1], false, cancelled.Token));
    }

    private static int[] ParitySigns(SvgDevicePoint[] points, int[] ends)
    {
        int[] signs = new int[ends.Length];
        SvgContourNesting.ComputeParitySigns(points, ends, signs, false, CancellationToken.None);
        return signs;
    }

    private static int[] WindingRoles(SvgDevicePoint[] points, int[] ends)
    {
        int[] roles = new int[ends.Length];
        SvgContourNesting.ComputeWindingRoles(points, ends, roles, false, CancellationToken.None);
        return roles;
    }

    [Fact]
    public void Touching_nested_pair_alternates_through_unshared_vertices()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(0, 0), new SvgDevicePoint(1, 4), new SvgDevicePoint(4, 1)];

        Assert.Equal([1, -1], ParitySignsSkipping(points, [4, 7]));
    }

    [Fact]
    public void Corner_touching_siblings_share_positive_roles()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(0, 4), new SvgDevicePoint(4, 4), new SvgDevicePoint(8, 4), new SvgDevicePoint(8, 8), new SvgDevicePoint(4, 8)];

        Assert.Equal([1, 1], WindingRolesSkipping(points, [4, 8]));
    }

    [Fact]
    public void Nested_touch_hole_subtracts()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(0, 0), new SvgDevicePoint(1, 4), new SvgDevicePoint(4, 1)];

        Assert.Equal([1, -1], WindingRolesSkipping(points, [4, 7]));
    }

    [Fact]
    public void Fully_shared_loops_report_uncontained()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8)];

        Assert.Equal([1, 1], ParitySignsSkipping(points, [4, 7]));
        Assert.Equal([1, 1], WindingRolesSkipping(points, [4, 7]));
    }

    [Fact]
    public void Boundary_first_vertex_skips_to_off_boundary_sample()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(4, 0), new SvgDevicePoint(12, 0), new SvgDevicePoint(12, 4)];

        Assert.Equal([1, 1], ParitySignsSkipping(points, [4, 7]));
        Assert.Equal([1, 1], WindingRolesSkipping(points, [4, 7]));
    }

    [Fact]
    public void Shared_first_vertex_keeps_positive_roles()
    {
        // Split-lobe shape: the second loop starts at the shared vertex, so the
        // first-vertex test would sample the boundary without skipping.
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(8, 8), new SvgDevicePoint(12, 4), new SvgDevicePoint(12, 8)];

        Assert.Equal([1, 1], WindingRolesSkipping(points, [3, 6]));
    }

    private static int[] ParitySignsSkipping(SvgDevicePoint[] points, int[] ends)
    {
        int[] signs = new int[ends.Length];
        SvgContourNesting.ComputeParitySigns(points, ends, signs, true, CancellationToken.None);
        return signs;
    }

    private static int[] WindingRolesSkipping(SvgDevicePoint[] points, int[] ends)
    {
        int[] roles = new int[ends.Length];
        SvgContourNesting.ComputeWindingRoles(points, ends, roles, true, CancellationToken.None);
        return roles;
    }
}
