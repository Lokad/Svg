using TextEncoding = System.Text.Encoding;
using System.Xml;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgForeignMetadataTests
{
    [Theory]
    [InlineData("<s:namedview xmlns:s='urn:editor'><s:page/></s:namedview>")]
    [InlineData("<d:SVGTestCase xmlns:d='http://www.w3.org/2000/02/svg/testsuite/description/'><d:OperatorScript><d:Paragraph>Example</d:Paragraph></d:OperatorScript></d:SVGTestCase>")]
    public void Recognized_foreign_descriptions_do_not_suppress_following_fonts(string metadata)
    {
        byte[] input = Document(metadata + "<text font-family='Example'>A</text>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        using SvgRoot scene = SvgRootParser.Parse(Document(metadata + "<rect width='4' height='4' fill='red'/>"), SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(scene.HasUnsupportedChild);
        Assert.Single(scene.Geometries);
    }

    [Theory]
    [InlineData("<rect width='4' height='4'/>")]
    [InlineData("<text font-family='Unused'>A</text>")]
    [InlineData("<style>text{font-family:Unused}</style>")]
    public void Foreign_descriptions_with_svg_descendants_still_reject_without_requesting_fonts(string child)
    {
        byte[] input = Document("<d:SVGTestCase xmlns:d='http://www.w3.org/2000/02/svg/testsuite/description/' id='first'>"
            + child + "</d:SVGTestCase><text font-family='Example'>A</text>");
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal("SVGTestCase", Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic).ElementName);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(2, true)]
    public void Shared_foreign_traversal_counts_each_element_and_leaves_the_closing_node(int maximumNodes, bool refused)
    {
        using StringReader source = new("<d:SVGTestCase xmlns:d='http://www.w3.org/2000/02/svg/testsuite/description/'><d:Paragraph/><d:Paragraph/></d:SVGTestCase>");
        using XmlReader reader = XmlReader.Create(source);
        reader.MoveToContent();
        SvgRenderLimits limits = new(1024, 16, 16, 1024, maximumNodes, 1024, 256, 256);
        if (refused)
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgStructuralAdmission.ConsumeForeignMetadata(reader, limits, 1, CancellationToken.None)).ErrorCode);
            return;
        }
        SvgForeignSubtree subtree = SvgStructuralAdmission.ConsumeForeignMetadata(reader, limits, 1, CancellationToken.None);
        Assert.Equal(3, subtree.NodeCount);
        Assert.False(subtree.ContainsSvg);
        Assert.Equal(XmlNodeType.EndElement, reader.NodeType);
        Assert.Equal("SVGTestCase", reader.LocalName);
    }

    private static byte[] Document(string body) => TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + body + "</svg>");
}
