using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternResolutionTests
{
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
            RenderBudget budget = new(20_000_000);
            ParserWorkCounter work = new(count * 40 + 2000);
            using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), SvgRenderLimits.Default, budget, null, work, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan(0, 4).ToArray());
            if (previous != 0) Assert.True(work.Used <= previous * 5 / 2);
            previous = work.Used;
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
        RenderBudget budget = new(20_000_000);
        ParserWorkCounter work = new(100_000);
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), SvgRenderLimits.Default, budget, null, work, CancellationToken.None))
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
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retried_forward_graph_releases_storage_at_each_work_boundary()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='ready' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='4'/></pattern><pattern id='alias' href='#ready'/><pattern id='p' href='#q'/><pattern id='r' href='#p'/></defs><rect width='1' height='1'/><defs><pattern id='q' href='#ready'/></defs><rect width='8' height='8' fill='url(#r)'/></svg>"u8.ToArray();
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(10_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget denied = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }
}
