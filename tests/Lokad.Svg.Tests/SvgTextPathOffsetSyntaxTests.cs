using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgTextPathOffsetSyntaxTests
{
    [Theory]
    [InlineData("1px%")]
    [InlineData("1PX%")]
    [InlineData("-2.5px%")]
    [InlineData("1e2px%")]
    public void Percentage_offsets_do_not_accept_a_second_length_unit(string offset)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Parse(offset)).ErrorCode);

    [Theory]
    [InlineData("1", 1, false)]
    [InlineData("1px", 1, false)]
    [InlineData("1PX", 1, false)]
    [InlineData("1%", 1, true)]
    [InlineData("-2.5%", -2.5, true)]
    [InlineData("1e2%", 100, true)]
    [InlineData("1.7976931348623157e308%", double.MaxValue, true)]
    public void Number_length_and_percentage_offsets_keep_their_finite_value_and_domain(string offset, double expected, bool percentage)
    {
        SvgTextPathReference reference = Parse(offset);
        Assert.Equal(expected, reference.StartOffset);
        Assert.Equal(percentage, reference.IsPercentage);
    }

    private static SvgTextPathReference Parse(string offset)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<textPath href='#p' startOffset='" + offset + "'/>"));
        reader.MoveToContent();
        return SvgTextPathSyntax.Parse(reader, CancellationToken.None);
    }
}
