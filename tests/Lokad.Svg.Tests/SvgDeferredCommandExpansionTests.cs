using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDeferredCommandExpansionTests
{
    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Clipped_forward_paints_stay_within_linear_work(int count)
    {
        RenderBudget budget = new(50_000_000);
        using (SvgRoot root = SvgRootParser.Parse(Repeated(count, true), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Twelve_thousand_clipped_forward_paints_match_earlier_definitions()
        => Assert.Equal(Render(Repeated(12_000, false)), Render(Repeated(12_000, true)));

    [Fact]
    public void Empty_single_multi_and_filtered_paints_keep_interleaved_use_and_scope_order()
        => Assert.Equal(Render(Mixed(false)), Render(Mixed(true)));

    [Fact]
    public void Batch_maps_original_and_new_scope_boundaries_without_touching_other_scenes()
    {
        RenderBudget budget = new(100_000);
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        using (SvgSceneBuilder foreign = new(budget, CancellationToken.None))
        {
            SvgGeometry rectangle = SvgGeometry.FromRectangle(new(0, 0, 1, 1, SvgColor.Transparent, true, null));
            int first = state.Scene.AddPaintPlaceholder();
            state.Scene.AddGeometry(rectangle);
            int second = state.Scene.AddPaintPlaceholder();
            state.Scene.AddGeometry(rectangle);
            state.PendingTextRuns.Add(new(null, null, SvgAffineTransform.Identity, SvgColor.Transparent, second, budget.Reserve(1, SvgMemoryCategory.Parser), state.Scene));
            state.PendingTextRuns.Add(new(null, null, SvgAffineTransform.Identity, SvgColor.Transparent, second, budget.Reserve(1, SvgMemoryCategory.Parser), foreign));
            state.PendingGroupClips.Add(new("c", null, SvgAffineTransform.Identity, 1, 4, 0, state.Scene));
            state.PendingGroupClips.Add(new("c", null, SvgAffineTransform.Identity, 1, -1, 0, foreign));
            state.PendingGroupMasks.Add(new("m", 1, 3, 0, null, SvgAffineTransform.Identity, state.Scene, false));
            state.PendingGroupMasks.Add(new("m", 1, -1, 0, null, SvgAffineTransform.Identity, foreign, false));
            using SvgPaintCommandBatch commands = new(state, state.Scene, default, state.PendingCommandEnd, CancellationToken.None);
            SvgPaintCommandEmission later = commands.Begin(second);
            state.Scene.AddGeometries([rectangle, rectangle]);
            state.PendingGroupClips.Add(new("c", null, SvgAffineTransform.Identity, later.TailStart, later.TailStart + 2, 0, state.Scene));
            state.PendingGroupMasks.Add(new("m", later.TailStart, later.TailStart + 1, 0, null, SvgAffineTransform.Identity, state.Scene, false));
            commands.Retain(later);
            SvgPaintCommandEmission earlier = commands.Begin(first);
            state.Scene.AddGeometries([rectangle, rectangle]);
            state.PendingGroupMasks.Add(new("m", earlier.TailStart, earlier.TailStart + 2, 0, null, SvgAffineTransform.Identity, state.Scene, false));
            commands.Retain(earlier);
            commands.Complete();

            Assert.Equal(6, state.Scene.CommandCount);
            Assert.Equal(3, state.PendingTextRuns[0].CommandIndex);
            Assert.Equal(second, state.PendingTextRuns[1].CommandIndex);
            Assert.Equal((2, 6), (state.PendingGroupClips[0].FirstCommandIndex, state.PendingGroupClips[0].EndCommandIndex));
            Assert.Equal((1, -1), (state.PendingGroupClips[1].FirstCommandIndex, state.PendingGroupClips[1].EndCommandIndex));
            Assert.Equal((3, 5), (state.PendingGroupClips[2].FirstCommandIndex, state.PendingGroupClips[2].EndCommandIndex));
            Assert.Equal((2, 5), (state.PendingGroupMasks[0].FirstCommandIndex, state.PendingGroupMasks[0].EndCommandIndex));
            Assert.Equal((1, -1), (state.PendingGroupMasks[1].FirstCommandIndex, state.PendingGroupMasks[1].EndCommandIndex));
            Assert.Equal((3, 4), (state.PendingGroupMasks[2].FirstCommandIndex, state.PendingGroupMasks[2].EndCommandIndex));
            Assert.Equal((0, 2), (state.PendingGroupMasks[3].FirstCommandIndex, state.PendingGroupMasks[3].EndCommandIndex));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancellation_before_batch_completion_releases_scratch_and_scene_owners()
    {
        RenderBudget budget = new(100_000);
        using CancellationTokenSource cancellation = new();
        using (SvgDocumentState state = new(budget, cancellation.Token))
        {
            int slot = state.Scene.AddPaintPlaceholder();
            using SvgPaintCommandBatch commands = new(state, state.Scene, default, state.PendingCommandEnd, cancellation.Token);
            SvgPaintCommandEmission emission = commands.Begin(slot);
            commands.Retain(emission);
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(commands.Complete).CancellationToken);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Batch_replay_releases_storage_on_memory_denial_and_cancellation()
    {
        byte[] input = Mixed(true);
        RenderBudget measured = new(2_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(2_000_000);
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Repeated(int count, bool forward)
    {
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>");
        const string source = "<defs><linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='red'/></linearGradient></defs>";
        if (!forward) xml.Append(source);
        for (int index = 0; index < count; index++) xml.Append("<g clip-path='url(#c)'><polygon points='0,0 4,0 0,4' fill='url(#p)' stroke='blue' stroke-width='1'/></g>");
        if (forward) xml.Append(source);
        xml.Append("<defs><clipPath id='c'><rect width='8' height='8'/></clipPath></defs></svg>");
        return System.Text.Encoding.UTF8.GetBytes(xml.ToString());
    }

    private static byte[] Mixed(bool forward)
    {
        const string paint = "<defs><linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='red'/></linearGradient></defs>";
        const string body = "<g opacity='.7' clip-path='url(#c)' mask='url(#m)'><polygon points='0,0 4,0 0,4' fill='url(#p)' stroke='blue' stroke-width='1'/><rect x='2' y='2' width='2' height='2' fill='green'/><path d='' fill='url(#p)'/><rect x='3' y='1' width='2' height='2' fill='url(#p)'/><use href='#u'/><polygon points='0,0 4,0 0,4' fill='url(#p)' stroke='blue' stroke-width='1' filter='url(#f)' mask='url(#m)'/></g><svg x='1' y='1' width='6' height='6'><rect width='2' height='2' fill='url(#p)'/></svg><rect x='7' width='1' height='8' fill='green'/>";
        const string effects = "<defs><rect id='u' x='4' y='4' width='2' height='2' fill='blue'/><clipPath id='c'><rect width='7' height='8'/></clipPath><mask id='m'><rect width='8' height='8' fill='white'/></mask><filter id='f'><feOffset dx='1' dy='0'/></filter></defs>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + (forward ? body + paint : paint + body) + effects + "</svg>");
    }

    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
