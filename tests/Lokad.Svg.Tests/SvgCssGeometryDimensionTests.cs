using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCssGeometryDimensionTests
{
    [Theory]
    [InlineData("rect", "width", false)]
    [InlineData("rect", "height", false)]
    [InlineData("rect", "width", true)]
    [InlineData("rect", "height", true)]
    [InlineData("image", "width", false)]
    [InlineData("image", "height", false)]
    [InlineData("image", "width", true)]
    [InlineData("image", "height", true)]
    public void Unsupported_css_dimensions_report_the_winning_property_and_release_storage(string target, string property, bool sheet)
    {
        string image = target == "image" ? " href='" + ImageHref() + "'" : string.Empty;
        string shape = "<" + target + " id='target' width='4' height='4'" + image;
        string control = shape + "/>";
        // The !important declaration wins over a different normal inline value.
        string actual = sheet
            ? shape + " style='" + property + ":8px'/><style>#target{" + property + ":12px!important}</style>"
            : shape + " style='" + property + ":12px!important'/><style>#target{" + property + ":8px}</style>";
        byte[] input = Document(actual);
        Assert.NotEmpty(Render(control));
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal(target, source.ElementName);
        Assert.Equal("target", source.ElementId);
        Assert.Equal(property, source.AttributeName);
        Assert.Equal("12px", source.AttributeValue);
        Assert.Equal(2, source.LineNumber);

        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<defs><rect id='source' class='target' width='4' height='4'/></defs><use href='#source'/>")]
    [InlineData("<defs><symbol id='source'><rect class='target' width='4' height='4'/></symbol></defs><use href='#source'/>")]
    [InlineData("<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect class='target' width='4' height='4'/></pattern></defs><rect width='16' height='16' fill='url(#p)'/>")]
    [InlineData("<defs><mask id='m'><rect class='target' width='4' height='4' fill='white'/></mask></defs><rect width='16' height='16' mask='url(#m)'/>")]
    [InlineData("<defs><clipPath id='c'><rect class='target' width='4' height='4'/></clipPath></defs><rect width='16' height='16' clip-path='url(#c)'/>")]
    public void Matched_resource_and_instance_source_dimensions_are_rejected(string content)
    {
        Assert.NotEmpty(Render(content));
        SvgException error = Assert.Throws<SvgException>(() => Render(content + "<style>.target{width:12px}</style>"));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal("rect", source.ElementName);
        Assert.Equal("width", source.AttributeName);
        Assert.Equal("12px", source.AttributeValue);
    }

    [Theory]
    [InlineData("<g><rect width='4' height='4'/></g>", "g")]
    [InlineData("<circle cx='4' cy='4' r='3'/>", "circle")]
    [InlineData("<svg width='8' height='8'><rect width='4' height='4'/></svg>", "svg>svg")]
    [InlineData("<defs><symbol id='s'><rect width='4' height='4'/></symbol></defs><use href='#s'/>", "symbol")]
    [InlineData("<defs><rect id='s' width='4' height='4'/></defs><use href='#s'/>", "use")]
    [InlineData("<rect width='4' height='4'/>", ".unmatched")]
    public void Nonapplicable_and_unmatched_dimension_rules_remain_inert(string content, string selector)
        => Assert.Equal(Render(content), Render(content + "<style>" + selector + "{width:12px;height:12px}</style>"));

    [Fact]
    public void Root_sizing_and_dimension_inspection_keep_their_cascade()
    {
        const string geometry = "<rect width='16' height='16' fill='red'/>";
        byte[] input = Document(geometry + "<style>svg{width:12px!important;height:8px}</style>");
        byte[] expected = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='12' height='8'>" + geometry + "</svg>");
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        SvgDocumentDimensions dimensions = SvgConverter.InspectDimensions(input, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(12, dimensions.LogicalViewport.Width);
        Assert.Equal(8, dimensions.LogicalViewport.Height);
        // Inspection reads root sizing without asserting leaf renderability.
        dimensions = SvgConverter.InspectDimensions(Document("<rect width='4' height='4' style='width:12px'/>"), SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(16, dimensions.LogicalViewport.Width);
    }

    [Fact]
    public void Retained_image_dimensions_are_rejected_before_replay()
    {
        string content = "<defs><image id='source' href='" + ImageHref() + "' width='4' height='4'/></defs><use href='#source'/>";
        Assert.NotEmpty(Render(content));
        SvgException error = Assert.Throws<SvgException>(() => Render(content + "<style>#source{height:12px}</style>"));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal("height", Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic).AttributeName);
    }

    private static string ImageHref()
        => "data:image/png;base64," + Convert.ToBase64String(Render("<rect width='16' height='16' fill='red'/>"));

    private static byte[] Document(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>\n" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Document(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
