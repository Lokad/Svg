using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData("left", -1, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAHz1HMhwcGA4FAAAAAAAAAAAme8vmvIBDGQUAAAAAAAAAAAAJCQkAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("left", 0, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAHz1HMhwcGA4FAAAAAAAAAAAme8vmvIBDGQUAAAAAAAAAAAAPFBQFAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("left", 1, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAHz1HMhwcGA4FAAAAAAAAAAAbcMDmvIBDGQUAAAAAAAAAAAAPFBQFAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("upper", -1, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAkTHBgOBQAAAAAAAAAAAAAbUZLD2OPn8fr65rBpKAkAAAAAAAAAAAAAAABQpfr/+ua8gEMZBQAAAAAAAAAAAAAAAAAAACtrloBDGQUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("upper", 0, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAkTHBgOBQAAAAAAAAAAAAAbUZLD2OPn8fr65rx1NAkAAAAAAAAAAAAAAABQpfr/+ua8gEMZBQAAAAAAAAAAAAAAAAAAACtrloBDGQUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("upper", 1, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAkTHBgOBQAAAAAAAAAAAAAbUZLD2OPj7Pb65ryAQxkFAAAAAAAAAAAAAABGm/D/+ua8gEMZBQAAAAAAAAAAAAAAAAAAACZnkYBDGQUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("bar", 0, true, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAALIDVAQEBANSALAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("vertical-long", 0, true, "AAAAAAAAAAAABQ4YHhQLAAAAAAAAJnG95+zhvHU0CQAAAAAAAAAAJmeRdTQJAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("vertical-short", 0, true, "AAAAAAAAAAAABQUFAAAAAAAAAAAmcb3j59KbTxkFAAAAAAAAAAAmZ4VkIwUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("left", 0, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAHz1HMhwcGA4FAAAAAAAAAAAme8vmvIBDGQUAAAAAAAAAAAAFCQkFAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("upper", 0, false, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAkTHBMJAAAAAAAAAAAAAAAbUZLD2OPn8fr65rx1NAkAAAAAAAAAAAAAAABQpfr/+ua8gEMZBQAAAAAAAAAAAAAAAAAAACZnkYBDGQUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Symmetric_smart_corner_continuation_matches_native_limits(string shape, int offset, bool enabled, string rgb)
    {
        // Original CC0 triangles/bar, independently captured with DirectWrite:
        // UPEM 2048, em 32, origin (20,80), SCANCTRL 511, SCANTYPE 2 or 5.
        // Whole primitive windows include the neighboring phase and upright
        // counterexamples; the separate rectangle only establishes glyph bounds.
        OpenTypeOutlinePoint[] points = shape switch
        {
            "left" => [P(169, 714, offset), P(3, 620, offset), P(23, 727, offset)],
            "upper" => [P(385, 850, offset), P(47, 640, offset), P(80, 808, offset)],
            "bar" => [P(3, 621, offset), P(3, 632, offset), P(169, 632, offset), P(169, 621, offset)],
            "vertical-long" => [P(226, 788, offset), P(27, 640, offset), P(27, 747, offset)],
            "vertical-short" => [P(176, 776, offset), P(7, 640, offset), P(7, 750, offset)],
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        OpenTypeQuadraticSegment[] segments = new OpenTypeQuadraticSegment[points.Length + 4];
        for (int index = 0; index < points.Length; index++)
        {
            OpenTypeOutlinePoint next = points[(index + 1) % points.Length];
            segments[index] = new(points[index], next, next);
        }
        Rectangle(30, 64, 31, 80).CopyTo(segments, points.Length);
        int width = shape == "upper" ? 9 : 6, height = shape == "upper" ? 7 : 5, top = shape == "upper" ? 65 : 67;
        const int left = 19;
        byte[] expected = Convert.FromBase64String(rgb);
        Assert.Equal(width * height * 3, expected.Length);
        ParserWorkCounter work = new(long.MaxValue);
        RenderBudget budget = new(long.MaxValue);
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, enabled, false, enabled, SvgGlyphLcdMode.Symmetric, work, CancellationToken.None);
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

        static OpenTypeOutlinePoint P(int x, int y, int shift) => new(20 + (x + shift) / 64d, 80 - y / 64d);
    }
}
