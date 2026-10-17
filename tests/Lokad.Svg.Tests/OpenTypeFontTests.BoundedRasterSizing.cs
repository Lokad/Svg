using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public async Task Bounded_raw_and_prepared_output_fit_glyphs_and_resources_at_the_final_scale()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='4800' height='2400' viewBox='0 0 4800 2400'><defs><pattern id='p' width='40' height='40' patternUnits='userSpaceOnUse'><rect width='20' height='40' fill='red'/></pattern><clipPath id='c'><rect x='0' y='0' width='1200' height='600'/></clipPath><rect id='tile' width='1200' height='600' fill='url(#p)'/></defs><svg x='25%' y='25%' width='50%' height='50%' viewBox='0 0 2400 1200'><use href='#tile' clip-path='url(#c)'/><text font-family='Example' font-size='400' x='1300' y='600'>A</text></svg></svg>"u8.ToArray();
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(svg, new(null, .125, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
        SvgRasterBounds bounds = new(800, 600, 180_000);
        Assert.Equal(expected, SvgConverter.ConvertToPng(svg, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot, bounds), CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(svg, catalog, provider,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, bounds), CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(2, provider.RequestCount); Assert.Equal(2, source.RequestCount);
    }

    [Fact]
    public async Task Bounded_preparation_rejects_invalid_sizing_before_any_font_acquisition()
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='12em'><text font-family='Example'>A</text></svg>"u8.ToArray();
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(svg, catalog, provider,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, new(128, 128, null)), CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal(0, provider.RequestCount); Assert.Equal(0, source.RequestCount);
    }
}
