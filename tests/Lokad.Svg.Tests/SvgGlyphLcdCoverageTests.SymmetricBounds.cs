using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(7, 20.5, 80, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACyA1NSALAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(58, 20.5, 80, "AAAAAAAAAAAAAAAAAAAABQ4YGA4FAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(32, 20.5, 80, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADCM7OyMMAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(7, -19.5, 0, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACyA1NSALAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(58, -19.5, 0, "AAAAAAAAAAAAAAAAAAAABQ4YGA4FAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Symmetric_thin_glyphs_preserve_the_native_rounded_height(int y, double originX, double originY, string rgb)
    {
        // Original CC0 instruction-free rectangle, independent DirectWrite
        // RGB: UPEM 2048, em 32, gasp 10, one font unit high. Y=7 and 58
        // collapse during bound rounding; Y=32 retains a normal interval.
        // Native captures at both signed origins retain the selected row.
        OpenTypeQuadraticSegment[] segments = Rectangle(originX, originY - (y + 1) / 64d, originX + 1, originY - y / 64d);
        AssertSymmetricNativeRows(segments, false, true, false, (int)Math.Floor(originX) - 1, (int)originY - 3, 4, 4, rgb);
    }

    private static void AssertSymmetricNativeRows(OpenTypeQuadraticSegment[] segments, bool smart, bool simple, bool excludeStubs,
        int left, int top, int width, int height, string rgb)
    {
        byte[] expected = Convert.FromBase64String(rgb);
        Assert.Equal(width * height * 3, expected.Length);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, smart, simple, excludeStubs,
            SvgGlyphLcdMode.Symmetric, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Symmetric,
            budget, CancellationToken.None) ?? throw new InvalidOperationException("The native control must fit."))
        {
            int[] counts = new int[10];
            for (int y = top - 1; y <= top + height; y++)
            {
                SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, y, counts, CancellationToken.None);
                for (int x = left - 1; x <= left + width; x++)
                {
                    int index = ((y - top) * width + x - left) * 3;
                    SvgGlyphRgbCoverage pixel = x >= left && x < left + width && y >= top && y < top + height
                        ? new(expected[index], expected[index + 1], expected[index + 2]) : default;
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, CancellationToken.None));
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, CancellationToken.None));
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
