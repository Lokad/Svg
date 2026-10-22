using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> FontScalarWhitespaceCases()
    {
        foreach (object[] separator in SvgCssSelectorTests.ClassSeparators())
        foreach ((string property, string value) in new[] { ("font-weight", "400"), ("font-style", "normal"),
            ("font-size", "200px"), ("letter-spacing", "0px"), ("word-spacing", "normal"),
            ("white-space", "pre"), ("text-anchor", "start"), ("text-decoration", "underline") })
            yield return [separator[0], separator[1], property, value];
    }

    [Theory]
    [MemberData(nameof(FontScalarWhitespaceCases))]
    public async Task Text_scalar_boundaries_use_css_whitespace_in_all_admitted_locations(int character, bool whitespace, string property, string value)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        foreach (string location in property == "text-anchor" ? new[] { "attribute" } : property == "white-space" ? new[] { "inline", "rule" } : new[] { "attribute", "inline", "rule" })
        foreach (string scope in new[] { "text", "group", "symbol" })
        {
            string token = char.ConvertFromUtf32(character) + value + char.ConvertFromUtf32(character);
            byte[] input = Input(token);
            if (whitespace)
            {
                byte[] expected = SvgConverter.ConvertToPng(Input(value), options, CancellationToken.None).ToArray();
                Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
                Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
            }
            else
            {
                SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None));
                Assert.True(error.ErrorCode is SvgErrorCode.UnsupportedFeature or SvgErrorCode.MalformedInput);
                error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
                Assert.True(error.ErrorCode is SvgErrorCode.UnsupportedFeature or SvgErrorCode.MalformedInput);
            }

            byte[] Input(string scalar)
            {
                string declaration = location == "attribute" ? property + "='" + scalar + "'" : location == "inline" ? "style='" + property + ":" + scalar + "'" : "";
                string text = "<text " + (scope == "group" ? "" : "id='t' " + declaration) + " font-family='Example'" + (property == "font-size" ? "" : " font-size='200'") + " x='0' y='7'>A</text>";
                if (scope == "group") text = "<g id='t' " + declaration + ">" + text + "</g>";
                if (scope == "symbol") text = "<defs><symbol id='s'>" + text + "</symbol></defs><use href='#s'/>";
                if (location == "rule") text = "<style>#t{" + property + ":" + scalar + "}</style>" + text;
                return Document(text);
            }
        }
    }
}
