using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternViewBoxTests
{
    [Theory]
    [InlineData("0 0 -1 8", SvgErrorCode.MalformedInput)]
    [InlineData("0 0 8", SvgErrorCode.MalformedInput)]
    [InlineData("0 0 1e100 8", SvgErrorCode.LimitExceeded)]
    public void Invalid_pattern_view_boxes_reject_and_release(string viewBox, SvgErrorCode expected)
    {
        byte[] input = Input("<defs><pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8' viewBox='" + viewBox + "'><rect width='8' height='8'/></pattern></defs><rect width='64' height='48' fill='url(#p)'/>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("0", "8")]
    [InlineData("8", "0")]
    public void Empty_view_box_tiles_do_not_remove_sibling_paint(string width, string height)
    {
        string sibling = "<rect width='3' height='3' fill='blue'/>";
        string pattern = "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='" + width + "' height='" + height + "' viewBox='0 0 8 8'><rect width='8' height='8' fill='red'/></pattern></defs>";
        Assert.Equal(Render(sibling), Render(pattern + "<rect width='64' height='48' fill='url(#p)'/>" + sibling));
    }

    [Fact]
    public void Pattern_view_box_does_not_replace_enclosing_percentage_bases()
    {
        string template = "<defs><pattern id='p' patternUnits='userSpaceOnUse' patternContentUnits='objectBoundingBox' width='16' height='12' viewBox='0 0 8 6'><rect width='100%' height='100%' fill='blue'/><rect x='25%' y='25%' width='50%' height='50%' fill='red'/></pattern></defs><rect width='64' height='48' fill='url(#p)'/>";
        string absolute = template.Replace("100%", "64", StringComparison.Ordinal).Replace("x='25%'", "x='16'", StringComparison.Ordinal)
            .Replace("y='25%'", "y='12'", StringComparison.Ordinal).Replace("width='50%'", "width='32'", StringComparison.Ordinal)
            .Replace("height='50%'", "height='24'", StringComparison.Ordinal).Replace("height='64'", "height='48'", StringComparison.Ordinal);
        Assert.Equal(Render(absolute), Render(template));
    }

    [Theory]
    [InlineData("<pattern id='p' href='#missing' viewBox='0 0 8 8'/>")]
    [InlineData("<pattern id='p' href='#q' viewBox='0 0 8 8'/><pattern id='q' href='#p' preserveAspectRatio='none'/>")]
    public void Invalid_view_box_template_graphs_reject_and_release(string patterns)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        byte[] input = Input("<defs>" + patterns + "</defs><rect width='64' height='48' fill='url(#p)'/>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void View_box_alias_replay_observes_work_storage_and_cancellation()
    {
        byte[] input = Input("<defs><pattern id='alias' href='#p' viewBox='0 2 8 8'/><pattern id='p' patternUnits='userSpaceOnUse' width='12' height='10' viewBox='0 0 8 8'><rect y='-4' width='8' height='8' fill='red'/><rect y='4' width='8' height='8' fill='blue'/></pattern></defs><rect width='64' height='48' fill='url(#alias)'/>");
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, null, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48'>" + content + "</svg>");
    private static byte[] Render(string content) => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
