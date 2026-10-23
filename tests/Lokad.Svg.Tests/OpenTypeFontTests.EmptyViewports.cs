using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> EmptySymbolViewports()
    {
        foreach (string size in new[] { "width='0%' height='32'", "width='32' height='0px'", "width='0' height='0'" })
        foreach (string aspect in new[] { "xMidYMid meet", "xMaxYMax slice", "none" })
        foreach (string consumer in new[] { "direct", "group", "alias", "pattern" })
        foreach (string payload in new[] { "rectangle", "mask", "opacity", "children", "filter", "own-filter" })
            yield return [size, aspect, consumer, payload];
    }

    [Theory]
    [MemberData(nameof(EmptySymbolViewports))]
    public void Empty_symbol_viewports_preserve_source_and_effect_boundaries(string size, string aspect, string consumer, string payload)
    {
        const string rectangle = "<rect width='32' height='32' fill='red'/>";
        string content = payload switch
        {
            "mask" => rectangle.Replace("/>", " mask='url(#m)'/>", StringComparison.Ordinal),
            "opacity" => "<g opacity='.5'>" + rectangle + "</g>",
            "children" => "<rect width='16' height='32' fill='red'/><rect x='16' width='16' height='32' fill='blue'/>",
            "filter" => "<g filter='url(#f)'>" + rectangle + "</g>",
            _ => rectangle,
        };
        const string resources = "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
        byte[] input = EmptyViewportDocument(size + (payload == "own-filter" ? " filter='url(#f)'" : ""), aspect, consumer, content, resources);
        byte[] control = EmptyViewportDocument("", aspect, "empty", "", "");
        if (payload == "own-filter")
        {
            string source = System.Text.Encoding.UTF8.GetString(EmptyViewportDocument(size + " filter='url(#f)'", aspect, consumer, "", resources));
            source = source.Replace("<symbol id='s' viewBox='0 0 32 32' preserveAspectRatio='" + aspect + "'></symbol>", "<g id='s'/>", StringComparison.Ordinal);
            control = System.Text.Encoding.UTF8.GetBytes(source);
        }
        byte[] expected = SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] unchanged = input.ToArray();
        RenderBudget budget = new(4_000_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            {
                if (payload == "own-filter") Assert.Contains(surface.Pixels, value => value != 0);
                else Assert.All(surface.Pixels, value => Assert.Equal(0, value));
            }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("width='0%' height='32'", "direct", false)]
    [InlineData("width='32' height='0px'", "group", false)]
    [InlineData("width='0' height='0'", "alias", false)]
    [InlineData("width='0%' height='32'", "pattern", false)]
    [InlineData("width='32' height='0px'", "direct", true)]
    [InlineData("width='0' height='0'", "group", true)]
    [InlineData("width='0%' height='32'", "alias", true)]
    [InlineData("width='32' height='0px'", "pattern", true)]
    [InlineData("width='0%' height='32' mask='url(#m)'", "direct", true)]
    [InlineData("width='32' height='0px' mask='url(#m)'", "group", true)]
    [InlineData("width='0' height='0' mask='url(#m)'", "alias", true)]
    [InlineData("width='0%' height='32' mask='url(#m)'", "pattern", true)]
    public async Task Empty_symbol_viewports_do_not_lower_or_acquire_child_fonts(string size, string consumer, bool masked)
    {
        const string text = "<text x='2' y='14' fill='white' font-family='Unavailable' font-size='200'>A</text>";
        string resources = masked ? "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g>" + text + "</g></mask>" : "";
        string content = masked ? "<rect width='32' height='32' fill='red' mask='url(#m)'/>" : text;
        byte[] input = EmptyViewportDocument(size, "xMidYMid meet", consumer, content, resources), unchanged = input.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(EmptyViewportDocument("", "none", "empty", "", ""), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    private static byte[] EmptyViewportDocument(string size, string aspect, string consumer, string content, string resources)
    {
        string instance = "<use href='#s' " + size + "/>";
        string definitions = resources + "<symbol id='s' viewBox='0 0 32 32' preserveAspectRatio='" + aspect + "'>" + content + "</symbol>";
        string body = consumer switch
        {
            "group" => "<g transform='rotate(15 16 16)'>" + instance + "</g>",
            "alias" => "<use href='#alias'/>",
            "pattern" => "<rect width='32' height='32' fill='url(#p)'/>",
            "empty" => "",
            _ => instance,
        };
        if (consumer == "alias") definitions += "<g id='alias'>" + instance + "</g>";
        if (consumer == "pattern") definitions += "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + instance + "</pattern>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "<defs>" + definitions + "</defs></svg>");
    }

    [Theory]
    [InlineData("rectangle")]
    [InlineData("group")]
    [InlineData("text")]
    public async Task Zero_use_dimensions_do_not_hide_nonviewport_targets(string kind)
    {
        const string glyph = "<text x='2' y='14' font-family='Example' font-size='200' fill='red'>A</text>";
        string source = kind switch
        {
            "text" => glyph.Replace("<text ", "<text id='source' ", StringComparison.Ordinal),
            "group" => "<g id='source'>" + glyph + "</g>",
            _ => "<rect id='source' width='16' height='16' fill='red'/>",
        };
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, new CountingFontProvider(new CountingFontSource(CreateTriangleFont())), CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(Document(""), options, CancellationToken.None).ToArray();
        foreach (string size in new[] { "width='0%' height='32'", "width='32' height='0px'", "width='0' height='0'" })
        {
            byte[] input = Document(size);
            Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(kind == "rectangle" ? 0 : 3, provider.RequestCount);

        byte[] Document(string size) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><use href='#source' " + size + "/><defs>" + source + "</defs></svg>");
    }

    [Theory]
    [InlineData("width='0%' height='32'", false)]
    [InlineData("width='32' height='0px'", false)]
    [InlineData("width='0' height='0'", false)]
    [InlineData("width='0%' height='32'", true)]
    [InlineData("width='32' height='0px'", true)]
    [InlineData("width='0' height='0'", true)]
    public async Task Empty_nested_svg_viewports_do_not_lower_or_acquire_fonts(string size, bool masked)
    {
        const string text = "<text x='2' y='14' font-family='Unavailable' font-size='200' fill='white'>A</text>";
        string definitions = masked ? "<defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g>" + text + "</g></mask></defs>" : "";
        string content = masked ? "<rect width='32' height='32' fill='red' mask='url(#m)'/>" : text;
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + definitions + "<svg " + size + " viewBox='0 0 32 32'>" + content + "</svg></svg>");
        byte[] expected = SvgConverter.ConvertToPng(EmptyViewportDocument("", "none", "empty", "", ""), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("href='#missing' width='0' height='32'")]
    [InlineData("href='elsewhere.svg#s' width='0' height='32'")]
    [InlineData("href='#s' width='0' height='32' clip-path='url(#missing)'")]
    [InlineData("href='#s' width='0' height='32' mask='url(#missing)'")]
    public void Empty_instances_preserve_own_reference_validation(string attributes)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><symbol id='s' viewBox='0 0 32 32'><rect width='32' height='32'/></symbol></defs><use " + attributes + "/></svg>");
        RenderBudget budget = new(4_000_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Sources_inside_empty_nested_viewports_can_still_be_used_outside()
    {
        const string text = "<text id='label' x='2' y='14' font-family='Example' font-size='200' fill='red'>A</text>";
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + "<svg width='0' height='32' viewBox='0 0 32 32'><defs>" + text + "</defs></svg><use href='#label'/></svg>");
        byte[] control = System.Text.Encoding.UTF8.GetBytes(prefix + text + "</svg>");
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        Assert.Equal(SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Empty_viewport_source_selection_releases_storage_after_denial_cancellation_and_retry()
    {
        string text = "<text font-family='Unavailable'>" + new string('A', 8192) + "</text>";
        byte[] input = EmptyViewportDocument("width='0' height='32'", "xMidYMid meet", "alias", text, "");
        RenderBudget budget = new(8_000_000);
        ParserWorkCounter measured = new(1_000_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, measured, budget, CancellationToken.None));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            RenderBudget denied = new(budget.Snapshot().PeakBytes * 3 / 4);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(1_000_000), denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(measured.Used - 1), budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(1_000_000), budget, cancelled.Token)).CancellationToken);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
                Assert.False(root.HasUnsupportedChild);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(1_000_000), budget, CancellationToken.None));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parent_filters_can_paint_from_empty_symbol_or_nested_viewport_sources(bool nested)
    {
        const string text = "<text font-family='Unavailable' font-size='12'>A</text>";
        const string filter = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        string source = nested ? "<svg width='0' height='32' viewBox='0 0 32 32'>" + text + "</svg>" : "<use href='#s' width='0' height='32'/>";
        string definitions = filter + (nested ? "" : "<symbol id='s' viewBox='0 0 32 32'>" + text + "</symbol>");
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + "<defs>" + definitions + "</defs><g filter='url(#f)'>" + source + "</g></svg>");
        byte[] control = System.Text.Encoding.UTF8.GetBytes(prefix + "<defs>" + filter + "</defs><g filter='url(#f)'/></svg>");
        byte[] expected = SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("alias")]
    [InlineData("pattern")]
    public async Task Masks_on_filtered_empty_instances_acquire_only_the_mask_font(string consumer)
    {
        const string content = "<text font-family='Unavailable'>A</text>";
        const string resources = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g><text x='2' y='14' font-family='Example' font-size='200' fill='white'>A</text></g></mask>";
        byte[] input = EmptyViewportDocument("width='0' height='32' filter='url(#f)' mask='url(#m)'", "xMidYMid meet", consumer, content, resources);
        byte[] control = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + resources + "</defs><rect width='32' height='32' fill='blue' mask='url(#m)'/></svg>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(control, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(2, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }
}
