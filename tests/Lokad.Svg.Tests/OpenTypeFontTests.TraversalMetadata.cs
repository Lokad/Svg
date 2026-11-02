using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("padding:1.3px 4.25px 2px 3.5px", "padding:-1px")]
    [InlineData("border:2px solid currentColor;padding:1.3px", "border:-1px solid red")]
    [Trait("Layer", "Contract")]
    public async Task Root_box_validates_before_acquisition_and_keeps_prepared_text_mapping(string declarations, string invalidDeclarations)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='24' style='" + declarations + ";background-color:#2080c0'><text font-family='Example' font-size='12' x='2' y='14'>AA</text></svg>");
        byte[] invalid = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' style='" + invalidDeclarations + "'><text font-family='Example'>A</text></svg>");
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        SvgRenderOptions options = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () =>
        {
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(invalid, catalog, provider, options, CancellationToken.None);
        })).ErrorCode);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(input, new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Canvas_background_validates_before_acquisition_and_preserves_prepared_retry()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='24' style='background-color:rgba(48,80,112,.7);opacity:.4;display:none'><text display='inherit' font-family='Unused'>X</text><text font-family='Example' font-size='12' x='2' y='14'>AA</text></svg>"u8.ToArray();
        byte[] invalid = "<svg xmlns='http://www.w3.org/2000/svg' style='background-color:unused'><text font-family='Example'>A</text></svg>"u8.ToArray();
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () =>
        {
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(invalid, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        })).ErrorCode);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
        using SvgFontSnapshot controlFonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, controlFonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Physical_pattern_dimensions_preserve_selected_font_discovery_and_prepared_replay()
    {
        string content = "<defs>" + PatternFontDefinition("width='1pc' height='12pt'", PatternFontText) + "</defs>" + PatternFontTarget;
        byte[] input = PatternFontInput(content);
        byte[] control = PatternFontInput(content.Replace("width='1pc' height='12pt'", PatternFontSize, StringComparison.Ordinal));
        (var catalog, var provider, var source) = PatternFontPack(false);
        using SvgFontSnapshot controlFonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, controlFonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Inactive_css_preserves_one_selected_font_and_prepared_replay()
    {
        const string inactive = "g:hover text, text:focus{font-family:Unused;unsupported-paint:1}";
        string markup = "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='24' style='float:right'><style>text{font-family:Example;font-size:12px}"
            + inactive + "</style><g><text x='2' y='14'>AA</text></g></svg>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(markup);
        byte[] control = System.Text.Encoding.UTF8.GetBytes(markup.Replace(inactive, "", StringComparison.Ordinal).Replace(" style='float:right'", "", StringComparison.Ordinal));
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot controlFonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, controlFonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Retained_script_markers_spend_the_original_node_allowance_before_later_geometry()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='24'><text id='t' font-family='Example' font-size='12'>A<script/>A</text><rect width='1' height='1'/><rect width='1' height='1'/></svg>"u8.ToArray();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderLimits limits = new(100_000, 32, 1024, 100_000, 4, 1024);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits, fonts);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("direct")]
    [InlineData("retained")]
    [InlineData("use")]
    public async Task Prepared_static_scripts_preserve_live_and_retained_text_with_one_font_acquisition(string context)
    {
        string script = "<script href='https://example.invalid/unused.js' style='font-family:Unused'><![CDATA[" + new string('x', Lokad.Svg.Parsing.SvgTextLimits.MaximumCharacters + 1) + "]]></script>";
        byte[] input = Document(script), control = Document("");
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot controlFonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, controlFonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);

        byte[] Document(string ignored)
        {
            string text = "<text " + (context == "direct" ? "" : "id='t' ") + "font-family='Example' font-size='12' x='2' y='14'>A" + ignored + "<tspan>A</tspan>A</text>";
            string body = context == "use" ? "<defs>" + text + "</defs><use href='#t'/>" : text;
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='24'>" + body + "</svg>");
        }
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_events_preserve_span_and_path_font_discovery_and_replay()
    {
        string markup = "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='32' onload='font-family:Unused'>"
            + "<defs onclick='ignored'><path id='p' d='M2 28H46'/></defs><g onfocus='ignored'>"
            + "<text font-family='Example' font-size='12' x='2' y='12' onclick='ignored'>A<tspan onmouseover='ignored'>A</tspan></text>"
            + "<text font-family='Example' font-size='12'><textPath href='#p' onblur='ignored'>A</textPath></text></g></svg>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(markup);
        foreach (string attribute in new[] { " onload='font-family:Unused'", " onclick='ignored'", " onfocus='ignored'", " onmouseover='ignored'", " onblur='ignored'" })
            markup = markup.Replace(attribute, "", StringComparison.Ordinal);
        byte[] control = System.Text.Encoding.UTF8.GetBytes(markup);
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot controlFonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(control, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, controlFonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

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
