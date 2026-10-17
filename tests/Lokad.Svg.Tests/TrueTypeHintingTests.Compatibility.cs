using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData(false, 73)]
    [InlineData(true, 65)]
    public void Subpixel_positioned_right_phantom_keeps_its_linear_position(bool subpixel, int expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), default, new(65, 0, 65, 0), default, default];
        TrueTypeHintTarget target = Target with { Grid = TrueTypeHintGrid.ClearType, QueryFlags = subpixel ? TrueTypeHintQueryFlags.SubpixelPositioned : TrueTypeHintQueryFlags.None };
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 3), [], points, [0], 9,
            new(65536, 65536), target, budget, new(long.MaxValue), CancellationToken.None))
        {
            machine.Run(Convert.FromHexString("B104038EB1020838B00015B1020838B00246B0002342"), TrueTypeHintProgramKind.Glyph);
            Assert.Equal(expected, points[2].X);
            Assert.Equal(65, points[2].OriginalX);
            Assert.Equal(8, machine.Storage[0]); // The same-numbered twilight slot is independent.
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false, false, 64)]
    [InlineData(true, false, 72)]
    [InlineData(true, true, 64)]
    public void Compatibility_deltas_require_a_touched_vertical_point_before_both_interpolations(bool touched, bool interpolated, int expected)
    {
        TrueTypeHintPoint[] points = [new(0, 64, 0, 64)];
        points[0].TouchedY = touched;
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [0], 9,
            new(65536, 65536), Target with { Grid = TrueTypeHintGrid.ClearType }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        byte[] program = interpolated ? Convert.FromHexString("003130B20800015D") : Convert.FromHexString("00B20800015D");
        machine.Run(program, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[0].Y);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 8)]
    public void Composite_vertical_deltas_allow_previously_fitted_child_points(bool composite, int expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), default, default, default, default];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], [], [], 9,
            new(65536, 65536), Target with { Grid = TrueTypeHintGrid.ClearType }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.HintGlyph(Convert.FromHexString("00B20800015D"), points, [0], composite);
        Assert.Equal(expected, points[0].Y);
    }

    [Fact]
    public void Ignoring_prepared_graphics_state_also_resets_the_native_ClearType_waiver()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], [], [], 9,
            new(65536, 65536), Target with { Grid = TrueTypeHintGrid.ClearType }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Prepare(Convert.FromHexString("B104038EB102028E"), Target with { Grid = TrueTypeHintGrid.ClearType });
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), default, default, default, default];
        machine.HintGlyph(Convert.FromHexString("B20800015D"), points, [0], false);
        Assert.Equal(0, points[0].X);
    }

    [Fact]
    public void Native_ClearType_waiver_is_glyph_local_and_control_deltas_remain_enabled()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [64], [], [], 9,
            new(65536, 65536), Target with { Grid = TrueTypeHintGrid.ClearType }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        TrueTypeHintPoint[] first = [new(0, 0, 0, 0), default, default, default, default];
        machine.HintGlyph(Convert.FromHexString("B104038EB20800015D"), first, [0], false);
        Assert.Equal(8, first[0].X);
        TrueTypeHintPoint[] second = [new(0, 0, 0, 0), default, default, default, default];
        machine.HintGlyph(Convert.FromHexString("B20800015DB208000173"), second, [0], false);
        Assert.Equal(0, second[0].X); Assert.Equal(72, machine.ControlValues[0]);
    }

    [Theory]
    [InlineData(false, 23316)]
    [InlineData(true, 23318)]
    public void Native_projection_components_round_to_nearest_before_projecting(bool symmetric, int expected)
    {
        // Independent native GPV/MD controls for an exact 1000:333 line.
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(1000, 333, 1000, 333), new(24576, 0, 24576, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [], points, [2], 48,
            new(65536, 65536), Target with { QueryFlags = symmetric ? TrueTypeHintQueryFlags.Symmetric : TrueTypeHintQueryFlags.None }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010006B1020049B0002342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, machine.Storage[0]);
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(true, 0)]
    public void Native_projection_rounds_an_exact_negative_half_toward_positive(bool symmetric, int expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(4096, 7094, 4096, 7094), new(1, 0, 1, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [], points, [2], 48,
            new(65536, 65536), Target with { QueryFlags = symmetric ? TrueTypeHintQueryFlags.Symmetric : TrueTypeHintQueryFlags.None }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010006B1000249B0002342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, machine.Storage[0]);
    }

    [Theory]
    [InlineData(false, 31)]
    [InlineData(true, 32)]
    public void Native_freedom_movement_rounds_an_exact_negative_half_toward_positive(bool symmetric, int expectedY)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(7094, 4096, 7094, 4096), new(0, 32, 0, 32)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [2], 48,
            new(65536, 65536), Target with { QueryFlags = symmetric ? TrueTypeHintQueryFlags.Symmetric : TrueTypeHintQueryFlags.None }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010008B002B8FFFF38"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(-1, points[2].X); Assert.Equal(expectedY, points[2].Y);
    }

    [Theory]
    [InlineData(4, 3, 4, 20)]
    [InlineData(3, 4, 4, 20)]
    [InlineData(1, 1, 5, 20)]
    [InlineData(0, 1, 5, 0)]
    public void ClearType_grid_includes_oblique_projections_independently_of_freedom(int dx, int dy, byte freedom, int expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(dx, dy, dx, dy)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [], points, [1], 48,
            new(65536, 65536), Target with { Grid = TrueTypeHintGrid.ClearType }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB1, 1, 0, 0x06, freedom, 0xB0, 21, 0x68, 0xB0, 0, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, machine.Storage[0]);
    }
}
