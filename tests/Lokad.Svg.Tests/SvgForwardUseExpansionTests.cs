using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgForwardUseExpansionTests
{
    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Clipped_forward_uses_stay_within_linear_work(int count)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(50_000_000));
        using (SvgRoot root = SvgRootParser.Parse(Repeated(count, true), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Twelve_thousand_clipped_forward_uses_match_backward_definitions()
        => Assert.Equal(Render(Repeated(12_000, false)), Render(Repeated(12_000, true)));

    [Fact]
    public void Mixed_empty_single_and_multiple_geometry_uses_keep_paint_and_effect_order()
        => Assert.Equal(Render(Mixed(false)), Render(Mixed(true)));

    [Fact]
    public void Expansion_remaps_command_boundaries_only_for_the_owning_scene()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        using (SvgSceneBuilder foreign = new(budget, CancellationToken.None))
        {
            SvgGeometry rectangle = SvgGeometry.FromRectangle(new(0, 0, 1, 1, SvgColor.Transparent, true, null));
            SvgUseReference reference = new("p", 0, 0, 0, 0, SvgAffineTransform.Identity, SvgAffineTransform.Identity, 255, true, null, null, null);
            for (int index = 0; index < 2; index++)
            {
                SvgReferenceResolver.ResolveOrDefer(reference, state.Definitions, state.PendingUses, state.Scene, null, null, null, null, state, CancellationToken.None);
                state.Scene.AddGeometry(rectangle);
            }
            state.Definitions.TryAdd("p", new([rectangle, rectangle], false, null, null));
            state.PendingTextRuns.Add(new(null, null, SvgAffineTransform.Identity, SvgColor.Transparent, 2, budget.Reserve(1, SvgMemoryCategory.Parser), state.Scene));
            state.PendingTextRuns.Add(new(null, null, SvgAffineTransform.Identity, SvgColor.Transparent, 2, budget.Reserve(1, SvgMemoryCategory.Parser), foreign));
            state.PendingGroupClips.Add(new("c", null, SvgAffineTransform.Identity, 1, 4, 0, state.Scene));
            state.PendingGroupClips.Add(new("c", null, SvgAffineTransform.Identity, 1, -1, 0, foreign));
            state.PendingGroupMasks.Add(new("m", 1, 3, 0, null, SvgAffineTransform.Identity, state.Scene, false));
            state.PendingGroupMasks.Add(new("m", 1, -1, 0, null, SvgAffineTransform.Identity, foreign, false));

            SvgReferenceResolver.ResolvePendingUses(state.Definitions, state.PendingUses, state.Scene, state, CancellationToken.None);

            Assert.Equal(6, state.Scene.CommandCount);
            Assert.Equal(3, state.PendingTextRuns[0].CommandIndex);
            Assert.Equal(2, state.PendingTextRuns[1].CommandIndex);
            Assert.Equal(2, state.PendingGroupClips[0].FirstCommandIndex);
            Assert.Equal(6, state.PendingGroupClips[0].EndCommandIndex);
            Assert.Equal(1, state.PendingGroupClips[1].FirstCommandIndex);
            Assert.Equal(-1, state.PendingGroupClips[1].EndCommandIndex);
            Assert.Equal(2, state.PendingGroupMasks[0].FirstCommandIndex);
            Assert.Equal(5, state.PendingGroupMasks[0].EndCommandIndex);
            Assert.Equal(1, state.PendingGroupMasks[1].FirstCommandIndex);
            Assert.Equal(-1, state.PendingGroupMasks[1].EndCommandIndex);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Batch_expansion_releases_storage_on_memory_denial_and_cancellation()
    {
        byte[] input = Mixed(true);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new RenderBudget(new HostMemoryBudget(2_000_000));
        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, null, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Repeated(int count, bool forward)
    {
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>");
        const string source = "<defs><polygon id='p' points='0,0 4,0 0,4' fill='red' stroke='blue' stroke-width='1'/></defs>";
        if (!forward) xml.Append(source);
        for (int index = 0; index < count; index++) xml.Append("<g clip-path='url(#c)'><use href='#p'/></g>");
        if (forward) xml.Append(source);
        xml.Append("<defs><clipPath id='c'><rect width='8' height='8'/></clipPath></defs></svg>");
        return System.Text.Encoding.UTF8.GetBytes(xml.ToString());
    }

    private static byte[] Mixed(bool forward)
    {
        const string source = "<defs><rect id='single' width='2' height='2' fill='red'/><polygon id='multi' points='0,0 4,0 0,4' fill='red' stroke='blue' stroke-width='1'/><symbol id='empty'/><g id='group'><rect width='2' height='2' fill='blue'/><rect x='2' width='2' height='2' fill='red'/></g></defs>";
        const string body = "<g opacity='.7' clip-path='url(#c)' mask='url(#m)'><use href='#single'/><rect x='2' y='2' width='2' height='2' fill='blue'/><use href='#multi' x='1' filter='url(#f)'/><use href='#empty'/><use href='#group' y='4'/></g><rect x='7' width='1' height='8' fill='green'/>";
        const string effects = "<defs><clipPath id='c'><rect width='7' height='8'/></clipPath><mask id='m'><rect width='8' height='8' fill='white'/></mask><filter id='f'><feOffset dx='1' dy='0'/></filter></defs>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + (forward ? body + source : source + body) + effects + "</svg>");
    }

    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
