namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_lowercase_discovers_only_emitted_glyphs_in_direct_and_instance_text()
    {
        byte[] program = CreateTriangleFont();
        Fonts.OpenTypeTableRange cmap = Fonts.OpenTypeFont.ParseMetrics(program, CancellationToken.None).Cmap ?? throw new InvalidDataException();
        WriteUInt16(program, cmap.Offset + 12 + 14, 'a');
        WriteUInt16(program, cmap.Offset + 12 + 20, 'a');
        WriteInt16(program, cmap.Offset + 12 + 24, 1 - 'a');
        (var catalog, var provider, var source) = CreateExamplePack(program);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80' font-family='Example' font-size='20' style='text-transform:lowercase'><defs><g id='label'><text x='12' y='65'>A</text></g></defs><text x='12' y='35'>A</text><use href='#label'/></svg>"u8.ToArray();
        byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80' font-family='Example' font-size='20'><defs><g id='label'><text x='12' y='65'>a</text></g></defs><text x='12' y='35'>a</text><use href='#label'/></svg>"u8.ToArray();
        using Fonts.SvgFontSnapshot fonts = await Fonts.SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_uppercase_discovers_emitted_glyphs_in_direct_and_instance_text()
    {
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80' font-family='Example' font-size='20' style='text-transform:uppercase'><defs><g id='label'><text x='12' y='65'>a</text></g></defs><text x='12' y='35'>a</text><use href='#label'/></svg>"u8.ToArray();
        byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80' font-family='Example' font-size='20'><defs><g id='label'><text x='12' y='65'>A</text></g></defs><text x='12' y='35'>A</text><use href='#label'/></svg>"u8.ToArray();
        using Fonts.SvgFontSnapshot fonts = await Fonts.SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }
}
