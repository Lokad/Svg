using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(400, true)]
    [InlineData(500, true)]
    [InlineData(700, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    [InlineData(2000, false)]
    [InlineData(int.MaxValue, false)]
    public async Task Svg_font_weights_validate_the_css_range_before_discovery_or_selection(int weight, bool admitted)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (string location in new[] { "attribute", "inline", "rule" })
        foreach (string scope in new[] { "text", "group", "symbol", "pattern" })
        {
            string value = weight.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string declaration = location == "attribute" ? "font-weight='" + value + "'" : location == "inline" ? "style='font-weight:" + value + "'" : "";
            string text = "<text x='0' y='7' font-family='Example' font-size='200' " + (scope == "group" ? "" : "id='t' " + declaration) + ">A</text>";
            if (scope == "group") text = "<g id='t' " + declaration + ">" + text + "</g>";
            if (scope == "symbol") text = "<defs><symbol id='s'>" + text + "</symbol></defs><use href='#s'/>";
            if (scope == "pattern") text = "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'>" + text + "</pattern></defs><rect width='8' height='8' fill='url(#p)'/>";
            if (location == "rule") text = "<style>#t{font-weight:" + value + "}</style>" + text;
            byte[] input = Document(text);
            if (!admitted)
            {
                Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
                Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None))).ErrorCode);
                continue;
            }
            Assert.Equal(weight, Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Weight);
            using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            byte[] expected = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
            Assert.NotEqual(Render(Document("")), expected);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
    }

    [Fact]
    public void Literal_public_font_requests_keep_their_existing_positive_weight_contract()
    {
        (var catalog, _, _) = CreateExamplePack(CreateTriangleFont());
        SvgFontFaceRequest request = new("Example", int.MaxValue, false);
        Assert.Equal(int.MaxValue, request.Weight);
        Assert.Equal(400, catalog.Resolve(request).Face.Weight);
    }
}
