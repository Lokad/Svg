using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData("ring", 2, 74, 12, 8, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACuA1f///9WAKwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAK4DV/9WAKwAAACuA1f//qlUAAAAAAAAAAAAAAAAAAAAAAAArgNX//6pVAAAAAABVqv//1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq//////+qVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ring", 3, 74, 12, 8, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv////+qVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq//+qVQAAAABVqv//1YArAAAAAAAAAAAAAAAAAAAAAAAAVar//9WAKwAAAAArgNX//6pVAAAAAAAAAAAAAAAAAAAAAAAAAAAAACuA1f/////VgCsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ring-turning-guard", 2, 75, 11, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv///////9WAKwAAAAAAAAAAAAAAAAAAAAAAAAArgNWqVQAAAAAAACuA1apVAAAAAAAAAAAAAAAAAAAAAAAAAABVqv///////9WAKwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ring-turning-guard", 3, 75, 11, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX///////+qVQAAAAAAAAAAAAAAAAAAAAAAAAAAVarVgCsAAAAAAABVqtWAKwAAAAAAAAAAAAAAAAAAAAAAAAArgNX///////+qVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-left", 2, 76, 10, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv/////VgCsAAAAAAAAAAAAAAAAAAAAAVar///+qVQAAAAArgNX/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-left", 3, 76, 10, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX/////qlUAAAAAAAAAAAAAAAAAAAAAK4DV///VgCsAAAAAVar/1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-right", 2, 75, 12, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX///////////////+qVQAAAAAAAAAAAAAAAAAAAAAAVarVgCsAAAAAAAAAAAAAAABVqv//qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-right", 3, 75, 12, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAVar////////////////VgCsAAAAAAAAAAAAAAAAAAAAAK4DVqlUAAAAAAAAAAAAAAAArgNX/1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-half-midpoint-guard", 2, 76, 10, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq/6pVAAAAAAAAAAAAAAAAAAAAAAAAVar//////6pVAFWq////qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-half-midpoint-guard", 3, 76, 10, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACuA1dWAKwAAAAAAAAAAAAAAAAAAAAAAK4DV/////9WAKyuA1f//1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-turning-guard", 2, 76, 12, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgKqAKwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACuA1f/////VgFWA1f///////6pVAAAAAAAAAAAAAAAAAAAAVYCAKwAAAAAAAAAAAAAAAAAAAFWqqlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-turning-guard", 3, 76, 12, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAVaqqVQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABVqv//////qlVVqv///////9WAKwAAAAAAAAAAAAAAAAAAK4CAVQAAAAAAAAAAAAAAAAAAACuAqoArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-turning-shift-guard", 2, 75, 12, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAArgNX/////////////////qlUAAAAAAAAAAAAAAAAAAAAAVarVgCsAAAAAAAAAAAAAAAAAVar/qlUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("band-turning-shift-guard", 3, 75, 12, 7, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAVar/////////////////1YArAAAAAAAAAAAAAAAAAAAAK4DVqlUAAAAAAAAAAAAAAAAAK4DV1YArAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Natural_smart_conics_keep_parameter_and_profile_rounding(string shape, int phase,
        int top, int width, int height, string rgb)
    {
        // Original CC0 outlines, independent DirectWrite RGB: UPEM 2048,
        // em 32, gasp 7, SCANCTRL 511 / SCANTYPE 4. Both retained strike
        // phases protect monotonic smart ordinates. Counter-arcs and the
        // nearby bands retain their distinct turning profiles.
        double origin = 20 + phase / 6d;
        OpenTypeQuadraticSegment[] segments = shape switch
        {
            "ring" => Ring(224, 449, 128, 128, 80, 369, 80),
            "ring-turning-guard" => Ring(193, 386, 96, 96, 64, 322, 64),
            "band-left" => Band(323, 65, 97, 16, 25),
            "band-right" => Band(449, 96, 65, 48, 32),
            "band-half-midpoint-guard" => Band(323, 64, 97, 0, 24),
            "band-turning-guard" => Band(449, 96, 65, 0, 32),
            "band-turning-shift-guard" => Band(449, 95, 65, 48, 25),
            _ => throw new InvalidOperationException()
        };
        AssertNaturalRows(segments, false, false, 18, top, width, height, Convert.FromBase64String(rgb));

        OpenTypeOutlinePoint P(double x, double y) => new(origin + x / 64, 80 - y / 64);

        OpenTypeQuadraticSegment[] Ring(int centerX, int rightX, int centerY, int outerRadius,
            int innerLeft, int innerRight, int innerRadius)
        {
            OpenTypeOutlinePoint[][] controls =
            [
                [P(0, centerY), P(centerX, centerY + outerRadius), P(rightX, centerY), P(centerX, centerY - outerRadius)],
                [P(innerLeft, centerY), P(centerX, centerY - innerRadius), P(innerRight, centerY), P(centerX, centerY + innerRadius)]
            ];
            List<OpenTypeQuadraticSegment> curves = [];
            foreach (OpenTypeOutlinePoint[] points in controls)
                for (int index = 0; index < points.Length; index++)
                {
                    OpenTypeOutlinePoint previous = points[(index + points.Length - 1) % points.Length],
                        current = points[index], next = points[(index + 1) % points.Length];
                    curves.Add(new(new((previous.X + current.X) / 2, (previous.Y + current.Y) / 2), current,
                        new((current.X + next.X) / 2, (current.Y + next.Y) / 2)));
                }
            return curves.ToArray();
        }

        OpenTypeQuadraticSegment[] Band(int bandWidth, int leftHeight, int rightHeight, int shift, int gap)
        {
            int leftControl = bandWidth / 3, rightControl = bandWidth - leftControl;
            OpenTypeOutlinePoint a = P(0, shift), b = P(leftControl, leftHeight + shift),
                c = P(rightControl, rightHeight + shift), d = P(bandWidth, shift),
                e = P(bandWidth, shift - gap), f = P(rightControl, rightHeight + shift - gap),
                g = P(leftControl, leftHeight + shift - gap), h = P(0, shift - gap);
            OpenTypeOutlinePoint upper = new((b.X + c.X) / 2, (b.Y + c.Y) / 2),
                lower = new((f.X + g.X) / 2, (f.Y + g.Y) / 2);
            return [new(a, b, upper), new(upper, c, d), new(d, e, e),
                new(e, f, lower), new(lower, g, h), new(h, a, a)];
        }
    }

    [Fact]
    public void Natural_fractional_flat_scan_tangents_keep_their_native_profile()
    {
        // Original CC0 counter-arc and distant bounds, independent native RGB:
        // UPEM 2048, em 32, origin (120,80), SCANCTRL 511 / SCANTYPE 4.
        // Its implied half-unit start and flat scan tangent need the existing
        // profile; broad parameter bracketing moves two pixels.
        OpenTypeOutlinePoint a = P(249, 2144), b = P(249, 2048), c = P(310, 2099), d = P(310, 2144),
            e = P(310, 2021), f = P(249, 2021), middle = new((b.X + c.X) / 2, (b.Y + c.Y) / 2);
        OpenTypeQuadraticSegment[] segments = [new(a, b, middle), new(middle, c, d),
            new(d, e, e), new(e, f, f), new(f, a, a), .. Rectangle(130, 76, 131, 80)];
        AssertNaturalRows(segments, false, false, 122, 45, 5, 6,
            Convert.FromBase64String("AAAAAAAAAAAAAAAAAAAAAAAAACsrKysrKwAAAAAAAAAAACsrgICAKwAAAAAAAAAAAABVVVUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

        static OpenTypeOutlinePoint P(int x, int y) => new(120 + x / 64d, 80 - y / 64d);
    }
}
