using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRenderablePathTests
{
    [Theory]
    [InlineData("0")]
    [InlineData(",")]
    [InlineData("\u00a0")]
    public void Invalid_tail_cannot_hide_full_input_command_and_work_limits(string error)
    {
        string value = "M0 0H8V8H0Z" + error + string.Concat(Enumerable.Repeat("M0 0", 20));
        RenderBudget budget = new(1_000_000);
        SvgException commands = Assert.Throws<SvgException>(() => SvgRenderablePathData.Read(value, 5, budget, new(100_000), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, commands.ErrorCode);
        SvgException work = Assert.Throws<SvgException>(() => SvgRenderablePathData.Read(value, 100, budget, new(1), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, work.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Prefix_storage_is_temporary_and_denial_leaves_no_owner()
    {
        const string valid = "M0 0H8V8H0Z";
        RenderBudget budget = new(1_000_000);
        using (SvgRenderablePathData original = SvgRenderablePathData.Read(valid, 10, budget, new(100_000), CancellationToken.None))
        {
            Assert.Same(valid, original.Value);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        using (SvgRenderablePathData shortened = SvgRenderablePathData.Read(valid + "0", 10, budget, new(100_000), CancellationToken.None))
        {
            Assert.Equal(valid, shortened.Value);
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(budget.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRenderablePathData.Read(valid + "0", 10, denied, new(100_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancelled_prefix_scan_preserves_the_token_and_borrowed_input()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget budget = new(1_000_000);
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRenderablePathData.Read("M0 0H8V8H0Z0", 10, budget, new(100_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
