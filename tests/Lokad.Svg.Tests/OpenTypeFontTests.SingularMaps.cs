using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_collapsed_pattern_instance_does_not_acquire_unused_font_programs()
    {
        (var catalog, var provider, _) = CreateUnusedFontPack();
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'><text x='2' y='14' font-family='Example' font-size='12'>Ag</text></pattern><rect id='s' width='32' height='32' fill='url(#p)'/></defs><use href='#s' transform='scale(0)'/></svg>"u8.ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        using SvgPngResult actual = prepared.ConvertToOwnedPng(CancellationToken.None);
        using SvgPngResult expected = SvgConverter.ConvertToOwnedPng("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.Memory.ToArray(), actual.Memory.ToArray());
        Assert.Equal(0, provider.RequestCount);
    }
}
