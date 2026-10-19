using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData("near-below", false, 20, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFD9DLwUAAAAAAAAAAAAAAAAFGU+WlmAUAAAAAAAAAAAAAAAAAAAAFDZGMg8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("round-down", false, 20, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACBKSisAAAAAAAAAAAAAAAAgcMXQgCsAAAAAAAAAAAAFJXXF+v/VgCsAAAAAAAAAAAAAIHDF+v/VgCsAAAAAAAAAAAAAAAAUZLnQgCsAAAAAAAAAAAAAAAAAABQ6OiYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("round-up", false, 20, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABQ6OiYAAAAAAAAAAAAAAAAUZLnQgCsAAAAAAAAAAAAAIHDF+v/VgCsAAAAAAAAAAAAFJXXF+v/VgCsAAAAAAAAAAAAAAAAgcMXQgCsAAAAAAAAAAAAAAAAAACBKSisAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("negative-half", false, 20, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFGysmDwAAAAAAAAAAAAAAAAAAAAUbKyYPAAAAAAAAAAAAAAAAAAAABQ8PCwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("exact", false, 20, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFD9DLwUAAAAAAAAAAAAAAAAFGU+WlmAUAAAAAAAAAAAAAAAAAAAAFEFGMgUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("near-above", true, 20, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPJisbBQAAAAAAAAAAAAAAAAAAAAAAAAAAAAUbKyYPAAAAAAAAAAAAAAAAAAAAAAAAAAAAABsbGwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("round-up", true, 20, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAALLGOkz+Pj4+Pj4+Pj4+PZr3MwDwAAAAAAAAAAFD+P0Pr///rcm08ZBQAAAAAAAAAAAAAAAAAABSNOTi8FAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("round-down", true, 20, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPMHOv2ePj4+Pj4+Pj4+PPpGMsCwAAAAAAAAAFGU+b3Pr///rQjz8UAAAAAAAAAAAAAAAAAAAABS9OTiMFAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("near-below", false, 20, 0, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFD9DLwUAAAAAAAAAAAAAAAAFGU+WlmAUAAAAAAAAAAAAAAAAAAAAFDZGMg8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("negative-half", false, 20, 0, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFGysmDwAAAAAAAAAAAAAAAAAAAAUbKyYPAAAAAAAAAAAAAAAAAAAABQ8PCwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("near-above", true, -20, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPJisbBQAAAAAAAAAAAAAAAAAAAAAAAAAAAAUbKyYPAAAAAAAAAAAAAAAAAAAAAAAAAAAAABsbGwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("near-below", false, 20, 80, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFD9DLwUAAAAAAAAAAAAAAAAAFEqWlmAUAAAAAAAAAAAAAAAAAAAAFDY2IgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("near-above", true, 20, 80, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMGxsPAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA8PDwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Symmetric_smart_midpoints_use_rounded_profiles(string shape, bool primary, int originX, int originY, bool enabled, string rgb)
    {
        // Original CC0 triangles captured independently with DirectWrite:
        // UPEM 2048, em 32, gasp 15, SCANCTRL 511, SCANTYPE 2 or 4.
        // Full primitive windows distinguish profile rounding from a midpoint
        // bias on both axes, including signed positions and half-unit ties.
        (int adx, int ady, int bdx, int bdy) = (primary, shape) switch
        {
            (false, "near-below") => (125, 124, 38, 38),
            (false, "round-down") => (125, 184, 125, 188),
            (false, "round-up") => (125, 188, 125, 184),
            (false, "negative-half") => (100, 149, 2, 3),
            (false, "exact") => (125, 125, 38, 38),
            (true, "near-above") => (124, 150, 5, 6),
            (true, "round-up") => (184, 150, 188, 150),
            (true, "round-down") => (188, 150, 184, 150),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        OpenTypeOutlinePoint[] points = primary
            ? [P(-adx, 671 + ady, originX, originY), P(bdx, 671 + bdy, originX, originY), P(0, 671, originX, originY)]
            : [P(15 + adx, 640 - ady, originX, originY), P(15, 640, originX, originY), P(15 + bdx, 640 + bdy, originX, originY)];
        OpenTypeQuadraticSegment[] segments = new OpenTypeQuadraticSegment[7];
        for (int index = 0; index < points.Length; index++)
        {
            OpenTypeOutlinePoint next = points[(index + 1) % points.Length];
            segments[index] = new(points[index], next, next);
        }
        Rectangle(originX + 20, originY - 16, originX + 21, originY).CopyTo(segments, 3);
        int left = originX - (primary ? 4 : 1), top = originY - 14;
        int width = primary ? 8 : 6, height = primary ? 5 : 8;
        byte[] expected = Convert.FromBase64String(rgb);
        Assert.Equal(width * height * 3, expected.Length);
        ParserWorkCounter work = new(long.MaxValue);
        RenderBudget budget = new(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, enabled, false, false,
            SvgGlyphLcdMode.Symmetric, work, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Symmetric,
            budget, work, CancellationToken.None) ?? throw new InvalidOperationException("The native control must fit."))
        {
            int[] counts = new int[10];
            for (int y = top - 1; y <= top + height; y++)
            {
                SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, y, counts, work, CancellationToken.None);
                for (int x = left - 1; x <= left + width; x++)
                {
                    int index = ((y - top) * width + x - left) * 3;
                    SvgGlyphRgbCoverage pixel = x >= left && x < left + width && y >= top && y < top + height
                        ? new(expected[index], expected[index + 1], expected[index + 2]) : default;
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, work, CancellationToken.None));
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, work, CancellationToken.None));
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static OpenTypeOutlinePoint P(int x, int y, int dx, int dy) => new(dx + x / 64d, dy - y / 64d);
    }
}
