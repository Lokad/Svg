using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternResolutionTests
{
    [Theory]
    [InlineData(128, false, false)]
    [InlineData(256, false, false)]
    [InlineData(512, false, false)]
    [InlineData(1024, false, false)]
    [InlineData(512, true, false)]
    [InlineData(512, false, true)]
    [InlineData(512, true, true)]
    public void Separate_defs_and_unrelated_definitions_do_not_retry_blocked_aliases(int count, bool chain, bool unrelated)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(30_000_000));
        using (SvgRoot root = SvgRootParser.Parse(SeparateDefinitions(count, true, chain, unrelated), SvgRenderLimits.Default, budget, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan(0, 4).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Seven_thousand_separate_alias_definitions_match_one_defs_block()
        => Assert.Equal(Render(SeparateDefinitions(7000, false, false, false)), Render(SeparateDefinitions(7000, true, false, false)));

    [Fact]
    public void Cancellation_before_ready_aliases_drain_releases_waiting_and_ready_storage()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using CancellationTokenSource cancellation = new();
        using (SvgDocumentState state = new(budget, cancellation.Token))
        {
            SvgPatternTemplate template = default;
            state.PatternDefinitions.TryAdd("alias", new(template with { Href = "base" }, template.Grid, null, default, false));
            SvgPatternReferenceResolver.Register(state, "alias", cancellation.Token);
            state.PatternDefinitions.TryAdd("base", new(template, template.Grid, null, default, false));
            state.Patterns.TryAdd("base", template.Grid);
            SvgPatternReferenceResolver.Register(state, "base", cancellation.Token);
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgPatternReferenceResolver.Resolve(state, true, cancellation.Token)).CancellationToken);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<pattern id='p' href='#missing'/>", "not defined")]
    [InlineData("<pattern id='p' href='#p'/>", "Cyclic")]
    [InlineData("<pattern id='p' href='#q'/></defs><defs><pattern id='q' href='#p'/>", "Cyclic")]
    public void Waiting_templates_keep_missing_and_cycle_diagnostics(string definitions, string message)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>" + definitions + "</defs></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        SvgException failure = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Contains(message, failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] SeparateDefinitions(int count, bool split, bool chain, bool unrelated)
    {
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>");
        for (int index = 0; index < count; index++)
        {
            xml.Append("<pattern id='a").Append(index).Append("' href='#").Append(chain && index + 1 < count ? "a" + (index + 1) : "base").Append("'/>");
            if (split) xml.Append("</defs><defs>");
        }
        if (unrelated)
            for (int index = 0; index < count; index++)
                xml.Append("<pattern id='u").Append(index).Append("' patternUnits='userSpaceOnUse' width='8' height='8'/></defs><rect width='1' height='1'/><defs>");
        xml.Append("<pattern id='base' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#a0)'/></svg>");
        return System.Text.Encoding.UTF8.GetBytes(xml.ToString());
    }

    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Forward_chain_work_grows_linearly_across_defs_boundaries(bool split, bool reverse)
    {
        long previous = 0;
        foreach (int count in new[] { 128, 256, 512 })
        {
            StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>");
            for (int at = 0; at < count; at++)
            {
                int index = reverse ? count - at - 1 : at;
                xml.Append("<pattern id='p").Append(index).Append("' href='#p").Append(index + 1).Append("'/>");
            }
            if (split) xml.Append("</defs><defs>");
            xml.Append("<pattern id='p").Append(count).Append("' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p0)'/></svg>");
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(20_000_000));
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), SvgRenderLimits.Default, budget, null, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan(0, 4).ToArray());
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (previous != 0) Assert.True(allocated <= previous * 5 / 2);
            previous = allocated;
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Later_shapes_do_not_rewalk_resolved_or_blocked_aliases(bool pending)
    {
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>");
        for (int index = 0; index < 512; index++) xml.Append("<pattern id='p").Append(index).Append("' href='#base'/>");
        if (!pending) AppendBase();
        xml.Append("</defs>");
        for (int index = 0; index < 1024; index++) xml.Append("<rect width='1' height='1'/>");
        if (pending) { xml.Append("<defs>"); AppendBase(); xml.Append("</defs>"); }
        xml.Append("<rect width='8' height='8' fill='url(#p0)'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(20_000_000));
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), SvgRenderLimits.Default, budget, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan(0, 4).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        void AppendBase() => xml.Append("<pattern id='base' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='4' fill='red'/></pattern>");
    }

    [Fact]
    public void Ten_thousand_aliases_render_identically_with_a_later_defs_block()
    {
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>");
        for (int index = 0; index < 10_000; index++) xml.Append("<pattern id='p").Append(index).Append("' href='#p").Append(index + 1).Append("'/>");
        int boundary = xml.Length;
        xml.Append("<pattern id='p10000' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p0)'/></svg>");
        byte[] contiguous = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        xml.Insert(boundary, "</defs><defs>");
        byte[] split = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(contiguous, split);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<defs><pattern id='q' href='#p'/></defs>")]
    public void Blocked_graphs_still_reject_missing_targets_and_later_cycles(string tail)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' href='#q'/></defs><rect width='1' height='1'/>" + tail + "</svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retried_forward_graph_releases_storage_at_each_work_boundary()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='ready' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='4'/></pattern><pattern id='alias' href='#ready'/><pattern id='p' href='#q'/><pattern id='r' href='#p'/></defs><rect width='1' height='1'/><defs><pattern id='q' href='#ready'/></defs><rect width='8' height='8' fill='url(#r)'/></svg>"u8.ToArray();
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }
}
