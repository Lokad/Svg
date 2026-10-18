using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData("B00158B0061CB100634259B1000742")]
    [InlineData("B00158B1060178B100634259B1000742")]
    [InlineData("B00158B1060079B100634259B1000742")]
    [InlineData("B1000242B00043B00161B0002342B0004358B8FFEF1C59B1000742")]
    public void Relative_jumps_can_leave_and_reenter_lexically_balanced_conditionals(string program)
    {
        // Forward early exits and a backward loop reduce the reached Courier
        // New control flow to authored programs without proprietary font bytes.
        var budget = new RenderBudget(long.MaxValue);
        using (TrueTypeHinting machine = Create([], [], [], budget, new(long.MaxValue), CancellationToken.None))
        {
            machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph);
            Assert.Equal(7, machine.Storage[0]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
