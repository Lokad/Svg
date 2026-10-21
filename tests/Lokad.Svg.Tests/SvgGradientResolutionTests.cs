using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientResolutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Forward_chain_resolution_has_linear_work_in_both_orders(bool radial, bool reverse)
    {
        long previous = 0;
        foreach (int count in new[] { 128, 256, 512 })
        {
            byte[] input = Chain(count, radial, reverse);
            RenderBudget budget = new(20_000_000);
            ParserWorkCounter work = new(count * 40 + 2000);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, work, CancellationToken.None)) { }
            if (previous != 0) Assert.True(work.Used <= previous * 5 / 2);
            previous = work.Used;
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Twenty_thousand_aliases_render_identically_in_both_orders(bool radial)
    {
        byte[] control = SvgConverter.ConvertToPng(Chain(0, radial, false), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        foreach (bool reverse in new[] { false, true })
            Assert.Equal(control, SvgConverter.ConvertToPng(Chain(20_000, radial, reverse), SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Unrelated_definitions_do_not_rewalk_waiting_aliases()
    {
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs>");
        for (int index = 0; index < 1024; index++) xml.Append("<linearGradient id='g").Append(index).Append("' href='#later'/>");
        for (int index = 0; index < 1024; index++) xml.Append("<radialGradient id='unused").Append(index).Append("'/>");
        xml.Append("</defs><defs><linearGradient id='later'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='4' height='4' fill='url(#g0)'/></svg>");
        RenderBudget budget = new(20_000_000);
        ParserWorkCounter work = new(50_000);
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), SvgRenderLimits.Default, budget, null, work, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<linearGradient id='g0' href='#missing'/>")]
    [InlineData("<radialGradient id='g0' href='#missing'/>")]
    [InlineData("<linearGradient id='g0' href='#g0'/>")]
    [InlineData("<linearGradient id='g0' href='#g1'/><radialGradient id='g1' href='#g0'/>")]
    [InlineData("<linearGradient id='a' href='#base'/><linearGradient id='base'/><radialGradient id='g0' href='#missing'/>")]
    public void Missing_and_cyclic_templates_keep_their_failure_category_and_cleanup(string definitions)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs>" + definitions + "</defs><rect width='4' height='4' fill='url(#g0)'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Mixed_forward_templates_release_storage_at_work_memory_and_cancellation_boundaries()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs><linearGradient id='g0' href='#g1'/><radialGradient id='g1' href='#base'/><linearGradient id='g2' href='#base'/><radialGradient id='g3' href='#g0'/></defs><defs><linearGradient id='base'><stop stop-color='red'/><stop offset='1' stop-color='red'/></linearGradient></defs><rect width='4' height='4' fill='url(#g3)'/></svg>"u8.ToArray();
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
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(1_000_000);
        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, null, new(10_000), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Chain(int count, bool radial, bool reverse)
    {
        string kind = radial ? "radialGradient" : "linearGradient";
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs>");
        for (int at = 0; at < count; at++)
        {
            int index = reverse ? count - at - 1 : at;
            xml.Append('<').Append(kind).Append(" id='g").Append(index).Append("' href='#g").Append(index + 1).Append("'/>");
        }
        xml.Append('<').Append(kind).Append(" id='g").Append(count).Append("'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></").Append(kind).Append("></defs><rect width='4' height='4' fill='url(#g0)'/></svg>");
        return System.Text.Encoding.UTF8.GetBytes(xml.ToString());
    }
}
