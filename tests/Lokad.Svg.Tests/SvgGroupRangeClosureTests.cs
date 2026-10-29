using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGroupRangeClosureTests
{
    [Theory]
    [InlineData(128, "clip")]
    [InlineData(512, "clip")]
    [InlineData(2048, "clip")]
    [InlineData(128, "mask")]
    [InlineData(512, "mask")]
    [InlineData(512, "both")]
    public void Plain_groups_after_deferred_scopes_keep_linear_parser_work(int count, string kind)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(40_000_000));
        using (SvgRoot root = SvgRootParser.Parse(Repeated(count, true, kind), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("clip")]
    [InlineData("mask")]
    public void Ten_thousand_scope_pairs_keep_default_limit_outcome_independent_of_group_order(string kind)
        => Assert.Equal(Render(Repeated(10_000, false, kind)), Render(Repeated(10_000, true, kind)));

    [Fact]
    public void Nested_sibling_and_definition_scopes_close_their_own_ranges()
        => Assert.Equal(Render(Mixed(false)), Render(Mixed(true)));

    [Fact]
    public void Direct_group_closure_releases_owners_on_memory_denial_and_cancellation()
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
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Repeated(int count, bool rangesFirst, string kind)
    {
        StringBuilder xml = new("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>");
        string effects = kind switch { "clip" => " clip-path='url(#c)'", "mask" => " mask='url(#m)'", "both" => " clip-path='url(#c)' mask='url(#m)'", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        if (rangesFirst) Append(effects);
        Append("");
        if (!rangesFirst) Append(effects);
        xml.Append(Resources).Append("</svg>");
        return System.Text.Encoding.UTF8.GetBytes(xml.ToString());

        void Append(string attributes)
        {
            for (int index = 0; index < count; index++) xml.Append("<g").Append(attributes).Append("><rect width='4' height='4' fill='red'/></g>");
        }
    }

    private static byte[] Mixed(bool forward)
    {
        const string body = "<g opacity='.7' clip-path='url(#c)' mask='url(#m)'><g><rect width='8' height='8' fill='red'/></g><g clip-path='url(#left)' mask='url(#m)' filter='url(#f)'><rect width='8' height='8' fill='blue'/><g><rect width='2' height='2' fill='green'/></g></g><g clip-path='url(#left)'><rect width='8' height='2' fill='green'/></g></g><g><rect x='7' width='1' height='8' fill='blue'/></g>";
        const string tile = "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='4' filter='url(#f)' mask='url(#m)'/><g clip-path='url(#c)'><rect width='2' height='2'/></g></pattern></defs>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + tile + (forward ? body + Resources : Resources + body) + "</svg>");
    }

    private const string Resources = "<defs><clipPath id='c'><rect width='8' height='8'/></clipPath><clipPath id='left'><rect width='4' height='8'/></clipPath><mask id='m'><rect width='8' height='8' fill='white'/></mask><filter id='f'><feOffset dx='0' dy='0'/></filter></defs>";

    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
