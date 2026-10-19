using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(0, 20, 78, 6, 2, "AAAAAAArgNX/////qlUAAAAAK4DV///VgCsAAAAAVar/1YAr")]
    [InlineData(1, 20, 78, 8, 2, "AAAAVar////////////////VgCsAAAAAK4DVqlUAAAAAAAAAAAAAAAArgNX/1YAr")]
    public void Natural_integral_monotonic_conics_retain_parameter_precision(int shape,
        int left, int top, int width, int height, string rgb)
    {
        // Original CC0 outlines with explicit on-curve points: UPEM 16384,
        // em 32, origin (20.5,80), gasp 7, SCANCTRL 511 / SCANTYPE 4.
        // Frozen independent DirectWrite RGB protects the real-point encoding
        // separately from the existing implied-midpoint cases.
        OpenTypeQuadraticSegment[] segments = shape switch
        {
            0 =>
            [
                new(P(0, 16), P(107, 81), P(162, 97)),
                new(P(162, 97), P(216, 113), P(323, 16)),
                new(P(323, 16), P(323, -9), P(323, -9)),
                new(P(323, -9), P(216, 88), P(162, 72)),
                new(P(162, 72), P(107, 56), P(0, -9)),
                new(P(0, -9), P(0, 16), P(0, 16))
            ],
            1 =>
            [
                new(P(0, 48), P(149, 144), P(225, 129)),
                new(P(225, 129), P(300, 113), P(449, 48)),
                new(P(449, 48), P(449, 16), P(449, 16)),
                new(P(449, 16), P(300, 81), P(225, 97)),
                new(P(225, 97), P(149, 112), P(0, 16)),
                new(P(0, 16), P(0, 48), P(0, 48))
            ],
            _ => throw new InvalidOperationException()
        };
        AssertNaturalRows(segments, false, false, left, top, width, height, Convert.FromBase64String(rgb));

        static OpenTypeOutlinePoint P(int x, int y) => new(20.5 + x / 64d, 80 - y / 64d);
    }
}
