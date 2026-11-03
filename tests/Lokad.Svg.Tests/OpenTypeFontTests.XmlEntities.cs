using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_entity_text_discovers_and_acquires_the_expanded_instance_font_once()
    {
        byte[] program = CreateTriangleFont();
        (var catalog, var provider, var source) = CreateExamplePack(program);
        const string content = "<defs><g id='label'><text x='2' y='25' font-family='Example' font-size='20'>A</text></g></defs><use href='#label'/>";
        string encoded = string.Concat(content.Select(c => "&#" + ((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture) + ";"));
        string root = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE svg [<!ENTITY label \"" + encoded + "\">]>" + root + "&label;</svg>");
        byte[] control = System.Text.Encoding.UTF8.GetBytes(root + content + "</svg>");
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }
}
