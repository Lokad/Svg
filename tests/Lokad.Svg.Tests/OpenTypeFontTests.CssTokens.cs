using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> PaddedPresentationKeywords()
    {
        foreach ((string property, string value) in new[] { ("display", "inline"), ("display", "none"),
            ("visibility", "visible"), ("visibility", "hidden"), ("fill-rule", "evenodd") })
        foreach (string scope in new[] { "root", "leaf", "group", "use", "symbol" })
        foreach (string location in new[] { "attribute", "inline", "rule" }) yield return [property, value, scope, location];
    }

    [Theory]
    [MemberData(nameof(PaddedPresentationKeywords))]
    public async Task Presentation_keywords_accept_ascii_padding_before_live_and_retained_resolution(string property, string value, string scope, string location)
    {
        byte[] expected = Render(Svg(value, "attribute"));
        byte[] input = Svg(" \t" + value + "\n ", location);
        Assert.Equal(expected, Render(input));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);

        byte[] Svg(string token, string source)
        {
            string declaration = source == "attribute" ? property + "='" + token + "'"
                : source == "inline" ? "style='" + property + ":" + token + "'" : "class='p'";
            string sheet = source == "rule" ? "<style>.p{" + property + ":" + token + "}</style>" : "";
            string path = "<path d='M0 0H8V8H0ZM2 2H6V6H2Z'" + (scope == "leaf" ? " " + declaration : "") + "/>";
            string body = scope switch
            {
                "group" => "<g " + declaration + ">" + path + "</g>",
                "symbol" => "<defs><symbol id='s' " + declaration + ">" + path + "</symbol></defs><use href='#s'/>",
                "use" => "<defs><path id='s' d='M0 0H8V8H0ZM2 2H6V6H2Z'/></defs><use href='#s' " + declaration + "/>",
                _ => path
            };
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' fill='red' "
                + (scope == "root" ? declaration : "") + ">" + sheet + body + "</svg>");
        }

        static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
    }
}
