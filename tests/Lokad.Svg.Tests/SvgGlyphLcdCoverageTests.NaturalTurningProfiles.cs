using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(0, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv////+qVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq//+qVQAAAABVqv//1YArAAAAAAAAAAAAAAAAAAAAAAAAVar//9WAKwAAAAArgNX//6pVAAAAAAAAAAAAAAAAAAAAAAAAAAAAACuA1f/////VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(1, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv////+qVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq//+qVQAAAABVqv//1YArAAAAAAAAAAAAAAAAAAAAAAAAVar//9WAKwAAAAArgNX//6pVAAAAAAAAAAAAAAAAAAAAAAAAAAAAACuA1f/////VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_integral_turning_profiles_keep_scan_direction(int shape, string rgb)
    {
        // Original CC0 ring outlines; independent DirectWrite RGB and reversed
        // sixfold one-bit masks. UPEM 16384/2048, em 32, origin (20.5,80),
        // SCANCTRL 511 / SCANTYPE 4. Real endpoints and reversed implied
        // contours independently protect integral turning-profile direction.
        OpenTypeQuadraticSegment[] segments = shape switch
        {
            0 =>
            [
                new(P(112, 64), P(0, 128), P(112, 192)),
                new(P(112, 192), P(224, 256), P(337, 192)),
                new(P(337, 192), P(449, 128), P(337, 64)),
                new(P(337, 64), P(224, 0), P(112, 64)),
                new(P(152, 168), P(80, 128), P(152, 88)),
                new(P(152, 88), P(224, 48), P(297, 88)),
                new(P(297, 88), P(369, 128), P(297, 168)),
                new(P(297, 168), P(224, 208), P(152, 168))
            ],
            1 =>
            [
                new(P(152, 168), P(224, 208), P(296.5, 168)),
                new(P(296.5, 168), P(369, 128), P(296.5, 88)),
                new(P(296.5, 88), P(224, 48), P(152, 88)),
                new(P(152, 88), P(80, 128), P(152, 168)),
                new(P(112, 64), P(224, 0), P(336.5, 64)),
                new(P(336.5, 64), P(449, 128), P(336.5, 192)),
                new(P(336.5, 192), P(224, 256), P(112, 192)),
                new(P(112, 192), P(0, 128), P(112, 64))
            ],
            _ => throw new InvalidOperationException()
        };
        AssertNaturalRows(segments, false, false, 18, 74, 12, 8, Convert.FromBase64String(rgb));

        static OpenTypeOutlinePoint P(double x, double y) => new(20.5 + x / 64, 80 - y / 64);
    }
}
