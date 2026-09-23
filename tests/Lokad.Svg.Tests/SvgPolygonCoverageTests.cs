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
        Assert.Equal(0, SvgPolygonCoverage.GetExactCoverage([], 0, 0, CancellationToken.None), precision: 10);
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
}
