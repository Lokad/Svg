using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData((int)TrueTypeHintGrid.Physical, 0x01, 0)]
    [InlineData((int)TrueTypeHintGrid.ClearType, 0x01, 20)]
    [InlineData((int)TrueTypeHintGrid.ClearType, 0x00, 0)]
    public void Explicit_ClearType_grid_rounds_horizontal_points_to_sixteenths_only(int grid, byte axis, int expected)
    {
        // Native 48px/2048-unit rectangle: an authored 14-unit X coordinate
        // scales to 21/64px and MDAP fits it to 20/64px, not zero.
        TrueTypeHintPoint[] points = [new(21, 21, 14, 14)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [0], 48,
            new(98304, 98304), Target with { Grid = (TrueTypeHintGrid)grid }, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(new byte[] { axis, 0xB0, 0, 0x2F }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, axis == 0 ? points[0].Y : points[0].X);
        Assert.Equal(21, axis == 0 ? points[0].X : points[0].Y);
    }

    [Theory]
    [InlineData(0x18, 20)]
    [InlineData(0x19, 22)]
    [InlineData(0x3D, 22)]
    [InlineData(0x7A, 21)]
    [InlineData(0x7C, 24)]
    [InlineData(0x7D, 20)]
    public void ClearType_round_states_share_the_selected_horizontal_grid(byte round, int expected)
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], [], [], 48,
            new(98304, 98304), Target with { Grid = TrueTypeHintGrid.ClearType }, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(new byte[] { round, 0xB0, 21, 0x68, 0xB0, 0, 0x23, 0x42, 0xB8, 0xFF, 0xEB, 0x68, 0xB0, 1, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, machine.Storage[0]); Assert.Equal(-expected, machine.Storage[1]);
    }

    [Fact]
    public void ClearType_preparation_rounding_remains_on_the_physical_grid()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [], [], [], 48,
            new(98304, 98304), Target with { Grid = TrueTypeHintGrid.ClearType }, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Prepare(Convert.FromHexString("01B01568B0002342"), Target with { Grid = TrueTypeHintGrid.ClearType });
        Assert.Equal(0, machine.Storage[0]);
    }

    [Theory]
    [InlineData((int)TrueTypeHintGrid.Physical, 64)]
    [InlineData((int)TrueTypeHintGrid.ClearType, 32)]
    public void ClearType_horizontal_relative_minimum_is_half_the_authored_distance(int grid, int expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(20, 0, 20, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [1], 16,
            new(65536, 65536), Target with { Grid = (TrueTypeHintGrid)grid }, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(Convert.FromHexString("B001C8"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[1].X);
    }

    [Theory]
    [InlineData((int)TrueTypeHintGrid.Physical, 64)]
    [InlineData((int)TrueTypeHintGrid.ClearType, 72)]
    public void ClearType_horizontal_relative_cutin_is_one_sixteenth_of_the_authored_value(int grid, int expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(70, 0, 70, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [95], points, [1], 16,
            new(65536, 65536), Target with { Grid = (TrueTypeHintGrid)grid }, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(Convert.FromHexString("B10100E4"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[1].X);
    }

    [Fact]
    public void Unknown_grid_policy_rejects_before_scratch_allocation()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => TrueTypeHinting.Create(new(8, 0, 0, 0), [], [], [], 16,
            new(65536, 65536), Target with { Grid = (TrueTypeHintGrid)99 }, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Fact]
    public void Preparation_transition_cannot_change_the_grid_policy()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], [], [], 16,
            new(65536, 65536), Target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Prepare(ReadOnlyMemory<byte>.Empty,
            Target with { Grid = TrueTypeHintGrid.ClearType })).ErrorCode);
    }
}
