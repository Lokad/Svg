using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class BudgetedTextBufferTests
{
    [Fact]
    public void Transfers_the_final_string_charge_and_releases_it_with_the_consumer()
    {
        RenderBudget budget = new(1_000);
        using (var buffer = new BudgetedTextBuffer(budget))
        {
            buffer.Append("A", 10, new ParserWorkCounter(10), CancellationToken.None);
            using BudgetedText text = buffer.Detach();

            Assert.Equal("A", text.Value);
            Assert.Equal(34, budget.Snapshot().CurrentBytes);
        }

        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Reserves_replacement_character_storage_before_growth()
    {
        RenderBudget budget = new(1_500);
        using var buffer = new BudgetedTextBuffer(budget);
        buffer.Append(new string('A', 256), 512, new ParserWorkCounter(10), CancellationToken.None);

        SvgException exception = Assert.Throws<SvgException>(() => buffer.Append("A", 512, new ParserWorkCounter(10), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(544, budget.Snapshot().CurrentBytes);
    }
}
