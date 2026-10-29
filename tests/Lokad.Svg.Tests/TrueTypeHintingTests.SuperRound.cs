using Lokad.Svg.Rendering;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData(0x08, 21, 32)]
    [InlineData(0x48, 21, 0)]
    [InlineData(0x58, 21, 16)]
    [InlineData(0x88, 21, 0)]
    [InlineData(0xB8, 21, 96)]
    [InlineData(0x40, 21, 64)]
    [InlineData(0x40, 0, 0)]
    [InlineData(0x40, -1, -64)]
    [InlineData(0x48, 31, 0)]
    [InlineData(0x48, 32, 64)]
    [InlineData(0x48, -32, -64)]
    [InlineData(0x49, 23, 0)]
    [InlineData(0x49, 24, 64)]
    [InlineData(0x49, -24, -64)]
    [InlineData(0x51, 10, 16)]
    [InlineData(0x51, -10, -16)]
    [InlineData(0x58, 0, 16)]
    [InlineData(0x4F, 0, 64)]
    [InlineData(0x0148, 21, 0)]
    [InlineData(-184, 21, 0)]
    public void Super_round_uses_declared_period_phase_threshold_and_signed_distances(int selector, int value, int expected)
    {
        // Physical-grid cases 21 -> 0/16/32/64/96 have independent DirectWrite
        // outline-mask witnesses. Boundary/sign cases follow the TrueType definition.
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [], [], [], 48,
            new(98304, 98304), Target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(new byte[] { 0xB8, (byte)(selector >> 8), (byte)selector, 0x76,
            0xB8, (byte)(value >> 8), (byte)value, 0x68, 0xB0, 0, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, machine.Storage[0]);
    }

    [Theory]
    [InlineData(0x08, 32)]
    [InlineData(0x48, 0)]
    [InlineData(0x58, 16)]
    [InlineData(0x88, 0)]
    [InlineData(0xB8, 96)]
    [InlineData(0x40, 64)]
    public void Super_round_remains_physical_for_ClearType_horizontal_point_fitting(byte selector, int expected)
    {
        TrueTypeHintPoint[] points = [new(21, 0, 14, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [0], 48,
            new(98304, 98304), Target with { Grid = TrueTypeHintGrid.ClearType }, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(new byte[] { 1, 0xB0, selector, 0x76, 0xB0, 0, 0x2F }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[0].X);
    }

    [Fact]
    public void Super_round_parameters_are_restored_between_glyph_and_composite_calls()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], [], [], 48,
            new(98304, 98304), Target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Prepare(Convert.FromHexString("B05876"), Target);
        TrueTypeHintPoint[] child = [new(21, 0, 14, 0), default, default, default, default];
        machine.HintGlyph(Convert.FromHexString("B00876B0002F"), child, [0], false);
        Assert.Equal(32, child[0].X);
        TrueTypeHintPoint[] parent = [new(21, 0, 21, 0), default, default, default, default];
        machine.HintGlyph(Convert.FromHexString("B0002F"), parent, [0], true);
        Assert.Equal(16, parent[0].X);
        TrueTypeHintPoint[] next = [new(21, 0, 14, 0), default, default, default, default];
        machine.HintGlyph(Convert.FromHexString("B0002F"), next, [0], false);
        Assert.Equal(16, next[0].X);
    }

    [Theory]
    [InlineData(0xC0)]
    [InlineData(0xC8)]
    [InlineData(0xFF)]
    public void Super_round_reserved_periods_remain_explicit_unsupported_values(byte selector)
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], [], [], 48,
            new(98304, 98304), Target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        Assert.Equal(SvgErrorCode.UnsupportedFeature,
            Assert.Throws<SvgException>(() => machine.Run(new byte[] { 0xB0, selector, 0x76 }, TrueTypeHintProgramKind.Glyph)).ErrorCode);
    }
}
