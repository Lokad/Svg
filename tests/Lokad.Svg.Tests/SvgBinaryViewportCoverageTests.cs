using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgBinaryViewportCoverageTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Layer", "Mechanism")]
    public void Overlapping_rectangles_preserve_nonzero_evenodd_and_inverse_fill(bool evenOdd, bool inverse)
    {
        byte[] actual = Rasterize([new(2, 2, 6, 6), new(5, 4, 7, 6)], SvgAffineTransform.Identity, evenOdd, inverse);
        byte[] expected = new byte[16 * 12];
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 16; x++)
        {
            bool first = x >= 2 && x < 8 && y >= 2 && y < 8;
            bool second = x >= 5 && x < 12 && y >= 4 && y < 10;
            bool inside = evenOdd ? first ^ second : first || second;
            expected[y * 16 + x] = inside != inverse ? (byte)255 : (byte)0;
        }
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("affine")]
    [InlineData("rotated")]
    [InlineData("fractional")]
    [Trait("Layer", "Mechanism")]
    public void Nonzero_exclusions_equal_sequential_binary_clip_out(string placement)
    {
        SvgAffineTransform map = placement switch
        {
            "affine" => new(1, .25, .1, 1, .25, -.5),
            "rotated" => SvgAffineTransform.FromTranslation(8, 6).ComposeChecked(SvgAffineTransform.FromRotationDegrees(17))
                .ComposeChecked(SvgAffineTransform.FromTranslation(-8, -6)),
            "fractional" => SvgAffineTransform.FromTranslation(.375, .625),
            _ => throw new InvalidDataException("Unknown mechanism placement.")
        };
        SvgClipBox[] boxes = [new(2.125, 2.25, 6.375, 5.5), new(5.25, 2.25, 7.125, 5.5), new(11.75, 2.25, 2, 5.5)];
        byte[] expected = Enumerable.Repeat((byte)255, 16 * 12).ToArray();
        foreach (SvgClipBox box in boxes)
        {
            byte[] excluded = Rasterize([box], map, false, true);
            for (int index = 0; index < expected.Length; index++) expected[index] &= excluded[index];
        }
        Assert.Equal(expected, Rasterize(boxes, map, false, true));
    }

    private static byte[] Rasterize(ReadOnlySpan<SvgClipBox> boxes, SvgAffineTransform map, bool evenOdd, bool inverse)
    {
        using HostMemoryBudget host = new(1_000_000);
        RenderBudget budget = new(host);
        byte[] pixels = new byte[16 * 12 * 4];
        using (BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, CancellationToken.None))
        {
            foreach (SvgClipBox box in boxes)
            {
                commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(box.X, box.Bottom), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(box.X, box.Y), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(box.Right, box.Y), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(box.Right, box.Bottom), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            }
            using SvgAuthoredPath path = SvgAuthoredPath.Create(commands.Detach(), SvgAffineTransform.Identity, budget);
            SvgCpuRasterGrid grid = new(16, 12);
            using SvgCpuClipCoverage coverage = new(grid, budget);
            SvgBinaryViewportCoverage.Rasterize(path, map, grid, new(0, 0, 16, 12), pixels, 16, coverage, evenOdd, inverse, budget, CancellationToken.None);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        return pixels.Where(static (_, index) => index % 4 == 3).ToArray();
    }
}
