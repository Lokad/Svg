using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class TrueTypeCompositeStateTests
{
    private static TrueTypeHintTarget Target => new(35, TrueTypeHintQueryFlags.Grayscale, TrueTypeHintControlScale.FullPrecision, TrueTypeHintPreparation.Single);

    [Fact]
    public void Composite_original_distances_and_control_writes_use_device_coordinates_once()
    {
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 4, 0, 0), [0], [], [], 32, 131072, Target, budget, new(long.MaxValue), CancellationToken.None))
        {
            TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(64, 0, 64, 0), default, default, default, default];
            byte[] program = [0xB0, 0, 0xB1, 0, 1, 0x4A, 0x42, 0xB1, 0, 64, 0x70];
            machine.HintGlyph(program, points, [1], true);
            Assert.Equal(-64, machine.Storage[0]);
            Assert.Equal(64, machine.ControlValues[0]);
            machine.HintGlyph(program, points, [1], false);
            Assert.Equal(-128, machine.Storage[0]);
            Assert.Equal(128, machine.ControlValues[0]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Component_calls_preserve_control_and_storage_changes_and_restore_prepared_rounding()
    {
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 4, 0, 0), [0], [], [], 32, 65536, Target, budget, new(long.MaxValue), CancellationToken.None))
        {
            machine.Prepare(ReadOnlyMemory<byte>.Empty, Target);
            machine.HintGlyph(new byte[] { 0xB1, 0, 27, 0x44, 0xB1, 1, 91, 0x42, 0x7A }, [new(0, 0, 0, 0), default, default, default, default], [0], false);
            TrueTypeHintPoint[] parent = [new(35, 0, 35, 0), default, default, default, default];
            machine.HintGlyph(new byte[] { 0xB0, 0, 0x2F }, parent, [0], true);
            Assert.Equal(64, parent[0].X);
            Assert.Equal(27, machine.ControlValues[0]);
            Assert.Equal(91, machine.Storage[1]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Failed_component_unbinds_points_and_restores_prepared_graphics_state()
    {
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 4, 0, 0), [], [], [], 32, 65536, Target, budget, new(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => machine.HintGlyph(new byte[] { 0x7A, 0x4C }, [new(35, 0, 35, 0), default, default, default, default], [0], true)).ErrorCode);
            TrueTypeHintPoint[] next = [new(35, 0, 35, 0), default, default, default, default];
            machine.HintGlyph(new byte[] { 0xB0, 0, 0x2F }, next, [0], false);
            Assert.Equal(64, next[0].X);
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.HintGlyph(ReadOnlyMemory<byte>.Empty, new TrueTypeHintPoint[6], [0], true)).ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
