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
        using var preservedBuffer = new BudgetedTextBuffer(new RenderBudget(1_000));
        var preserved = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Preserve);
        preserved.Append(" \tA\r", preservedBuffer, 16, new ParserWorkCounter(20), CancellationToken.None);
        preserved.Append("\n A  ", preservedBuffer, 16, new ParserWorkCounter(20), CancellationToken.None);
        using BudgetedText preservedText = preservedBuffer.Detach();

        Assert.Equal("  A   A  ", preservedText.Value);
        Assert.Equal(9, preserved.SourceCharacterCount);
        var denied = new SvgTextWhitespaceNormalizer();
        using var deniedBuffer = new BudgetedTextBuffer(new RenderBudget(1_000));
        SvgException exception = Assert.Throws<SvgException>(() => denied.Append("     ", deniedBuffer, 4, new ParserWorkCounter(20), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Retains_only_normalized_letter_spacing_intervals_that_differ_from_the_parent_text()
    {
        RenderBudget budget = new(2_000);
        var workCounter = new ParserWorkCounter(100);
        using var buffer = new SvgTextLayoutBuffer(budget, 0, workCounter, CancellationToken.None);
        var parent = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 0);
        var spaced = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 5);

        parent.Append("A", buffer, 10, workCounter, CancellationToken.None);
        spaced.Append("A", buffer, 10, workCounter, CancellationToken.None);
        parent.Append("A", buffer, 10, workCounter, CancellationToken.None);
        using BudgetedTextLayout text = buffer.Detach();

        Assert.Equal("AAA", text.Value);
        Assert.Equal([new SvgTextLetterSpacingSpan(0, 1, 0), new SvgTextLetterSpacingSpan(1, 2, 5), new SvgTextLetterSpacingSpan(2, 3, 0)], text.LetterSpacingSpans);
        Assert.True(budget.Snapshot().CurrentBytes > 0);
        text.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
