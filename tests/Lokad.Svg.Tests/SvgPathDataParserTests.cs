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
        SvgPathDataParser.ParseLinePath("M1 1L2 2Z", SvgAffineTransform.FromScale(2, 3), builder, 16, new ParserWorkCounter(1_000_000), CancellationToken.None);

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
    public void Curve_letters_are_unsupported()
    {
        Assert.Equal(SvgErrorCode.UnsupportedFeature, ParseError("M0 0C1 1 2 2 3 3Z"));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, ParseError("M0 0S1 1 2 2Z"));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, ParseError("M0 0Q1 1 2 2Z"));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, ParseError("M0 0T1 1Z"));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, ParseError("M0 0A1 1 0 0 0 2 2Z"));
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

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgPathDataParser.ParseLinePath("M0 0L1 1Z", SvgAffineTransform.Identity, builder, 1, new ParserWorkCounter(1_000_000), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Work_budget_denies()
    {
        SvgContourBuilder builder = new(16, 4);

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgPathDataParser.ParseLinePath("M0 0L1 1Z", SvgAffineTransform.Identity, builder, 16, new ParserWorkCounter(0), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Precancelled_token_throws()
    {
        SvgContourBuilder builder = new(16, 4);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgPathDataParser.ParseLinePath("M0 0L1 1Z", SvgAffineTransform.Identity, builder, 16, new ParserWorkCounter(1_000_000), cancelled.Token));
    }

    private static SvgContourBuilder Parse(string? value)
    {
        SvgContourBuilder builder = new(64, 8);
        SvgPathDataParser.ParseLinePath(value, SvgAffineTransform.Identity, builder, 64, new ParserWorkCounter(1_000_000), CancellationToken.None);
        return builder;
    }

    private static SvgErrorCode ParseError(string value)
    {
        return Assert.Throws<SvgException>(() => Parse(value)).ErrorCode;
    }
}