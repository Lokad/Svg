using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> LiveMaskFonts()
    {
        foreach (string content in new[] { "group", "use", "alias", "symbol" })
        foreach (string consumer in new[] { "direct", "group", "use", "symbol", "pattern" })
        foreach (bool forward in new[] { false, true })
        foreach (bool normalized in new[] { false, true })
        foreach (string type in new[] { "alpha", "luminance" })
            yield return [content, consumer, forward, normalized, type];
    }

    [Theory]
    [MemberData(nameof(LiveMaskFonts))]
    public async Task Live_mask_fonts_are_discovered_from_the_resource_context(string content, string consumer, bool forward, bool normalized, string type)
    {
        string text = "<text x='" + (normalized ? ".0625" : "2") + "' y='" + (normalized ? ".4375" : "14") + "' fill='white'>A</text>";
        string definitions = content switch
        {
            "use" => text.Replace("<text ", "<text id='t' ", StringComparison.Ordinal),
            "alias" => text.Replace("<text ", "<text id='t' ", StringComparison.Ordinal) + "<g id='alias'><use href='#t'/></g>",
            "symbol" => "<symbol id='glyph'>" + text + "</symbol>",
            _ => "",
        };
        string maskContent = content switch
        {
            "use" => "<use href='#t'/>", "alias" => "<use href='#alias'/>", "symbol" => "<use href='#glyph'/>",
            _ => "<g>" + text + "</g>",
        };
        string mask = "<mask id='m' maskUnits='userSpaceOnUse' maskContentUnits='" + (normalized ? "objectBoundingBox" : "userSpaceOnUse") + "' mask-type='" + type + "' x='0' y='0' width='32' height='32'>" + maskContent + "</mask>";
        string resource = "<g display='none' font-family='Example' font-size='" + (normalized ? "6.25" : "200") + "'><defs>" + (forward ? mask + definitions : definitions + mask) + "</defs></g>";
        const string shape = "<rect width='32' height='32' fill='red' mask='url(#m)'/>";
        string body = consumer switch
        {
            "group" => "<g mask='url(#m)'><rect width='32' height='32' fill='red'/></g>",
            "use" => "<defs><rect id='r' width='32' height='32' fill='red'/></defs><use href='#r' mask='url(#m)'/>",
            "symbol" => "<defs><symbol id='s'>" + shape + "</symbol></defs><use href='#s'/>",
            "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + shape + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
            _ => shape,
        };
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' font-family='Unavailable'>" + (forward ? body + resource : resource + body) + "</svg>"), unchanged = input.ToArray();
        byte[] font = CreateTriangleFont();
        (var catalog, var provider, var source) = CreateExamplePack(font);
        using SvgFontSnapshot explicitFonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, new CountingFontProvider(source), CancellationToken.None);
        SvgRenderOptions explicitOptions = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, explicitFonts);
        RenderBudget budget = new(4_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, explicitFonts, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, explicitOptions, budget, CancellationToken.None))
            Assert.Contains(surface.Pixels, value => value != 0);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        byte[] expected = SvgConverter.ConvertToPng(input, explicitOptions, CancellationToken.None).ToArray();
        using SvgFontSnapshot discovered = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        SvgRenderOptions discoveredOptions = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, discovered);
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, discoveredOptions, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(2, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("<rect width='32' height='32' mask='url(#m)' display='none'/> ")]
    [InlineData("<style>.off{display:none}</style><rect class='off' width='32' height='32' mask='url(#m)'/>")]
    [InlineData("<switch><rect width='1' height='1'/><rect width='32' height='32' mask='url(#m)'/></switch>")]
    [InlineData("<defs><symbol id='s'><rect width='32' height='32' mask='url(#m)'/></symbol></defs>")]
    public async Task Unpainted_mask_consumers_do_not_select_a_font(string body)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g><text x='2' y='14' font-family='Unavailable' font-size='200' fill='white'>A</text></g></mask></defs>" + body + "</svg>");
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Fact]
    public void Mask_font_discovery_releases_borrowed_storage_after_denial_cancellation_and_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><rect width='32' height='32' mask='url(#m)'/><defs><mask id='m'><g font-family='Example'><text>" + new string('A', 8192) + "<tspan dx='3'>A</tspan></text></g></mask></defs></svg>");
        RenderBudget budget = new(8_000_000);
        ParserWorkCounter measured = new(1_000_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Single(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, measured, budget, CancellationToken.None));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            RenderBudget denied = new(budget.Snapshot().PeakBytes * 3 / 4);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(1_000_000), denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(measured.Used - 1), budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(1_000_000), budget, cancelled.Token)).CancellationToken);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            Assert.Single(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, new(1_000_000), budget, CancellationToken.None));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("ancestor")]
    [InlineData("mask")]
    [InlineData("group")]
    public void Mask_fonts_keep_inherited_face_selection_and_child_overrides(string location)
    {
        const string style = " font-family='Unavailable, Example' font-weight='700' font-style='italic'";
        string text = "<text x='2' y='14' fill='white'>A<tspan font-family='Child' font-weight='400' font-style='normal'>A</tspan></text>";
        string mask = "<mask id='m'" + (location == "mask" ? " style='font-family:Unavailable, Example;font-weight:700;font-style:italic'" : "") + "><g" + (location == "group" ? style : "") + ">" + text + "</g></mask>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'" + (location == "root" ? style : "") + "><g display='none'" + (location == "ancestor" ? style : "") + "><defs>" + mask + "</defs></g><rect font-family='Unrelated' width='32' height='32' mask='url(#m)'/></svg>");
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, CancellationToken.None);
        SvgFontFaceRequest inherited = Assert.Single(requests, request => request.Weight == 700);
        Assert.Equal("Unavailable, Example", inherited.Family);
        Assert.True(inherited.Italic);
        SvgFontFaceRequest child = Assert.Single(requests, request => request.Weight == 400);
        Assert.Equal("Child", child.Family);
        Assert.False(child.Italic);
    }

    [Fact]
    public async Task Mask_font_lists_use_catalog_fallback_without_requesting_the_missing_family()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><mask id='m' maskUnits='userSpaceOnUse' width='32' height='32' style='font-family:Unavailable, Example;font-size:200px'><g><text x='2' y='14' fill='white'>A</text></g></mask></defs><rect width='32' height='32' mask='url(#m)'/></svg>");
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
        Assert.NotEmpty(prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Fact]
    public void Unsupported_direct_mask_text_does_not_select_a_font()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><mask id='m'><text font-family='Unavailable'>A</text></mask></defs><rect width='32' height='32' mask='url(#m)'/></svg>");
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_mask_programs_keep_the_original_text_diagnostic_and_allow_retry(bool group)
    {
        const string text = "<text id='source' x='2' y='14' font-family='Example' font-size='200' fill='white'>A</text>";
        string definitions = group ? "" : text;
        string content = group ? "<g>" + text + "</g>" : "<use href='#source'/>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>\n<defs>" + definitions + "<mask id='m' maskUnits='userSpaceOnUse' width='32' height='32'>" + content + "</mask></defs><rect width='32' height='32' mask='url(#m)'/></svg>");
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, new MissingFontProvider(), SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnresolvedResource, error.ErrorCode);
        SvgSourceDiagnostic diagnostic = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal("text", diagnostic.ElementName);
        Assert.Equal("source", diagnostic.ElementId);
        Assert.Equal(2, diagnostic.LineNumber);
        Assert.Equal(0, provider.RequestCount);
        using SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        Assert.NotEmpty(retry.ConvertToPng(CancellationToken.None).ToArray());
    }
}
