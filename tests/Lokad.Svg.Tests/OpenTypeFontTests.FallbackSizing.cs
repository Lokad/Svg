using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><svg><text font-family='Example'>A</text></svg></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><svg x='1' y='1'><rect width='2' height='2' fill='red'/></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 4 4'><rect width='4' height='4' fill='red'/></symbol></defs><use href='#s'/><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='1' y='1'/><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    public async Task Root_fallback_font_documents_match_explicit_logical_viewports(string svg)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount); Assert.Equal(1, source.RequestCount);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        string explicitViewport = svg.Insert(svg.IndexOf('>'), " width='300' height='150'");
        Assert.Equal(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(explicitViewport), options, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }
}
