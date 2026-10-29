using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData(0x01, 32, 32, 13)]
    [InlineData(0x01, -32, -32, 13)]
    [InlineData(0x00, 32, 21, 32)]
    public void Stack_coordinate_moves_the_projected_axis_without_changing_glyph_originals(byte axis, int value, int x, int y)
    {
        TrueTypeHintPoint[] points = [new(21, 13, 21, 13)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [0], 16,
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { axis, 0xB0, 0, 0xB8, (byte)(value >> 8), (byte)value, 0x48 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(x, points[0].X); Assert.Equal(y, points[0].Y);
        Assert.Equal(21, points[0].OriginalX); Assert.Equal(13, points[0].OriginalY);
        Assert.Equal(axis == 1, points[0].TouchedX); Assert.Equal(axis == 0, points[0].TouchedY);
    }

    [Fact]
    public void Stack_coordinate_uses_projection_for_distance_and_freedom_for_movement()
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 3, 4), new(0, 0, 0, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [2], 16,
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010006B1022048"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(53, points[2].X); Assert.Equal(0, points[2].Y);
        Assert.True(points[2].TouchedX); Assert.False(points[2].TouchedY);
    }

    [Fact]
    public void Stack_coordinate_initializes_the_twilight_original_position()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 1), [], [], [], 16,
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B00015B1002048B00046B0002342B00047B0012342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(32, machine.Storage[0]); Assert.Equal(32, machine.Storage[1]);
    }

    [Theory]
    [InlineData(0x86, 38, 51)]
    [InlineData(0x87, -51, -38)]
    public void Dual_line_projection_measures_current_and_original_outlines_separately(byte opcode, int current, int original)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 4, 3) { OriginalX = 4, OriginalY = 3 }, new(64, 0, 64, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], points, [2], 16,
            new(65536, 65536), Target with { QueryFlags = TrueTypeHintQueryFlags.Symmetric }, new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB1, 1, 0, opcode, 0xB1, 2, 0, 0x49, 0xB0, 0, 0x23, 0x42,
            0xB1, 2, 0, 0x4A, 0xB0, 1, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(current, machine.Storage[0]); Assert.Equal(original, machine.Storage[1]);
    }

    [Fact]
    public void Coincident_original_dual_line_clears_perpendicular_for_the_current_projection()
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 0, 0) { OriginalX = 0, OriginalY = 0 }, new(64, 0, 64, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], points, [2], 16,
            new(65536, 65536), Target with { QueryFlags = TrueTypeHintQueryFlags.Symmetric }, new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010087B1020049B0002342B102004AB0012342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(38, machine.Storage[0]); Assert.Equal(64, machine.Storage[1]);
    }

    [Fact]
    public void Stack_projection_replaces_a_distinct_dual_projection()
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 4, 3) { OriginalX = 4, OriginalY = 3 }, new(64, 0, 64, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [], points, [2], 16,
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010086B9000040000AB102004AB0002342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(0, machine.Storage[0]);
    }

    [Theory]
    [InlineData(16384, 0, 16384, 0)]
    [InlineData(0, -16384, 0, -16384)]
    [InlineData(3, 4, 9830, 13107)]
    [InlineData(-3, 4, -9830, 13107)]
    public void Stack_projection_normalizes_signed_F2Dot14_components(int x, int y, int expectedX, int expectedY)
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], [], [], 16,
            new(65536, 65536), Target with { QueryFlags = TrueTypeHintQueryFlags.Symmetric }, new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB9, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, 0x0A,
            0x0C, 0xB0, 1, 0x23, 0x42, 0xB0, 0, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expectedX, machine.Storage[0]); Assert.Equal(expectedY, machine.Storage[1]);
    }

    [Fact]
    public void Stack_projection_uses_low_signed_words_and_preserves_the_freedom_vector()
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], points, [0], 16,
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
        // 0x4000 * 320 / 64 = 0x14000; only the signed low word selects X.
        // SHPIX must still use the prior Y freedom vector.
        machine.Run(Convert.FromHexString("00B84000B8014063B0000A0CB0012342B0002342B1002038"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(16384, machine.Storage[0]); Assert.Equal(0, machine.Storage[1]);
        Assert.Equal(0, points[0].X); Assert.Equal(32, points[0].Y);
    }

    [Fact]
    public void Zero_stack_projection_preserves_the_previous_direction()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], [], [], 16,
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("02B100000A0CB0012342B0002342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(0, machine.Storage[0]); Assert.Equal(16384, machine.Storage[1]);
    }

    [Theory]
    [InlineData(0x02, 0, 16384)]
    [InlineData(0x03, 16384, 0)]
    public void Projection_query_pushes_X_then_Y_without_changing_the_vector(byte axis, int x, int y)
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], [], [], 16,
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { axis, 0x0C, 0xB0, 1, 0x23, 0x42, 0xB0, 0, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(x, machine.Storage[0]); Assert.Equal(y, machine.Storage[1]);
    }

    [Theory]
    [InlineData(0x06, 9830, 13107)]
    [InlineData(0x07, -13107, 9830)]
    public void Projection_query_preserves_signed_oblique_F2Dot14_components(byte opcode, int x, int y)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 3, 4)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], points, [1], 16,
            new(65536, 65536), Target with { QueryFlags = TrueTypeHintQueryFlags.Symmetric }, new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB1, 1, 0, opcode, 0x0C, 0xB0, 1, 0x23, 0x42, 0xB0, 0, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(x, machine.Storage[0]); Assert.Equal(y, machine.Storage[1]);
    }

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
            new(long.MaxValue), CancellationToken.None);
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
            new(65536, 65536), Target, budget, CancellationToken.None))
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
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
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
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
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
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
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
            new(65536, 65536), Target, new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB1, 1, 0, 0x06, 0xB1, 3, 2, opcode, 0xB0, 0, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(1, machine.Storage[0]);
    }

    [Fact]
    public void Anisotropic_control_values_follow_the_projection_not_the_freedom_vector()
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 3, 4)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [1000], points, [1], 16,
            new(65536, 131072), Target with { HorizontalRatio = 2 }, new(long.MaxValue), CancellationToken.None);
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
            new(65536, 65536), Target, budget, CancellationToken.None))
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
