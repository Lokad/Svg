using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class BudgetedTextBufferTests
{
    [Fact]
    public void Collapsed_spaces_keep_the_first_span_owner_and_trim_only_the_final_separator()
    {
        RenderBudget budget = new(20_000);
        SvgInheritedTextStyle font = new("Example", 400, false, 18, SvgTextWhitespaceMode.Default, 1, SvgTextAnchor.Start) { WordSpacing = 5 };
        using (SvgTextPaintOutput output = new(budget, 1, SvgTextPaintStyle.Default, font, CancellationToken.None))
        {
            SvgTextWhitespaceNormalizer parent = new(SvgTextWhitespaceMode.Default, 1, 0, false);
            parent.Append("A ", output, 20, CancellationToken.None);
            output.EnterStyleSpan(SvgTextPaintStyle.Default, new("Example", 400, false, 18, SvgTextWhitespaceMode.Default, 1, SvgTextAnchor.Start));
            output.EnterPositioningSpan(new SvgTextPositionLists("20 30", null, null, null, null), 1, CancellationToken.None);
            SvgTextWhitespaceNormalizer child = new(SvgTextWhitespaceMode.Default, 1, 1, true);
            child.Append(" B ", output, 20, CancellationToken.None);
            output.ExitPositioningSpan(1);
            output.ExitPaintSpan();
            parent.Append("C ", output, 20, CancellationToken.None);
            using SvgExtractedTextRun run = output.Detach(null, false);
            Assert.Equal("A B C", run.Layout.Value);
            Assert.Equal([
                new SvgTextLetterSpacingSpan(0, 2, 1) { WordSpacing = 5 },
                new SvgTextLetterSpacingSpan(2, 4, 1),
                new SvgTextLetterSpacingSpan(4, 5, 1) { WordSpacing = 5 }], run.Layout.LetterSpacingSpans);
            Assert.Equal([new SvgTextPositionAdjustment(2, 20, null, 0, 0), new SvgTextPositionAdjustment(3, 30, null, 0, 0)], run.Layout.PositionAdjustments);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Nested_lists_count_normalized_scalars_across_chunks_and_resume_ancestor_values()
    {
        RenderBudget budget = new(20_000);
        using SvgTextLayoutBuffer buffer = new(budget, 0, CancellationToken.None);
        buffer.Append("A", 0, 0, 20, CancellationToken.None);
        buffer.EnterPositioningSpan(new SvgTextPositionLists("10 20 30 40", null, null, null, "5 10"), 1, CancellationToken.None);
        buffer.Append(" ", 0, 0, 20, CancellationToken.None);
        buffer.Append("\ud83d", 0, 1, 20, CancellationToken.None);
        buffer.Append("\ude00", 0, 1, 20, CancellationToken.None);
        buffer.EnterPositioningSpan(new SvgTextPositionLists(null, null, "0 2", null, "0"), 2, CancellationToken.None);
        buffer.Append("BC", 0, 2, 20, CancellationToken.None);
        buffer.ExitPositioningSpan(2);
        buffer.Append("D", 0, 1, 20, CancellationToken.None);
        buffer.ExitPositioningSpan(1);
        using (BudgetedTextLayout text = buffer.Detach())
        {
            Assert.Equal("A 😀BCD", text.Value);
            Assert.Equal(new SvgTextPositionAdjustment(2, 10, null, 0, 0) { Rotation = 5 }, text.PositionAdjustments[0]);
            Assert.Equal(new SvgTextPositionAdjustment(4, 20, null, 0, 0) { Rotation = 0, OverridesDx = true }, text.PositionAdjustments[1]);
            Assert.Equal(new SvgTextPositionAdjustment(5, 30, null, 2, 0) { Rotation = 0, OverridesDx = true }, text.PositionAdjustments[2]);
            Assert.Equal(new SvgTextPositionAdjustment(6, 40, null, 0, 0) { Rotation = 10 }, text.PositionAdjustments[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Scoped_list_denial_and_cancellation_release_active_and_detached_storage()
    {
        RenderBudget accepted = new(100_000);
        Run(accepted, CancellationToken.None, null);
        long peak = accepted.Snapshot().PeakBytes;
        foreach (long limit in new[] { 1, peak / 2, peak - 1 })
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Run(denied, CancellationToken.None, null)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        using CancellationTokenSource cancellation = new();
        OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(() => Run(accepted, cancellation.Token, cancellation));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        Run(accepted, CancellationToken.None, null);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);

        static void Run(RenderBudget budget, CancellationToken token, CancellationTokenSource? cancelBeforeDetach)
        {
            using SvgTextLayoutBuffer buffer = new(budget, 0, token);
            buffer.EnterPositioningSpan(new SvgTextPositionLists("10 20 30", null, null, null, "0 10"), 1, token);
            buffer.Append("ABC", 0, 4, 1, 20, token);
            buffer.ExitPositioningSpan(1);
            cancelBeforeDetach?.Cancel();
            using BudgetedTextLayout text = buffer.Detach();
        }
    }

    [Fact]
    public void Transfers_the_final_string_charge_and_releases_it_with_the_consumer()
    {
        RenderBudget budget = new(1_000);
        using (var buffer = new BudgetedTextBuffer(budget))
        {
            buffer.Append("A", 10, CancellationToken.None);
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
        buffer.Append(new string('A', 256), 512, CancellationToken.None);

        SvgException exception = Assert.Throws<SvgException>(() => buffer.Append("A", 512, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(544, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Normalizes_direct_text_whitespace_across_reader_chunks_without_bypassing_the_source_limit()
    {
        RenderBudget budget = new(1_000);
        using var buffer = new BudgetedTextBuffer(budget);
        var normalizer = new SvgTextWhitespaceNormalizer();

        normalizer.Append(" \tA\r", buffer, 16, CancellationToken.None);
        normalizer.Append("\n A  ", buffer, 16, CancellationToken.None);
        using BudgetedText text = buffer.Detach();

        Assert.Equal("A A", text.Value);
        Assert.Equal(9, normalizer.SourceCharacterCount);
        using var preservedBuffer = new BudgetedTextBuffer(new RenderBudget(1_000));
        var preserved = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Preserve);
        preserved.Append(" \tA\r", preservedBuffer, 16, CancellationToken.None);
        preserved.Append("\n A  ", preservedBuffer, 16, CancellationToken.None);
        using BudgetedText preservedText = preservedBuffer.Detach();

        Assert.Equal("  A   A  ", preservedText.Value);
        Assert.Equal(9, preserved.SourceCharacterCount);
        var denied = new SvgTextWhitespaceNormalizer();
        using var deniedBuffer = new BudgetedTextBuffer(new RenderBudget(1_000));
        SvgException exception = Assert.Throws<SvgException>(() => denied.Append("     ", deniedBuffer, 4, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Retains_only_normalized_letter_spacing_intervals_that_differ_from_the_parent_text()
    {
        RenderBudget budget = new(2_000);
        using var buffer = new SvgTextLayoutBuffer(budget, 0, CancellationToken.None);
        var parent = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 0);
        var spaced = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 5);

        parent.Append("A", buffer, 10, CancellationToken.None);
        spaced.Append("A", buffer, 10, CancellationToken.None);
        parent.Append("A", buffer, 10, CancellationToken.None);
        using BudgetedTextLayout text = buffer.Detach();

        Assert.Equal("AAA", text.Value);
        Assert.Equal([new SvgTextLetterSpacingSpan(0, 1, 0), new SvgTextLetterSpacingSpan(1, 2, 5), new SvgTextLetterSpacingSpan(2, 3, 0)], text.LetterSpacingSpans);
        Assert.True(budget.Snapshot().CurrentBytes > 0);
        text.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retains_a_positioned_span_offset_for_its_first_descendant_character()
    {
        RenderBudget budget = new(2_000);
        using var buffer = new SvgTextLayoutBuffer(budget, 0, CancellationToken.None);
        var descendant = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 0, 2, false);

        buffer.EnterPositioningSpan(new SvgTextPositionOffset(5, -2), 1, CancellationToken.None);
        descendant.Append("AA", buffer, 10, CancellationToken.None);
        buffer.ExitPositioningSpan(1);
        using BudgetedTextLayout text = buffer.Detach();

        Assert.Equal("AA", text.Value);
        Assert.Equal([new SvgTextPositionAdjustment(0, 5, -2)], text.PositionAdjustments);
        Assert.True(budget.Snapshot().CurrentBytes > 0);
        text.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Lets_a_nearer_zero_position_offset_override_an_outer_span()
    {
        RenderBudget budget = new(2_000);
        using var buffer = new SvgTextLayoutBuffer(budget, 0, CancellationToken.None);
        var descendant = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 0, 2, true);

        buffer.EnterPositioningSpan(new SvgTextPositionOffset(5, -2), 1, CancellationToken.None);
        buffer.EnterPositioningSpan(new SvgTextPositionOffset(0, 0), 2, CancellationToken.None);
        descendant.Append("A", buffer, 10, CancellationToken.None);
        buffer.ExitPositioningSpan(2);
        buffer.ExitPositioningSpan(1);
        using BudgetedTextLayout text = buffer.Detach();

        Assert.Equal("A", text.Value);
        Assert.Empty(text.PositionAdjustments);
    }

    [Fact]
    public void Resolves_each_position_offset_axis_from_its_nearest_span()
    {
        RenderBudget budget = new(2_000);
        using var buffer = new SvgTextLayoutBuffer(budget, 0, CancellationToken.None);
        var descendant = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 0, 2, true);

        buffer.EnterPositioningSpan(new SvgTextPositionOffset(5, null), 1, CancellationToken.None);
        buffer.EnterPositioningSpan(new SvgTextPositionOffset(null, -2), 2, CancellationToken.None);
        descendant.Append("A", buffer, 10, CancellationToken.None);
        buffer.ExitPositioningSpan(2);
        buffer.ExitPositioningSpan(1);
        using BudgetedTextLayout text = buffer.Detach();

        Assert.Equal("A", text.Value);
        Assert.Equal([new SvgTextPositionAdjustment(0, 5, -2)], text.PositionAdjustments);
    }

    [Fact]
    public void Retains_absolute_zero_and_resolves_coordinates_separately_from_relative_offsets()
    {
        RenderBudget budget = new(2_000);
        using var buffer = new SvgTextLayoutBuffer(budget, 0, CancellationToken.None);
        var descendant = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 0, 2, true);

        buffer.EnterPositioningSpan(new SvgTextPositionOffset(12, 40, 5, null), 1, CancellationToken.None);
        buffer.EnterPositioningSpan(new SvgTextPositionOffset(0, null, 0, -2), 2, CancellationToken.None);
        descendant.Append(" AA", buffer, 10, CancellationToken.None);
        buffer.ExitPositioningSpan(2);
        buffer.ExitPositioningSpan(1);
        using BudgetedTextLayout text = buffer.Detach();

        Assert.Equal("AA", text.Value);
        Assert.Equal([new SvgTextPositionAdjustment(0, 0, 40, 0, -2)], text.PositionAdjustments);
        text.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Empty_absolute_position_scopes_do_not_reposition_later_parent_text()
    {
        RenderBudget budget = new(2_000);
        using var buffer = new SvgTextLayoutBuffer(budget, 0, CancellationToken.None);
        var empty = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 0, 1, true);
        var parent = new SvgTextWhitespaceNormalizer(SvgTextWhitespaceMode.Default, 0);

        buffer.EnterPositioningSpan(new SvgTextPositionOffset(0, 0, null, null), 1, CancellationToken.None);
        empty.Append(" \t", buffer, 10, CancellationToken.None);
        buffer.ExitPositioningSpan(1);
        parent.Append("A", buffer, 10, CancellationToken.None);
        using BudgetedTextLayout text = buffer.Detach();

        Assert.Equal("A", text.Value);
        Assert.Empty(text.PositionAdjustments);
    }
}
