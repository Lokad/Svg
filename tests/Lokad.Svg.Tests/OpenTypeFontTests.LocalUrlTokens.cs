using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> LocalUrlTokens()
    {
        foreach (string effect in new[] { "fill-linear", "fill-radial", "fill-pattern", "stroke-linear", "stroke-radial", "stroke-pattern", "clip-path", "mask", "filter", "marker-start" })
        foreach (string function in new[] { "url", "URL", "Url" })
        foreach (string quote in new[] { "", "'", "&quot;" })
        foreach (string location in new[] { "attribute", "inline", "rule" })
            if (location == "attribute" || effect is not ("clip-path" or "mask")) yield return [effect, function, quote, location];
    }

    [Theory]
    [MemberData(nameof(LocalUrlTokens))]
    public async Task Local_url_function_case_preserves_identifier_quoting_and_dispatch(string effect, string function, string quote, string location)
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        foreach (bool retained in new[] { false, true })
        {
            byte[] expected = Render(Svg("url", retained)), input = Svg(function, retained);
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(0, provider.RequestCount);

        byte[] Svg(string spelling, bool retained)
        {
            string resource = effect switch
            {
                "fill-linear" or "stroke-linear" => "<linearGradient id='Paint'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
                "fill-radial" or "stroke-radial" => "<radialGradient id='Paint'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
                "fill-pattern" or "stroke-pattern" => "<pattern id='Paint' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='2' fill='red'/></pattern>",
                "clip-path" => "<clipPath id='Paint'><rect width='4' height='8'/></clipPath>",
                "mask" => "<mask id='Paint'><rect width='8' height='8' fill='white'/></mask>",
                "filter" => "<filter id='Paint'><feOffset dx='1'/></filter>",
                _ => "<marker id='Paint' markerUnits='userSpaceOnUse' markerWidth='2' markerHeight='2'><rect width='2' height='2' fill='blue'/></marker>"
            };
            string property = effect.StartsWith("fill-", StringComparison.Ordinal) ? "fill" : effect.StartsWith("stroke-", StringComparison.Ordinal) ? "stroke" : effect;
            string value = spelling + "(" + quote + "#Paint" + quote + ")";
            string declaration = location == "attribute" ? property + "=\"" + value + "\"" : location == "inline" ? "style=\"" + property + ":" + value + "\"" : "class='p'";
            string sheet = location == "rule" ? "<style>.p{" + property + ":" + value + "}</style>" : "";
            string shape = "<path d='M1 1H7V7H1Z' id='shape' " + declaration + (property == "stroke" ? " fill='none' stroke-width='2'" : property == "fill" ? "" : " fill='red'") + "/>";
            string body = retained ? "<defs>" + resource + shape + "</defs><use href='#shape'/>" : "<defs>" + resource + "</defs>" + shape;
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + sheet + body + "</svg>");
        }
        static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
    }

    [Theory]
    [InlineData("filter='NONE'")]
    [InlineData("style='filter:None'")]
    public void No_filter_keyword_is_case_insensitive(string declaration)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' " + declaration + "/></svg>");
        byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Local_url_function_normalization_preserves_identifier_case_and_unicode_contents()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='Paint\u00a0Name'><stop stop-color='red'/></linearGradient></defs><rect width='8' height='8' fill='URL(#Paint\u00a0Name)'/></svg>");
        byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='red'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        string wrongCase = System.Text.Encoding.UTF8.GetString(input).Replace("URL(#Paint", "URL(#paint", StringComparison.Ordinal);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(
            System.Text.Encoding.UTF8.GetBytes(wrongCase), SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }
}
