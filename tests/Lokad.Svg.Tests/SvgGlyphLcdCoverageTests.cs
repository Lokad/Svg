using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 5, 24)]
    [InlineData(0, 0, 0, 1, 4, 30)]
    [InlineData(0, 0, 0, 2, 6, 50)]
    [InlineData(0, 0, 0, 6, 1, 68)]
    [InlineData(0, 0, 1, 1, 0, 22)]
    [InlineData(0, 0, 1, 3, 1, 48)]
    [InlineData(0, 0, 1, 4, 0, 54)]
    [InlineData(0, 0, 1, 4, 3, 68)]
    public void Vertical_filter_matches_independent_native_cell_masks(int first, int second, int third, int fourth, int fifth, byte expected)
    {
        // Original CC0 grid fonts: UPEM 960, em 48, integer contours. Values were
        // measured independently from DirectWrite, before managed implementation.
        int[] counts = [first, second, third, fourth, fifth];
        List<OpenTypeQuadraticSegment> segments = [];
        for (int row = 0; row < counts.Length; row++)
            if (counts[row] != 0) segments.AddRange(Rectangle(0, row / 5d, Math.Round(counts[row] * 20d / 6) / 20, (row + 1) / 5d));
        SvgGlyphRgbCoverage coverage = SvgGlyphLcdCoverage.GetCoverage(segments.ToArray(), 0, 0, new(long.MaxValue), CancellationToken.None);
        Assert.Equal(expected, coverage.Green);
    }

    [Theory]
    [InlineData(-1, 0, 0, 85)]
    [InlineData(0, 170, 255, 255)]
    [InlineData(1, 255, 255, 255)]
    [InlineData(5, 255, 255, 170)]
    [InlineData(6, 85, 0, 0)]
    public void Displaced_channels_match_independent_six_pixel_square(int x, byte red, byte green, byte blue)
        => Assert.Equal(new SvgGlyphRgbCoverage(red, green, blue), SvgGlyphLcdCoverage.GetCoverage(Rectangle(0, 0, 6, 6), x, 0, new(long.MaxValue), CancellationToken.None));

    [Fact]
    public void Opposite_winding_cancels_counters_and_same_winding_keeps_union()
    {
        OpenTypeQuadraticSegment[] outer = Rectangle(-1, -1, 2, 2), inner = Rectangle(0, 0, 1, 1);
        OpenTypeQuadraticSegment[] reversed = inner.Reverse().Select(segment => new OpenTypeQuadraticSegment(segment.End, segment.Start, segment.Start)).ToArray();
        Assert.Equal((byte)255, SvgGlyphLcdCoverage.GetCoverage([.. outer, .. inner], 0, 0, new(long.MaxValue), CancellationToken.None).Green);
        Assert.Equal((byte)0, SvgGlyphLcdCoverage.GetCoverage([.. outer, .. reversed], 0, 0, new(long.MaxValue), CancellationToken.None).Green);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Quadratic_endpoint_on_sample_row_keeps_closed_winding(bool reverse, bool reflect)
    {
        OpenTypeQuadraticSegment[] segments =
        [
            new(new(0, .5), new(1, 0), new(2, 0)),
            new(new(2, 0), new(2, 1), new(2, 1)),
            new(new(2, 1), new(0, 1), new(0, 1)),
            new(new(0, 1), new(0, .5), new(0, .5))
        ];
        if (reflect) segments = segments.Select(s => new OpenTypeQuadraticSegment(Mirror(s.Start), Mirror(s.Control), Mirror(s.End))).ToArray();
        if (reverse) segments = segments.Reverse().Select(s => new OpenTypeQuadraticSegment(s.End, s.Control == s.End ? s.Start : s.Control, s.Start)).ToArray();
        Assert.Equal(default(SvgGlyphRgbCoverage), SvgGlyphLcdCoverage.GetCoverage(segments, 3, 0, new(long.MaxValue), CancellationToken.None));
        Assert.NotEqual((byte)0, SvgGlyphLcdCoverage.GetCoverage(segments, 1, 0, new(long.MaxValue), CancellationToken.None).Green);

        static OpenTypeOutlinePoint Mirror(OpenTypeOutlinePoint point) => new(point.X, 1 - point.Y);
    }

    [Fact]
    public void Coverage_observes_cancellation_and_bounds_segment_work()
    {
        OpenTypeQuadraticSegment[] segments = Rectangle(0, 0, 1, 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphLcdCoverage.GetCoverage(segments, 0, 0, new(2), CancellationToken.None)).ErrorCode);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphLcdCoverage.GetCoverage([], 0, 0, new(long.MaxValue), cancelled.Token));
    }

    private static OpenTypeQuadraticSegment[] Rectangle(double left, double top, double right, double bottom)
    {
        OpenTypeOutlinePoint[] points = [new(left, top), new(right, top), new(right, bottom), new(left, bottom)];
        return Enumerable.Range(0, 4).Select(i => new OpenTypeQuadraticSegment(points[i], points[(i + 1) % 4], points[(i + 1) % 4])).ToArray();
    }
}
