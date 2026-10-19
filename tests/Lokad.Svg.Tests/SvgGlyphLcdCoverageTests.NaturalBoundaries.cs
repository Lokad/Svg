using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(0, 76, 10, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX/////qlUAAAAAAAAAAAAAAAAAAAAAK4DV///VgCsAAAAAVar/1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(1, 75, 12, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAVar////////////////VgCsAAAAAAAAAAAAAAAAAAAAAK4DVqlUAAAAAAAAAAAAAAAArgNX/1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_smart_boundaries_preserve_reversed_contours(int shape,
        int top, int width, int height, string rgb)
    {
        // Original CC0 implied-point bands; independent reversed DirectWrite
        // masks reintegrate to the same RGB as their forward references.
        // UPEM 2048, em 32, origin (20.5,80), SCANCTRL 511 / SCANTYPE 4.
        OpenTypeQuadraticSegment[] segments = shape switch
        {
            0 =>
            [
                new(P(0, 16), P(107, 81), P(161.5, 97)),
                new(P(161.5, 97), P(216, 113), P(323, 16)),
                new(P(323, 16), P(323, -9), P(323, -9)),
                new(P(323, -9), P(216, 88), P(161.5, 72)),
                new(P(161.5, 72), P(107, 56), P(0, -9)),
                new(P(0, -9), P(0, 16), P(0, 16))
            ],
            1 =>
            [
                new(P(0, 48), P(149, 144), P(224.5, 128.5)),
                new(P(224.5, 128.5), P(300, 113), P(449, 48)),
                new(P(449, 48), P(449, 16), P(449, 16)),
                new(P(449, 16), P(300, 81), P(224.5, 96.5)),
                new(P(224.5, 96.5), P(149, 112), P(0, 16)),
                new(P(0, 16), P(0, 48), P(0, 48))
            ],
            _ => throw new InvalidOperationException()
        };
        Array.Reverse(segments);
        for (int index = 0; index < segments.Length; index++)
        {
            OpenTypeQuadraticSegment segment = segments[index];
            segments[index] = new(segment.End, segment.Control, segment.Start);
        }
        AssertNaturalRows(segments, false, false, 18, top, width, height, Convert.FromBase64String(rgb));

        static OpenTypeOutlinePoint P(double x, double y) => new(20.5 + x / 64, 80 - y / 64);
    }
}
