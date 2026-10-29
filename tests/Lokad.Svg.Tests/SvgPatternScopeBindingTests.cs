using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternScopeBindingTests
{
    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Separate_tile_group_clips_keep_linear_parser_work(int count)
    {
        RenderBudget budget = new(30_000_000);
        using (SvgRoot root = SvgRootParser.Parse(Repeated(count, true), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Seven_thousand_group_clips_match_one_enclosing_clip_under_default_limits()
        => Assert.Equal(Render(Repeated(7000, false)), Render(Repeated(7000, true)));

    [Theory]
    [InlineData(256)]
    [InlineData(1024)]
    public void Separate_tile_scenes_do_not_rescan_unrelated_document_ranges(int count)
    {
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>");
        for (int index = 0; index < count; index++) xml.Append("<g clip-path='url(#c)'><rect width='1' height='1' fill='red'/></g>");
        xml.Append("<defs>");
        for (int index = 0; index < count; index++)
            xml.Append("<pattern id='p").Append(index).Append("' patternUnits='userSpaceOnUse' width='8' height='8'><g clip-path='url(#c)'><rect width='4' height='4' fill='red'/></g></pattern>");
        xml.Append("<clipPath id='c'><rect width='8' height='8'/></clipPath></defs><rect width='8' height='8' fill='url(#p0)'/></svg>");
        RenderBudget budget = new(40_000_000);
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Sibling_masks_with_shared_recording_context_bind_to_their_own_groups()
    {
        const string actual = "<g mask='url(#white)'><rect width='2' height='8' fill='red'/></g><g mask='url(#m)'><rect x='4' width='2' height='8' fill='blue'/></g>";
        const string expected = "<rect width='2' height='8' fill='red'/><rect x='4' width='2' height='8' fill='blue' opacity='.5'/>";
        Assert.Equal(Render(Tile(expected)), Render(Tile(actual)));
    }

    [Fact]
    public void Nested_object_clips_masks_and_expanding_uses_keep_composition_order()
        => Assert.Equal(Render(Mixed(false)), Render(Mixed(true)));

    [Fact]
    public void Filtered_leaf_masks_keep_boundaries_through_group_compaction_and_use_expansion()
    {
        const string leaf = "<rect width='8' height='8' fill='red' filter='url(#f)' mask='url(#m)'{0}/>";
        string actual = "<use href='#r'/><g clip-path='url(#c)'>" + string.Format(leaf, string.Empty) + "</g><use href='#r' x='6'/>";
        string expected = "<use href='#r'/>" + string.Format(leaf, " clip-path='url(#c)'") + "<use href='#r' x='6'/>";
        Assert.Equal(Render(Tile(expected)), Render(Tile(actual)));
    }

    [Fact]
    public void Scope_binding_releases_owners_on_memory_denial_and_cancellation()
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

    private static byte[] Repeated(int count, bool split)
    {
        StringBuilder body = new();
        if (!split) body.Append("<g clip-path='url(#c)'>");
        for (int index = 0; index < count; index++)
        {
            if (split) body.Append("<g clip-path='url(#c)'>");
            body.Append("<rect width='4' height='4' fill='red'/>");
            if (split) body.Append("</g>");
        }
        if (!split) body.Append("</g>");
        return Tile(body.ToString());
    }

    private static byte[] Mixed(bool grouped)
    {
        const string source = "<g clip-path='url(#c)'><use href='#shape'/></g><g clip-path='url(#obj)'><rect x='4' width='4' height='8' fill='blue'/></g><g clip-path='url(#c)'><path d=''/></g>";
        const string direct = "<rect width='4' height='8' fill='red'/><rect x='2' y='2' width='2' height='4' fill='blue'/><rect x='4' width='2' height='8' fill='blue'/>";
        return Tile("<g opacity='.7' mask='url(#m)'>" + (grouped ? source : direct) + "</g><rect x='6' y='6' width='2' height='2' fill='green'/>");
    }

    private static byte[] Tile(string body)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'>" + body + "</pattern><rect id='r' width='1' height='1' fill='blue'/><g id='shape'><rect width='8' height='8' fill='red'/><rect x='2' y='2' width='4' height='4' fill='blue'/></g><clipPath id='c'><rect width='4' height='8'/></clipPath><clipPath id='obj' clipPathUnits='objectBoundingBox'><rect width='.5' height='1'/></clipPath><mask id='m' maskContentUnits='objectBoundingBox'><rect width='1' height='1' fill='#808080'/></mask><mask id='white' maskContentUnits='objectBoundingBox'><rect width='1' height='1' fill='white'/></mask><filter id='f'><feOffset dx='0' dy='0'/></filter></defs><rect width='8' height='8' fill='url(#p)'/></svg>");

    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
