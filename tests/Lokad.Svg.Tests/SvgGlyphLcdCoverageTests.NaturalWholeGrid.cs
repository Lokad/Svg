using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(0, 18, 74, 12, 8, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv////+qVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq//+qVQAAAABVqv//1YArAAAAAAAAAAAAAAAAAAAAAAAAVar//9WAKwAAAAArgNX//6pVAAAAAAAAAAAAAAAAAAAAAAAAAAAAACuA1f/////VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(1, 18, 76, 10, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKysrAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAK4DVqlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(2, 18, 76, 10, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKysrAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAK4DVqlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(3, 18, 76, 10, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArVVUrAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAK4CqgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(4, 18, 76, 10, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKysrAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAK4DVqlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_whole_grid_profiles_keep_split_precision_and_flat_minima(int shape,
        int left, int top, int width, int height, string rgb)
    {
        // Original CC0 outlines; independent DirectWrite RGB and sixfold
        // one-bit masks. UPEM 16384/2048, em 32, origin (20.5,80),
        // SCANCTRL 511 / SCANTYPE 4. Explicit split points, both contour
        // directions and the adjacent flat mate protect distinct rounding.
        OpenTypeQuadraticSegment[] segments = shape switch
        {
            0 =>
            [
                new(P(112, 64), P(56, 96), P(56, 128)),
                new(P(56, 128), P(56, 160), P(112, 192)),
                new(P(112, 192), P(168, 224), P(224, 224)),
                new(P(224, 224), P(280, 224), P(337, 192)),
                new(P(337, 192), P(393, 160), P(393, 128)),
                new(P(393, 128), P(393, 96), P(337, 64)),
                new(P(337, 64), P(280, 32), P(224, 32)),
                new(P(224, 32), P(168, 32), P(112, 64)),
                new(P(152, 168), P(116, 148), P(116, 128)),
                new(P(116, 128), P(116, 108), P(152, 88)),
                new(P(152, 88), P(188, 68), P(224, 68)),
                new(P(224, 68), P(260, 68), P(297, 88)),
                new(P(297, 88), P(333, 108), P(333, 128)),
                new(P(333, 128), P(333, 148), P(297, 168)),
                new(P(297, 168), P(260, 188), P(224, 188)),
                new(P(224, 188), P(188, 188), P(152, 168))
            ],
            1 =>
            [
                new(P(0, 73), P(66, 73), P(66, 73)),
                new(P(66, 73), P(66, 56), P(66, 56)),
                new(P(66, 56), P(33, 56), P(0, 39)),
                new(P(0, 39), P(0, 73), P(0, 73)),
                new(P(640, 0), P(640, 256), P(640, 256)),
                new(P(640, 256), P(704, 256), P(704, 256)),
                new(P(704, 256), P(704, 0), P(704, 0)),
                new(P(704, 0), P(640, 0), P(640, 0))
            ],
            2 =>
            [
                new(P(640, 0), P(704, 0), P(704, 0)),
                new(P(704, 0), P(704, 256), P(704, 256)),
                new(P(704, 256), P(640, 256), P(640, 256)),
                new(P(640, 256), P(640, 0), P(640, 0)),
                new(P(0, 73), P(0, 39), P(0, 39)),
                new(P(0, 39), P(33, 56), P(66, 56)),
                new(P(66, 56), P(66, 73), P(66, 73)),
                new(P(66, 73), P(0, 73), P(0, 73))
            ],
            3 =>
            [
                new(P(640, 0), P(704, 0), P(704, 0)),
                new(P(704, 0), P(704, 256), P(704, 256)),
                new(P(704, 256), P(640, 256), P(640, 256)),
                new(P(640, 256), P(640, 0), P(640, 0)),
                new(P(0, 75), P(0, 39), P(0, 39)),
                new(P(0, 39), P(33, 56), P(66, 56)),
                new(P(66, 56), P(66, 75), P(66, 75)),
                new(P(66, 75), P(0, 75), P(0, 75))
            ],
            4 =>
            [
                new(P(0, 74), P(66, 74), P(66, 74)),
                new(P(66, 74), P(66, 56), P(66, 56)),
                new(P(66, 56), P(33, 56), P(0, 39)),
                new(P(0, 39), P(0, 74), P(0, 74)),
                new(P(640, 0), P(640, 256), P(640, 256)),
                new(P(640, 256), P(704, 256), P(704, 256)),
                new(P(704, 256), P(704, 0), P(704, 0)),
                new(P(704, 0), P(640, 0), P(640, 0))
            ],
            _ => throw new InvalidOperationException()
        };
        AssertNaturalRows(segments, false, false, left, top, width, height, Convert.FromBase64String(rgb));

        static OpenTypeOutlinePoint P(double x, double y) => new(20.5 + x / 64, 80 - y / 64);
    }
}
