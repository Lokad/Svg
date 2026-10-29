using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> PatternOverrideCases()
    {
        foreach (string mode in new[] { "self-g", "balanced-g", "nested-g", "title", "desc", "metadata", "zero-rect", "zero-circle", "empty-path", "missing-image", "hidden-rect", "disabled-rect", "g-title", "disabled-g", "own-filter", "empty-self", "empty-balanced", "whitespace", "comment", "cdata-whitespace", "cdata-text", "foreign-empty" })
        foreach (string content in new[] { "text", "text-use", "mask-group", "mask-use" })
        foreach (bool forward in new[] { false, true })
        foreach (string consumer in new[] { "fill", "stroke", "symbol" })
            yield return new object[] { mode, content, forward, consumer };
    }

    [Theory]
    [MemberData(nameof(PatternOverrideCases))]
    public async Task Pattern_child_elements_override_parent_content_without_requesting_the_parent_font(string mode, string content, bool forward, string consumer)
    {
        bool inherits = mode is "empty-self" or "empty-balanced" or "whitespace" or "comment" or "cdata-whitespace" or "cdata-text";
        string family = inherits ? "Example" : "Unavailable";
        string child = mode switch
        {
            "self-g" => "<g/>", "balanced-g" => "<g></g>", "nested-g" => "<g><g/></g>",
            "title" => "<title>inert</title>", "desc" => "<desc>inert</desc>", "metadata" => "<metadata>inert</metadata>",
            "zero-rect" => "<rect width='0' height='32'/>", "zero-circle" => "<circle r='0'/>", "empty-path" => "<path d=''/>",
            "missing-image" => "<image width='32' height='32'/>", "hidden-rect" => "<rect width='32' height='32' visibility='hidden'/>",
            "disabled-rect" => "<rect width='32' height='32' display='none'/>", "g-title" => "<g><title>inert</title></g>", "disabled-g" => "<g display='none'/>",
            "own-filter" => "<g filter='url(#f)'/>", "whitespace" => "\n  ", "comment" => "<!-- inert -->",
            "cdata-whitespace" => "<![CDATA[  ]]>", "cdata-text" => "<![CDATA[inert]]>", "foreign-empty" => "<custom xmlns='urn:example'/>",
            _ => ""
        };
        string glyph = "<text x='2' y='14' font-family='" + family + "' font-size='200' fill='blue'>A</text>";
        string resources = glyph.Replace("<text ", "<text id='t' ", StringComparison.Ordinal) + glyph.Replace("<text ", "<text id='w' ", StringComparison.Ordinal).Replace("fill='blue'", "fill='white'", StringComparison.Ordinal);
        resources += "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + (content == "mask-group" ? "<g>" + glyph.Replace("fill='blue'", "fill='white'", StringComparison.Ordinal) + "</g>" : "<use href='#w'/>") + "</mask>";
        resources += "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
        string payload = content == "text" ? glyph : content == "text-use" ? "<use href='#t'/>" : "<rect width='32' height='32' fill='blue' mask='url(#m)'/>";
        string source = "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + payload + "</pattern>";
        string alias = mode == "empty-self" ? "<pattern id='a' href='#p'/>" : "<pattern id='a' href='#p'>" + child + "</pattern>";
        string target = consumer == "stroke" ? "<rect x='4' y='4' width='24' height='24' fill='none' stroke='url(#a)' stroke-width='8'/>" : "<rect width='32' height='32' fill='url(#a)'/>";
        string control = inherits ? target.Replace("url(#a)", "url(#p)", StringComparison.Ordinal) : mode == "own-filter" ? target.Replace("url(#a)", "blue", StringComparison.Ordinal) : "";
        string controlResources = resources;
        if (consumer == "symbol")
        {
            resources += "<symbol id='s' viewBox='0 0 32 32'>" + target + "</symbol>";
            target = "<use href='#s'/>";
        }
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        string definitions = "<defs>" + resources + (forward ? alias + source : source + alias) + "</defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + (forward ? target + definitions : definitions + target) + "</svg>"), unchanged = input.ToArray();
        byte[] expectedInput = System.Text.Encoding.UTF8.GetBytes(prefix + (inherits ? "<defs>" + controlResources + source + "</defs>" : "") + control + "</svg>");
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot? fonts = inherits ? await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, new CountingFontProvider(new CountingFontSource(CreateTriangleFont())), CancellationToken.None) : null;
        SvgRenderOptions options = fonts is null ? SvgRenderOptions.Default : new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(expectedInput, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, CancellationToken.None);
        if (inherits) Assert.Equal("Example", Assert.Single(requests).Family);
        else Assert.Empty(requests);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(inherits ? 1 : 0, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("<rect width='32' height='32' mask='url(#missing)'/>")]
    [InlineData("<g mask='url(#missing)'><rect width='32' height='32'/></g>")]
    [InlineData("<rect width='32' height='32' filter='url(#missing)'/>")]
    [InlineData("<g filter='url(#missing)'><rect width='32' height='32'/></g>")]
    [InlineData("<rect width='32' height='32' clip-path='url(#missing)'/>")]
    [InlineData("<rect width='32' height='32' fill='url(#missing)'/>")]
    [InlineData("<use href='#missing'/>")]
    [InlineData("<rect width='bad' height='32'/>")]
    [InlineData("<rect width='32' height='32' mask='elsewhere.svg#m'/>")]
    public async Task Unselected_pattern_sources_preserve_reference_and_geometry_validation(string child)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + child + "</pattern><pattern id='a' href='#p'><title>inert</title></pattern></defs><rect width='32' height='32' fill='url(#a)'/></svg>");
        RenderBudget budget = new(4_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData("<g display='none' mask='url(#missing)'/>")]
    [InlineData("<rect width='32' height='32' display='none' filter='url(#missing)'/>")]
    [InlineData("<g visibility='hidden'><rect width='32' height='32' mask='url(#missing)'/></g>")]
    [InlineData("<metadata><rect width='32' height='32' mask='url(#missing)' fill='url(#p)'/></metadata>")]
    public void Unselected_pattern_validation_preserves_inert_or_suppressed_effects(string child)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + child + "</pattern><pattern id='a' href='#p'><title>inert</title></pattern></defs><rect width='32' height='32' fill='url(#a)'/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Pattern_source_selection_and_effect_validation_release_borrowed_storage_on_denial_and_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><mask id='m'><rect width='32' height='32' fill='white'/></mask><filter id='f'><feFlood flood-color='blue'/></filter><pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><g mask='url(#m)' filter='url(#f)'><text font-family='Unavailable'>" + new string('A', 8192) + "</text></g></pattern><pattern id='a' href='#p'><title>inert</title></pattern></defs><rect width='32' height='32' fill='url(#a)'/></svg>");
        byte[] unchanged = input.ToArray();
        RenderBudget budget = new(8_000_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            RenderBudget tight = new(budget.Snapshot().PeakBytes - 74);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, tight, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.All(surface.Pixels, value => Assert.Equal(0, value));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
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
