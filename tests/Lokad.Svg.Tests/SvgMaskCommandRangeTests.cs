using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgMaskCommandRangeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Adjacent_mask_scopes_keep_distinct_values_and_do_not_isolate_empty_ranges(bool forward, bool empty)
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(Input(forward, empty), SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
        {
            SvgDrawCommand[] expected = empty
                ? [Begin(192, new(16.9, 8.7, 25.2, 27.6)), Geometry(0), End()]
                : [Begin(64, new(0.5, 2.9, 30, 25.2)), Geometry(0), End(), Begin(192, new(16.9, 8.7, 25.2, 27.6)), Geometry(1), End()];
            SvgLeafMaskAssertions.CommandSequence(expected, scene.Commands);
            Assert.Equal(empty ? 1 : 2, scene.Geometries.Length);
            Assert.All(scene.Geometries, geometry => Assert.Equal(255, geometry.Rectangle.Color.Alpha));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expanded_reference_commands_move_the_start_of_the_immediately_following_mask_scope(bool forward)
    {
        const string masks = "<defs><mask id='a'><rect width='47' height='41' fill='#404040'/></mask><mask id='b'><rect width='47' height='41' fill='#c0c0c0'/></mask></defs>";
        const string body = "<g mask='url(#a)'><use href='#p'/></g><g mask='url(#b)'><rect x='19' y='11' width='21' height='23' fill='#3b82bd'/></g>";
        const string target = "<defs><g id='p' fill='#b14676'><rect width='10' height='4'/><rect y='4' width='4' height='6'/></g></defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='47' height='41'>" + (forward ? body + masks : masks + body) + target + "</svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
        {
            SvgLeafMaskAssertions.CommandSequence([Begin(64, new(-1, -1, 12, 12)), Geometry(0), Geometry(2), End(), Begin(192, new(16.9, 8.7, 25.2, 27.6)), Geometry(1), End()], scene.Commands);
            Assert.Equal(3, scene.Geometries.Length);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_mask_scopes_release_storage_on_memory_work_or_token_denial(bool forward)
    {
        byte[] input = Input(forward, false);
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, null, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        RenderBudget denied = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(1_000_000), cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    private static SvgDrawCommand Begin(byte maskAlpha, SvgClipBox region) => new(SvgDrawCommandKind.BeginGroup, 1, 255, region, -1, maskAlpha);
    private static SvgDrawCommand Geometry(int index) => new(SvgDrawCommandKind.Geometry, index, 255, null, -1, 255);
    private static SvgDrawCommand End() => new(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255);

    private static byte[] Input(bool forward, bool empty)
    {
        const string masks = "<defs><mask id='a'><rect width='47' height='41' fill='#404040'/></mask><mask id='b'><rect width='47' height='41' fill='#c0c0c0'/></mask></defs>";
        string body = "<g mask='url(#a)'>" + (empty ? "" : "<rect x='3' y='5' width='25' height='21' fill='#b14676'/>") + "</g><g mask='url(#b)'><rect x='19' y='11' width='21' height='23' fill='#3b82bd'/></g>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='47' height='41'>" + (forward ? body + masks : masks + body) + "</svg>");
    }
}
