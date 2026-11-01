using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("B82666B833330AB001B00008B000107AB87FFF1DB002B8FF773A", -61, -125)] // Independent equal words.
    [InlineData("B83C42B8EA6E0AB000B00108B000107AB87FFF1DB002B0003A", -31, -83)] // Negative compensation.
    [InlineData("B83C42B8EA6E0AB000B00108B000107AB87FFF1DB002B8FF773A", -309, -455)] // Component rounding reaches Y.
    [InlineData("B82666B833330AB001B000080EB000107AB87FFF1DB002B8FF773A", -62, -125)] // Copied direction.
    [InlineData("B82666B833330AB001B000080E0C0AB000107AB87FFF1DB002B8FF773A", -61, -125)] // Projection reload uncouples.
    [InlineData("B82666B833330AB001B000080EB001B00008B000107AB87FFF1DB002B8FF773A", -61, -125)] // Freedom reload uncouples.
    [InlineData("B84000B0000AB001B00008B000107AB87FFF1DB002B8FF773A", -137, -225)] // Exact X ratio.
    [InlineData("B82666B833330AB001B000080EB000107AB87FFF1DB002B8D8F048", -5979, -8015)] // Unit compensation after copy.
    public void Windows_movement_preserves_native_compensation_rounding_and_vector_transitions(string program, int x, int y)
    {
        // Original CC0 numeric states; independently captured programs/markers:
        // tests/Research/windows-movement. The large coordinate uses SCFS;
        // relative-distance cut-in is a separate responsibility.
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(384, 512, 384, 512), new(128, 128, 128, 128), new(0, 512, 0, 512)];
        TrueTypeHintTarget target = new OpenTypeWindowsGlyphProfile(SvgGlyphLcdMode.Natural, true).CreateHintTarget(1);
        RenderBudget budget = new(new HostMemoryBudget(long.MaxValue));
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(16, 0, 0, 0), [], points, [3], 32,
            new(65536, 65536), target, budget, CancellationToken.None))
        {
            machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph);
            Assert.Equal(x, points[2].X);
            Assert.Equal(y, points[2].Y);
            Assert.Equal(128, points[2].OriginalX);
            Assert.Equal(128, points[2].OriginalY);
            Assert.True(points[2].TouchedX);
            Assert.True(points[2].TouchedY);
            Assert.Equal(384, points[1].X);
            Assert.Equal(512, points[1].Y);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
