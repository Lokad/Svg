using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_styled_paths_select_emitted_fonts_in_direct_and_instance_text()
    {
        byte[] program = CreateTriangleFont();
        (var catalog, var provider, var source) = CreateExamplePack(program);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='112' font-family='NeverOpen' font-size='20'><defs><path id='p' d='M12 40H148'/><path id='q' d='M12 88H148'/><g id='label'><text><textPath href='#q' font-family='Example' style='text-transform:uppercase'>a</textPath></text></g></defs><text><textPath href='#p' font-family='Example' style='text-transform:uppercase'>a</textPath><textPath href='#p' style='display:none'>Z</textPath></text><use href='#label'/></svg>"u8.ToArray();
        byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='112' font-family='Example' font-size='20'><defs><path id='p' d='M12 40H148'/><path id='q' d='M12 88H148'/><g id='label'><text><textPath href='#q'>A</textPath></text></g></defs><text><textPath href='#p'>A</textPath></text><use href='#label'/></svg>"u8.ToArray();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Styled_path_parser_preserves_borrowed_fonts_and_storage_after_refusal_and_retry()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgParseOwnershipAssertions.PreservesBorrowedStorageOnDenialCancellationAndRetry(
            "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80' font-family='Example' font-size='20'><defs><path id='p' d='M12 40H148'/></defs><text x='12' y='20'>A<textPath href='#p' startOffset='50%' style='font-size:16px;text-anchor:end;fill:red'>A</textPath>A</text></svg>"u8.ToArray(), fonts);
    }
}
