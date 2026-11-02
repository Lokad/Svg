using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    private const string LegacyDeclarationInput = """
        <svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='5' height='5'>
          <defs><font horiz-adv-x='10000'>
            <font-face font-family='Example' font-style='italic' font-weight='900' units-per-em='16'>
              <font-face-src><font-face-uri xlink:href='https://example.invalid/unused.ttf'/></font-face-src>
            </font-face>
            <glyph unicode='A' d='M0 0H10000V10000H0Z'/><glyph unicode='B' d='M0 0H10000V10000H0Z'/>
          </font></defs>
          <text font-family='Example' font-size='100' y='3'>A</text>
        </svg>
        """;

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Dormant_legacy_font_descriptors_do_not_change_the_live_request()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(LegacyDeclarationInput);
        SvgFontFaceRequest request = Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        Assert.Equal("Example", request.Family);
        Assert.Equal(400, request.Weight);
        Assert.False(request.Italic);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_text_keeps_the_host_program_despite_a_legacy_font_declaration()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(LegacyDeclarationInput);
        Assert.Equal(SvgErrorCode.UnsupportedFeature,
            Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();
        byte[] program = CreateTriangleFont();
        (var catalog, var provider, var source) = CreateExamplePack(program);
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        Assert.True(DecodeAlpha(expected, 1, 1) > 0);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }
}
