using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(70.296875, "dFFRUVxo")]
    [InlineData(70.28125, "dEcyJzxd")]
    public void Symmetric_conic_profiles_keep_native_flat_counter_terminals(double top, string rgb)
    {
        // Original CC0 box/counter, independently rendered by DirectWrite:
        // UPEM 2048, em 32, origin (20,80), SCANTYPE 2, symmetric LCD.
        // The first cap is one mapped F26 unit short of a center. Reducing its
        // coefficients moves the terminal beyond that scan; the adjacent
        // control remains an ordinary counter intersection. Dropouts are off.
        OpenTypeOutlinePoint a = new(23, top), b = new(21, 71.25), c = new(26, 74);
        OpenTypeQuadraticSegment[] segments = [.. Rectangle(20, 69, 28, 76), new(a, new(21.75, top), b), new(b, c, c), new(c, new(25.75, top), a)];
        byte[] expected = Convert.FromBase64String(rgb);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, false, false, false, SvgGlyphLcdMode.Symmetric, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Symmetric, budget, CancellationToken.None) ?? throw new InvalidOperationException())
        {
            Span<int> counts = stackalloc int[10];
            SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, 70, counts, CancellationToken.None);
            for (int x = 22; x <= 23; x++)
            {
                int offset = (x - 22) * 3;
                SvgGlyphRgbCoverage value = new(expected[offset], expected[offset + 1], expected[offset + 2]);
                Assert.Equal(value, SvgGlyphLcdCoverage.GetCoverage(segments, x, 70, scratch, bounds, CancellationToken.None));
                Assert.Equal(value, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, CancellationToken.None));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
