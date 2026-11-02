using Lokad.Svg.Parsing;
using System.Xml;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgStaticScriptTests
{
    [Theory]
    [InlineData("<!--comment--><![CDATA[<style>ignored</style>]]>", false, 2)]
    [InlineData("<g><rect/></g>", true, 4)]
    public void Shared_script_scan_counts_each_node_and_stops_before_the_following_sibling(string body, bool unsupported, int expectedNodes)
    {
        using StringReader input = new("<svg xmlns='http://www.w3.org/2000/svg'><script>" + body + "</script><rect/></svg>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        Assert.True(reader.ReadToDescendant("script"));
        SvgLeafSubtree result = SvgStructuralAdmission.ConsumeScriptSubtree(reader, SvgRenderLimits.Default, 2, 0, CancellationToken.None);
        Assert.Equal(expectedNodes, result.NodeCount);
        Assert.Equal(unsupported, result.HasUnsupportedChildren);
        Assert.Equal(XmlNodeType.EndElement, reader.NodeType);
        Assert.Equal("script", reader.LocalName);
        Assert.True(reader.Read());
        Assert.Equal("rect", reader.LocalName);
    }

    [Theory]
    [InlineData(1, 8, 8, 0)]
    [InlineData(100, 1, 8, 0)]
    [InlineData(100, 8, 1, 0)]
    [InlineData(100, 4, 8, 3)]
    public void Script_scanning_preserves_nodes_attributes_and_absolute_depth(int nodes, int depth, int attributes, int offset)
    {
        using StringReader input = new("<script xmlns='http://www.w3.org/2000/svg'><g a='1' b='2'><rect/></g></script>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        SvgRenderLimits limits = new(1024, 16, 256, 1024, nodes, 1024, depth, attributes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgStructuralAdmission.ConsumeScriptSubtree(reader, limits, 1, offset, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Inert_script_scanning_preserves_xml_failure()
    {
        using StringReader input = new("<script xmlns='http://www.w3.org/2000/svg'><g></script>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        Assert.Throws<XmlException>(() => SvgStructuralAdmission.ConsumeScriptSubtree(reader, SvgRenderLimits.Default, 1, 0, CancellationToken.None));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("group")]
    [InlineData("defs")]
    [InlineData("leaf")]
    [InlineData("gradient")]
    [InlineData("filter")]
    public void Character_data_scripts_do_not_change_static_sources(string context)
    {
        const string script = "<s:script xmlns:s='http://www.w3.org/2000/svg' href='https://example.invalid/unused.js' style='font-family:Unused;fill:url(#missing)'><![CDATA[<style>ignored</style>]]></s:script>";
        const string rectangle = "<rect width='8' height='8' fill='red'/>";
        string content = context switch
        {
            "root" => script + rectangle,
            "group" => "<g>" + script + rectangle + "</g>",
            "defs" => "<defs>" + script + "<rect id='r' width='8' height='8' fill='red'/></defs><use href='#r'/>",
            "leaf" => "<rect width='8' height='8' fill='red'>" + script + "</rect>",
            "gradient" => "<defs><linearGradient id='p'>" + script + "<stop stop-color='red'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>",
            "filter" => "<defs><filter id='f'>" + script + "<feOffset dx='1'/></filter></defs><rect width='8' height='8' filter='url(#f)'/>",
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
        using SvgRoot root = SvgRootParser.Parse(Document(content), SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        Assert.NotEmpty(root.Geometries);
    }

    [Fact]
    public void A_script_consumes_the_first_switch_slot_without_selecting_the_following_font()
    {
        byte[] input = Document("<switch><script>void 0</script><text font-family='Unused'>A</text></switch>");
        Assert.Empty(Lokad.Svg.Fonts.SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        Assert.Empty(root.Geometries);
    }

    [Theory]
    [InlineData("<script><rect width='1' height='1'/></script>")]
    [InlineData("<script><style>rect{fill:red}</style></script>")]
    [InlineData("<script xmlns='urn:foreign'>ignored</script>")]
    public void Static_script_admission_preserves_character_data_and_namespace_boundaries(string child)
    {
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(Document(child), SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }

    private static byte[] Document(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
}
