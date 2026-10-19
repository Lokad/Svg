using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(322, 224, 48, 32, 2, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAK4DV////////qlUAAAAAAAAAAAAAAAAAAABVqtWAKwAAAABVqqpVAAAAAAAAAAAAAAAAAAAAACuA1f////+qVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(322, 224, 48, 32, 3, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq////////1YArAAAAAAAAAAAAAAAAAAArgNWqVQAAAAArgKqAKwAAAAAAAAAAAAAAAAAAAABVqv/////VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(257, 160, 48, 24, 2, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqqpVAAAAAAAAAAAAAAAAAAAAAAAAACuA1f+qVVWq/9WAKwAAAAAAAAAAAAAAAAAAAAAAK4DV///VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(257, 160, 48, 24, 3, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgKqAKwAAAAAAAAAAAAAAAAAAAAAAAABVqv/VgFWA1f+qVQAAAAAAAAAAAAAAAAAAAAAAAFWq////qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_turning_profiles_preserve_native_subdivision_carries(int width, int height, int insetX, int insetY, int phase, string rgb)
    {
        // Original CC0 turning rings: independent DirectWrite masks,
        // UPEM 2048, em 32, gasp 7, SCANCTRL 511 / SCANTYPE 4.
        int middle = width / 2;
        OpenTypeOutlinePoint[][] controls =
        [
            [P(0, height / 2), P(middle, height), P(width, height / 2), P(middle, 0)],
            [P(insetX, height / 2), P(middle, insetY), P(width - insetX, height / 2), P(middle, height - insetY)]
        ];
        List<OpenTypeQuadraticSegment> segments = [];
        foreach (OpenTypeOutlinePoint[] points in controls)
            for (int i = 0; i < points.Length; i++)
            {
                OpenTypeOutlinePoint previous = points[(i + points.Length - 1) % points.Length], current = points[i], next = points[(i + 1) % points.Length];
                segments.Add(new(new((previous.X + current.X) / 2, (previous.Y + current.Y) / 2), current, new((current.X + next.X) / 2, (current.Y + next.Y) / 2)));
            }
        AssertNaturalRows(segments.ToArray(), false, false, 19, 75, 9, 7, Convert.FromBase64String(rgb));

        OpenTypeOutlinePoint P(int x, int y) => new(20 + phase / 6d + x / 64d, 80 - y / 64d);
    }

    [Theory]
    [InlineData(323, 65, 97, 25, 0, 2, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq/6pVAAAAAAAAAAAAAAAAAAAAVar//////6pVAFWq////qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(323, 65, 97, 25, 0, 3, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACuA1dWAKwAAAAAAAAAAAAAAAAAAK4DV/////9WAKyuA1f//1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(323, 64, 97, 24, 16, 2, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX////VgCsAAAAAAAAAAAAAAAAAVar////VgCsAAAArgNX/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(323, 64, 97, 24, 16, 3, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAVar/////qlUAAAAAAAAAAAAAAAAAK4DV////qlUAAAAAVar/1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(254, 65, 98, 25, 3, 3, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAVarVgCsAAAAAAAAAAAAAAAAAAAAAAFWq////qlUrgNX/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_monotonic_profiles_preserve_native_half_unit_interpolation(int width, int firstHeight, int lastHeight, int gap, int shift, int phase, bool excludeStubs, string rgb)
    {
        // Independent original CC0 asymmetric bands, with the same native
        // recipe. Integer and half-unit implied endpoints retain both phases;
        // the even-width guard retains the on-grid profile despite half-unit Y.
        int firstControl = width / 3, lastControl = width - firstControl;
        double center = width / 2d;
        double middle = (firstHeight + lastHeight) / 2d + shift;
        OpenTypeQuadraticSegment[] segments =
        [
            new(P(0, shift), P(firstControl, firstHeight + shift), P(center, middle)),
            new(P(center, middle), P(lastControl, lastHeight + shift), P(width, shift)),
            new(P(width, shift), P(width, shift - gap), P(width, shift - gap)),
            new(P(width, shift - gap), P(lastControl, lastHeight + shift - gap), P(center, middle - gap)),
            new(P(center, middle - gap), P(firstControl, firstHeight + shift - gap), P(0, shift - gap)),
            new(P(0, shift - gap), P(0, shift), P(0, shift))
        ];
        AssertNaturalRows(segments, false, excludeStubs, 19, 75, 9, 7, Convert.FromBase64String(rgb));

        OpenTypeOutlinePoint P(double x, double y) => new(20 + phase / 6d + x / 64, 80 - y / 64);
    }
}
