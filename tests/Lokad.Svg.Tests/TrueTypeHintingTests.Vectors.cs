using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData(false, -284)]
    [InlineData(true, -285)]
    public void Windows_diagonal_projection_rounds_each_component_before_adding(bool symmetric, int expected)
    {
        // Native MD witnesses on the same 855:2240 line distinguish component
        // products from rounding their sum, without depending on rendered pixels.
        TrueTypeHintPoint[] points = [new(24, 0, 24, 0), new(879, 2240, 879, 2240), new(548, 576, 548, 576)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], points, [2], 48,
            new(65536, 65536), Target with { QueryFlags = symmetric ? TrueTypeHintQueryFlags.Symmetric : TrueTypeHintQueryFlags.None },
            new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010007B1020049B0002342B1020149B0012342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, machine.Storage[0]); Assert.Equal(-284, machine.Storage[1]);
    }

    [Theory]
    [InlineData(0x06, 53, 0)]
    [InlineData(0x07, -40, 0)]
    [InlineData(0x08, 32, 43)]
    [InlineData(0x09, 32, -24)]
    public void Line_vectors_keep_projection_and_freedom_distinct(byte opcode, int x, int y)
    {
        // The 3:4:5 line has projection (3/5,4/5); its CCW normal is (-4/5,3/5).
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 3, 4), new(0, 0, 0, 0)];
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [32], points, [2], 16,
            new(65536, 65536), Target, budget, new(long.MaxValue), CancellationToken.None))
            machine.Run(new byte[] { 0xB1, 1, 0, opcode, 0xB1, 2, 0, 0xE0 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(x, points[2].X); Assert.Equal(y, points[2].Y);
        Assert.True(points[2].TouchedX); Assert.Equal(opcode >= 8, points[2].TouchedY);
        Assert.Equal(0, points[2].OriginalX); Assert.Equal(0, points[2].OriginalY);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Pixel_shift_uses_freedom_without_projection_compensation()
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 3, 4), new(0, 0, 0, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [2], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010008B1024038"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(38, points[2].X); Assert.Equal(51, points[2].Y);
        Assert.True(points[2].TouchedX); Assert.True(points[2].TouchedY);
    }

    [Theory]
    [InlineData(0x02, 0x05, 32, 0)]
    [InlineData(0x03, 0x04, 0, 32)]
    public void Independent_axis_vectors_keep_perpendicular_moves_bounded(byte projection, byte freedom, int x, int y)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(0, 0, 0, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [32], points, [1], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { projection, freedom, 0xB1, 1, 0, 0xE0 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(x, points[1].X); Assert.Equal(y, points[1].Y);
    }

    [Theory]
    [InlineData(0, 0, 32)]
    [InlineData(0, 10000, 32)]
    [InlineData(1, 10000, 32)]
    [InlineData(-3, -4, -53)]
    public void Coincident_and_nearly_perpendicular_projection_lines_do_not_create_spikes(int dx, int dy, int expectedX)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(dx, dy, dx, dy), new(0, 0, 0, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [32], points, [2], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010006B10200E0"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expectedX, points[2].X); Assert.Equal(0, points[2].Y);
    }

    [Theory]
    [InlineData(0x49)]
    [InlineData(0x4A)]
    public void Projected_distances_subtract_points_before_fixed_point_rounding(byte opcode)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 3, 4), new(1, 0, 1, 0), new(2, 0, 2, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [], points, [3], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB1, 1, 0, 0x06, 0xB1, 3, 2, opcode, 0xB0, 0, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(1, machine.Storage[0]);
    }

    [Fact]
    public void Anisotropic_control_values_follow_the_projection_not_the_freedom_vector()
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 3, 4)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [1000], points, [1], 16,
            new(65536, 131072), Target with { HorizontalRatio = 2 }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010006B00045B0002342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(1442, machine.Storage[0]);
    }

    [Theory]
    [InlineData("06")]
    [InlineData("B1010306")]
    [InlineData("B1030007")]
    [InlineData("B00108")]
    public void Invalid_line_operands_fail_with_categorized_cleanup(string program)
    {
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], [new(0, 0, 0, 0)], [0], 16,
            new(65536, 65536), Target, budget, new(long.MaxValue), CancellationToken.None))
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
