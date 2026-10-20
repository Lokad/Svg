using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPolygonCoverageTests
{
    [Fact]
    public void Fully_covered_pixel_reports_unit_coverage()
    {
        SvgPoint[] triangle = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(0, 8)];

        Assert.Equal(1, SvgPolygonCoverage.GetExactCoverage(triangle, 0, 0, CancellationToken.None), precision: 10);
    }

    [Fact]
    public void Diagonal_edge_splits_boundary_pixel_in_half()
    {
        SvgPoint[] triangle = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(0, 8)];

        Assert.Equal(0.5, SvgPolygonCoverage.GetExactCoverage(triangle, 7, 0, CancellationToken.None), precision: 10);
    }

    [Fact]
    public void Pixel_outside_the_polygon_reports_zero()
    {
        SvgPoint[] triangle = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(0, 8)];

        Assert.Equal(0, SvgPolygonCoverage.GetExactCoverage(triangle, 7, 7, CancellationToken.None), precision: 10);
    }

    [Fact]
    public void Reversed_orientation_reports_identical_coverage()
    {
        SvgPoint[] clockwise = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(0, 8)];
        SvgPoint[] counterClockwise = [new SvgPoint(0, 0), new SvgPoint(0, 8), new SvgPoint(8, 0)];

        for (int y = -1; y <= 8; y++)
        for (int x = -1; x <= 8; x++)
        {
            Assert.Equal(
                SvgPolygonCoverage.GetExactCoverage(clockwise, x, y, CancellationToken.None),
                SvgPolygonCoverage.GetExactCoverage(counterClockwise, x, y, CancellationToken.None),
                precision: 10);
        }
    }

    [Fact]
    public void Pixel_coverages_sum_to_the_shoelace_area()
    {
        SvgPoint[] triangle = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(0, 8)];

        Assert.Equal(32, SumCoverages(triangle, -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Research_triangle_coverages_sum_to_its_area()
    {
        SvgPoint[] triangle = [new SvgPoint(1, 1), new SvgPoint(7, 2), new SvgPoint(2, 7)];

        Assert.Equal(17.5, SumCoverages(triangle, 0, 8, 0, 8), precision: 10);
    }

    [Fact]
    public void Concave_quad_coverages_sum_to_its_area()
    {
        SvgPoint[] dart = [new SvgPoint(0, 0), new SvgPoint(4, 0), new SvgPoint(2, 1), new SvgPoint(4, 4), new SvgPoint(0, 4)];

        Assert.Equal(12, SumCoverages(dart, -1, 5, -1, 5), precision: 10);
    }

    [Fact]
    public void Short_contours_enclose_no_area()
    {
        SvgPoint[] empty = []; Assert.Equal(0, SvgPolygonCoverage.GetExactCoverage(empty, 0, 0, CancellationToken.None), precision: 10);
        SvgPoint[] segment = [new SvgPoint(0, 0), new SvgPoint(8, 8)];
        Assert.Equal(0, SvgPolygonCoverage.GetExactCoverage(segment, 0, 0, CancellationToken.None), precision: 10);
    }

    [Fact]
    public void Collinear_triangle_covers_nothing()
    {
        SvgPoint[] flat = [new SvgPoint(0, 0), new SvgPoint(4, 4), new SvgPoint(8, 8)];

        Assert.Equal(0, SumCoverages(flat, -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Precancelled_token_throws()
    {
        SvgPoint[] triangle = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(0, 8)];
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgPolygonCoverage.GetExactCoverage(triangle, 0, 0, cancelled.Token));
    }

    private static double SumCoverages(SvgPoint[] contour, int minimumX, int maximumX, int minimumY, int maximumY)
    {
        double total = 0;
        for (int y = minimumY; y <= maximumY; y++)
        for (int x = minimumX; x <= maximumX; x++)
        {
            total += SvgPolygonCoverage.GetExactCoverage(contour, x, y, CancellationToken.None);
        }
        return total;
    }

    [Fact]
    public void Fractional_phase_triangle_preserves_area()
    {
        SvgDevicePoint[] triangle = [new SvgDevicePoint(0.5, 0.5), new SvgDevicePoint(8.5, 0.5), new SvgDevicePoint(0.5, 8.5)];

        Assert.Equal(32, SumCoverages(triangle, 0, 9, 0, 9), precision: 9);
    }

    [Fact]
    public void Scaled_research_triangle_preserves_scaled_area()
    {
        SvgAffineTransform scaleTwo = SvgAffineTransform.FromScale(2, 2);
        SvgPoint[] source = [new SvgPoint(1, 1), new SvgPoint(7, 2), new SvgPoint(2, 7)];
        SvgDevicePoint[] triangle = [scaleTwo.Apply(source[0]), scaleTwo.Apply(source[1]), scaleTwo.Apply(source[2])];

        Assert.Equal(new SvgDevicePoint(2, 2), triangle[0]);
        Assert.Equal(70, SumCoverages(triangle, 0, 16, 0, 16), precision: 9);
    }

    [Fact]
    public void Reflected_triangle_preserves_area()
    {
        SvgAffineTransform mirror = SvgAffineTransform.FromScaleOffset(-1, 1, 8, 0);
        SvgPoint[] source = [new SvgPoint(0, 0), new SvgPoint(8, 0), new SvgPoint(0, 8)];
        SvgDevicePoint[] triangle = [mirror.Apply(source[0]), mirror.Apply(source[1]), mirror.Apply(source[2])];

        Assert.Equal(32, SumCoverages(triangle, -1, 9, -1, 9), precision: 9);
    }

    [Fact]
    public void Winding_preserves_orientation_sign()
    {
        SvgDevicePoint[] counterClockwise = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        SvgDevicePoint[] clockwise = [new SvgDevicePoint(0, 0), new SvgDevicePoint(0, 8), new SvgDevicePoint(8, 0)];

        Assert.Equal(32, SumWinding(counterClockwise, [3], -1, 9, -1, 9), precision: 10);
        Assert.Equal(-32, SumWinding(clockwise, [3], -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Opposite_wound_hole_subtracts()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(2, 6), new SvgDevicePoint(6, 6), new SvgDevicePoint(6, 2)];

        Assert.Equal(48, SumWinding(points, [4, 8], -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Same_wound_overlap_adds()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];

        Assert.Equal(80, SumWinding(points, [4, 8], -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Disjoint_contours_add()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(0, 4), new SvgDevicePoint(8, 8), new SvgDevicePoint(12, 8), new SvgDevicePoint(12, 12), new SvgDevicePoint(8, 12)];

        Assert.Equal(32, SumWinding(points, [4, 8], -1, 13, -1, 13), precision: 10);
    }

    [Fact]
    public void Disjoint_opposite_contours_add_unsigned_role_areas()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0.1, 0.1), new SvgDevicePoint(0.4, 0.1), new SvgDevicePoint(0.1, 0.4), new SvgDevicePoint(0.6, 0.6), new SvgDevicePoint(0.6, 0.9), new SvgDevicePoint(0.9, 0.6)];

        Assert.Equal(0.09, SvgPolygonCoverage.GetWindingCoverage(points, [3, 6], [1, 1], 0, 0, CancellationToken.None), precision: 10);
    }

    [Fact]
    public void Reversed_contours_keep_role_coverage()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0.1, 0.1), new SvgDevicePoint(0.4, 0.1), new SvgDevicePoint(0.1, 0.4), new SvgDevicePoint(0.6, 0.6), new SvgDevicePoint(0.9, 0.6), new SvgDevicePoint(0.6, 0.9)];

        Assert.Equal(0.09, SvgPolygonCoverage.GetWindingCoverage(points, [3, 6], [1, 1], 0, 0, CancellationToken.None), precision: 10);
    }

    [Fact]
    public void Hole_roles_subtract()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(2, 6), new SvgDevicePoint(6, 6), new SvgDevicePoint(6, 2)];

        Assert.Equal(0, SvgPolygonCoverage.GetWindingCoverage(points, [4, 8], [1, -1], 4, 4, CancellationToken.None), precision: 10);
        Assert.Equal(1, SvgPolygonCoverage.GetWindingCoverage(points, [4, 8], [1, -1], 1, 1, CancellationToken.None), precision: 10);
    }

    [Fact]
    public void Roles_reject_mismatched_shapes()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgPolygonCoverage.GetWindingCoverage(points, [3], [1, 1], 0, 0, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgPolygonCoverage.GetWindingCoverage(points, [3], [2], 0, 0, CancellationToken.None));
    }

    [Fact]
    public void Short_and_empty_contours_contribute_zero()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];

        Assert.Equal(0, SumWinding([], [], 0, 1, 0, 1), precision: 10);
        Assert.Equal(32, SumWinding(points, [0, 2, 5], -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Hole_pixel_cancels_exactly()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(2, 6), new SvgDevicePoint(6, 6), new SvgDevicePoint(6, 2)];
        int[] ends = [4, 8];

        Assert.Equal(0, SvgPolygonCoverage.GetWindingCoverage(points, ends, 4, 4, CancellationToken.None), precision: 10);
        Assert.Equal(1, SvgPolygonCoverage.GetWindingCoverage(points, ends, 1, 1, CancellationToken.None), precision: 10);
    }

    [Fact]
    public void Invalid_ends_are_rejected()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgPolygonCoverage.GetWindingCoverage(points, [3, 2], 0, 0, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgPolygonCoverage.GetWindingCoverage(points, [4], 0, 0, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgPolygonCoverage.GetWindingCoverage(points, [-1, 3], 0, 0, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgPolygonCoverage.GetWindingCoverage(points, [2], 0, 0, CancellationToken.None));
    }

    [Fact]
    public void Winding_precancelled_token_throws()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgPolygonCoverage.GetWindingCoverage(points, [3], 0, 0, cancelled.Token));
    }

    [Fact]
    public void Same_wound_hole_subtracts_under_evenodd()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];

        Assert.Equal(48, SumEvenOdd(points, [4, 8], [1, -1], -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Opposite_wound_hole_matches_winding()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(2, 6), new SvgDevicePoint(6, 6), new SvgDevicePoint(6, 2)];

        Assert.Equal(48, SumEvenOdd(points, [4, 8], [1, -1], -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Resolved_parity_drives_hole()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];
        int[] ends = [4, 8];
        int[] signs = new int[2];
        SvgContourNesting.ComputeParitySigns(points, ends, signs, false, CancellationToken.None);

        Assert.Equal([1, -1], signs);
        Assert.Equal(48, SumEvenOdd(points, ends, signs, -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Zero_sign_masks_contour()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];

        Assert.Equal(64, SumEvenOdd(points, [4, 8], [1, 0], -1, 9, -1, 9), precision: 10);
    }

    [Fact]
    public void Invalid_sign_is_rejected()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgPolygonCoverage.GetEvenOddCoverage(points, [3], [2], 0, 0, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgPolygonCoverage.GetEvenOddCoverage(points, [3], [1, -1], 0, 0, CancellationToken.None));
    }

    [Fact]
    public void Evenodd_hole_pixel_contrasts_winding()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(2, 2), new SvgDevicePoint(6, 2), new SvgDevicePoint(6, 6), new SvgDevicePoint(2, 6)];
        int[] ends = [4, 8];
        int[] signs = [1, -1];

        Assert.Equal(0, SvgPolygonCoverage.GetEvenOddCoverage(points, ends, signs, 4, 4, CancellationToken.None), precision: 10);
        Assert.Equal(2, SvgPolygonCoverage.GetWindingCoverage(points, ends, 4, 4, CancellationToken.None), precision: 10);
    }

    [Fact]
    public void Evenodd_precancelled_token_throws()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgPolygonCoverage.GetEvenOddCoverage(points, [3], [1], 0, 0, cancelled.Token));
    }

    private static double SumEvenOdd(SvgDevicePoint[] points, int[] ends, int[] signs, int minimumX, int maximumX, int minimumY, int maximumY)
    {
        double total = 0;
        for (int y = minimumY; y <= maximumY; y++)
        for (int x = minimumX; x <= maximumX; x++)
        {
            total += SvgPolygonCoverage.GetEvenOddCoverage(points, ends, signs, x, y, CancellationToken.None);
        }

        return total;
    }
    private static double SumWinding(SvgDevicePoint[] points, int[] ends, int minimumX, int maximumX, int minimumY, int maximumY)
    {
        double total = 0;
        for (int y = minimumY; y <= maximumY; y++)
        for (int x = minimumX; x <= maximumX; x++)
        {
            total += SvgPolygonCoverage.GetWindingCoverage(points, ends, x, y, CancellationToken.None);
        }

        return total;
    }
    private static double SumCoverages(SvgDevicePoint[] contour, int minimumX, int maximumX, int minimumY, int maximumY)
    {
        double total = 0;
        for (int y = minimumY; y <= maximumY; y++)
        for (int x = minimumX; x <= maximumX; x++)
        {
            total += SvgPolygonCoverage.GetExactCoverage(contour, x, y, CancellationToken.None);
        }
        return total;
    }
}
