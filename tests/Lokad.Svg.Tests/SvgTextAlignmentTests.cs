using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Xml;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgTextAlignmentTests
{
    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("center")]
    [InlineData("justify")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("match-parent")]
    [InlineData("inherit")]
    [InlineData("initial")]
    [InlineData("unset")]
    [InlineData("revert")]
    [InlineData("CeNtEr !IMPORTANT")]
    public void Preformatted_alignment_is_validated_without_retained_style(string value)
    {
        InlineStyle style = SvgInlineStyleParser.Parse("text-align:" + value, CancellationToken.None, true);
        Assert.Equal(default, style);
    }

    [Theory]
    [InlineData("middle")]
    [InlineData("left right")]
    [InlineData("var(--align)")]
    [InlineData("center!important!important")]
    public void Unadmitted_alignment_keeps_the_shared_declaration_validation(string value)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgInlineStyleParser.Parse("text-align:" + value, CancellationToken.None, true)).ErrorCode);

    [Theory]
    [InlineData("<g style='text-align:center'><path d='M2 2H30V30Z'/></g>", 1)]
    [InlineData("<defs><symbol id='s' style='text-align:center'/></defs><use href='#s'/>", 0)]
    [InlineData("<defs><symbol id='s' style='text-align:center'><path d='M2 2H30V30Z'/></symbol></defs><use href='#s'/>", 1)]
    public void Inert_container_alignment_does_not_require_a_font(string body, int expectedGeometries)
    {
        byte[] input = Wrap(body);
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(expectedGeometries, root.Geometries.Count());
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
    }

    [Fact]
    public void Alignment_does_not_replace_anchors_or_change_discovered_characters()
    {
        byte[] input = Wrap("<style>text{text-align:right!important}</style><g style='text-align:center' font-family='Example'><text x='5 15 25' text-anchor='end' style='text-align:left'>A<tspan text-anchor='middle' style='text-align:justify'>V</tspan>A</text></g>");
        using SvgFontCoverageDiscovery coverage = SvgFontRequestDiscovery.DiscoverWithCoverage(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgFontCoverageRequest entry = Assert.Single(coverage.Requests);
        Assert.Equal(new uint[] { 'A', 'V' }, entry.Scalars.Order());
        SvgFontFaceRequest request = entry.Request;
        Assert.Equal("Example", request.Family);
        Assert.Equal(400, request.Weight);
        Assert.False(request.Italic);
        using XmlReader reader = XmlReader.Create(new StringReader("<text text-anchor='end'/>"));
        Assert.True(reader.Read());
        SvgInheritedTextStyle font = SvgTextInheritance.Resolve(reader, SvgInheritedTextStyle.Default,
            SvgInlineStyleParser.Parse("text-align:center!important", CancellationToken.None, true), default);
        Assert.Equal(SvgTextAnchor.End, font.Anchor);
    }

    private static byte[] Wrap(string body)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
}
