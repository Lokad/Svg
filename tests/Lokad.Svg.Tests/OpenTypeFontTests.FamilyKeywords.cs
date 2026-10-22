using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> LiteralFamilyKeywordCases()
    {
        foreach (char character in new[] { '\u00a0', '\u2003', '\u202f', '\u3000', '\u200b', '\ufeff' })
        foreach (string word in new[] { "inherit", "initial", "unset", "serif" })
        foreach (string position in new[] { "before", "after", "both" })
            yield return [(position == "after" ? "" : character.ToString()) + word + (position == "before" ? "" : character.ToString())];
    }

    [Theory]
    [MemberData(nameof(LiteralFamilyKeywordCases))]
    public async Task Unicode_family_identifiers_do_not_become_global_keywords(string family)
    {
        byte[] font = CreateTriangleFont();
        (SvgFontPackCatalog _, CountingFontProvider provider, _) = CreateExamplePack(font);
        string manifest = System.Text.Json.JsonSerializer.Serialize(new
        {
            packId = "test-pack",
            files = new[] { new { relativePath = "files/example.ttf", byteSize = font.Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(font)) } },
            families = new[] { new { requestedFamily = family, resolvedFamily = "Example", relativeFontFile = "files/example.ttf" } }
        });
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest(family, 400, false) }, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        foreach (string location in new[] { "attribute", "inline", "rule" })
        foreach (string scope in new[] { "text", "group", "symbol", "pattern" })
        {
            byte[] input = Input(false), control = Input(true);
            SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
            Assert.Equal(family, Assert.Single(request.Families).Name);
            Assert.False(Assert.Single(request.Families).IsGeneric);
            byte[] expected = SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray();
            Assert.NotEqual(Render(Document("")), expected);
            Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

            byte[] Input(bool quoted)
            {
                string value = quoted ? "&quot;" + family + "&quot;" : family;
                string declaration = location == "attribute" ? "font-family='" + value + "'" : location == "inline" ? "style='font-family:" + value + "'" : "";
                string text = "<text x='0' y='7' font-size='200' " + (scope == "group" ? "" : "id='t' " + declaration) + ">A</text>";
                if (scope == "group") text = "<g id='t' " + declaration + ">" + text + "</g>";
                if (scope == "symbol") text = "<defs><symbol id='s'>" + text + "</symbol></defs><use href='#s'/>";
                if (scope == "pattern") text = "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'>" + text + "</pattern></defs><rect width='8' height='8' fill='url(#p)'/>";
                if (location == "rule") text = "<style>#t{font-family:" + value + "}</style>" + text;
                return Document(text);
            }
        }
    }
}
