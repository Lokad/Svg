using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_group_pattern_with_foreign_description_acquires_only_its_selected_font()
    {
        const string description = "<d:SVGTestCase xmlns:d='http://www.w3.org/2000/02/svg/testsuite/description/'><d:Paragraph font-family='Unused'>Data</d:Paragraph></d:SVGTestCase>";
        string pattern = PatternFontDefinition(PatternFontSize, PatternFontText);
        byte[] input = PatternFontInput(description + "<g>" + pattern + "</g>" + PatternFontTarget);
        byte[] control = PatternFontInput("<defs>" + pattern + "</defs>" + PatternFontTarget);
        (var catalog, var provider, var source) = PatternFontPack(false);
        using SvgFontSnapshot controlFonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, controlFonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }
}
