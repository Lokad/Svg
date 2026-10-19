using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(0, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAspSHSLbTYAAAAAAAAAAAAAAAUODgkAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(0, true, "AAAAAAAAAAAAAAAAAAAAAAAAAA8uTXSLbTYAAAAAAAAAAAAAAAUODgkAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(1, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAspSHSLbTYAAAAAAAAAAAAAAAAJCQkAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(1, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAspSHSLbTYAAAAAAAAAAAAAAAAJCQkAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Symmetric_stubs_preserve_full_scan_vertices_but_require_half_scan_center_coverage(int shift, bool enabled, string rgb)
    {
        // Original CC0 five-point flag, independent DirectWrite texture:
        // UPEM 2048, em 32, origin (20.25,80), gasp 15, SCANTYPE 2/5.
        // The adjacent phases distinguish a full-scan vertex from a half-scan
        // span that enters the gap without reaching the selected cell center.
        OpenTypeOutlinePoint[] points = [P(16 + shift, 200), P(32 + shift, 200), P(128 + shift, 180), P(128 + shift, 232), P(32 + shift, 212)];
        OpenTypeQuadraticSegment[] segments = new OpenTypeQuadraticSegment[9];
        for (int index = 0; index < points.Length; index++)
        {
            OpenTypeOutlinePoint next = points[(index + 1) % points.Length];
            segments[index] = new(points[index], next, next);
        }
        Rectangle(30 + 1d / 3, 64, 31 + 1d / 3, 80).CopyTo(segments, 5);
        byte[] expected = Convert.FromBase64String(rgb);
        Assert.Equal(5 * 4 * 3, expected.Length);
        ParserWorkCounter work = new(long.MaxValue);
        RenderBudget budget = new(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, enabled, false, enabled,
            SvgGlyphLcdMode.Symmetric, work, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Symmetric,
            budget, work, CancellationToken.None) ?? throw new InvalidOperationException("The native control must fit."))
        {
            int[] counts = new int[10];
            for (int y = 74; y <= 79; y++)
            {
                SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, y, counts, work, CancellationToken.None);
                for (int x = 18; x <= 24; x++)
                {
                    int index = ((y - 75) * 5 + x - 19) * 3;
                    SvgGlyphRgbCoverage pixel = x >= 19 && x < 24 && y >= 75 && y < 79
                        ? new(expected[index], expected[index + 1], expected[index + 2]) : default;
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, work, CancellationToken.None));
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, work, CancellationToken.None));
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static OpenTypeOutlinePoint P(int x, int y) => new(20 + 1d / 3 + x / 64d, 80 - y / 64d);
    }
}
