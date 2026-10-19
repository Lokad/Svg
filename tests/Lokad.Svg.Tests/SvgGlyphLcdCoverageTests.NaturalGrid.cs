using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    private const string NaturalGrid254 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAVarVgCsAAAAAAAAAAAAAAAAAAAAAAAAAAFWq////qlUrgNX/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string NaturalGrid258 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAFWq///VgCsAVar/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string NaturalGrid254IncludeStubs = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAVarVgCsAAAAAAAAAAAAAAAAAAAAAAAAAK4DV////qlUrgNX/1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Theory]
    [InlineData(254, 0, 20.5, 80, false, true, NaturalGrid254)]
    [InlineData(254, 3, 20.5, 80, false, true, NaturalGrid254)]
    [InlineData(258, 4, 20.5, 80, false, true, NaturalGrid258)]
    [InlineData(254, 0, -19.5, 0, false, true, NaturalGrid254)]
    [InlineData(258, 4, -19.5, 0, false, true, NaturalGrid258)]
    [InlineData(254, 0, 20.5, 80, true, true, NaturalGrid254)]
    [InlineData(258, 4, 20.5, 80, true, true, NaturalGrid258)]
    [InlineData(254, 0, 20.5, 80, false, false, NaturalGrid254IncludeStubs)]
    public void Natural_smart_conics_preserve_source_grid_precision(int width, int shift, double originX,
        int originY, bool reverse, bool excludeStubs, string rgb)
    {
        // Original CC0 bands, independent DirectWrite RGB: UPEM 2048,
        // em 32, gasp 7, SCANCTRL 511 / SCANTYPE 4 or 5. Whole glyph-grid
        // X coordinates need rounded 26.6 smart points, independently of
        // ordinary fill and span/stub eligibility. The nearby phase guards
        // against applying the fractional-endpoint precision everywhere.
        OpenTypeQuadraticSegment[] segments = CreateNaturalConicBand(width, 98, shift, originX, originY, reverse);
        AssertNaturalRows(segments, false, excludeStubs, (int)Math.Floor(originX) - 2, originY - 4,
            10, 7, Convert.FromBase64String(rgb));
    }

    [Fact]
    public void Natural_integer_point_conics_keep_the_authored_profile()
    {
        // Original CC0 arc and flat mate, independent DirectWrite RGB:
        // UPEM 2048, em 32, gasp 7, SCANCTRL 511 / SCANTYPE 4. Every
        // source point is integral in 26.6; a separate tall contour retains
        // both candidate rows. Normalizing this profile moves four pixels.
        OpenTypeOutlinePoint a = P(0, 75), b = P(66, 75), c = P(66, 56), d = P(0, 39);
        OpenTypeQuadraticSegment[] segments = [new(a, b, b), new(b, c, c), new(c, P(33, 56), d),
            new(d, a, a), .. Rectangle(30.5, 76, 31.5, 80)];
        AssertNaturalRows(segments, false, false, 18, 76, 10, 7,
            Convert.FromBase64String("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArVVUrAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAK4CqgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

        static OpenTypeOutlinePoint P(int x, int y) => new(20.5 + x / 64d, 80 - y / 64d);
    }

    private static OpenTypeQuadraticSegment[] CreateNaturalConicBand(int width, int rightHeight, int shift,
        double originX, int originY, bool reverse)
    {
        int leftControl = width / 3, rightControl = width - leftControl;
        OpenTypeOutlinePoint a = P(0, shift), b = P(leftControl, 65 + shift);
        OpenTypeOutlinePoint c = P(rightControl, rightHeight + shift), d = P(width, shift);
        OpenTypeOutlinePoint e = P(width, shift - 25), f = P(rightControl, rightHeight + shift - 25);
        OpenTypeOutlinePoint g = P(leftControl, 65 + shift - 25), h = P(0, shift - 25);
        OpenTypeOutlinePoint upper = new((b.X + c.X) / 2, (b.Y + c.Y) / 2);
        OpenTypeOutlinePoint lower = new((f.X + g.X) / 2, (f.Y + g.Y) / 2);
        OpenTypeQuadraticSegment[] segments = [new(a, b, upper), new(upper, c, d), new(d, e, e),
            new(e, f, lower), new(lower, g, h), new(h, a, a)];
        if (reverse)
            segments = segments.Reverse().Select(segment => new OpenTypeQuadraticSegment(segment.End,
                segment.Control == segment.End ? segment.Start : segment.Control, segment.Start)).ToArray();
        return segments;

        OpenTypeOutlinePoint P(int x, int y) => new(originX + x / 64d, originY - y / 64d);
    }
}
