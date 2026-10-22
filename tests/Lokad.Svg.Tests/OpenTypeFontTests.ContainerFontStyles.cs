using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("group")]
    [InlineData("nested-group")]
    [InlineData("symbol")]
    [InlineData("symbol-group")]
    [InlineData("pattern")]
    [InlineData("pattern-template")]
    [InlineData("marker")]
    public async Task Font_inline_styles_remain_valid_when_containers_retain_sources(string scope)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        foreach ((string property, string value) in new[] { ("font-family", "Example"), ("font-size", "200px"),
            ("font-weight", "400"), ("font-style", "normal"), ("letter-spacing", "0"), ("word-spacing", "0"), ("text-decoration", "underline") })
        {
            byte[] expected = SvgConverter.ConvertToPng(Input(false), options, CancellationToken.None).ToArray();
            byte[] input = Input(true);
            Assert.True(expected.AsSpan().SequenceEqual(SvgConverter.ConvertToPng(input, options, CancellationToken.None).Span), scope + ": " + property);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

            byte[] Input(bool inline)
            {
                bool ruleControl = !inline && (scope.StartsWith("pattern", StringComparison.Ordinal) || scope == "marker");
                string declaration = inline ? "style='" + property + ":" + value + "'" : ruleControl ? "" : property + "='" + value + "'";
                string text = "<text x='0' y='7'" + (property == "font-family" ? "" : " font-family='Example'") + (property == "font-size" ? "" : " font-size='200'") + ">A</text>";
                string group = "<g id='g' " + declaration + ">" + text + "</g>";
                string content = scope switch
                {
                    "nested-group" => "<g id='parent'>" + group + "</g>",
                    "symbol" => "<defs><symbol id='s' " + declaration + ">" + text + "</symbol></defs><use href='#s'/>",
                    "symbol-group" => "<defs><symbol id='s'>" + group + "</symbol></defs><use href='#s'/>",
                    "pattern" => "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse' " + declaration + ">" + text + "</pattern></defs><rect width='8' height='8' fill='url(#p)'/>",
                    "pattern-template" => "<defs><pattern id='base' patternUnits='userSpaceOnUse' " + declaration + ">" + text + "</pattern><pattern id='p' href='#base' width='8' height='8'/></defs><rect width='8' height='8' fill='url(#p)'/>",
                    "marker" => "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='.01em' markerHeight='2' " + declaration + "><rect width='2' height='2'/></marker></defs><path d='M1 1H7' marker-start='url(#m)'/>",
                    _ => group
                };
                if (ruleControl) content = "<style>#" + (scope == "pattern" ? "p" : scope == "marker" ? "m" : "base") + "{" + property + ":" + value + "}</style>" + content;
                return Document(content);
            }
        }
    }
}
