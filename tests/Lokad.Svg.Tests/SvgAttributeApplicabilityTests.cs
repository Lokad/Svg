using System.Xml;
using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgAttributeApplicabilityTests
{
    [Theory]
    [InlineData("svg", "smiling='url(#missing)' viewbox='0 0 1 1'")]
    [InlineData("svg", "viewport-fill-new='red'")]
    [InlineData("g", "x='not-a-length' y='900'")]
    [InlineData("rect", "r='not-a-length'")]
    [InlineData("rect", "href='unused.svg#shape'")]
    [InlineData("rect", "export-data='url(#missing)'")]
    [InlineData("path", "title='description'")]
    [InlineData("circle", "width='not-a-length' height='900'")]
    [InlineData("linearGradient", "x3='not-a-length'")]
    [InlineData("filter", "blend='not-a-mode'")]
    [InlineData("feFlood", "in='SourceGraphic'")]
    [InlineData("feMergeNode", "dx='1'")]
    [InlineData("style", "export-data='unused'")]
    [InlineData("defs", "export-data='unused'")]
    public void Inapplicable_attributes_do_not_supply_values_to_geometry_or_resources(string element, string attributes)
    {
        const string shape = "<rect width='8' height='8' fill='red'/>";
        string content = element switch
        {
            "svg" => shape,
            "rect" => "<rect " + attributes + " width='8' height='8' fill='red'/>",
            "circle" => "<circle " + attributes + " cx='4' cy='4' r='4' fill='red'/>",
            "path" => "<path " + attributes + " d='M0 0H8V8H0Z' fill='red'/>",
            "linearGradient" => "<defs><linearGradient id='p' " + attributes + "><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>",
            "filter" => "<defs><filter id='f' " + attributes + "><feFlood flood-color='red'/></filter></defs><rect width='8' height='8' filter='url(#f)'/>",
            "feFlood" => "<defs><filter id='f'><feFlood " + attributes + " flood-color='red'/></filter></defs><rect width='8' height='8' filter='url(#f)'/>",
            "feMergeNode" => "<defs><filter id='f'><feMerge><feMergeNode " + attributes + "/></feMerge></filter></defs><rect width='8' height='8' filter='url(#f)'/>",
            "style" => "<style " + attributes + ">rect{fill:red}</style>" + shape,
            "defs" => "<defs " + attributes + "/>" + shape,
            _ => "<g " + attributes + ">" + shape + "</g>"
        };
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' "
            + (element == "svg" ? attributes : "") + ">" + content + "</svg>");
        RenderBudget budget = new(new HostMemoryBudget(long.MaxValue));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            Assert.False(scene.HasUnsupportedChild);
            Assert.Single(scene.Geometries);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("textPath")]
    [InlineData("tspan")]
    public void Text_syntax_keeps_ignored_metadata_without_consuming_the_element(string element)
    {
        using StringReader source = new("<" + element + " href='#p' export-data='untouched'/>");
        using XmlReader reader = XmlReader.Create(source);
        reader.MoveToContent();
        if (element == "textPath") Assert.Equal("p", SvgTextPathSyntax.Parse(reader, CancellationToken.None).PathId);
        else Assert.True(SvgTextSyntax.TryParseChildStyle(reader, new(SvgTextWhitespaceMode.Default, 0), default, CancellationToken.None, out _));
        Assert.Equal(XmlNodeType.Element, reader.NodeType);
        Assert.Equal("untouched", reader.GetAttribute("export-data"));
    }

    [Theory]
    [InlineData("<rect width='8' height='8' shape-rendering='crispEdges'/>")]
    [InlineData("<rect width='8' height='8' transform-origin='4 4' transform='rotate(15)'/>")]
    public void Applicable_unimplemented_presentation_still_rejects(string content)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData("color-rendering")]
    [InlineData("requiredFeatures")]
    public void Legacy_standard_names_remain_meaningful_after_the_svg2_table_is_applied(string attribute)
    {
        using StringReader source = new("<rect " + attribute + "='untouched'/>");
        using XmlReader reader = XmlReader.Create(source);
        reader.MoveToContent();
        Assert.True(reader.MoveToFirstAttribute());
        Assert.False(SvgAttributeSemantics.IsInert("rect", reader));
        Assert.Equal(attribute, reader.LocalName);
        Assert.Equal("untouched", reader.Value);
    }

    [Theory]
    [InlineData("linearGradient", "absent", false, false)]
    [InlineData("linearGradient", "objectBoundingBox", false, true)]
    [InlineData("linearGradient", "userSpaceOnUse", true, true)]
    [InlineData("linearGradient", "invalid", false, true)]
    [InlineData("linearGradient", " userSpaceOnUse ", false, true)]
    [InlineData("linearGradient", "USERSPACEONUSE", false, true)]
    [InlineData("linearGradient", "", false, true)]
    [InlineData("radialGradient", "absent", false, false)]
    [InlineData("radialGradient", "objectBoundingBox", false, true)]
    [InlineData("radialGradient", "userSpaceOnUse", true, true)]
    [InlineData("radialGradient", "invalid", false, true)]
    [InlineData("radialGradient", " userSpaceOnUse ", false, true)]
    [InlineData("radialGradient", "USERSPACEONUSE", false, true)]
    [InlineData("radialGradient", "", false, true)]
    public void Gradient_unit_fallback_keeps_explicitness_for_template_inheritance(string element, string units, bool userSpace, bool specified)
    {
        using StringReader source = new("<" + element + " href='#parent'"
            + (units == "absent" ? "" : " gradientUnits='" + units + "'") + "/>");
        using XmlReader reader = XmlReader.Create(source);
        reader.MoveToContent();
        if (element == "linearGradient")
        {
            SvgGradientTemplate template = SvgGradientSyntax.ParseLinearVector(reader, CancellationToken.None);
            Assert.Equal(userSpace, template.IsUserSpace);
            Assert.Equal(specified, template.HasUnits);
        }
        else
        {
            SvgRadialTemplate template = SvgGradientSyntax.ParseRadialVector(reader, CancellationToken.None);
            Assert.Equal(userSpace, template.IsUserSpace);
            Assert.Equal(specified, template.HasUnits);
        }
    }
}
