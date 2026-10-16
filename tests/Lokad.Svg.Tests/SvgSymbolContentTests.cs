using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgSymbolContentTests
{
    [Theory]
    [InlineData("display='unknown'")]
    [InlineData("visibility='collapse'")]
    public void Invalid_symbol_presentation_releases_retained_sources(string presentation)
    {
        RenderBudget budget = new(1_000_000);
        byte[] input = Input("<symbol id='s' " + presentation + "><rect width='4' height='4'/></symbol><use href='#s'/>");
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<symbol id='s'><use href='#s'/></symbol>", SvgErrorCode.MalformedInput)]
    [InlineData("<symbol id='a'><use href='#b'/></symbol><symbol id='b'><use href='#a'/></symbol>", SvgErrorCode.MalformedInput)]
    [InlineData("<symbol><g id='a'><g><use href='#a'/></g></g></symbol>", SvgErrorCode.MalformedInput)]
    [InlineData("<symbol><use id='a' href='#a'/></symbol>", SvgErrorCode.MalformedInput)]
    [InlineData("<symbol><g><use href='#missing'/></g></symbol>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<symbol id='s'><g><use href='#missing'/></g></symbol><use href='#s'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<symbol id='s'><g id='s'/></symbol>", SvgErrorCode.MalformedInput)]
    [InlineData("<symbol><g id='a'/><use id='a' href='#a'/></symbol>", SvgErrorCode.MalformedInput)]
    [InlineData("<symbol><g id='a'><rect id='a' width='4' height='4'/></g></symbol>", SvgErrorCode.MalformedInput)]
    public void Invalid_graphs_and_duplicate_ids_release_retained_sources(string content, SvgErrorCode error)
    {
        RenderBudget budget = new(10_000_000);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(content), SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(error, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Diamond_dependencies_preserve_repeated_paint_order()
    {
        byte[] actual = Input("<use href='#a'/><symbol id='a'><use href='#b'/><use href='#c' x='4'/></symbol><symbol id='b'><use href='#leaf'/></symbol><symbol id='c'><g><use href='#leaf'/></g></symbol><defs><rect id='leaf' width='8' height='8' fill='red'/></defs>");
        byte[] expected = Input("<rect width='8' height='8' fill='red'/><rect x='4' width='8' height='8' fill='red'/>");
        Assert.Equal(Render(expected), Render(actual));
    }

    [Theory]
    [InlineData("<symbol id='empty'/>")]
    [InlineData("<symbol id='empty'><g/><g><g/></g></symbol>")]
    public void Empty_forward_references_preserve_sibling_commands(string definition)
    {
        string siblings = "<rect width='4' height='8' fill='red'/><use href='#empty'/><rect x='8' width='4' height='8' fill='blue'/>";
        Assert.Equal(Render(Input(siblings.Replace("<use href='#empty'/>", ""))), Render(Input(siblings + definition)));
    }

    [Fact]
    public void Deep_flat_reference_chains_expand_without_recursive_geometry_calls()
    {
        StringBuilder content = new("<use href='#s0'/>");
        for (int index = 0; index < 2048; index++)
            content.Append("<symbol id='s").Append(index).Append("'><use href='#s").Append(index + 1).Append("'/></symbol>");
        content.Append("<symbol id='s2048'><rect width='4' height='4' fill='red'/></symbol>");
        Assert.Equal(Render(Input("<rect width='4' height='4' fill='red'/>")), Render(Input(content.ToString())));
    }

    [Fact]
    public void Exponential_expansion_observes_work_limits_and_releases_partial_output()
    {
        StringBuilder content = new("<use href='#s0'/>");
        for (int index = 0; index < 24; index++)
            content.Append("<symbol id='s").Append(index).Append("'><use href='#s").Append(index + 1).Append("'/><use href='#s").Append(index + 1).Append("'/></symbol>");
        content.Append("<symbol id='s24'><circle r='3'/></symbol>");
        RenderBudget budget = new(100_000_000);
        ParserWorkCounter work = new(50_000);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(content.ToString()), SvgRenderLimits.Default, budget, null, work, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.True(work.Used >= 49_999);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse(Input("<symbol id='s'><g><circle r='3'/></g></symbol><use href='#s'/>"), SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.Single(retry.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void Cancellation_during_content_sealing_preserves_sources_for_retry(int checkpoint)
    {
        RenderBudget budget = new(1_000_000);
        ParserWorkCounter work = new(100_000);
        SvgSymbolPresentation presentation = new(SvgPaint.Default, SvgInheritedStrokeStyle.Default, null, false, null, null, SvgPaintOpacity.Opaque);
        using (SvgDocumentState state = new(budget, work, CancellationToken.None))
        using (SvgSymbolDefinitionBuilder builder = SvgSymbolDefinitionBuilder.Create(budget, work, CancellationToken.None, presentation))
        {
            SvgPolygonFill fill = SvgPolygonFill.Create([new(0, 0), new(8, 0), new(0, 8)], [3], null, new(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
            try { builder.Add(new([SvgGeometry.FromPolygonFill(fill)], false, null, null) { SymbolFillBindings = [new(true, null, null)] }); }
            catch { fill.Dispose(); throw; }
            SvgSymbolContent content = builder.EnableContent();
            content.BeginGroup(2, null, presentation, new(SvgPaint.Default, false, null, null, SvgPaintOpacity.Opaque, true, null), SvgAffineTransform.Identity);
            Assert.True(content.EndGroup(2, state));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => builder.Build(null, state, new(0), CancellationToken.None)).ErrorCode);
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            int copied = 0;
            builder.AssemblyCheckpoint = count => { copied = count; if (count == checkpoint) cancellation.Cancel(); };
            OperationCanceledException cancelled = Assert.ThrowsAny<OperationCanceledException>(() => builder.Build(null, state, new(100_000), cancellation.Token));
            Assert.Equal(checkpoint, copied);
            Assert.Equal(cancellation.Token, cancelled.CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            builder.AssemblyCheckpoint = null;
            SvgDefinition definition = builder.Build(null, state, new(100_000), CancellationToken.None);
            try
            {
                SvgSymbolContentRange range = Assert.IsType<SvgSymbolContentRange>(definition.SymbolContent);
                Assert.Equal(5, range.Count);
                Assert.Equal(1, range.Content.Nodes.ToArray().Count(value => value.Kind == SvgSymbolContentKind.Leaf));
            }
            finally { definition.SymbolContent?.Content.Dispose(); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Nested_nonrendering_groups_preserve_font_discovery_and_late_stylesheets()
    {
        RenderBudget budget = new(1_000_000);
        byte[] input = Input("<symbol id='s'><g font-family='Unused'><g><circle r='3'/></g></g></symbol><use href='#s'/><text font-family='Example'>A</text>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(100_000), budget, CancellationToken.None)).Family);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        byte[] lateStyle = Input("<symbol id='s'><g/></symbol><style>rect { fill: red; }</style><rect width='4' height='4'/>");
        Assert.Equal(SvgConverter.ConvertToPng(Input("<rect width='4' height='4' fill='red'/>"), SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(lateStyle, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Empty(SvgFontRequestDiscovery.Discover(lateStyle, SvgRenderLimits.Default, new(100_000), budget, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
