using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPathDataParserTests
{
    [Fact]
    public void Null_and_empty_draw_nothing()
    {
        Assert.Equal(0, Parse(null).ContourCount);
        Assert.Equal(0, Parse(string.Empty).ContourCount);
        Assert.Equal(0, Parse("  ,\t\n ").ContourCount);
    }

    [Fact]
    public void Absolute_triangle_is_exact()
    {
        SvgContourBuilder builder = Parse("M0 0L8 0L0 8Z");

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Relative_triangle_matches_absolute()
    {
        SvgContourBuilder builder = Parse("m0 0l8 0l-8 8z");

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Implicit_lineto_after_moveto_is_exact()
    {
        SvgContourBuilder builder = Parse("M0 0 8 0 0 8Z");

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Implicit_repeated_lineto_is_exact()
    {
        SvgContourBuilder builder = Parse("M0 0L8 0 4 4Z");

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(4, 4)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Horizontal_vertical_square_is_exact()
    {
        SvgContourBuilder builder = Parse("M0 0H8V8H0Z");

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Relative_horizontal_vertical_is_exact()
    {
        SvgContourBuilder builder = Parse("m1 1h7v7h-7z");

        Assert.Equal([new SvgDevicePoint(1, 1), new SvgDevicePoint(8, 1), new SvgDevicePoint(8, 8), new SvgDevicePoint(1, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Fractional_exponent_numbers_are_exact()
    {
        SvgContourBuilder builder = Parse("M0.5 0L8.25 5e-1Z");

        Assert.Equal([new SvgDevicePoint(0.5, 0), new SvgDevicePoint(8.25, 0.5)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Sign_abutment_splits_numbers()
    {
        SvgContourBuilder builder = Parse("M1 1L9-2Z");

        Assert.Equal([new SvgDevicePoint(1, 1), new SvgDevicePoint(9, -2)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Transform_maps_vertices()
    {
        SvgContourBuilder builder = new(16, 4);
        SvgPathDataParser.ParsePath("M1 1L2 2Z", SvgAffineTransform.FromScale(2, 3), builder, new SvgFlattenPolicy(0.5, 64, 256), 16, new ParserWorkCounter(1_000_000), CancellationToken.None, null);

        Assert.Equal([new SvgDevicePoint(2, 3), new SvgDevicePoint(4, 6)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Two_moves_make_two_contours()
    {
        SvgContourBuilder builder = Parse("M0 0L8 0L0 8Z M16 16L24 16L16 24Z");

        Assert.Equal(2, builder.ContourCount);
        Assert.Equal(new SvgDevicePoint(16, 16), builder.GetContour(1)[0]);
        Assert.Equal(new SvgDevicePoint(16, 24), builder.GetContour(1)[^1]);
    }

    [Fact]
    public void Unclosed_contour_seals_implicitly()
    {
        SvgContourBuilder builder = Parse("M0 0L8 0L0 8");

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Repeated_closepath_is_noop()
    {
        SvgContourBuilder builder = Parse("M0 0L8 0ZZ");

        Assert.Equal(1, builder.ContourCount);
        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Absolute_cubic_chains_flattened_points()
    {
        SvgContourBuilder builder = ParseCurve("M0 0C8 0 8 8 0 8Z", 3.0);

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(6, 4), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Relative_cubic_matches_absolute()
    {
        SvgContourBuilder builder = ParseCurve("m0 0c8 0 8 8 0 8Z", 3.0);

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(6, 4), new SvgDevicePoint(0, 8)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Smooth_cubic_reflects_previous_control()
    {
        SvgContourBuilder builder = ParseCurve("M0 0C8 0 8 8 0 8S-8 8 -8 0Z", 3.0);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);

        Assert.Equal(new SvgDevicePoint(6, 4), contour[1]);
        Assert.Equal(new SvgDevicePoint(-8, 0), contour[^1]);
        Assert.True(contour.Length >= 4);
    }

    [Fact]
    public void Smooth_cubic_without_previous_uses_current()
    {
        SvgContourBuilder builder = ParseCurve("M0 0S8 8 8 0Z", 0.5);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);

        Assert.Equal(new SvgDevicePoint(0, 0), contour[0]);
        Assert.Equal(new SvgDevicePoint(8, 0), contour[^1]);
        Assert.True(contour.Length >= 2);
    }

    [Fact]
    public void Absolute_quadratic_chains_flattened_points()
    {
        SvgContourBuilder builder = ParseCurve("M0 0Q4 8 8 0Z", 2.0);

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(8, 0)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Relative_quadratic_matches_absolute()
    {
        SvgContourBuilder builder = ParseCurve("m0 0q4 8 8 0Z", 2.0);

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 4), new SvgDevicePoint(8, 0)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Smooth_quadratic_reflects_previous_control()
    {
        SvgContourBuilder builder = ParseCurve("M0 0Q4 8 8 0T16 0Z", 2.0);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);

        Assert.Equal(new SvgDevicePoint(4, 4), contour[1]);
        Assert.Equal(new SvgDevicePoint(16, 0), contour[^1]);
        Assert.True(contour.Length >= 3);
    }

    [Fact]
    public void Smooth_quadratic_without_previous_draws_line()
    {
        SvgContourBuilder builder = ParseCurve("M0 0T8 0Z", 0.5);

        Assert.Equal([new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0)], builder.GetContour(0).ToArray());
    }

    [Fact]
    public void Arc_lowering_brackets_exact_endpoints()
    {
        SvgContourBuilder builder = ParseCurve("M8 0A8 8 0 0 1 0 8Z", 0.5);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);

        Assert.True(contour.Length >= 3);
        Assert.Equal(new SvgDevicePoint(8, 0), contour[0]);
        Assert.Equal(new SvgDevicePoint(0, 8), contour[^1]);
    }

    [Fact]
    public void Abutted_arc_flags_match_spaced_flags()
    {
        SvgContourBuilder spaced = ParseCurve("M8 0A8 8 0 0 1 0 8Z", 0.5);
        SvgContourBuilder abutted = ParseCurve("M8 0A8 8 0 01 0 8Z", 0.5);

        Assert.Equal(spaced.GetContour(0).ToArray(), abutted.GetContour(0).ToArray());
    }

    [Fact]
    public void Relative_arc_matches_absolute()
    {
        SvgContourBuilder builder = ParseCurve("m8 0a8 8 0 0 1 -8 8Z", 0.5);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);

        Assert.Equal(new SvgDevicePoint(8, 0), contour[0]);
        Assert.Equal(new SvgDevicePoint(0, 8), contour[^1]);
    }

    [Fact]
    public void Arc_radii_apply_in_device_frame()
    {
        SvgContourBuilder scaled = new(256, 4);
        SvgPathDataParser.ParsePath("M8 0A8 8 0 0 1 0 8Z", SvgAffineTransform.FromScale(2, 2), scaled, new SvgFlattenPolicy(0.5, 64, 256), 64, new ParserWorkCounter(1_000_000), CancellationToken.None, null);
        SvgContourBuilder direct = ParseCurve("M16 0A8 8 0 0 1 0 16Z", 0.5);

        Assert.Equal(direct.GetContour(0).ToArray(), scaled.GetContour(0).ToArray());
    }

    [Fact]
    public void Truncated_curves_are_malformed()
    {
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0C1 1 2 2"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0Q1 1"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0A8 8 0 0"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0A8 8 0 2 1 0 8"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0C1 1"));
    }

    private static SvgContourBuilder ParseCurve(string value, double flatness)
    {
        SvgContourBuilder builder = new(256, 4);
        SvgPathDataParser.ParsePath(value, SvgAffineTransform.Identity, builder, new SvgFlattenPolicy(flatness, 64, 256), 64, new ParserWorkCounter(1_000_000), CancellationToken.None, null);
        return builder;
    }

    [Fact]
    public void Structural_violations_are_malformed()
    {
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("L8 0Z"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0L1 1Z2 2"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M10"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0X1 1"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0Lx 0"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0L1e999 0"));
        Assert.Equal(SvgErrorCode.MalformedInput, ParseError("M0 0L1e 0"));
    }

    [Fact]
    public void Relative_overflow_is_LimitExceeded()
    {
        Assert.Equal(SvgErrorCode.LimitExceeded, ParseError("m1e308 0l1e308 0Z"));
    }

    [Fact]
    public void Command_budget_denies()
    {
        SvgContourBuilder builder = new(16, 4);

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgPathDataParser.ParsePath("M0 0L1 1Z", SvgAffineTransform.Identity, builder, new SvgFlattenPolicy(0.5, 64, 256), 1, new ParserWorkCounter(1_000_000), CancellationToken.None, null)).ErrorCode);
    }

    [Fact]
    public void Work_budget_denies()
    {
        SvgContourBuilder builder = new(16, 4);

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgPathDataParser.ParsePath("M0 0L1 1Z", SvgAffineTransform.Identity, builder, new SvgFlattenPolicy(0.5, 64, 256), 16, new ParserWorkCounter(0), CancellationToken.None, null)).ErrorCode);
    }

    [Fact]
    public void Precancelled_token_throws()
    {
        SvgContourBuilder builder = new(16, 4);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgPathDataParser.ParsePath("M0 0L1 1Z", SvgAffineTransform.Identity, builder, new SvgFlattenPolicy(0.5, 64, 256), 16, new ParserWorkCounter(1_000_000), cancelled.Token, null));
    }

    private static SvgContourBuilder Parse(string? value)
    {
        SvgContourBuilder builder = new(64, 8);
        SvgPathDataParser.ParsePath(value, SvgAffineTransform.Identity, builder, new SvgFlattenPolicy(0.5, 64, 256), 64, new ParserWorkCounter(1_000_000), CancellationToken.None, null);
        return builder;
    }

    private static SvgErrorCode ParseError(string value)
    {
        return Assert.Throws<SvgException>(() => Parse(value)).ErrorCode;
    }

    [Fact]
    public void Single_closed_subpath_reads_closed_single()
    {
        ParserWorkCounter counter = new(1000);
        Assert.True(SvgPathDataParser.IsSingleClosedPath("M0,0 L8,0 L8,8 Z", counter, CancellationToken.None));
        Assert.True(SvgPathDataParser.IsSingleClosedPath("m0 0l8 0l-8 8z", counter, CancellationToken.None));
        Assert.True(SvgPathDataParser.IsSingleClosedPath("M1e-2,0 L2,2e0 A24,24 0 1 1 5,5 Z", counter, CancellationToken.None));
    }

    [Fact]
    public void Open_or_multi_subpaths_read_not_closed_single()
    {
        ParserWorkCounter counter = new(1000);
        Assert.False(SvgPathDataParser.IsSingleClosedPath(null, counter, CancellationToken.None));
        Assert.False(SvgPathDataParser.IsSingleClosedPath(string.Empty, counter, CancellationToken.None));
        Assert.False(SvgPathDataParser.IsSingleClosedPath("M0,0 L8,0 L8,8", counter, CancellationToken.None));
        Assert.False(SvgPathDataParser.IsSingleClosedPath("M0,0Z M2,2Z", counter, CancellationToken.None));
        Assert.False(SvgPathDataParser.IsSingleClosedPath("M0,0 L8,0 Z L2,2", counter, CancellationToken.None));
    }
}
