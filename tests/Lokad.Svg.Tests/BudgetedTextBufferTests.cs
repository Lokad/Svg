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

    [Fact]
    public void Normalizes_direct_text_whitespace_across_reader_chunks_without_bypassing_the_source_limit()
    {
        RenderBudget budget = new(1_000);
        using var buffer = new BudgetedTextBuffer(budget);
        var normalizer = new SvgTextWhitespaceNormalizer();

        normalizer.Append(" \tA\r", buffer, 16, new ParserWorkCounter(20), CancellationToken.None);
        normalizer.Append("\n A  ", buffer, 16, new ParserWorkCounter(20), CancellationToken.None);
        using BudgetedText text = buffer.Detach();

        Assert.Equal("A A", text.Value);
        Assert.Equal(9, normalizer.SourceCharacterCount);
        var denied = new SvgTextWhitespaceNormalizer();
        using var deniedBuffer = new BudgetedTextBuffer(new RenderBudget(1_000));
        SvgException exception = Assert.Throws<SvgException>(() => denied.Append("     ", deniedBuffer, 4, new ParserWorkCounter(20), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }
}
