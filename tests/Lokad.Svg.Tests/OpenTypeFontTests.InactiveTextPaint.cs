using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> InactiveTextConsumers()
    {
        foreach (string paint in new[] { "filter", "gradient", "pattern" })
        foreach (string consumer in new[] { "direct", "spans", "identified", "use", "symbol", "mask", "tile" })
        foreach (bool forward in new[] { false, true })
            if (paint != "pattern" || consumer != "tile") yield return [paint, consumer, forward];
    }

    [Theory]
    [MemberData(nameof(InactiveTextConsumers))]
    public async Task Unavailable_text_paints_skip_font_acquisition_and_glyph_construction(string paint, string consumer, bool forward)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] empty = Render(InactiveTextDocument(""));
        foreach (string display in paint == "pattern" ? new[] { "display='none'", "style='display:none'", "class='hidden'" } : new[] { "class='hidden'" })
        {
            byte[] input = Input(display);
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
            Assert.Equal(empty, Render(input));
            using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            Assert.Empty(snapshot.Fonts);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(empty, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(0, provider.RequestCount);
        byte[] visible = Input("");
        Assert.Single(SvgFontRequestDiscovery.Discover(visible, CancellationToken.None));
        using SvgPreparedDocument control = await SvgPreparedDocument.CreateAsync(visible, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.NotEqual(empty, control.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);

        byte[] Input(string display)
        {
            string text = "<text x='1' y='14' " + (paint == "filter" ? "fill='blue' filter='url(#r)'" : "fill='url(#r)'") + ">" + (consumer == "spans" ? "<tspan>A</tspan>" : "A") + "</text>";
            string extra = consumer switch
            {
                "use" or "mask" => text.Replace("<text ", "<text id='t' ", StringComparison.Ordinal),
                "symbol" => "<symbol id='t' viewBox='0 0 32 32'>" + text + "</symbol>",
                "tile" => "<pattern id='tile' width='32' height='32' patternUnits='userSpaceOnUse'>" + text + "</pattern>",
                _ => ""
            };
            if (consumer == "mask") extra += "<mask id='m' mask-type='alpha'><use href='#t'/></mask>";
            string body = consumer switch
            {
                "use" or "symbol" => "<use href='#t' width='32' height='32'/>",
                "mask" => "<rect width='32' height='32' fill='blue' mask='url(#m)'/>",
                "tile" => "<rect width='32' height='32' fill='url(#tile)'/>",
                "identified" => text.Replace("<text ", "<text id='t' ", StringComparison.Ordinal),
                _ => text
            };
            string resources = "<defs>" + InactiveTextResource(paint, display) + extra + "</defs>";
            return InactiveTextDocument(forward ? body + resources : resources + body);
        }
    }

    [Theory]
    [InlineData("gradient", "override")]
    [InlineData("pattern", "override")]
    [InlineData("gradient", "stroke")]
    [InlineData("pattern", "stroke")]
    [InlineData("gradient", "decoration")]
    [InlineData("pattern", "decoration")]
    public async Task Unavailable_text_fill_preserves_independent_solid_paint(string paint, string kind)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string text = kind switch
        {
            "override" => "<text x='1' y='14' fill='url(#r)'><tspan fill='blue'>A</tspan></text>",
            "stroke" => "<text x='1' y='14' fill='url(#r)' stroke='blue' stroke-width='1'>A</text>",
            _ => "<text x='1' y='14' fill='blue' text-decoration='underline'><tspan fill='url(#r)'>A</tspan></text>"
        };
        byte[] input = InactiveTextDocument("<defs>" + InactiveTextResource(paint, "class='hidden'") + "</defs>" + text);
        byte[] control = InactiveTextDocument(text.Replace("url(#r)", "none", StringComparison.Ordinal));
        Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray();
        Assert.NotEqual(Render(InactiveTextDocument("")), expected);
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inactive_filter_scope_respects_referenced_source_and_resource_boundaries(bool forward)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        const string text = "<text id='t' x='1' y='14' fill='blue'>A</text>";
        string definitions = "<defs>" + InactiveTextResource("filter", "class='hidden'") + "</defs><g filter='url(#r)'>" + text + "</g>";
        const string use = "<use href='#t'/>";
        byte[] live = InactiveTextDocument(forward ? use + definitions : definitions + use);
        Assert.Single(SvgFontRequestDiscovery.Discover(live, CancellationToken.None));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(live, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(live, catalog, provider, CancellationToken.None);
        Assert.Equal(SvgConverter.ConvertToPng(InactiveTextDocument(text), new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray(), prepared.ConvertToPng(CancellationToken.None).ToArray());
        byte[] inactive = InactiveTextDocument("<defs>" + InactiveTextResource("filter", "class='hidden'") + text + "</defs><g filter='url(#r)'>" + use + "</g>");
        Assert.Empty(SvgFontRequestDiscovery.Discover(inactive, CancellationToken.None));
        Assert.Equal(Render(InactiveTextDocument("")), Render(inactive));
    }

    [Theory]
    [InlineData("gradient", false)]
    [InlineData("gradient", true)]
    [InlineData("pattern", false)]
    [InlineData("pattern", true)]
    public async Task Inactive_spans_preserve_advance_fonts_but_independent_siblings_do_not(string paint, bool sequential)
    {
        byte[] font = CreateTriangleFont();
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(font));
        string manifest = System.Text.Json.JsonSerializer.Serialize(new
        {
            packId = "test-pack",
            files = new[]
            {
                new { relativePath = "files/example.ttf", byteSize = font.Length, sha256 = hash },
                new { relativePath = "files/advance.ttf", byteSize = font.Length, sha256 = hash }
            },
            families = new[]
            {
                new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = "files/example.ttf" },
                new { requestedFamily = "Advance", resolvedFamily = "Advance", relativeFontFile = "files/advance.ttf" }
            }
        });
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        CountingFontProvider provider = new(new CountingFontSource(font));
        string hidden = "<tspan font-family='Advance' font-size='12' fill='url(#r)'>A</tspan>";
        string text = sequential ? "<text x='1' y='14' fill='blue'>" + hidden + "A</text>"
            : "<text x='1' y='14'>" + hidden + "</text><text x='12' y='14' fill='blue'>A</text>";
        byte[] input = InactiveTextDocument("<defs>" + InactiveTextResource(paint, "class='hidden'") + "</defs>" + text);
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, CancellationToken.None);
        Assert.Equal(sequential ? 2 : 1, requests.Length);
        Assert.Contains(requests, r => r.Family == "Example");
        Assert.Equal(sequential, requests.Any(r => r.Family == "Advance"));
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        // Both requested families deliberately share the same program digest.
        Assert.Equal(1, provider.RequestCount);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(InactiveTextDocument(text.Replace("url(#r)", "none", StringComparison.Ordinal)), options, CancellationToken.None).ToArray();
        Assert.NotEqual(Render(InactiveTextDocument("")), expected);
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("gradient")]
    [InlineData("pattern")]
    public async Task Enclosing_filter_retains_inactive_text_source_metrics(string paint)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string definitions = "<defs>" + InactiveTextResource(paint, "class='hidden'") + "<filter id='outer' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter><text id='t' x='1' y='14' fill='url(#r)'>A</text></defs>";
        byte[] input = InactiveTextDocument(definitions + "<g filter='url(#outer)'><use href='#t'/></g>");
        Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(Render(InactiveTextDocument("<rect width='32' height='32' fill='blue'/>")), prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("filter", "stroke='blue' stroke-width='bad'")]
    [InlineData("gradient", "stroke='blue' stroke-width='bad'")]
    [InlineData("pattern", "stroke='blue' stroke-width='bad'")]
    [InlineData("filter", "font-weight='bad'")]
    [InlineData("gradient", "font-weight='bad'")]
    [InlineData("pattern", "font-weight='bad'")]
    public void Unavailable_paint_still_validates_authored_text_values(string paint, string invalid)
    {
        byte[] input = InactiveTextDocument("<defs>" + InactiveTextResource(paint, "class='hidden'") + "</defs><text x='1' y='14' " + (paint == "filter" ? "fill='blue' filter='url(#r)'" : "fill='url(#r)'") + " " + invalid + ">A</text>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
    }

    [Theory]
    [InlineData("gradient", false)]
    [InlineData("gradient", true)]
    [InlineData("pattern", false)]
    [InlineData("pattern", true)]
    public async Task Unavailable_text_stroke_preserves_independent_fill(string paint, bool fill)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string text = "<text x='1' y='14' fill='" + (fill ? "blue" : "none") + "' stroke='url(#r)' stroke-width='1'>A</text>";
        byte[] input = InactiveTextDocument("<defs>" + InactiveTextResource(paint, "class='hidden'") + "</defs>" + text);
        Assert.Equal(fill ? 1 : 0, SvgFontRequestDiscovery.Discover(input, CancellationToken.None).Length);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(fill ? 1 : 0, provider.RequestCount);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(InactiveTextDocument(text.Replace("stroke='url(#r)'", "stroke='none'", StringComparison.Ordinal)), new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        if (!fill) Assert.Equal(expected, Render(input));
    }

    [Theory]
    [InlineData("filter", false)]
    [InlineData("filter", true)]
    [InlineData("gradient", false)]
    [InlineData("gradient", true)]
    [InlineData("pattern", false)]
    [InlineData("pattern", true)]
    public void Inactive_text_preserves_zero_width_stroke_reference_validation(string paint, bool zero)
    {
        byte[] input = InactiveTextDocument("<defs>" + InactiveTextResource(paint, "class='hidden'") + "</defs><text x='1' y='14' " + (paint == "filter" ? "fill='blue' filter='url(#r)'" : "fill='url(#r)'") + " stroke='url(#missing)' stroke-width='" + (zero ? "0" : "1") + "'>A</text>");
        if (zero) Assert.Equal(Render(InactiveTextDocument("")), Render(input));
        else Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("gradient")]
    [InlineData("pattern")]
    public void Inactive_text_paint_discovery_releases_budget_on_denial(string paint)
    {
        byte[] input = InactiveTextDocument("<defs>" + InactiveTextResource(paint, "class='hidden'") + "</defs><text x='1' y='14' " + (paint == "filter" ? "fill='blue' filter='url(#r)'" : "fill='url(#r)'") + ">A</text>");
        RenderBudget budget = new(1_000_000);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        foreach (long limit in new long[] { 1, budget.Snapshot().PeakBytes - 1 })
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover(input, cancellation.Token)).CancellationToken);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
    }

    private static byte[] InactiveTextDocument(string body)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' font-family='Example' font-size='200'><style>.hidden{display:none}</style>" + body + "</svg>");

    private static string InactiveTextResource(string paint, string attributes) => paint switch
    {
        "filter" => "<filter id='r' " + attributes + "><feFlood flood-color='blue'/></filter>",
        // Gradients currently admit CSS display through selectors, not presentation attributes.
        "gradient" => "<style>#r{display:" + (attributes.Length == 0 ? "inline" : "none") + "}</style><linearGradient id='r' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
        _ => "<pattern id='r' width='32' height='32' patternUnits='userSpaceOnUse' " + attributes + "><rect width='32' height='32' fill='blue'/></pattern>"
    };
}
