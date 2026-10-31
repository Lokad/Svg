using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_extension_and_hint_program_matches_its_direct_pair_control()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='208' height='64'><text font-family='Example' font-size='32' x='16' y='40'>AV To W</text></svg>"u8.ToArray();
        byte[] bytes = ReadKerningFlagFont("GposExtension");
        (var catalog, var provider, var source) = CreateExamplePack(bytes);
        using SvgFontSnapshot control = await SvgFontSnapshot.CreateAsync([CreateResolution(ReadKerningFlagFont("GposMarks"))], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, control), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }
}
