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
        await AssertPreparedCasing(program, "lowercase", 'A', 'a');
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public Task Prepared_uppercase_discovers_emitted_glyphs_in_direct_and_instance_text()
        => AssertPreparedCasing(CreateTriangleFont(), "uppercase", 'a', 'A');

    [Fact]
    [Trait("Layer", "Contract")]
    public Task Prepared_capitalization_discovers_emitted_glyphs_in_direct_and_instance_text()
        => AssertPreparedCasing(CreateTriangleFont(), "capitalize", 'a', 'A');

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_deleted_only_font_is_not_selected_in_direct_or_instance_text()
    {
        byte[] program = CreateTriangleFont();
        Fonts.OpenTypeTableRange cmap = Fonts.OpenTypeFont.ParseMetrics(program, CancellationToken.None).Cmap ?? throw new InvalidDataException();
        WriteUInt16(program, cmap.Offset + 12 + 14, 'i');
        WriteUInt16(program, cmap.Offset + 12 + 20, 'i');
        WriteInt16(program, cmap.Offset + 12 + 24, 1 - 'i');
        (var catalog, var provider, var source) = CreateExamplePack(program);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80' font-family='Example' font-size='20' xml:lang='tr' style='text-transform:lowercase'><defs><g id='label'><text x='12' y='65'>I<tspan font-family='NeverOpen'>\u0307</tspan></text></g></defs><text x='12' y='35'>I<tspan font-family='NeverOpen'>\u0307</tspan></text><use href='#label'/></svg>"u8.ToArray();
        byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80' font-family='Example' font-size='20'><defs><g id='label'><text x='12' y='65'>i</text></g></defs><text x='12' y='35'>i</text><use href='#label'/></svg>"u8.ToArray();
        using Fonts.SvgFontSnapshot fonts = await Fonts.SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    private static async Task AssertPreparedCasing(byte[] program, string transform, char original, char emitted)
    {
        (var catalog, var provider, var source) = CreateExamplePack(program);
        byte[] input = System.Text.Encoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80' font-family='Example' font-size='20' style='text-transform:{transform}'><defs><g id='label'><text x='12' y='65'>{original}</text></g></defs><text x='12' y='35'>{original}</text><use href='#label'/></svg>");
        byte[] control = System.Text.Encoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80' font-family='Example' font-size='20'><defs><g id='label'><text x='12' y='65'>{emitted}</text></g></defs><text x='12' y='35'>{emitted}</text><use href='#label'/></svg>");
        using Fonts.SvgFontSnapshot fonts = await Fonts.SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }
}
