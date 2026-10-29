using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(-1, "ACuA1dWAKwAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(0, "ACuA1dWAKwAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(1, "AABVqtWAKwAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_stubs_keep_exact_neighbor_vertices(int offset, string rgb)
    {
        // Original CC0 triangle, independently rendered by DirectWrite at
        // em 32 / UPEM 2048, origin (20,80), SCANCTRL 511 / SCANTYPE 5.
        // The middle case touches the neighboring full scan at a single vertex.
        OpenTypeOutlinePoint tip = P(48 + offset, 259), upper = P(112, 300), lower = P(112, 256);
        OpenTypeQuadraticSegment[] segments = [new(tip, upper, upper), new(upper, lower, lower), new(lower, tip, tip),
            .. Rectangle(30, 64, 31, 80)];

        AssertNaturalNativeMask(segments, 20, 75, 4, 2, rgb);

        static OpenTypeOutlinePoint P(int x, int y) => new(20 + x / 64d, 80 - y / 64d);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    public void Natural_stubs_exclude_half_scan_caps(int thickness)
    {
        // Original CC0 rectangles, independently rendered by DirectWrite with
        // the same program at origin (20.75,80). Its native six-column strike
        // maps the X phase to 5/6. Distant bounds prevent exclusive-bound fallback.
        const double originX = 20 + 5d / 6;
        OpenTypeQuadraticSegment[] segments = [.. Rectangle(originX + 51d / 64, 80 - (40 + thickness) / 64d,
            originX + 179d / 64, 80 - 40d / 64), .. Rectangle(originX + 10, 64, originX + 11, 80)];

        AssertNaturalNativeMask(segments, 21, 79, 5, 1, "ACuA1f//1YArAAAAAAAA");
    }

    private static void AssertNaturalNativeMask(OpenTypeQuadraticSegment[] segments, int left, int top, int width, int height, string rgb)
    {
        byte[] expected = Convert.FromBase64String(rgb);
        Assert.Equal(width * height * 3, expected.Length);
        RenderBudget budget = new(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, true, false, true,
            SvgGlyphLcdMode.Natural, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Natural,
            budget, CancellationToken.None) ?? throw new InvalidOperationException("The native control must fit."))
        {
            int[] counts = new int[2];
            for (int y = top - 1; y <= top + height; y++)
            {
                SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, y, counts, CancellationToken.None);
                for (int x = left - 1; x <= left + width; x++)
                {
                    int index = ((y - top) * width + x - left) * 3;
                    SvgGlyphRgbCoverage pixel = x >= left && x < left + width && y >= top && y < top + height
                        ? new(expected[index], expected[index + 1], expected[index + 2]) : default;
                    SvgGlyphRgbCoverage actual = SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, CancellationToken.None);
                    Assert.True(pixel == actual, $"Native pixel ({x},{y}): expected {pixel}, actual {actual}.");
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, CancellationToken.None));
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
