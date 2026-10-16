using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgMarkerSyntaxTests
{
    [Theory]
    [InlineData("display='grid'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("visibility='collapse'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stroke-linecap='triangle'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stroke-linejoin='arcs'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stroke-dasharray='-1 2'", SvgErrorCode.MalformedInput)]
    [InlineData("stroke-dashoffset='25%'", SvgErrorCode.UnsupportedFeature)]
    public void Invalid_marker_container_presentation_rejects_without_retaining_sources(string attributes, SvgErrorCode category)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg' width='32' height='24'><defs><marker id='m' {attributes}><path d='M0 0L3 2L0 4Z'/></marker></defs><path d='M4 12H24' stroke='blue' marker-end='url(#m)'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(category, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Zero_width_marker_remains_nonpainting_with_inherited_stroke_presentation()
    {
        const string body = "<path d='M4 12H24' fill='none' stroke='blue'";
        const string root = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='24'>";
        byte[] expected = System.Text.Encoding.UTF8.GetBytes(root + body + "/></svg>");
        byte[] input = System.Text.Encoding.UTF8.GetBytes(root + "<defs><marker id='m' markerWidth='0' display='none' visibility='visible' stroke='red' stroke-linecap='round' stroke-linejoin='round' stroke-dasharray='2 1' stroke-dashoffset='.5'><path d='M0 0L3 2L0 4'/></marker></defs>" + body + " marker-end='url(#m)'/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Font_relative_marker_lengths_keep_em_values_and_defer_ex_metrics()
    {
        SvgMarkerDefinition em = Parse("font-size='12' markerWidth='1.5em' markerHeight='1em' refX='.25em' refY='.5em'", default);
        Assert.Equal((18d, 12d, 3d, 6d), (em.Width, em.Height, em.RefX, em.RefY));
        Assert.Null(em.FontLengths);
        SvgMarkerDefinition ex = Parse("font-family='Example' font-size='18' markerWidth='3ex' markerHeight='2ex' refX='.5ex' refY='1ex'", default);
        SvgMarkerFontLengths lengths = Assert.IsType<SvgMarkerFontLengths>(ex.FontLengths);
        Assert.Equal("Example", lengths.Font.Family);
        Assert.Equal(18, lengths.Font.FontSize);
        Assert.Equal((3d, 2d, .5d, 1d), (lengths.Width, lengths.Height, lengths.RefX, lengths.RefY));
    }

    [Fact]
    public void Axis_half_turn_at_a_repeated_closed_vertex_retains_the_native_bisector()
    {
        using StringReader input = new("<path xmlns='http://www.w3.org/2000/svg' d='M24 27L24 27L78 27L78 66L24 66L24 66Z'/>");
        using XmlReader reader = XmlReader.Create(input); reader.MoveToContent();
        RenderBudget budget = new(100_000);
        ParserWorkCounter work = new(100_000);
        using (SvgDocumentState state = new(budget, work, CancellationToken.None))
        using (BudgetedParserList<SvgMarkerPosition> positions = SvgMarkerVertices.Read(reader, 100, state, work, CancellationToken.None))
        {
            Assert.Equal(7, positions.Count);
            Assert.Equal(new SvgDevicePoint(24, 66), positions[4].Point);
            Assert.Equal(90, positions[4].Angle);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("1in", 96)]
    [InlineData("1.27cm", 48)]
    [InlineData("12.7mm", 48)]
    [InlineData("24Q", 22.677165985107422)]
    [InlineData("9pt", 12)]
    [InlineData(".75pc", 12)]
    [InlineData("12PX", 12)]
    [InlineData("1e2px", 100)]
    public void Absolute_lengths_resolve_dimensions_and_anchors(string length, double expected)
    {
        SvgMarkerDefinition marker = Parse($"markerWidth='{length}' markerHeight='{length}' refX='{length}' refY='{length}'", new(80, 40));
        Assert.Equal(expected, marker.Width);
        Assert.Equal(expected, marker.Height);
        Assert.Equal(expected, marker.RefX);
        Assert.Equal(expected, marker.RefY);
    }

    [Theory]
    [InlineData("45", 45)]
    [InlineData("45deg", 45)]
    [InlineData("50grad", 45)]
    [InlineData(".7853981633974483rad", 44.999996185302734)]
    [InlineData(".125turn", 45)]
    [InlineData("+45deg", 45)]
    [InlineData("1e2deg", 100)]
    [InlineData("-450deg", -450)]
    public void Angles_keep_native_specified_precision_before_degree_conversion(string angle, double expected)
    {
        SvgMarkerDefinition marker = Parse($"orient='{angle}'", default);
        Assert.Equal(SvgMarkerOrientation.Angle, marker.Orientation);
        Assert.Equal(expected, marker.Angle);
    }

    [Fact]
    public void Percentages_retain_each_viewport_axis_and_zero_dimensions_remain_admitted()
    {
        SvgMarkerDefinition marker = Parse("markerWidth='50%' markerHeight='50%' refX='50%' refY='50%'", new(80, 40));
        Assert.Equal(40, marker.Width);
        Assert.Equal(20, marker.Height);
        Assert.Equal(40, marker.RefX);
        Assert.Equal(20, marker.RefY);
        Assert.Equal(0, Parse("markerWidth='0px' markerHeight='0pt'", default).Width);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Parse("refX='50%'", default)).ErrorCode);
    }

    [Theory]
    [InlineData("markerWidth='-1px'", SvgErrorCode.MalformedInput)]
    [InlineData("markerHeight='-1pt'", SvgErrorCode.MalformedInput)]
    [InlineData("markerWidth='12 px'", SvgErrorCode.MalformedInput)]
    [InlineData("orient='45 deg'", SvgErrorCode.MalformedInput)]
    [InlineData("orient='45DEG'", SvgErrorCode.MalformedInput)]
    [InlineData("markerWidth='NaNpx'", SvgErrorCode.MalformedInput)]
    [InlineData("markerWidth='1e38cm'", SvgErrorCode.LimitExceeded)]
    [InlineData("orient='1e38turn'", SvgErrorCode.LimitExceeded)]
    public void Invalid_or_excessive_values_retain_categorized_outcomes(string attributes, SvgErrorCode category)
        => Assert.Equal(category, Assert.Throws<SvgException>(() => Parse(attributes, default)).ErrorCode);

    [Fact]
    public void Marker_values_keep_token_bounds()
    {
        string length = new('0', SvgSyntaxLimits.MaximumValueTokenCharacters + 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Parse($"markerWidth='{length}px'", default)).ErrorCode);
    }

    private static SvgMarkerDefinition Parse(string attributes, SvgViewportLengthBases bases)
    {
        using StringReader input = new($"<marker xmlns='http://www.w3.org/2000/svg' {attributes}/>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        ParserWorkCounter work = new(10_000);
        SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, work, CancellationToken.None);
        return SvgMarkerSyntax.Parse(reader, style, bases, work, CancellationToken.None);
    }
}
