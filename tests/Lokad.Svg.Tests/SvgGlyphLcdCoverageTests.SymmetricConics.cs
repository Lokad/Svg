using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData("wedge", 20.5, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAABUyX05hNDAAAAAAAAAAAAAVVZGAPAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("wedge-down", 20.5, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAABUyX05hNDAAAAAAAAAAAAAVVZGAPAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("wedge-reverse", 20.5, 80, true, "AAAAAAAAAAAAAAAAAAAAAAAABUyX05hNDAAAAAAAAAAAAAVVZGAPAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("wedge", 20.5, 80, false, "AAAAAAAAAAAAAAAAAAAAAAAAAEeS04xBAAAAAAAAAAAAAAVQYFsPAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("cap", 20.25, 80, true, "AAAAAAAAAAAAAAAAAAAfKSkLAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("cap-narrow", 20.25, 80, true, "AAAAAAAAAAAAAAAAAAAfHx8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("cap-wide", 20.25, 80, true, "AAAAAAAAAAAAAAAAAAAfLi4PAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("cap-up", 20.25, 80, true, "AAAAAAAAAAAAAAAAAAAfIyMFAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("cap-down", 20.25, 80, true, "AAAAAAAAAAAAAAAAAAAfKSkLAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("cap-reverse", 20.25, 80, true, "AAAAAAAAAAAAAAAAAAAfKSkLAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("cap", 20.25, 80, false, "AAAAAAAAAAAAAAAAAAAfHx8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("wedge", 20.5, 0, true, "AAAAAAAAAAAAAAAAAAAAAAAABUyX05hNDAAAAAAAAAAAAAVVZGAPAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("cap", -19.75, 80, true, "AAAAAAAAAAAAAAAAAAAfKSkLAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Symmetric_smart_conic_profiles_match_native_masks(string shape, double originX, int originY, bool enabled, string rgb)
    {
        // Original CC0 quadratic wedge and implied-point cap, independently
        // captured with DirectWrite: UPEM 2048, em 32, gasp 15, SCANCTRL 511,
        // SCANTYPE 2 or 4. The distant rectangle establishes glyph bounds.
        (bool cap, int widthUnits, int shift, bool reverse) = shape switch
        {
            "wedge" => (false, 4, 0, false),
            "wedge-down" => (false, 4, -1, false),
            "wedge-reverse" => (false, 4, 0, true),
            "cap" => (true, 4, 0, false),
            "cap-narrow" => (true, 3, 0, false),
            "cap-wide" => (true, 5, 0, false),
            "cap-up" => (true, 4, 1, false),
            "cap-down" => (true, 4, -1, false),
            "cap-reverse" => (true, 4, 0, true),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        double phaseX = Math.Floor(originX * 6 + .5) / 6;
        List<OpenTypeQuadraticSegment> outline = [];
        if (cap)
        {
            const int center = 121;
            int leftSide = center - widthUnits - 9, rightSide = 130 + widthUnits, y = 179 + shift;
            OpenTypeOutlinePoint p = P(center, y + 13, phaseX, originY);
            foreach ((int x1, int y1, int x2, int y2, int x3, int y3) in new[]
            {
                (126, y + 13, rightSide, y + 7, rightSide, y),
                (rightSide, y - 6, 126, y - 13, center, y - 13),
                (116, y - 13, leftSide, y - 6, leftSide, y),
                (leftSide, y + 7, 116, y + 13, center, y + 13)
            })
            {
                OpenTypeOutlinePoint q = P(x1, y1, phaseX, originY), r = P(x2, y2, phaseX, originY), end = P(x3, y3, phaseX, originY);
                OpenTypeOutlinePoint implied = new((q.X + r.X) * .5, (q.Y + r.Y) * .5);
                outline.Add(new(p, q, implied)); outline.Add(new(implied, r, end)); p = end;
            }
        }
        else
        {
            OpenTypeOutlinePoint a = P(514, 762, phaseX, originY), b = P(556, 756 + shift, phaseX, originY);
            OpenTypeOutlinePoint c = P(594, 745, phaseX, originY), p = P(555, 636, phaseX, originY);
            outline.Add(new(a, b, c)); outline.Add(new(c, p, p)); outline.Add(new(p, a, a));
        }
        if (reverse)
        {
            outline.Reverse();
            for (int index = 0; index < outline.Count; index++)
            {
                OpenTypeQuadraticSegment s = outline[index];
                outline[index] = new(s.End, s.Control == s.End ? s.Start : s.Control, s.Start);
            }
        }
        outline.AddRange(Rectangle(phaseX + 20, originY - 16, phaseX + 21, originY));
        OpenTypeQuadraticSegment[] segments = outline.ToArray();
        int left = (int)Math.Floor(originX) + (cap ? 1 : 7), top = originY - (cap ? 4 : 13);
        int width = cap ? 4 : 5, height = cap ? 4 : 5;
        byte[] expected = Convert.FromBase64String(rgb);
        Assert.Equal(width * height * 3, expected.Length);
        ParserWorkCounter work = new(long.MaxValue);
        RenderBudget budget = new(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, enabled, false, false, SvgGlyphLcdMode.Symmetric, work, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Symmetric,
            budget, work, CancellationToken.None) ?? throw new InvalidOperationException("The native control must fit."))
        {
            int[] counts = new int[10];
            for (int y = top - 1; y <= top + height; y++)
            {
                SvgGlyphLcdCoverage.ScanRow row = storage.PrepareRow(segments, y, counts, work, CancellationToken.None);
                for (int x = left - 1; x <= left + width; x++)
                {
                    int index = ((y - top) * width + x - left) * 3;
                    SvgGlyphRgbCoverage pixel = x >= left && x < left + width && y >= top && y < top + height
                        ? new(expected[index], expected[index + 1], expected[index + 2]) : default;
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, work, CancellationToken.None));
                    Assert.Equal(pixel, SvgGlyphLcdCoverage.GetCoverage(row, x, scratch, bounds, work, CancellationToken.None));
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static OpenTypeOutlinePoint P(double x, double y, double ox, int oy) => new(ox + x / 64, oy - y / 64);
    }
}
