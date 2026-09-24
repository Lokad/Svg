using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgContourBuilderTests
{
    [Fact]
    public void Diagonal_triangle_covers_exact_area()
    {
        SvgContourBuilder builder = new(16, 4);
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddLine(new SvgDevicePoint(8, 0));
        builder.AddLine(new SvgDevicePoint(0, 8));
        builder.EndContour();

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal(3, builder.PointCount);
        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
        Assert.Equal(32, SumCoverages(builder.GetContour(0), 0, 9, 0, 9), precision: 9);
    }

    [Fact]
    public void Premapped_device_points_pass_through()
    {
        SvgAffineTransform scale = SvgAffineTransform.FromScale(2, 2);
        SvgDevicePoint mapped = scale.Apply(3, 4);
        SvgContourBuilder builder = new(16, 4);
        builder.BeginContour(scale.Apply(1, 1));
        builder.AddLine(mapped);
        builder.EndContour();

        Assert.Equal(new SvgDevicePoint(2, 2), builder.GetContour(0)[0]);
        Assert.Equal(new SvgDevicePoint(6, 8), builder.GetContour(0)[1]);
    }

    [Fact]
    public void Quadratic_chains_flattened_points()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddQuadratic(new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), new SvgFlattenPolicy(2.0, 64, 256), CancellationToken.None);
        builder.EndContour();

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(8, 0)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Cubic_chains_flattened_points()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddCubic(new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgFlattenPolicy(3.0, 64, 256), CancellationToken.None);
        builder.EndContour();

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(6, 4), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Arc_chains_sampled_points_with_exact_endpoints()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.BeginContour(new SvgDevicePoint(8, 0));
        builder.AddArc(8, 8, 0, false, true, new SvgDevicePoint(0, 8), new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None);
        builder.EndContour();

        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);
        Assert.True(contour.Length >= 3);
        Assert.Equal(new SvgDevicePoint(8, 0), contour[0]);
        Assert.Equal(new SvgDevicePoint(0, 8), contour[^1]);
    }

    [Fact]
    public void Arc_finer_flatness_never_shrinks_contour()
    {
        int coarse = ArcContourLength(2.0);
        int fine = ArcContourLength(0.125);

        Assert.True(coarse >= 3);
        Assert.True(fine >= coarse);
    }

    [Fact]
    public void Degenerate_arc_appends_line()
    {
        SvgContourBuilder builder = new(16, 4);
        builder.BeginContour(new SvgDevicePoint(1, 2));
        builder.AddArc(0, 4, 0, false, true, new SvgDevicePoint(7, 8), new SvgFlattenPolicy(0.5, 64, 16), CancellationToken.None);
        builder.EndContour();

        Assert.Equal([new SvgDevicePoint(1, 2), new SvgDevicePoint(7, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Two_figures_store_separate_contours()
    {
        SvgContourBuilder builder = new(32, 4);
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddLine(new SvgDevicePoint(8, 0));
        builder.AddLine(new SvgDevicePoint(8, 8));
        builder.AddLine(new SvgDevicePoint(0, 8));
        builder.EndContour();
        builder.BeginContour(new SvgDevicePoint(2, 2));
        builder.AddLine(new SvgDevicePoint(2, 6));
        builder.AddLine(new SvgDevicePoint(6, 6));
        builder.AddLine(new SvgDevicePoint(6, 2));
        builder.EndContour();

        Assert.Equal(2, builder.ContourCount);
        Assert.Equal(8, builder.PointCount);
        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
        Assert.Equal(new SvgDevicePoint(2, 2), builder.GetContour(1)[0]);
        Assert.Equal(new SvgDevicePoint(6, 2), builder.GetContour(1)[^1]);
    }

    [Fact]
    public void Point_capacity_throws_LimitExceeded()
    {
        SvgContourBuilder builder = new(2, 4);
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddLine(new SvgDevicePoint(8, 0));

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => builder.AddLine(new SvgDevicePoint(0, 8))).ErrorCode);
    }

    [Fact]
    public void Contour_capacity_throws_LimitExceeded()
    {
        SvgContourBuilder builder = new(16, 1);
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.EndContour();

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => builder.BeginContour(new SvgDevicePoint(2, 2))).ErrorCode);
    }

    [Fact]
    public void Misuse_throws_InvalidOperation()
    {
        SvgContourBuilder builder = new(16, 4);

        Assert.Throws<InvalidOperationException>(() => builder.EndContour());
        Assert.Throws<InvalidOperationException>(() => builder.AddLine(new SvgDevicePoint(1, 1)));
        builder.BeginContour(new SvgDevicePoint(0, 0));
        Assert.Throws<InvalidOperationException>(() => builder.BeginContour(new SvgDevicePoint(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.GetContour(0));
        builder.EndContour();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.GetContour(1));
    }

    [Fact]
    public void Clear_reuses_storage()
    {
        SvgContourBuilder builder = new(16, 4);
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddLine(new SvgDevicePoint(8, 0));
        builder.EndContour();
        builder.Clear();

        Assert.Equal(0, builder.PointCount);
        Assert.Equal(0, builder.ContourCount);
        builder.BeginContour(new SvgDevicePoint(1, 1));
        builder.AddLine(new SvgDevicePoint(2, 3));
        builder.EndContour();
        Assert.Equal([new SvgDevicePoint(1, 1), new SvgDevicePoint(2, 3)], builder.GetContour(0).ToArray());
    }

    private static int ArcContourLength(double flatness)
    {
        SvgContourBuilder builder = new(256, 4);
        builder.BeginContour(new SvgDevicePoint(8, 0));
        builder.AddArc(8, 8, 0, false, true, new SvgDevicePoint(0, 8), new SvgFlattenPolicy(flatness, 64, 256), CancellationToken.None);
        builder.EndContour();
        return builder.GetContour(0).Length;
    }

    private static double SumCoverages(ReadOnlySpan<SvgDevicePoint> contour, int minimumX, int maximumX, int minimumY, int maximumY)
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