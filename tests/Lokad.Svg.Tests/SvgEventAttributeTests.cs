using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgEventAttributeTests
{
    [Theory]
    [InlineData("svg", "onload")]
    [InlineData("defs", "onclick")]
    [InlineData("path", "onpointerenter")]
    [InlineData("style", "onerror")]
    [InlineData("textPath", "onfocus")]
    public void Event_metadata_leaves_the_reader_on_the_element(string element, string attribute)
    {
        string reference = element == "textPath" ? " href='#p'" : "";
        using StringReader input = new("<" + element + " " + attribute + "='fill:url(#missing);font-family:Unused'" + reference + "/>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        switch (element)
        {
            case "svg": SvgElementAttributePolicy.ValidateRoot(reader, CancellationToken.None); break;
            case "defs": SvgElementAttributePolicy.ValidateDefinitionContainer(reader, CancellationToken.None); break;
            case "path": SvgElementAttributePolicy.ValidatePath(reader, CancellationToken.None); break;
            case "style": SvgRootStyleSheetParser.ThrowIfAttributed(reader, CancellationToken.None); break;
            case "textPath": Assert.Equal("p", SvgTextPathSyntax.Parse(reader, CancellationToken.None).PathId); break;
            default: throw new ArgumentOutOfRangeException(nameof(element));
        }
        Assert.Equal(XmlNodeType.Element, reader.NodeType);
        Assert.Equal(element, reader.LocalName);
    }

    [Theory]
    [InlineData("xml:onload")]
    [InlineData("link:onload")]
    public void Event_admission_preserves_literal_names_and_reserved_namespaces(string attribute)
    {
        using StringReader input = new("<path xmlns:link='http://www.w3.org/1999/xlink' " + attribute + "='ignored'/>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgElementAttributePolicy.ValidatePath(reader, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("Onload")]
    public void Other_literal_names_remain_metadata_without_becoming_event_attributes(string attribute)
    {
        using StringReader input = new("<path " + attribute + "='ignored'/>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        Assert.True(reader.MoveToAttribute(attribute));
        Assert.False(SvgElementAttributePolicy.IsInertEventAttribute(reader));
        reader.MoveToElement();
        SvgElementAttributePolicy.ValidatePath(reader, CancellationToken.None);
        Assert.Equal(XmlNodeType.Element, reader.NodeType);
        Assert.Equal("ignored", reader.GetAttribute(attribute));
    }

    [Fact]
    public void Filter_and_merge_attributes_share_the_same_inert_rule()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>"
            + "<defs><filter id='f' onload='ignored'><feOffset dx='1' onerror='ignored'/><feMerge onclick='ignored'>"
            + "<feMergeNode onfocus='ignored'/></feMerge></filter></defs><rect width='8' height='8' filter='url(#f)'/></svg>");
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(scene.HasUnsupportedChild);
        Assert.Null((scene.FilterResources ?? throw new InvalidDataException("Missing filter resources.")).ResourceAt(0).Rejection);
    }
}
