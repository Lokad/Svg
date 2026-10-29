using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    private const string TurningFill254 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgKqAKwAAK4CAVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string TurningFill254Shifted = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgIBVAAAAAFVVVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string TurningFill258 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgIBVAAAAAFWAgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string TurningFill323 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqqpVAAAAAAAAAAAAAAAAAAAAAAAAAABVqqpVAAAAAAAAACuAgFUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Theory]
    [InlineData(254, 98, 0, 20.5, 80, false, TurningFill254)]
    [InlineData(254, 98, 3, 20.5, 80, false, TurningFill254Shifted)]
    [InlineData(258, 98, 4, 20.5, 80, false, TurningFill258)]
    [InlineData(323, 97, 16, 20.5, 80, false, TurningFill323)]
    [InlineData(254, 98, 0, -19.5, 0, false, TurningFill254)]
    [InlineData(254, 98, 0, 20.5, 80, true, TurningFill254)]
    [InlineData(323, 97, 16, -19.5, 0, false, TurningFill323)]
    [InlineData(323, 97, 16, 20.5, 80, true, TurningFill323)]
    public void Natural_turning_curves_preserve_native_fill_without_dropouts(int width, int rightHeight, int shift,
        double originX, int originY, bool reverse, string rgb)
    {
        // Original CC0 quadratic bands, independent DirectWrite RGB:
        // UPEM 2048, em 32, gasp 7, SCANCTRL 511 / SCANTYPE 2. The
        // width-254 witness isolates ordinary fill from dropout selection.
        // Nearby geometry, signed placement and contour reversal are native
        // captures too; band 323 protects the control-midpoint rounding.
        OpenTypeQuadraticSegment[] segments = CreateNaturalConicBand(width, rightHeight, shift, originX, originY, reverse);

        byte[] expected = Convert.FromBase64String(rgb);
        const int columns = 10, rows = 7;
        Assert.Equal(columns * rows * 3, expected.Length);
        int left = (int)Math.Floor(originX) - 2, top = originY - 4;
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, false, false, false,
            SvgGlyphLcdMode.Natural, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments,
            SvgGlyphLcdMode.Natural, budget, CancellationToken.None) ?? throw new InvalidOperationException())
        {
            Span<int> counts = stackalloc int[2];
            for (int y = top - 1; y <= top + rows; y++)
            {
                SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, y, counts, CancellationToken.None);
                for (int x = left - 1; x <= left + columns; x++)
                {
                    int index = ((y - top) * columns + x - left) * 3;
                    SvgGlyphRgbCoverage pixel = x >= left && x < left + columns && y >= top && y < top + rows
                        ? new(expected[index], expected[index + 1], expected[index + 2]) : default;
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, CancellationToken.None));
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, CancellationToken.None));
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

    }
}
