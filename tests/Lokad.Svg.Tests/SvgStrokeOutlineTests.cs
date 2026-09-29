using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStrokeOutlineTests
{
    [Fact]
    public void Horizontal_band_has_butt_caps()
    {
        SvgDevicePoint[] line = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(line, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(0, 1), new SvgDevicePoint(8, 1), new SvgDevicePoint(8, -1), new SvgDevicePoint(0, -1)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Horizontal_band_has_square_caps()
    {
        SvgDevicePoint[] line = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(line, 2, 4, SvgStrokeCap.Square, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(-1, 1), new SvgDevicePoint(9, 1), new SvgDevicePoint(9, -1), new SvgDevicePoint(-1, -1)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Vertical_band_has_butt_caps()
    {
        SvgDevicePoint[] line = [new SvgDevicePoint(0, 0), new SvgDevicePoint(0, 8)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(line, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(-1, 0), new SvgDevicePoint(-1, 8), new SvgDevicePoint(1, 8), new SvgDevicePoint(1, 0)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Left_turn_butts_inner_and_ears_outer()
    {
        SvgDevicePoint[] turn = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(turn, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(0, 1), new SvgDevicePoint(8, 1), new SvgDevicePoint(7, 0), new SvgDevicePoint(7, 8), new SvgDevicePoint(9, 8), new SvgDevicePoint(9, 0), new SvgDevicePoint(9, -1), new SvgDevicePoint(8, -1), new SvgDevicePoint(0, -1)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Reversed_turn_mirrors_the_forward_outline()
    {
        SvgDevicePoint[] turn = [new SvgDevicePoint(8, 8), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 0)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(turn, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(9, 8), new SvgDevicePoint(9, 0), new SvgDevicePoint(9, -1), new SvgDevicePoint(8, -1), new SvgDevicePoint(0, -1), new SvgDevicePoint(0, 1), new SvgDevicePoint(8, 1), new SvgDevicePoint(7, 0), new SvgDevicePoint(7, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Spike_reversal_bevels_and_retraces_both_chains()
    {
        SvgDevicePoint[] spike = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 0)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(spike, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(0, 1), new SvgDevicePoint(8, 1), new SvgDevicePoint(8, -1), new SvgDevicePoint(0, -1), new SvgDevicePoint(0, 1), new SvgDevicePoint(8, 1), new SvgDevicePoint(8, -1), new SvgDevicePoint(0, -1)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Sharp_turn_bevels_at_limit_one()
    {
        SvgDevicePoint[] turn = [new SvgDevicePoint(0, 0), new SvgDevicePoint(5, 0), new SvgDevicePoint(1, 3)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(turn, 2, 1, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);
        Assert.Equal(1, builder.ContourCount);
        Assert.True(contour.Length == 8, "Sharp bevel outline keeps both corners on each chain.");
        Assert.Equal(new SvgDevicePoint(0, 1), contour[0]);
        Assert.Equal(new SvgDevicePoint(5, 1), contour[1]);
        Assert.Equal(4.4, contour[2].X, 9);
        Assert.Equal(-0.8, contour[2].Y, 9);
        Assert.Equal(new SvgDevicePoint(0, -1), contour[7]);
    }

    [Fact]
    public void Sharp_turn_bevels_with_bevel_join()
    {
        SvgDevicePoint[] turn = [new SvgDevicePoint(0, 0), new SvgDevicePoint(5, 0), new SvgDevicePoint(1, 3)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(turn, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Bevel, builder, CancellationToken.None);

        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);
        Assert.Equal(1, builder.ContourCount);
        Assert.True(contour.Length == 8, "Bevel joins cut the outer corner like the miter-limit fallback.");
        Assert.Equal(new SvgDevicePoint(0, 1), contour[0]);
        Assert.Equal(new SvgDevicePoint(5, 1), contour[1]);
        Assert.Equal(4.4, contour[2].X, 9);
        Assert.Equal(-0.8, contour[2].Y, 9);
        Assert.Equal(new SvgDevicePoint(0, -1), contour[7]);
    }

    [Fact]
    public void Sharp_turn_miters_within_limit_four()
    {
        SvgDevicePoint[] turn = [new SvgDevicePoint(0, 0), new SvgDevicePoint(5, 0), new SvgDevicePoint(1, 3)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(turn, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);
        Assert.Equal(1, builder.ContourCount);
        Assert.True(contour.Length == 10, "Obtuse joins trace butt-corner ears around the tip.");
        Assert.Equal(new SvgDevicePoint(0, 1), contour[0]);
        Assert.Equal(new SvgDevicePoint(5, 1), contour[1]);
        Assert.Equal(2, contour[2].X, 9);
        Assert.Equal(1, contour[2].Y, 9);
        Assert.Equal(new SvgDevicePoint(4.4, -0.8), contour[3]);
        Assert.Equal(8, contour[7].X, 9);
        Assert.Equal(-1, contour[7].Y, 9);
        Assert.Equal(new SvgDevicePoint(0, -1), contour[9]);
    }

    [Fact]
    public void Sharp_turn_bevels_at_limit_two()
    {
        // The (0,0)->(5,0)->(1,3) turn has a true miter ratio of sqrt(10) (about 3.16).
        // Limit 2 must bevel; the former half-normalized comparison wrongly mitered it.
        SvgDevicePoint[] turn = [new SvgDevicePoint(0, 0), new SvgDevicePoint(5, 0), new SvgDevicePoint(1, 3)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(turn, 2, 2, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);
        Assert.Equal(1, builder.ContourCount);
        Assert.True(contour.Length == 8, "Miter-limit fallback keeps both corners on each chain.");
        Assert.Equal(new SvgDevicePoint(0, 1), contour[0]);
        Assert.Equal(new SvgDevicePoint(5, 1), contour[1]);
        Assert.Equal(4.4, contour[2].X, 9);
        Assert.Equal(-0.8, contour[2].Y, 9);
        Assert.Equal(new SvgDevicePoint(0, -1), contour[7]);
    }

    [Fact]
    public void Closed_square_emits_opposite_wound_outlines()
    {
        SvgDevicePoint[] square = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)];
        SvgContourBuilder builder = new(32, 4);

        SvgStrokeOutline.StrokeClosedPolyline(square, 2, 4, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(2, builder.ContourCount);
        ReadOnlySpan<SvgDevicePoint> outer = builder.GetContour(0);
        ReadOnlySpan<SvgDevicePoint> inner = builder.GetContour(1);
        Assert.Equal([new SvgDevicePoint(0, 1), new SvgDevicePoint(7, 1), new SvgDevicePoint(7, 7), new SvgDevicePoint(1, 7), new SvgDevicePoint(1, 1)], outer.ToArray());
        Assert.Equal([new SvgDevicePoint(-1, 0), new SvgDevicePoint(-1, 9), new SvgDevicePoint(9, 9), new SvgDevicePoint(9, -1), new SvgDevicePoint(-1, -1)], inner.ToArray());
        Assert.True(SignedArea(outer) > 0, "Outer outline keeps the input winding.");
        Assert.True(SignedArea(inner) < 0, "Inner outline reverses so nonzero filling keeps the band.");

        static double SignedArea(ReadOnlySpan<SvgDevicePoint> contour)
        {
            double total = 0;
            for (int index = 0; index < contour.Length; index++)
            {
                SvgDevicePoint current = contour[index];
                SvgDevicePoint next = contour[(index + 1) % contour.Length];
                total += current.X * next.Y - next.X * current.Y;
            }

            return total / 2;
        }
    }

    [Fact]
    public void Closed_square_with_explicit_closure_matches()
    {
        SvgDevicePoint[] square = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), new SvgDevicePoint(0, 0)];
        SvgContourBuilder builder = new(32, 4);

        SvgStrokeOutline.StrokeClosedPolyline(square, 2, 4, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(2, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(0, 1), new SvgDevicePoint(7, 1), new SvgDevicePoint(7, 7), new SvgDevicePoint(1, 7), new SvgDevicePoint(1, 1)], builder.GetContour(0).ToArray());
        Assert.Equal([new SvgDevicePoint(-1, 0), new SvgDevicePoint(-1, 9), new SvgDevicePoint(9, 9), new SvgDevicePoint(9, -1), new SvgDevicePoint(-1, -1)], builder.GetContour(1).ToArray());
    }

    [Fact]
    public void Closed_two_point_path_emits_the_outer_sliver_only()
    {
        SvgDevicePoint[] sliver = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeClosedPolyline(sliver, 2, 4, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(0, 1), new SvgDevicePoint(8, 1), new SvgDevicePoint(8, -1), new SvgDevicePoint(0, -1), new SvgDevicePoint(0, 1)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Zero_length_segments_are_skipped()
    {
        SvgDevicePoint[] turn = [new SvgDevicePoint(0, 0), new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(turn, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(0, 1), new SvgDevicePoint(8, 1), new SvgDevicePoint(7, 0), new SvgDevicePoint(7, 8), new SvgDevicePoint(9, 8), new SvgDevicePoint(9, 0), new SvgDevicePoint(9, -1), new SvgDevicePoint(8, -1), new SvgDevicePoint(0, -1)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Degenerate_input_draws_nothing()
    {
        SvgDevicePoint[] point = [new SvgDevicePoint(3, 3)];
        SvgDevicePoint[] pair = [new SvgDevicePoint(3, 3), new SvgDevicePoint(3, 3)];
        SvgDevicePoint[] triple = [new SvgDevicePoint(3, 3), new SvgDevicePoint(3, 3), new SvgDevicePoint(3, 3)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(point, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);
        SvgStrokeOutline.StrokeOpenPolyline(pair, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);
        SvgStrokeOutline.StrokeClosedPolyline(pair, 2, 4, SvgStrokeJoin.Miter, builder, CancellationToken.None);
        SvgStrokeOutline.StrokeClosedPolyline(triple, 2, 4, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(0, builder.ContourCount);
    }

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        SvgDevicePoint[] line = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0)];
        SvgContourBuilder builder = new(16, 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgStrokeOutline.StrokeOpenPolyline(line, 0, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgStrokeOutline.StrokeOpenPolyline(line, -2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgStrokeOutline.StrokeOpenPolyline(line, double.NaN, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgStrokeOutline.StrokeOpenPolyline(line, 2, 0.5, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgStrokeOutline.StrokeOpenPolyline(line, 2, double.NaN, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgStrokeOutline.StrokeClosedPolyline(line, 0, 4, SvgStrokeJoin.Miter, builder, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgStrokeOutline.StrokeClosedPolyline(line, 2, 0.5, SvgStrokeJoin.Miter, builder, CancellationToken.None));
        Assert.Equal(0, builder.ContourCount);
    }

    [Fact]
    public void Round_cap_extends_half_width_past_tips()
    {
        SvgDevicePoint[] line = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0)];
        SvgContourBuilder builder = new(32, 4);

        SvgStrokeOutline.StrokeOpenPolyline(line, 2, 4, SvgStrokeCap.Round, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        SvgDevicePoint[] contour = builder.GetContour(0).ToArray();
        Assert.Equal(15, contour.Length);
        Assert.InRange(contour.Select(point => point.X).Min(), -1, -0.75);
        Assert.InRange(contour.Select(point => point.X).Max(), 8.75, 9);
    }

    [Fact]
    public void Round_join_replaces_spike_with_arc()
    {
        SvgDevicePoint[] turn = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8)];
        SvgContourBuilder miterBuilder = new(16, 4);
        SvgContourBuilder roundBuilder = new(32, 4);

        SvgStrokeOutline.StrokeOpenPolyline(turn, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, miterBuilder, CancellationToken.None);
        SvgStrokeOutline.StrokeOpenPolyline(turn, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Round, roundBuilder, CancellationToken.None);

        SvgDevicePoint[] miter = miterBuilder.GetContour(0).ToArray();
        SvgDevicePoint[] round = roundBuilder.GetContour(0).ToArray();
        SvgDevicePoint vertex = new(8, 0);
        SvgDevicePoint[] shared = miter.Where(point => round.Contains(point)).ToArray();
        SvgDevicePoint[] added = round.Where(point => !miter.Contains(point)).ToArray();
        Assert.Equal(8, shared.Length);
        Assert.Equal(2, added.Length);
        foreach (SvgDevicePoint point in added) Assert.True(Distance(vertex, point) <= 1.000000001, "Round fan stays on the half-width circle.");

        static double Distance(SvgDevicePoint first, SvgDevicePoint second)
        {
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    [Fact]
    public void Spike_reversal_rounds_tip()
    {
        SvgDevicePoint[] spike = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 0)];
        SvgContourBuilder builder = new(32, 4);

        SvgStrokeOutline.StrokeOpenPolyline(spike, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Round, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.InRange(builder.GetContour(0).ToArray().Select(point => point.X).Max(), 8.75, 9);
    }
    [Fact]
    public void Butt_dot_draws_nothing()
    {
        SvgDevicePoint[] dot = [new SvgDevicePoint(4, 6)];
        SvgContourBuilder builder = new(32, 4);

        SvgStrokeOutline.StrokeOpenPolyline(dot, 4, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(0, builder.ContourCount);
    }

    [Fact]
    public void Round_dot_makes_disc_on_half_width_circle()
    {
        SvgDevicePoint center = new(4, 6);
        SvgDevicePoint[] dot = [center];
        SvgContourBuilder builder = new(64, 4);

        SvgStrokeOutline.StrokeOpenPolyline(dot, 4, 4, SvgStrokeCap.Round, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        SvgDevicePoint[] contour = builder.GetContour(0).ToArray();
        Assert.Equal(center.X + 2, contour[0].X);
        Assert.Equal(center.Y, contour[0].Y);
        Assert.True(contour.Length > 8, "Disc fan samples the full circle.");
        foreach (SvgDevicePoint vertex in contour)
        {
            double dx = vertex.X - center.X;
            double dy = vertex.Y - center.Y;
            Assert.InRange(Math.Sqrt(dx * dx + dy * dy), 1.999999999, 2.000000001);
        }
    }

    [Fact]
    public void Square_dot_makes_axis_aligned_square()
    {
        SvgDevicePoint[] dot = [new SvgDevicePoint(4, 6)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeOpenPolyline(dot, 4, 4, SvgStrokeCap.Square, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(2, 4), new SvgDevicePoint(6, 4), new SvgDevicePoint(6, 8), new SvgDevicePoint(2, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Repeated_point_collapses_to_round_dot()
    {
        SvgDevicePoint[] dot = [new SvgDevicePoint(4, 6), new SvgDevicePoint(4, 6), new SvgDevicePoint(4, 6)];
        SvgContourBuilder builder = new(64, 4);

        SvgStrokeOutline.StrokeOpenPolyline(dot, 4, 4, SvgStrokeCap.Round, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(1, builder.ContourCount);
        foreach (SvgDevicePoint vertex in builder.GetContour(0).ToArray())
        {
            double dx = vertex.X - 4;
            double dy = vertex.Y - 6;
            Assert.InRange(Math.Sqrt(dx * dx + dy * dy), 1.999999999, 2.000000001);
        }
    }

    [Fact]
    public void Closed_singleton_draws_nothing()
    {
        SvgDevicePoint[] dot = [new SvgDevicePoint(4, 6)];
        SvgContourBuilder builder = new(16, 4);

        SvgStrokeOutline.StrokeClosedPolyline(dot, 4, 4, SvgStrokeJoin.Miter, builder, CancellationToken.None);

        Assert.Equal(0, builder.ContourCount);
    }


    [Fact]
    public void Cancelled_strokes_throw()
    {
        SvgDevicePoint[] line = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0)];
        SvgDevicePoint[] square = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)];
        CancellationToken canceled = new(true);

        Assert.Throws<OperationCanceledException>(() => SvgStrokeOutline.StrokeOpenPolyline(line, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, new SvgContourBuilder(32, 4), canceled));
        Assert.Throws<OperationCanceledException>(() => SvgStrokeOutline.StrokeClosedPolyline(square, 2, 4, SvgStrokeJoin.Miter, new SvgContourBuilder(32, 4), canceled));
    }
}