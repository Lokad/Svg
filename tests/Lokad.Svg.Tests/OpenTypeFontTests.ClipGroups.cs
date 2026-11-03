namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_clip_descendant_selects_its_font_without_activating_sibling_text()
    {
        byte[] program = CreateTriangleFont();
        (var catalog, var provider, var source) = CreateExamplePack(program);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='48'><defs><clipPath id='c'><g><text font-family='NeverOpen'>Z</text><text id='label' font-family='Example' font-size='20' x='12' y='32'>A</text></g></clipPath></defs><use href='#label'/></svg>"u8.ToArray();
        byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='48'><text font-family='Example' font-size='20' x='12' y='32'>A</text></svg>"u8.ToArray();
        using Fonts.SvgFontSnapshot fonts = await Fonts.SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }
}
