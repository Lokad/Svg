using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("<text transform='translate(0)' y='3'>A</text>", "<text y='3'>A</text>")]
    [InlineData("<text transform='translate(5,2)' y='3'>A</text>", "<text x='5' y='5'>A</text>")]
    [InlineData("<g transform='translate(5.25,2.5)'><text x='3' y='1'>A</text></g>", "<text x='8.25' y='3.5'>A</text>")]
    [InlineData("<g transform='translate(8,4)'><a transform='translate(-2,1)'><text transform='translate(1,-2)' x='4' y='3'>A</text></a></g>", "<text x='11' y='6'>A</text>")]
    [InlineData("<g transform='translate(5,2)'><text x='90' y='3' text-anchor='middle'>AAA</text></g>", "<text x='95' y='5' text-anchor='middle'>AAA</text>")]
    [InlineData("<g transform='translate(5,2)' visibility='hidden'><text y='3'><tspan visibility='visible'>A</tspan></text></g>", "<text x='5' y='5'>A</text>")]
    public async Task Translation_preserves_text_layout_and_prepared_font_discovery(string content, string expectedContent)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='195' height='12'><g font-family='Example' font-size='100'>" + content + "</g></svg>");
        byte[] expectedInput = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='195' height='12'><g font-family='Example' font-size='100'>" + expectedContent + "</g></svg>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(expectedInput, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
        Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Translation_follows_absolute_span_positions_and_restored_parent_paint()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='195' height='12'><g transform='translate(5.25,2.5)' font-family='Example' font-size='100' fill='red'><text y='3'>A<tspan x='60' y='3' fill='blue'>A</tspan>A</text></g></svg>"u8.ToArray();
        byte[] expectedInput = "<svg xmlns='http://www.w3.org/2000/svg' width='195' height='12'><g font-family='Example' font-size='100' fill='red'><text x='5.25' y='5.5'>A</text><text x='65.25' y='5.5' fill='blue'>A</text><text x='125.25' y='5.5'>A</text></g></svg>"u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(expectedInput, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
        Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Translation_follows_deferred_text_path_layout_and_span_paint()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='195' height='12'><defs><path id='p' d='M0 3H200'/></defs><g transform='translate(5.25,2.5)'><text font-family='Example' font-size='100' fill='red'><textPath href='#p'>A<tspan fill='blue'>A</tspan>A</textPath></text></g></svg>"u8.ToArray();
        byte[] expectedInput = "<svg xmlns='http://www.w3.org/2000/svg' width='195' height='12'><g font-family='Example' font-size='100' fill='red'><text x='5.25' y='5.5'>A</text><text x='65.25' y='5.5' fill='blue'>A</text><text x='125.25' y='5.5'>A</text></g></svg>"u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(expectedInput, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
        Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("scale(2)")]
    [InlineData("rotate(5)")]
    [InlineData("skewX(10)")]
    public async Task Translation_admission_keeps_other_text_maps_rejected_before_font_acquisition(string transform)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><g transform='" + transform + "'><text font-family='Example'>A</text></g></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () =>
        {
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            document.ConvertToPng(CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
    }
}

public sealed class SvgRectangleTranslationTests
{
    [Theory]
    [InlineData("<g transform='translate(5,2)'>", "", "</g>", "9", "5", "")]
    [InlineData("", "transform='matrix(1 0 0 1 5.25 2.5)'", "", "9.25", "5.5", "")]
    [InlineData("<g transform='translate(8.25,3.5)'><g transform='translate(-3,-1)'>", "transform='translate(2,1)'", "</g></g>", "11.25", "6.5", "")]
    [InlineData("", "transform='translate(-8,-3)'", "", "-4", "0", "")]
    [InlineData("", "transform='translate(5.25,2.5)'", "", "9.25", "5.5", "fill-opacity='0.5' stroke-opacity='0.5'")]
    [InlineData("", "transform='translate(5.25,2.5)'", "", "9.25", "5.5", "opacity='0.5'")]
    public void Translation_preserves_rectangle_fill_and_stroke_against_independent_positions(string prefix, string placement, string suffix, string x, string y, string paint)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48'>" + prefix + "<rect x='4' y='3' width='24' height='22' fill='red' stroke='blue' stroke-width='2' " + placement + " " + paint + "/>" + suffix + "</svg>");
        byte[] expected = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48'><rect x='" + x + "' y='" + y + "' width='24' height='22' fill='red' stroke='blue' stroke-width='2' " + paint + "/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("scale(2)")]
    [InlineData("rotate(5)")]
    [InlineData("skewX(10)")]
    public void Translation_admission_keeps_other_rectangle_stroke_maps_explicit(string transform)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48'><rect x='4' y='3' width='24' height='22' stroke='blue' stroke-width='2' transform='" + transform + "'/></svg>");
        SvgException failure = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
    }
}
