using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgMarkerSyntaxTests
{
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
