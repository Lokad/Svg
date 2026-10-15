using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgClipUnionTests
{
    [Fact]
    public void Crossing_union_matches_native_boundary_and_releases_temporary_storage()
    {
        // Independent native path-ops observation for the frozen mixed rectangle/path
        // control. The platform visual compares rendered pixels against Chrome separately.
        SvgClipPolygon input = new(
            [new(16, 122), new(108, 122), new(108, 24), new(16, 24),
             new(90, 112), new(172, 136), new(212, 55), new(82, 38)], [4, 8]);
        SvgDevicePoint[] expected =
        [
            new(108, 122), new(16, 122), new(16, 24), new(108, 24),
            new(108, 41.400001525878906), new(212, 55), new(172, 136),
            new(108, 117.26829528808594), new(108, 122)
        ];
        RenderBudget budget = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgAuthoredPath output = Assert.IsType<SvgAuthoredPath>(SvgClipUnion.ResolveCrossings(input, budget, work, CancellationToken.None)))
        {
            Assert.Equal(expected, output.Commands.ToArray().Where(command => command.Verb != SvgAuthoredPathVerb.Close).Select(command => command.First));
            Assert.Equal(SvgAuthoredPathVerb.Close, output.Commands[^1].Verb);
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 1, budget.Snapshot().PeakBytes / 2 })
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgClipUnion.ResolveCrossings(input, denied, new(1_000_000), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgClipUnion.ResolveCrossings(input, budget, new(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            SvgClipUnion.ResolveCrossings(input, budget, new(1_000_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgAuthoredPath retry = Assert.IsType<SvgAuthoredPath>(SvgClipUnion.ResolveCrossings(input, budget, new(1_000_000), CancellationToken.None)))
            Assert.Equal(expected, retry.Commands.ToArray().Where(command => command.Verb != SvgAuthoredPathVerb.Close).Select(command => command.First));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Disjoint_union_keeps_existing_normalization_and_releases_scratch()
    {
        SvgClipPolygon input = new([new(0, 0), new(0, 4), new(4, 0), new(8, 0), new(8, 4), new(12, 0)], [3, 6]);
        RenderBudget budget = new(1_000_000);
        Assert.Null(SvgClipUnion.ResolveCrossings(input, budget, new(1_000_000), CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
