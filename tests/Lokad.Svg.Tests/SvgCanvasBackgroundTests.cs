using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Xml;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgCanvasBackgroundTests
{
    [Theory]
    [InlineData("background-color:red", "red", false)]
    [InlineData("BACKGROUND-COLOR:currentColor", "currentColor", false)]
    [InlineData("background-color:red;background-color:blue", "blue", false)]
    [InlineData("background-color:red!important;background-color:blue", "red", true)]
    [InlineData("background-color:red;background-color:blue!important", "blue", true)]
    [InlineData("background-color:red!important;background-color:blue!important", "blue", true)]
    [InlineData("background-color:inherit", "inherit", false)]
    public void Canvas_declarations_retain_value_and_ordinary_priority(string declaration, string expected, bool important)
    {
        InlineStyle style = SvgInlineStyleParser.Parse(declaration, CancellationToken.None);
        Assert.Equal(expected, style.BackgroundColor);
        Assert.Equal(important, (style.Important & InlineImportantProperties.BackgroundColor) != 0);
    }

    [Theory]
    [InlineData("inherit")]
    [InlineData("initial")]
    [InlineData("unset")]
    [InlineData("INITIAL")]
    public void Canvas_global_keywords_use_the_initial_transparent_document_background(string value)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' style='background-color:" + value + "'/>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(0, root.CanvasBackground.Alpha);
    }

    [Fact]
    public void Canvas_current_color_keeps_source_alpha_despite_hidden_zero_opacity_root()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' style='color:rgba(24,96,160,.5);background-color:currentColor;visibility:hidden;opacity:0'/>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(SvgColorParser.ParsePaint("rgba(24,96,160,.5)", SvgRootParser.CssNamedColors), root.CanvasBackground);
        Assert.Empty(root.Geometries);
    }

    [Fact]
    public void Nonapplicable_shape_and_nested_viewport_backgrounds_do_not_parse_color_or_inherit_canvas_paint()
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>.n{background-color:unused}</style><g style='background-color:unused'><rect width='2' height='2' style='background-color:inherit'/><svg class='n' width='4' height='4'><circle r='1'/></svg></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(0, root.CanvasBackground.Alpha);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void Dimension_inspection_admits_canvas_syntax_without_validating_paint()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='17' height='11' style='background-color:unused'/>"u8.ToArray();
        Assert.Equal(new SvgViewport(17, 11), SvgConverter.InspectDimensions(input, SvgRenderOptions.Default, CancellationToken.None).LogicalViewport);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData("display='none'", "")]
    [InlineData("style='display:none'", "")]
    [InlineData("id='r'", "#r{display:none}")]
    public void Image_root_generation_keeps_computed_display_for_explicit_descendant_inheritance(string attributes, string css)
    {
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (BudgetedParserStringStore strings = new(budget))
        using (BudgetedParserList<CssRule> rules = new(budget, SvgMemoryCategory.Parser, CancellationToken.None))
        using (StringReader input = new("<svg " + attributes + "/>"))
        using (XmlReader reader = XmlReader.Create(input))
        {
            if (css.Length != 0) SvgStyleSheet.Parse(css, 512, rules, strings, budget, CancellationToken.None);
            reader.MoveToContent();
            SvgRootAttributes root = SvgRootAttributes.Parse(reader, SvgRootParser.CssNamedColors, rules, strings, null, CancellationToken.None);
            Assert.True(root.InheritedPresentation.IsDisplayed);
            Assert.Equal(SvgDisplay.None, root.InheritedPresentation.ElementDisplay);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
