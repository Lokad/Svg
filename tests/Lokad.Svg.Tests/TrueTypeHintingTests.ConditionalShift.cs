using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(52, true, false)]
    public void Windows_conditional_shift_function_keeps_stack_and_loop_effects_without_moving_points(byte function, bool waiver, bool interpolated)
    {
        // Independent original-font controls distinguish this exact function
        // body from an inline equivalent and inert prefix/suffix variants.
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(0, 512, 0, 512), new(512, 512, 512, 512), new(512, 0, 512, 0)];
        TrueTypeHintTarget target = Target with { Grid = TrueTypeHintGrid.ClearType, WindowsArithmetic = true };
        RenderBudget budget = new(new HostMemoryBudget(long.MaxValue));
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(16, 2, function + 1, 0), [], points, [3], 32,
            new(65536, 65536), target, budget, CancellationToken.None))
        {
            byte[] definition = Convert.FromHexString("B0002C4B53234B515A58381B2121592D");
            definition[1] = function;
            machine.Run(definition, TrueTypeHintProgramKind.Font);
            if (waiver) machine.Prepare(Convert.FromHexString("B104038E"), target);
            byte[] program = [.. interpolated ? new byte[] { 0x30, 0x31 } : [], 0x01, 0xB3, 0, 1, 2, 3, 0xB0, 4, 0x17, 0xB8, 2, 0, 0xB1, 11, 40, 0xB0, function, 0x2B,
                0x24, 0xB0, 0, 0x23, 0x42, 0xB0, 1, 0xB0, 0, 0x46, 0x42, 0xB1, 0, 64, 0x38];
            machine.Run(program, TrueTypeHintProgramKind.Glyph);
            Assert.Equal(0, machine.Storage[0]);
            Assert.Equal(0, machine.Storage[1]);
            // A following ordinary shift consumes one point: the function's
            // ignored movement still resets the loop, and suppression ends.
            Assert.Equal(new[] { 64, 0, 512, 512 }, points.Select(point => point.X));
            Assert.True(points[0].TouchedX);
            Assert.All(points.Skip(1), point => Assert.False(point.TouchedX));
            Assert.All(points, point => Assert.False(point.TouchedY));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false, true, "", "", false)]
    [InlineData(true, false, "", "", false)]
    [InlineData(true, true, "B00021", "", false)]
    [InlineData(true, true, "", "B00021", false)]
    [InlineData(true, true, "", "", true)]
    public void Conditional_shift_suppression_preserves_other_targets_bodies_and_inline_execution(bool windows, bool clearType, string prefix, string suffix, bool inline)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0)];
        TrueTypeHintTarget target = Target with { Grid = clearType ? TrueTypeHintGrid.ClearType : TrueTypeHintGrid.Physical, WindowsArithmetic = windows };
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(16, 0, 1, 0), [], points, [0], 32,
            new(65536, 65536), target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        string body = prefix + "4B53234B515A58381B212159" + suffix;
        machine.Run(Convert.FromHexString("B0002C" + body + "2D"), TrueTypeHintProgramKind.Font);
        machine.Run(Convert.FromHexString("00B300400B28" + (inline ? body : "B0002B")), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(64, points[0].Y);
        Assert.True(points[0].TouchedY);
    }
}
