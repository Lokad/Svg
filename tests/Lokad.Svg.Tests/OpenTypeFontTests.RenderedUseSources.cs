using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_named_rendered_use_keeps_text_layout_and_one_font_acquisition()
    {
        byte[] input = Document("<g id='container'><text id='label' font-family='Example' font-size='12' x='2' y='15'>A</text><use id='placed' href='#label' x='14'/></g><use href='#placed' y='15'/>");
        byte[] control = Document("<text font-family='Example' font-size='12' x='2' y='15'>A</text><text font-family='Example' font-size='12' x='16' y='15'>A</text><text font-family='Example' font-size='12' x='16' y='30'>A</text>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);

        static byte[] Document(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='40'>" + content + "</svg>");
    }
}
