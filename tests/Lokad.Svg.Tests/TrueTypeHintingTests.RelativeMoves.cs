using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData(true, 1889)]
    [InlineData(false, 1888)]
    public void Direct_relative_original_scaling_preserves_target_specific_negative_half_rounding(bool symmetric, int expected)
    {
        // Independent native synthetic 48px/2048-unit rectangle: the reference
        // is fitted to 2112 and the lower edge is 149 design units below it.
        TrueTypeHintPoint[] points = [new(0, 2114, 0, 1409), new(0, 1890, 0, 1260)];
        points[0].Y = 2112;
        TrueTypeHintTarget target = Target with { QueryFlags = symmetric ? TrueTypeHintQueryFlags.Symmetric : TrueTypeHintQueryFlags.Grayscale };
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [1], 48,
            new(98304, 98304), target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("00B001C0"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[1].Y);
    }

    [Theory]
    [InlineData(0x32, 45)]
    [InlineData(0x33, 75)]
    public void Point_shifts_select_the_reference_displacement_and_move_from_current_coordinates(byte opcode, int expected)
    {
        TrueTypeHintPoint[] points = [new(10, 0, 10, 0), new(100, 0, 100, 0), new(40, 0, 40, 0)];
        points[0].X = 30; points[1].X = 90; points[2].X = 55;
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [2], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB0, 0, 0x11, 0xB0, 1, 0x12, 0xB0, 2, opcode }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[2].X); Assert.True(points[2].TouchedX);
        Assert.Equal(40, points[2].OriginalX);
    }

    [Theory]
    [InlineData(0xE4, 100, 68, true, 84)]
    [InlineData(0xE4, 100, 20, true, 148)]
    [InlineData(0xE0, 100, 20, true, 85)]
    [InlineData(0xE4, -100, 68, true, -44)]
    [InlineData(0xE0, -100, 68, true, -45)]
    [InlineData(0xE0, -100, 68, false, 85)]
    public void Indirect_relative_moves_apply_auto_flip_and_cut_in_only_with_rounding(byte opcode, int original, byte cutIn, bool flip, int expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(original, 0, original, 0)];
        points[0].X = 20; points[1].X = 145;
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [65], points, [1], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB0, cutIn, 0x1D, flip ? (byte)0x4D : (byte)0x4E, 0xB1, 1, 0, opcode }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[1].X); Assert.True(points[1].TouchedX);
    }

    [Fact]
    public void Indirect_twilight_moves_initialize_original_location_before_fitting_relative_to_current_reference()
    {
        TrueTypeHintPoint[] points = [new(13, 0, 13, 0)];
        points[0].X = 30;
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 2), [65], points, [0], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B00014B10100E4B00015B00146B0002342B00147B0012342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(94, machine.Storage[0]); Assert.Equal(78, machine.Storage[1]);
    }

    [Theory]
    [InlineData(95, 64)]
    [InlineData(-96, -128)]
    public void Stack_rounding_uses_the_same_signed_round_state(short value, int expected)
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [], [], [], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB8, (byte)(value >> 8), (byte)value, 0x68, 0xB0, 0, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, machine.Storage[0]);
    }

    [Fact]
    public void Point_interpolation_preserves_relative_stretch_touches_points_and_resets_loop()
    {
        TrueTypeHintPoint[] points = [new(10, 0, 10, 0), new(100, 0, 100, 0), new(40, 0, 40, 0), new(70, 0, 70, 0)];
        points[0].X = 20; points[1].X = 200;
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [3], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B00011B00112B00217B1020339B1020A38"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(90, points[2].X); Assert.Equal(140, points[3].X);
        Assert.True(points[2].TouchedX); Assert.True(points[3].TouchedX);
        Assert.Equal(40, points[2].OriginalX);
    }

    [Theory]
    [InlineData(98304, 43)]
    [InlineData(65536, 50)]
    public void Point_interpolation_preserves_native_uniform_and_anisotropic_original_precision(int verticalScale, int expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(5, 0, 3, 0), new(2, 0, 1, 0)];
        points[0].X = 10; points[1].X = 110;
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [2], 16,
            new(verticalScale, 98304), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B00011B00112B00239"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[2].X);
    }

    [Theory]
    [InlineData(0xC0, 70, 92)]
    [InlineData(0xC4, 70, 86)]
    [InlineData(0xC8, 20, 86)]
    [InlineData(0xCC, -20, -42)]
    public void Direct_relative_moves_use_original_distance_then_round_and_minimum(byte opcode, int distance, int expected)
    {
        TrueTypeHintPoint[] points = [new(13, 0, 13, 0), new(13 + distance, 0, 13 + distance, 0)];
        points[0].X = 22; points[1].X = 144;
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [1], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB0, 1, opcode }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[1].X); Assert.True(points[1].TouchedX);
        Assert.Equal(13 + distance, points[1].OriginalX);
    }

    [Theory]
    [InlineData(0xC4, 22)]
    [InlineData(0xD4, 86)]
    public void Direct_relative_moves_update_reference_zero_only_when_requested(byte opcode, int expected)
    {
        TrueTypeHintPoint[] points = [new(13, 0, 13, 0), new(83, 0, 83, 0), new(150, 0, 150, 0)];
        points[0].X = 22;
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [2], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB0, 1, opcode, 0xB0, 2, 0x3C }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[2].X);
    }

    [Fact]
    public void Direct_relative_moves_respect_prepared_single_width_and_glyph_minimum_state_is_restored()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], [], [], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Prepare(Convert.FromHexString("B00A1EB04B1FB0801A"), Target);
        TrueTypeHintPoint[] first = [new(13, 0, 13, 0), new(83, 0, 83, 0), default, default, default, default];
        first[0].X = 22;
        machine.HintGlyph(new byte[] { 0xB0, 1, 0xC0, 0xB0, 64, 0x1A }, first, [1], false);
        Assert.Equal(97, first[1].X);
        TrueTypeHintPoint[] next = [new(0, 0, 0, 0), new(20, 0, 20, 0), default, default, default, default];
        machine.HintGlyph(new byte[] { 0xB0, 1, 0xC8 }, next, [1], false);
        Assert.Equal(128, next[1].X);
    }

    [Theory]
    [InlineData(0x73, 9, 8, 72)]
    [InlineData(0x74, 25, 8, 72)]
    [InlineData(0x75, 41, 8, 72)]
    [InlineData(0x73, 9, 0, 0)]
    [InlineData(0x73, 10, 8, 64)]
    public void Control_deltas_apply_only_the_selected_ppem_and_keep_signed_step_mapping(byte opcode, int ppem, byte argument, int expected)
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [64], [], [], ppem,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB2, argument, 0, 1, opcode }, TrueTypeHintProgramKind.Preparation);
        Assert.Equal(expected, machine.ControlValues[0]);
    }

    [Fact]
    public void Control_deltas_use_the_axis_ppem_and_normalize_anisotropic_storage()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [64], [], [], 9,
            new(65536, 131072), Target with { HorizontalRatio = 2 }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB2, 0x98, 0, 1, 0x73 }, TrueTypeHintProgramKind.Preparation);
        Assert.Equal(68, machine.ControlValues[0]);
    }

    [Theory]
    [InlineData("B208010173")]
    [InlineData("B1080173")]
    [InlineData("B003D4")]
    [InlineData("B10101E4")]
    [InlineData("B000E4")]
    public void Malformed_control_delta_and_relative_point_indices_release_the_machine(string program)
    {
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [64], [new(0, 0, 0, 0)], [0], 9,
            new(65536, 65536), Target, budget, new(long.MaxValue), CancellationToken.None))
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
