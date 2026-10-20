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

    [Fact]
    public void Ellipse_appends_standalone_loop()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.AddEllipse(new SvgDevicePoint(4, 4), 8, 8, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);
        Assert.True(contour.Length > 4);
        Assert.Equal(new SvgDevicePoint(12, 4), contour[0]);
    }

    [Fact]
    public void Ellipse_rejects_open_contour_and_bad_shapes()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.BeginContour(new SvgDevicePoint(0, 0));

        Assert.Throws<InvalidOperationException>(() => builder.AddEllipse(new SvgDevicePoint(4, 4), 8, 8, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None));
        builder.EndContour();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddEllipse(new SvgDevicePoint(4, 4), 0, 8, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddEllipse(new SvgDevicePoint(double.NaN, 4), 8, 8, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None));
    }

    [Fact]
    public void Sharp_rect_is_exact()
    {
        SvgContourBuilder builder = new(16, 4);
        builder.AddRoundedRect(0, 0, 8, 4, 0, 0, new SvgFlattenPolicy(0.5, 64, 16), CancellationToken.None);

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 4), new SvgDevicePoint(0, 4)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Coarse_rounded_rect_is_exact()
    {
        // The contour starts at the top-right arc entrance with the same cyclic order, so the implicit
        // closing edge is the straight top edge and no closing duplicate is stored.
        SvgContourBuilder builder = new(256, 4);
        builder.AddRoundedRect(0, 0, 8, 4, 2, 1, new SvgFlattenPolicy(100.0, 64, 256), CancellationToken.None);

        Assert.Equal([new SvgDevicePoint(6, 0), new SvgDevicePoint(8, 1), new SvgDevicePoint(8, 3), new SvgDevicePoint(6, 4), new SvgDevicePoint(2, 4), new SvgDevicePoint(0, 3), new SvgDevicePoint(0, 1), new SvgDevicePoint(2, 0)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Oversized_corners_clamp_to_half_sides()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.AddRoundedRect(0, 0, 8, 4, 100, 100, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);

        Assert.Equal(new SvgDevicePoint(4, 0), contour[0]);
        Assert.True(contour.Length >= 9);
        Assert.Contains(new SvgDevicePoint(8, 2), contour.ToArray());
        Assert.Contains(new SvgDevicePoint(4, 4), contour.ToArray());
        Assert.Contains(new SvgDevicePoint(0, 2), contour.ToArray());
    }

    [Fact]
    public void Rounded_rect_rejects_bad_inputs_and_open_contour()
    {
        SvgContourBuilder builder = new(256, 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRoundedRect(0, 0, 8, 4, -1, 1, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRoundedRect(0, 0, -8, 4, 2, 1, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None));
        builder.BeginContour(new SvgDevicePoint(0, 0));
        Assert.Throws<InvalidOperationException>(() => builder.AddRoundedRect(0, 0, 8, 4, 2, 1, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None));
        builder.EndContour();
    }

    [Fact]
    public void Zero_sized_rect_draws_nothing()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.AddRoundedRect(0, 0, 0, 4, 0, 0, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None);
        builder.AddRoundedRect(0, 0, 8, 0, 0, 0, new SvgFlattenPolicy(0.5, 64, 256), CancellationToken.None);

        Assert.Equal(0, builder.ContourCount);
        Assert.Equal(0, builder.PointCount);
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

    [Fact]
    public void Growing_builder_matches_fixed_contours_and_bounds_maxima()
    {
        SvgFlattenPolicy policy = new(0.25, 64, 256);
        SvgContourBuilder fixedBuilder = new(4096, 64);
        SvgContourBuilder growingBuilder = SvgContourBuilder.CreateGrowing(4096, 64);
        for (int figure = 0; figure < 10; figure++)
        {
            double origin = figure * 20;
            fixedBuilder.BeginContour(new SvgDevicePoint(origin, 0));
            fixedBuilder.AddCubic(new SvgDevicePoint(origin + 8, 0), new SvgDevicePoint(origin + 8, 8), new SvgDevicePoint(origin, 8), policy, CancellationToken.None);
            fixedBuilder.AddCubic(new SvgDevicePoint(origin - 8, 8), new SvgDevicePoint(origin - 8, 0), new SvgDevicePoint(origin, 0), policy, CancellationToken.None);
            fixedBuilder.EndContour();
            growingBuilder.BeginContour(new SvgDevicePoint(origin, 0));
            growingBuilder.AddCubic(new SvgDevicePoint(origin + 8, 0), new SvgDevicePoint(origin + 8, 8), new SvgDevicePoint(origin, 8), policy, CancellationToken.None);
            growingBuilder.AddCubic(new SvgDevicePoint(origin - 8, 8), new SvgDevicePoint(origin - 8, 0), new SvgDevicePoint(origin, 0), policy, CancellationToken.None);
            growingBuilder.EndContour();
        }

        Assert.Equal(10, growingBuilder.ContourCount);
        for (int figure = 0; figure < 10; figure++) Assert.Equal(fixedBuilder.GetContour(figure).ToArray(), growingBuilder.GetContour(figure).ToArray());
        Assert.True(growingBuilder.PointCapacity < 4096);
        SvgContourBuilder boundedBuilder = SvgContourBuilder.CreateGrowing(4, 64);
        boundedBuilder.BeginContour(new SvgDevicePoint(0, 0));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => boundedBuilder.AddCubic(new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), policy, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Quadratic_records_slice_relative_span_with_control()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.EnableQuadSpanRetention();
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddLine(new SvgDevicePoint(8, 0));
        builder.AddQuadratic(new SvgDevicePoint(12, 8), new SvgDevicePoint(16, 0), new SvgFlattenPolicy(2.0, 64, 256), CancellationToken.None);
        builder.EndContour();

        SvgQuadSpan span = Assert.Single(builder.GetQuadSpans());
        Assert.Equal(0, span.Contour);
        Assert.Equal(new SvgDevicePoint(12, 8), span.Control);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);
        Assert.Equal(new SvgDevicePoint(8, 0), contour[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(16, 0), contour[span.EndIndex]);
        Assert.True(span.EndIndex > span.StartIndex);
    }
    [Fact]
    public void Quad_spans_stay_empty_without_retention_and_clear_resets()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddQuadratic(new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), new SvgFlattenPolicy(2.0, 64, 256), CancellationToken.None);
        builder.EndContour();
        Assert.Empty(builder.GetQuadSpans());

        builder.Clear();
        builder.EnableQuadSpanRetention();
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddQuadratic(new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), new SvgFlattenPolicy(2.0, 64, 256), CancellationToken.None);
        builder.EndContour();
        Assert.Single(builder.GetQuadSpans());
        builder.Clear();
        Assert.Empty(builder.GetQuadSpans());
    }

    [Fact]
    public void Quad_spans_track_contours_with_flattened_points_unchanged()
    {
        SvgContourBuilder retained = new(256, 8);
        retained.EnableQuadSpanRetention();
        retained.BeginContour(new SvgDevicePoint(0, 0));
        retained.AddQuadratic(new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), new SvgFlattenPolicy(2.0, 64, 256), CancellationToken.None);
        retained.EndContour();
        retained.BeginContour(new SvgDevicePoint(16, 0));
        retained.AddLine(new SvgDevicePoint(24, 0));
        retained.AddQuadratic(new SvgDevicePoint(28, 8), new SvgDevicePoint(32, 0), new SvgFlattenPolicy(2.0, 64, 256), CancellationToken.None);
        retained.EndContour();

        SvgContourBuilder plain = new(256, 8);
        plain.BeginContour(new SvgDevicePoint(0, 0));
        plain.AddQuadratic(new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), new SvgFlattenPolicy(2.0, 64, 256), CancellationToken.None);
        plain.EndContour();
        plain.BeginContour(new SvgDevicePoint(16, 0));
        plain.AddLine(new SvgDevicePoint(24, 0));
        plain.AddQuadratic(new SvgDevicePoint(28, 8), new SvgDevicePoint(32, 0), new SvgFlattenPolicy(2.0, 64, 256), CancellationToken.None);
        plain.EndContour();

        Assert.Equal(plain.GetContour(0).ToArray(), retained.GetContour(0).ToArray());
        Assert.Equal(plain.GetContour(1).ToArray(), retained.GetContour(1).ToArray());
        SvgQuadSpan[] spans = retained.GetQuadSpans();
        Assert.Equal(2, spans.Length);
        Assert.Equal(0, spans[0].Contour);
        Assert.Equal(1, spans[1].Contour);
        Assert.Equal(new SvgDevicePoint(4, 8), spans[0].Control);
        Assert.Equal(new SvgDevicePoint(28, 8), spans[1].Control);
        Assert.Equal(new SvgDevicePoint(32, 0), retained.GetContour(1)[spans[1].EndIndex]);
        Assert.Equal(new SvgDevicePoint(24, 0), retained.GetContour(1)[spans[1].StartIndex]);
    }
}
