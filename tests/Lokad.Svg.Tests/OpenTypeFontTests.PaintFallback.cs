using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("missing-none", 0)]
    [InlineData("available-pattern", 1)]
    [InlineData("filtered-span", 1)]
    [InlineData("span-override", 1)]
    [InlineData("inherited-currentColor", 1)]
    public async Task Paint_fallback_preserves_selected_fonts_and_prepared_text(string scenario, int expectedRequests)
    {
        const string definitions = "<defs><pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><text x='1' y='14'>A</text></pattern>"
            + "<linearGradient id='g'><stop stop-color='blue'/><stop offset='1' stop-color='red'/></linearGradient>"
            + "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='0' dy='0'/></filter></defs>";
        (string body, string expected) = scenario switch
        {
            "missing-none" => ("<text x='1' y='14' fill='url(#missing) none'>A</text>", ""),
            "available-pattern" => ("<rect width='32' height='32' fill='url(#p) none'/>", "<rect width='32' height='32' fill='url(#p)'/>"),
            "filtered-span" => ("<g filter='url(#f)'><text x='1' y='14' fill='url(#missing) none'><tspan fill='url(#g) blue'>A</tspan></text></g>",
                "<g filter='url(#f)'><text x='1' y='14' fill='none'><tspan fill='url(#g)'>A</tspan></text></g>"),
            "span-override" => ("<text x='1' y='14' fill='url(#missing) none'><tspan fill='blue'>A</tspan></text>",
                "<text x='1' y='14' fill='none'><tspan fill='blue'>A</tspan></text>"),
            _ => ("<g fill='url(#missing) currentColor' color='blue'><text x='1' y='14'><tspan>A</tspan></text></g>",
                "<g fill='currentColor' color='blue'><text x='1' y='14'><tspan>A</tspan></text></g>")
        };
        byte[] input = InactiveTextDocument(definitions + body);
        Assert.Equal(expectedRequests, SvgFontRequestDiscovery.Discover(input, CancellationToken.None).Length);
        // The contour runs from (10,10) to (20,20). Supply that box for object-box
        // gradients; the general synthetic-font helper leaves it empty.
        byte[] font = CreateTriangleFont();
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, glyph.Offset + 2, 10);
        WriteInt16(font, glyph.Offset + 4, 10);
        WriteInt16(font, glyph.Offset + 6, 20);
        WriteInt16(font, glyph.Offset + 8, 20);
        var (catalog, provider, _) = CreateExamplePack(font);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        byte[] actual = prepared.ConvertToPng(CancellationToken.None).ToArray();
        Assert.Equal(expectedRequests, provider.RequestCount);
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(InactiveTextDocument(definitions + expected), catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        Assert.Equal(SvgConverter.ConvertToPng(InactiveTextDocument(definitions + expected), options, CancellationToken.None).ToArray(), actual);
        if (expectedRequests > 0) Assert.NotEqual(Render(InactiveTextDocument("")), actual);
    }
}
