using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgLineHeightTests
{
    [Theory]
    [InlineData("normal")]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("0px")]
    [InlineData("1.5")]
    [InlineData("125%")]
    [InlineData("1e2%")]
    [InlineData("1em")]
    [InlineData("1rem")]
    [InlineData("1ex")]
    [InlineData("2PX")]
    [InlineData("1pt")]
    [InlineData("inherit")]
    [InlineData("initial")]
    [InlineData("unset")]
    [InlineData("revert")]
    [InlineData("NoRmAl !IMPORTANT")]
    public void Explicit_line_layout_validates_line_height_without_retained_state(string value)
        => Assert.Equal(default, SvgInlineStyleParser.Parse("line-height:" + value, CancellationToken.None, true));

    [Theory]
    [InlineData("-1", SvgErrorCode.MalformedInput)]
    [InlineData("-1%", SvgErrorCode.MalformedInput)]
    [InlineData("NaN", SvgErrorCode.MalformedInput)]
    [InlineData("1e999px", SvgErrorCode.MalformedInput)]
    [InlineData("1 px", SvgErrorCode.MalformedInput)]
    [InlineData("1\t%", SvgErrorCode.MalformedInput)]
    [InlineData("\u00a01px", SvgErrorCode.MalformedInput)]
    [InlineData("1px\u00a0", SvgErrorCode.MalformedInput)]
    [InlineData("calc(1em + 2px)", SvgErrorCode.UnsupportedFeature)]
    [InlineData("var(--leading)", SvgErrorCode.UnsupportedFeature)]
    [InlineData("normal!important!important", SvgErrorCode.UnsupportedFeature)]
    public void Unadmitted_line_height_keeps_the_shared_scalar_and_declaration_boundaries(string value, SvgErrorCode expected)
        => Assert.Equal(expected, Assert.Throws<SvgException>(() =>
            SvgInlineStyleParser.Parse("line-height:" + value, CancellationToken.None, true)).ErrorCode);

    [Theory]
    [InlineData("font-size")]
    [InlineData("line-height")]
    [InlineData("letter-spacing")]
    public void Inert_dimensions_require_contiguous_numbers_and_units(string property)
        => Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() =>
            SvgInlineStyleParser.ParseGeometry(property + ":1 px", CancellationToken.None)).ErrorCode);

    [Theory]
    [InlineData("<g style='line-height:125%'><path d='M2 2H30V30Z'/></g>", 1)]
    [InlineData("<defs><path id='p' d='M2 2H30V30Z'/></defs><use href='#p' style='line-height:0'/>", 1)]
    public void Container_and_instance_line_height_do_not_require_a_font(string body, int expectedGeometries)
    {
        byte[] input = Wrap(body);
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(expectedGeometries, root.Geometries.Count());
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
    }

    [Fact]
    public void Line_height_does_not_change_font_or_scalar_discovery_across_styles()
    {
        byte[] input = Wrap("<style>text{line-height:0!important}</style><g style='line-height:125%' font-family='Example'><text x='2' y='24'>AV<tspan x='2' y='30' style='line-height:2em'>A</tspan></text></g>");
        using SvgFontCoverageDiscovery coverage = SvgFontRequestDiscovery.DiscoverWithCoverage(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgFontCoverageRequest entry = Assert.Single(coverage.Requests);
        Assert.Equal(new uint[] { 'A', 'V' }, entry.Scalars.Order());
        Assert.Equal("Example", entry.Request.Family);
        Assert.Equal(400, entry.Request.Weight);
        Assert.False(entry.Request.Italic);
    }

    [Fact]
    public void Unknown_css_names_still_reject_through_the_shared_parser()
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgInlineStyleParser.Parse("stroke-fill:red", CancellationToken.None, true)).ErrorCode);

    private static byte[] Wrap(string body)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
}
