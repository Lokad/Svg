using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> InactivePatternPrimitives()
    {
        foreach (string target in new[] { "opacity", "display", "visibility", "line", "polyline", "path" })
        foreach (bool stroke in target is "line" or "polyline" or "path" ? new[] { false } : new[] { false, true })
        foreach (string content in new[] { "text", "use", "mask-group", "mask-use" })
        foreach (string consumer in new[] { "direct", "identified", "use", "group", "symbol", "mask", "filter", "leaf-filter" })
            yield return [target, stroke, content, consumer];
    }

    [Theory]
    [MemberData(nameof(InactivePatternPrimitives))]
    public async Task Intrinsically_inactive_primitives_need_no_pattern_fonts(string target, bool stroke, string content, string consumer)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] expected = Render(Input("solid", "attribute", false, false));
        if (consumer is not ("filter" or "leaf-filter")) Assert.Equal(Render(EmptyPatternDocument("")), expected);
        foreach (string cascade in new[] { "attribute", "inline", "sheet" })
        foreach (bool forward in new[] { false, true })
        {
            byte[] input = Input(content, cascade, forward, false);
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
            Assert.Equal(expected, Render(input));
            using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            Assert.Empty(fonts.Fonts);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(0, provider.RequestCount);
        byte[] live = Input(content, "attribute", false, true);
        Assert.Single(SvgFontRequestDiscovery.Discover(live, CancellationToken.None));
        using SvgPreparedDocument control = await SvgPreparedDocument.CreateAsync(live, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        if (consumer is not ("filter" or "leaf-filter")) Assert.NotEqual(expected, control.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);

        byte[] Input(string tile, string cascade, bool forward, bool live)
        {
            string paint = stroke ? "fill='none' stroke='url(#p)' stroke-width='2'" : "fill='url(#p)'";
            string value = target == "opacity" ? live ? "1" : "0" : target == "display" ? live ? "inline" : "none" : live ? "visible" : "hidden";
            string declaration = target is "opacity" or "display" or "visibility" ? cascade switch
            {
                "attribute" => " " + target + "='" + value + "'",
                "inline" => " style='" + target + ":" + value + "'",
                _ => " class='primitive'"
            } : "";
            string start = live ? "<rect width='32' height='32'" : target switch
            {
                "line" => "<line x1='0' y1='16' x2='32' y2='16'",
                "polyline" => "<polyline points='0,16 32,16'",
                "path" => "<path d='M0 16H32'",
                _ => "<rect width='32' height='32'"
            };
            string shape = start + " " + paint + declaration + "/>";
            string body = consumer switch
            {
                "identified" => shape.Replace("/>", " id='shape'/>", StringComparison.Ordinal),
                "use" => "<defs>" + shape.Replace("/>", " id='shape'/>", StringComparison.Ordinal) + "</defs><use href='#shape' opacity='1' display='inline' visibility='visible'/>",
                "group" => "<defs><g id='shape'>" + shape + "</g></defs><use href='#shape'/>",
                "symbol" => "<defs><symbol id='shape' viewBox='0 0 32 32'>" + shape + "</symbol></defs><use href='#shape' width='32' height='32'/>",
                "mask" => "<defs><mask id='target' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + shape + "</mask></defs><rect width='32' height='32' fill='blue' mask='url(#target)'/>",
                "filter" => "<g filter='url(#outer)'>" + shape + "</g>",
                "leaf-filter" => shape.Replace("/>", " filter='url(#outer)'/>", StringComparison.Ordinal),
                _ => shape
            };
            string rules = cascade == "sheet" && target is "opacity" or "display" or "visibility"
                ? "<style>.primitive { " + target + ":" + value + "!important }</style>" : "";
            string filters = consumer is "filter" or "leaf-filter" ? "<defs><filter id='outer' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter></defs>" : "";
            string resources = EmptyPatternResources(tile);
            return EmptyPatternDocument(rules + filters + (forward ? body + resources : resources + body));
        }
    }

    [Theory]
    [InlineData("display")]
    [InlineData("opacity")]
    [InlineData("visibility")]
    public async Task A_copied_primitive_resolves_inherited_activity_from_its_consumer(string property)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string inactive = property == "display" ? "none" : property == "opacity" ? "0" : "hidden";
        foreach (string declaration in new[] { property + "='inherit'", "style='" + property + ":inherit'" })
        {
            byte[] input = EmptyPatternDocument(EmptyPatternResources("text")
                + "<g " + property + "='" + inactive + "'><rect id='shape' width='32' height='32' fill='url(#p)' " + declaration + "/></g><use href='#shape'/>" );
            Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.NotEqual(Render(EmptyPatternDocument("")), prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(2, provider.RequestCount);
    }

    [Theory]
    [InlineData("<line x1='0' y1='16' x2='32' y2='16' fill='url(#p)' stroke='url(#p)' stroke-width='2'/>")]
    [InlineData("<polyline points='0,16 32,16' fill='url(#p)' stroke='url(#p)' stroke-width='2'/>")]
    [InlineData("<path d='M0 16H32' fill='url(#p)' stroke='url(#p)' stroke-width='2'/>")]
    [InlineData("<path d='M0 0H32V32H0Z' fill='url(#p)'/>")]
    [InlineData("<path d='M0 0H32ZM0 0V32H32Z' fill='url(#p)'/>")]
    [InlineData("<path d='M0 0H32ZV32H0Z' fill='url(#p)'/>")]
    [InlineData("<path d='M-10 -10C-10 64 64 64 32 -10Z' fill='url(#p)'/>")]
    [InlineData("<path d='M-10 -10Q16 64 42 -10Z' fill='url(#p)'/>")]
    [InlineData("<path d='M0 0A32 32 0 0 1 32 32Z' fill='url(#p)'/>")]
    [InlineData("<polygon points='0,0 32,0 0,32' fill='url(#p)'/>")]
    public async Task Independent_strokes_and_potential_fill_contours_keep_pattern_fonts(string shape)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] input = EmptyPatternDocument(EmptyPatternResources("text") + shape);
        Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        _ = prepared.ConvertToPng(CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
    }

    [Theory]
    [InlineData("M0 16H32")]
    [InlineData("m0 16h32")]
    [InlineData("M0 0L32 32")]
    [InlineData("M0 0 32 32Z")]
    [InlineData("M0 0L32 0ZM0 0L0 32Z")]
    [InlineData("M0 0H32ZV32Z")]
    [InlineData("M0 16H32 L?")]
    [InlineData("M0 16H32 A1")]
    public async Task Separate_linear_contours_and_recovered_prefixes_have_no_fill(string path)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] input = EmptyPatternDocument(EmptyPatternResources("text") + "<path d='" + path + "' fill='url(#p)'/>");
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(Render(EmptyPatternDocument("")), prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("polyline")]
    [InlineData("visibility")]
    public void Primitive_eligibility_releases_scratch_and_preserves_borrowers_on_denial_and_retry(string target)
    {
        string body = target == "path" ? "<path d='" + string.Concat(Enumerable.Repeat("M0 16H32 ", 512)) + "' fill='url(#p)'/>"
            : target == "polyline" ? "<polyline points='0,16 32,16' fill='url(#p)'/>"
            : "<g visibility='hidden'><rect id='shape' width='32' height='32' fill='url(#p)'/></g><use href='#shape'/>";
        string resources = target == "visibility" ? EmptyPatternResources("solid") : EmptyPatternResources("text");
        byte[] input = EmptyPatternDocument(resources + body);
        byte[] unchanged = input.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(budget.Snapshot().PeakBytes - 74));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using (SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource canceled = new(); canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, canceled.Token)).CancellationToken);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) { }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
