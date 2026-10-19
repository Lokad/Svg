using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(7, 20.5, 80, false, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX/qlUAAAAAAAAAAAAAAAAAAFWq///VgCsAVar/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(8, 20.5, 80, false, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv//qlUAAAAAAAAAAAAAAAAAAFWq//+qVQAAVar/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(9, 20.5, 80, false, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv//qlUAAAAAAAAAAAAAAAAAAFWq//+qVQAAVar/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(8, -19.5, 80, false, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv//qlUAAAAAAAAAAAAAAAAAAFWq//+qVQAAVar/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(8, 20.5, 0, false, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv//qlUAAAAAAAAAAAAAAAAAAFWq//+qVQAAVar/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(8, 20.5, 80, true, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv//qlUAAAAAAAAAAAAAAAAAAFWq//+qVQAAVar/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(8, 20.5, 80, false, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv//qlUAAAAAAAAAAAAAAAAAK4DV//+qVQAAVar/1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_dropout_profiles_retain_implied_half_ordinates(int shift, double originX, int originY, bool reverse, bool excludeStubs, string rgb)
    {
        // Original CC0 band, independently captured with DirectWrite:
        // UPEM 2048, em 32, gasp 7, SCANCTRL 511, SCANTYPE 4/5.
        // An implied half-unit Y distinguishes the dropout grid from fill;
        // adjacent phases, signed origins and both stub policies are retained.
        OpenTypeQuadraticSegment[] segments =
        [
            new(P(0, shift), P(84, 65 + shift), P(126.5, 81.5 + shift)),
            new(P(126.5, 81.5 + shift), P(169, 98 + shift), P(253, shift)),
            new(P(253, shift), P(253, shift - 25), P(253, shift - 25)),
            new(P(253, shift - 25), P(169, 73 + shift), P(126.5, 56.5 + shift)),
            new(P(126.5, 56.5 + shift), P(84, 40 + shift), P(0, shift - 25)),
            new(P(0, shift - 25), P(0, shift), P(0, shift))
        ];
        if (reverse)
            segments = segments.Reverse().Select(segment => segment.Control == segment.End
                ? new OpenTypeQuadraticSegment(segment.End, segment.Start, segment.Start)
                : new OpenTypeQuadraticSegment(segment.End, segment.Control, segment.Start)).ToArray();
        AssertNaturalRows(segments, false, excludeStubs, (int)Math.Floor(originX) - 1, originY - 4, 8, 6, Convert.FromBase64String(rgb));

        OpenTypeOutlinePoint P(double x, double y) => new(originX + x / 64, originY - y / 64);
    }

    [Theory]
    [InlineData(253, 13, 0, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq////////////qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(323, 13, 1, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq////////////////qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_line_span_ownership_retains_analytic_crossings(int width, int shift, int lowerSlopeDelta, string rgb)
    {
        // Independent original CC0 quadrilaterals with the same native recipe.
        // Rounding line crossings or outward midpoint limits loses cells that
        // the completed first pass retains, even though the band is narrow.
        OpenTypeOutlinePoint a = P(0, shift), b = P(width, 67 + shift);
        OpenTypeOutlinePoint c = P(width, 42 + shift + lowerSlopeDelta), d = P(0, shift - 25);
        OpenTypeQuadraticSegment[] segments = [new(a, b, b), new(b, c, c), new(c, d, d), new(d, a, a)];
        AssertNaturalRows(segments, false, true, 19, 76, 8, 6, Convert.FromBase64String(rgb));

        static OpenTypeOutlinePoint P(int x, int y) => new(20.5 + x / 64d, 80 - y / 64d);
    }
}
