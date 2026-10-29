using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Natural_inherited_candidates_observe_preceding_surviving_dropouts(bool preceding)
    {
        // Original CC0 bar and shallow quadratic cap: UPEM 2048, em 32,
        // origin (20,80), SCANCTRL 511 / SCANTYPE 5. Independent native masks
        // retain row 71 alone, or only row 70 when the preceding bar is present.
        // The rounded legacy profile reaches a center that the implicit scan
        // misses; restoring it still observes the already selected neighbor.
        OpenTypeOutlinePoint a = new(20, 71.484375), b = new(20, 71.0625), c = new(26.25, 71.0625), d = new(26.25, 71.484375);
        OpenTypeQuadraticSegment[] segments = [new(a, b, b), new(b, c, c), new(c, d, d), new(d, new(23.125, 71.515625), a), .. Rectangle(35, 64, 36, 80)];
        if (preceding) segments = [.. Rectangle(20, 70.0625, 26.25, 70.375), .. segments];
        byte[] expected = preceding ? [0, 0, 0, 255, 255, 255, 0, 0, 0, 0, 0, 0] : [0, 0, 0, 0, 0, 0, 255, 255, 255, 0, 0, 0];
        AssertNaturalRows(segments, false, true, 22, 69, 1, 4, expected);
    }

    [Theory]
    [InlineData(false, 2, "AAAAAAAAAFWq/6pVAAAAAAAAVar//////6pVAFWq////qlUA")]
    [InlineData(false, 3, "AAAAAAAAACuA1dWAKwAAAAAAK4DV/////9WAKyuA1f//1YAr")]
    [InlineData(true, 2, "AAAAAAAAAFWq//////+qVQAAAAAAAAAAAAAAK4DV/6pVAAAAAABVqv//qlUAAAAAAAArgNX//6pVAAAAAABVqv//1YArAAAAAAAAAAAAAFWq//////+qVQAAAAAAAAAA")]
    [InlineData(true, 3, "AAAAAAAAACuA1f/////VgCsAAAAAAAAAAAAAAFWq/9WAKwAAAAArgNX/1YArAAAAAAAAVar//9WAKwAAAAArgNX//6pVAAAAAAAAAAAAACuA1f/////VgCsAAAAAAAAA")]
    public void Natural_conic_profiles_keep_native_monotonic_and_turning_endpoint_rounding(bool ring, int phase, string rgb)
    {
        // Original CC0 conics, independently rendered by DirectWrite at UPEM
        // 2048 / em 32, SCANCTRL 511 / SCANTYPE 4. The asymmetric band needs
        // native implied-endpoint rounding; applying it to a turning arc moves
        // an already correct cell. Both quarter-pixel strike phases are retained.
        double origin = 20 + phase / 6d;
        OpenTypeQuadraticSegment[] segments;
        if (ring)
        {
            OpenTypeOutlinePoint[][] controls =
            [
                [P(0, 128), P(224, 256), P(449, 128), P(224, 0)],
                [P(80, 128), P(224, 32), P(369, 128), P(224, 224)]
            ];
            List<OpenTypeQuadraticSegment> curves = [];
            foreach (OpenTypeOutlinePoint[] points in controls)
                for (int i = 0; i < points.Length; i++)
                {
                    OpenTypeOutlinePoint previous = points[(i + points.Length - 1) % points.Length], current = points[i], next = points[(i + 1) % points.Length];
                    curves.Add(new(new((previous.X + current.X) / 2, (previous.Y + current.Y) / 2), current, new((current.X + next.X) / 2, (current.Y + next.Y) / 2)));
                }
            segments = curves.ToArray();
        }
        else segments = [new(P(0, 0), P(107, 64), P(161.5, 80.5)), new(P(161.5, 80.5), P(216, 97), P(323, 0)), new(P(323, 0), P(323, -24), P(323, -24)), new(P(323, -24), P(216, 73), P(161.5, 56.5)), new(P(161.5, 56.5), P(107, 40), P(0, -24)), new(P(0, -24), P(0, 0), P(0, 0))];
        int width = ring ? 8 : 6, height = ring ? 4 : 2, top = ring ? 76 : 78;
        byte[] native = Convert.FromBase64String(rgb), expected = new byte[(width + 2) * (height + 2) * 3];
        Assert.Equal(width * height * 3, native.Length);
        for (int row = 0; row < height; row++) native.AsSpan(row * width * 3, width * 3).CopyTo(expected.AsSpan(((row + 1) * (width + 2) + 1) * 3));
        AssertNaturalRows(segments, false, false, 19, top - 1, width + 2, height + 2, expected);

        OpenTypeOutlinePoint P(double x, double y) => new(origin + x / 64, 80 - y / 64);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Natural_second_pass_ignores_a_preceding_excluded_stub(bool simple, bool excludeStubs)
    {
        // Original CC0 rectangles, independently rendered by DirectWrite:
        // UPEM 2048, em 32, origin (20,80), SCANCTRL 511, SCANTYPE 0/1/4/5.
        OpenTypeQuadraticSegment[] segments = [.. Rectangle(22.0625, 70.0625, 22.109375, 70.375), .. Rectangle(20, 71.0625, 26.25, 71.375), .. Rectangle(35, 64, 36, 80)];
        byte[] expected = excludeStubs ? [0, 0, 0, 0, 0, 0, 255, 255, 255, 255, 255, 255] : [0, 0, 43, 43, 43, 0, 255, 255, 213, 213, 213, 255];
        AssertNaturalRows(segments, simple, excludeStubs, 21, 70, 2, 2, expected);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Natural_second_pass_observes_preceding_surviving_spans(bool simple, bool excludeStubs, bool separated)
    {
        // Same independent native recipe. Only adjacent selected cells suppress
        // a later span; a separated stroke retains its own dropout row.
        double top = separated ? 72.0625 : 71.0625;
        OpenTypeQuadraticSegment[] segments = [.. Rectangle(20, 70.0625, 26.25, 70.375), .. Rectangle(20, top, 26.25, top + .3125), .. Rectangle(35, 64, 36, 80)];
        byte[] expected = separated ? [255, 255, 255, 0, 0, 0, 255, 255, 255] : [255, 255, 255, 0, 0, 0, 0, 0, 0];
        AssertNaturalRows(segments, simple, excludeStubs, 22, 70, 1, 3, expected);
    }

    private static void AssertNaturalRows(OpenTypeQuadraticSegment[] segments, bool simple, bool excludeStubs, int left, int top, int width, int height, byte[] expected)
    {
        ParserWorkCounter work = new(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, !simple, simple, excludeStubs, SvgGlyphLcdMode.Natural, work, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        RenderBudget budget = new(long.MaxValue);
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Natural, budget, work, CancellationToken.None) ?? throw new InvalidOperationException())
        {
            Span<int> counts = stackalloc int[2];
            for (int y = top; y < top + height; y++)
            {
                SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, y, counts, work, CancellationToken.None);
                for (int x = left; x < left + width; x++)
                {
                    int index = ((y - top) * width + x - left) * 3;
                    SvgGlyphRgbCoverage value = new(expected[index], expected[index + 1], expected[index + 2]);
                    Assert.Equal(value, SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, work, CancellationToken.None));
                    Assert.Equal(value, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, work, CancellationToken.None));
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Natural_lower_dropout_bound_keeps_the_native_rounded_row(bool excludeStubs)
    {
        // Original CC0 rectangles: UPEM 2048, em 32, origin (20,80),
        // SCANCTRL 511 / SCANTYPE 4 or 5. A distant upper contour prevents
        // the single-row fallback from hiding the native lower-bound rounding.
        OpenTypeQuadraticSegment[] segments = [.. Rectangle(20, 70.5625, 26.25, 70.75), .. Rectangle(35, 64, 36, 65)];
        ParserWorkCounter work = new(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, true, false, excludeStubs, SvgGlyphLcdMode.Natural, work, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        RenderBudget budget = new(long.MaxValue);
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Natural, budget, work, CancellationToken.None) ?? throw new InvalidOperationException())
        {
            Span<int> counts = stackalloc int[2];
            for (int y = 69; y <= 71; y++)
            {
                SvgGlyphRgbCoverage expected = y == 70 ? new(255, 255, 255) : default;
                Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(segments, 22, y, scratch, bounds, work, CancellationToken.None));
                Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(storage.PrepareRow(segments, y, counts, work, CancellationToken.None), 22, scratch, bounds, work, CancellationToken.None));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Natural_smart_second_pass_keeps_cells_already_selected_by_neighboring_first_pass_rows()
    {
        // Original CC0 thin quadrilateral: UPEM 1024, em 16, origin (20.25,80),
        // SCANCTRL 511 / SCANTYPE 4. Independent DirectWrite RGB is (43,43,43)
        // at (20,78) and (20,79), with empty surrounding pixels.
        OpenTypeOutlinePoint[] points = [P(0, 24), P(12, 152), P(20, 152), P(8, 24)];
        OpenTypeQuadraticSegment[] segments = Enumerable.Range(0, points.Length)
            .Select(index => new OpenTypeQuadraticSegment(points[index], points[(index + 1) % points.Length], points[(index + 1) % points.Length])).ToArray();
        ParserWorkCounter work = new(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, true, false, false, SvgGlyphLcdMode.Natural, work, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[8];
        RenderBudget budget = new(long.MaxValue);
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Natural, budget, work, CancellationToken.None) ?? throw new InvalidOperationException())
        {
            Span<int> counts = stackalloc int[2];
            for (int y = 77; y <= 80; y++)
            {
                SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, y, counts, work, CancellationToken.None);
                for (int x = 19; x <= 21; x++)
                {
                    SvgGlyphRgbCoverage expected = x == 20 && y is 78 or 79 ? new(43, 43, 43) : default;
                    Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, work, CancellationToken.None));
                    Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, work, CancellationToken.None));
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static OpenTypeOutlinePoint P(int x, int y) => new(20 + 1d / 3 + x / 64d, 80 - y / 64d);
    }

    [Theory]
    [InlineData(-1, false, 69)]
    [InlineData(0, false, 69)]
    [InlineData(1, false, 69)]
    [InlineData(-1, true, 70)]
    [InlineData(0, true, 70)]
    [InlineData(1, true, 69)]
    public void Natural_smart_midpoint_keeps_native_ties_and_exclusive_glyph_bounds(int offset, bool extendedBounds, int selectedRow)
    {
        // Independent original CC0 font: UPEM 2048, em 32, origin (20,80),
        // SCANCTRL 511 / SCANTYPE 5. A distant contour distinguishes the
        // midpoint decision from the native glyph-bound fallback.
        OpenTypeQuadraticSegment[] segments = Rectangle(20, 80 - (656 + offset) / 64d, 26.25, 70.25);
        if (extendedBounds) segments = [.. segments, .. Rectangle(35.625, 64.375, 37.1875, 80)];
        var work = new ParserWorkCounter(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, true, false, true, SvgGlyphLcdMode.Natural, work, CancellationToken.None);
        for (int row = 69; row <= 70; row++)
        {
            byte expected = row == selectedRow ? (byte)255 : (byte)0;
            Assert.Equal(new SvgGlyphRgbCoverage(expected, expected, expected), SvgGlyphLcdCoverage.GetCoverage(segments, 22, row, new SvgGlyphLcdCoverage.Crossing[segments.Length * 2], bounds, work, CancellationToken.None));
        }
    }

    [Theory]
    [InlineData(false, false, 19, 76, 8, 4, "AAAAAAAAAAAAAAAAAAAAAAAAACsrKwAAAAAAAAAAAAAAAAAAAABVqv///9WAKwAAAAAAAAAAACuA1f////+qVQAAAAAAAAAAAABVqv///9WAKwAAAAAAAAAAAAAAAAAA")]
    [InlineData(false, true, 19, 78, 7, 2, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv///////////////9WAKwAA")]
    [InlineData(true, false, 19, 76, 8, 4, "AAAAAAAAAAAAAAAAAAAAAAAACSgoHwAAAAAAAAAAAAAAAAAABRlDdajJyaFVHwAAAAAAAAAJHjxplr7Tyax1QxkFAAAAAAAAAABMktO+ooBTLQ4FAAAAAAAAAAAAAAAA")]
    [InlineData(true, true, 19, 78, 7, 2, "AAAAAAUOGBwcMkdcXFxRMxQAAAAAAABVqv/////////////64ZhIDAAA")]
    public void Native_profiles_keep_distinct_curved_and_terminating_contour_masks(bool symmetric, bool corner, int left, int top, int width, int height, string rgb)
    {
        // Original CC0 contours, independently rendered by DirectWrite
        // LCD at em 32 / UPEM 2048, origin (20,80), SCANCTRL 511 / SCANTYPE 5.
        // These are native masks, not pixels generated by the managed sampler.
        OpenTypeQuadraticSegment[] segments = corner
            ? [new(P(0, 0), P(0, 64), P(0, 64)), new(P(0, 64), P(256, 96), P(256, 96)), new(P(256, 96), P(304, 32), P(304, 32)), new(P(304, 32), P(256, 0), P(256, 0)), new(P(256, 0), P(0, 0), P(0, 0))]
            : [new(P(0, 0), P(0, 48), P(0, 48)), new(P(0, 48), P(208, 114), P(384, 224)), new(P(384, 224), P(384, 176), P(384, 176)), new(P(384, 176), P(208, 66), P(0, 0))];
        byte[] expected = Convert.FromBase64String(rgb);
        Assert.Equal(width * height * 3, expected.Length);
        var work = new ParserWorkCounter(long.MaxValue);
        RenderBudget budget = new(long.MaxValue);
        SvgGlyphLcdMode mode = symmetric ? SvgGlyphLcdMode.Symmetric : SvgGlyphLcdMode.Natural;
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, true, false, true, mode, work, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, mode, budget, work, CancellationToken.None) ?? throw new InvalidOperationException("The native control must fit."))
        {
            int[] counts = new int[symmetric ? 10 : 2];
            for (int y = top - 1; y <= top + height; y++)
            {
                SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, y, counts, work, CancellationToken.None);
                for (int x = left - 1; x <= left + width; x++)
                {
                    int index = ((y - top) * width + x - left) * 3;
                    SvgGlyphRgbCoverage pixel = x >= left && x < left + width && y >= top && y < top + height
                        ? new(expected[index], expected[index + 1], expected[index + 2]) : default;
                    SvgGlyphRgbCoverage actual = SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, work, CancellationToken.None);
                    Assert.True(pixel == actual, $"Native pixel ({x},{y}): expected {pixel}, actual {actual}.");
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, work, CancellationToken.None));
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, mode, new(1), work, CancellationToken.None)).ErrorCode);

        static OpenTypeOutlinePoint P(int x, int y) => new(20 + x / 64d, 80 - y / 64d);
    }

    [Fact]
    public void Natural_row_storage_releases_denied_work_and_preserves_cancellation()
    {
        OpenTypeQuadraticSegment[] segments = Rectangle(0, 0, 1, 1);
        RenderBudget budget = new(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Natural, budget, new(0), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Natural, budget, new(long.MaxValue), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
