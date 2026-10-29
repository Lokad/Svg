using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgSymbolLocationTests
{
    [Theory]
    [InlineData("<symbol id='s'><circle r='3'/></symbol><defs><symbol id='s'/></defs>")]
    [InlineData("<defs><symbol id='s'/></defs><symbol id='s'><circle r='3'/></symbol>")]
    [InlineData("<symbol><circle id='leaf' r='3'/></symbol><symbol id='leaf'/>")]
    [InlineData("<symbol id='leaf'/><symbol><circle id='leaf' r='3'/></symbol>")]
    [InlineData("<g id='s'><symbol id='s'><circle r='3'/></symbol></g>")]
    [InlineData("<symbol id='s'/><g id='s'><circle r='3'/></g>")]
    public void Authored_ids_remain_unique_across_symbol_locations(string content)
    {
        RenderBudget budget = new(1_000_000);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(content), SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<style>rect { fill: red; }</style>")]
    [InlineData("<script/>")]
    [InlineData("<animate attributeName='opacity' values='0;1' dur='1s'/>")]
    [InlineData("<set attributeName='fill' to='red'/>")]
    public void Descriptive_symbol_metadata_does_not_hide_active_content(string active)
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(Input("<symbol id='s'><metadata>" + active + "</metadata><rect width='4' height='4'/></symbol><use href='#s'/>"), SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Metadata_geometry_neither_paints_nor_registers_child_ids()
    {
        byte[] input = Input("<symbol id='s'><metadata><rect id='leaf' width='16' height='16'/></metadata><rect width='4' height='4' fill='red'/></symbol><rect id='leaf' x='8' y='8' width='4' height='4' fill='blue'/><use href='#s'/><use href='#leaf' x='-4'/>");
        byte[] expected = Input("<rect x='8' y='8' width='4' height='4' fill='blue'/><rect width='4' height='4' fill='red'/><rect x='4' y='8' width='4' height='4' fill='blue'/>");
        Assert.True(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).Span.SequenceEqual(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).Span));
    }

    [Theory]
    [InlineData("<symbol/>")]
    [InlineData("<symbol id='s'/>")]
    [InlineData("<defs><symbol/></defs>")]
    public void Empty_symbols_allow_late_stylesheets_without_font_requests(string content)
    {
        byte[] input = Input(content + "<style>g { fill: red; }</style><rect width='4' height='4'/>");
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        Assert.Equal(SvgConverter.ConvertToPng(Input("<rect width='4' height='4'/>"), SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    public void Descriptive_symbol_nodes_observe_the_same_structural_limit_in_lowering_and_discovery(int maximumNodes, bool admitted)
    {
        byte[] input = Input("<symbol id='s'><title>Shape</title><desc>Original control</desc><metadata><m:note xmlns:m='urn:sample'/></metadata><rect width='4' height='4'/></symbol><use href='#s'/>");
        SvgRenderLimits limits = new(1_000_000, 8192, 32, 1024, 4096, maximumNodes, 128, 100_000, 16, 32);
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        if (admitted)
        {
            using (SvgRoot root = SvgRootParser.Parse(input, limits, budget, CancellationToken.None)) Assert.Single(root.Geometries);
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, limits, budget, CancellationToken.None));
        }
        else
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, limits, budget, CancellationToken.None)).ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<symbol id='s'><metadata><text font-family='Unused'>x</text></metadata><rect width='4' height='4'/></symbol>")]
    [InlineData("<g display='none'><symbol id='s'><rect width='4' height='4'/></symbol></g>")]
    public void Symbol_discovery_denial_and_retry_preserve_later_font_requests(string content)
    {
        byte[] input = Input(content + "<use href='#s'/><text font-family='Example'>A</text>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None)).Family);
        long peak = budget.Snapshot().PeakBytes;
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(peak - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException cancelled = Assert.ThrowsAny<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, cancellation.Token));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None)).Family);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
}
