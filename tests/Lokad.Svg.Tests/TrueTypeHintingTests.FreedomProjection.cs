using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0x02, 0x05, 0, 64)]
    [InlineData(0x03, 0x04, 64, 0)]
    public void Freedom_from_projection_replaces_the_previous_axis(byte projection, byte freedom, int x, int y)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0)];
        RenderBudget budget = new(new HostMemoryBudget(long.MaxValue));
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [0], 16,
            new(65536, 65536), Target, budget, CancellationToken.None))
            machine.Run(new byte[] { projection, freedom, 0x0E, 0xB1, 0, 64, 0x38 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(x, points[0].X);
        Assert.Equal(y, points[0].Y);
        Assert.Equal(x != 0, points[0].TouchedX);
        Assert.Equal(y != 0, points[0].TouchedY);
        Assert.Equal(0, points[0].OriginalX);
        Assert.Equal(0, points[0].OriginalY);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(3, 4, 0x06, 38, 51)]
    [InlineData(-3, 4, 0x06, -38, 51)]
    [InlineData(3, 4, 0x07, -51, 38)]
    public void Freedom_from_projection_preserves_signed_oblique_components(int lineX, int lineY, byte projection, int x, int y)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(lineX, lineY, lineX, lineY), new(0, 0, 0, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [2], 16,
            new(65536, 65536), Target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(new byte[] { 0xB1, 1, 0, projection, 0x0E, 0xB1, 2, 64, 0x38 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(x, points[2].X);
        Assert.Equal(y, points[2].Y);
        Assert.True(points[2].TouchedX);
        Assert.True(points[2].TouchedY);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Freedom_from_projection_preserves_the_operand_stack_and_dual_projection()
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 4, 3) { OriginalX = 4, OriginalY = 3 }, new(64, 0, 64, 0)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 3, 0, 0), [], points, [2], 16,
            new(65536, 65536), Target with { QueryFlags = TrueTypeHintQueryFlags.Symmetric }, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(Convert.FromHexString("B1010086B0250EB0002342B1020049B0012342B102004AB0022342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(37, machine.Storage[0]);
        Assert.Equal(38, machine.Storage[1]);
        Assert.Equal(51, machine.Storage[2]);
        Assert.Equal(64, points[2].X);
        Assert.Equal(0, points[2].Y);
    }
}
