using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(30, 20.5, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX/1YArAAAAAAAAAAAAAFWq///////VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(47, 20.5, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX/////1YArAAAAAAAAAAAAK4DV///VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(30, 20.5, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX/1YArAAAAAAAAAAAAAFWq///////VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(30, -19.5, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX/1YArAAAAAAAAAAAAAFWq///////VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_line_midpoints_preserve_native_interpolation_carries(int shift, double originX, bool reverse, string rgb)
    {
        // Original CC0 nonparallel band: independently captured DirectWrite
        // RGB, UPEM 2048, em 32, gasp 7, SCANCTRL 511 / SCANTYPE 5.
        // The two slopes distinguish the seeded remainder from analytical
        // or independently rounded intersections, including reversed scans.
        OpenTypeOutlinePoint a = P(0, shift), b = P(253, 67 + shift);
        OpenTypeOutlinePoint c = P(253, 43 + shift), d = P(0, shift - 25);
        OpenTypeQuadraticSegment[] segments = [new(a, b, b), new(b, c, c), new(c, d, d), new(d, a, a)];
        if (reverse)
            segments = segments.Reverse().Select(segment => new OpenTypeQuadraticSegment(segment.End, segment.Start, segment.Start)).ToArray();
        AssertNaturalRows(segments, false, true, (int)Math.Floor(originX) - 1, 76, 8, 6, Convert.FromBase64String(rgb));

        OpenTypeOutlinePoint P(int x, int y) => new(originX + x / 64d, 80 - y / 64d);
    }
}
