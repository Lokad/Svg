using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(15426, -5522)]
    [InlineData(-15426, 5522)]
    [InlineData(3, 4)]
    [InlineData(-3, 4)]
    [InlineData(0, 0)]
    [InlineData(16384, 0)]
    public void Windows_stack_projection_preserves_supplied_words_replaces_dual_and_keeps_freedom(short x, short y)
    {
        // SPVFS consumes already normalized F2Dot14 components. Renormalizing
        // rounded GPV output can change a bit. Native probes also distinguish
        // verbatim non-unit/zero handling from classic normalization.
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 4, 3) { OriginalX = 4, OriginalY = 3 }, new(16384, 0, 16384, 0)];
        TrueTypeHintTarget target = new OpenTypeWindowsGlyphProfile(SvgGlyphLcdMode.Natural, true).CreateHintTarget(1);
        RenderBudget budget = new(new HostMemoryBudget(long.MaxValue));
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 3, 0, 0), [], points, [2], 32,
            new(65536, 65536), target, budget, CancellationToken.None))
        {
            machine.Run(new byte[] { 0x00, 0xB1, 1, 0, 0x86, 0xB9, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, 0x0A,
                0x0C, 0xB0, 1, 0x23, 0x42, 0xB0, 0, 0x23, 0x42,
                0xB0, 2, 0xB0, 2, 0x47, 0x42, 0xB1, 0, 64, 0x38 }, TrueTypeHintProgramKind.Glyph);
            Assert.Equal(x, machine.Storage[0]);
            Assert.Equal(y, machine.Storage[1]);
            Assert.Equal(x, machine.Storage[2]);
            Assert.Equal(0, points[0].X);
            Assert.Equal(64, points[0].Y);
            Assert.False(points[0].TouchedX);
            Assert.True(points[0].TouchedY);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Windows_stack_projection_round_trip_keeps_the_quantized_line_vector_in_symmetric_mode()
    {
        TrueTypeHintPoint[] points = [new(8, 128, 8, 128), new(512, 1536, 512, 1536)];
        TrueTypeHintTarget target = new OpenTypeWindowsGlyphProfile(SvgGlyphLcdMode.Symmetric, true).CreateHintTarget(1);
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], points, [1], 32,
            new(65536, 65536), target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(Convert.FromHexString("B10001070C0A0CB0012342B0002342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(15426, machine.Storage[0]);
        Assert.Equal(-5522, machine.Storage[1]);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Windows_stack_projection_round_trip_preserves_a_following_relative_move()
    {
        // Independent original-font MSIRP witness: renormalizing GPV output
        // changes one projected distance and moves the target to 108, not 107.
        TrueTypeHintPoint[] points = [new(0, 128, 0, 128) { X = 8 }, new(512, 1664, 512, 1664) { Y = 1536 }, new(102, 0, 102, 0)];
        TrueTypeHintTarget target = new OpenTypeWindowsGlyphProfile(SvgGlyphLcdMode.Natural, true).CreateHintTarget(1);
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [2], 32,
            new(65536, 65536), target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(Convert.FromHexString("B100018705B0002E0C0AB804001DB002B800893A"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(107, points[2].X);
        Assert.Equal(0, points[2].Y);
        Assert.Equal(102, points[2].OriginalX);
        Assert.True(points[2].TouchedX);
    }
}
