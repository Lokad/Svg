using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgTextSyntaxTests
{
    [Fact]
    public void Parses_the_initial_explicit_text_style_and_baseline()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text font-family='Example' font-weight='bold' font-style='italic' font-size='12.5' x='2' y='3'/>"));
        reader.Read();

        SvgTextStyle style = SvgTextSyntax.Parse(reader);

        Assert.Equal("Example", style.Font.Family);
        Assert.Equal(700, style.Font.Weight);
        Assert.True(style.Font.Italic);
        Assert.Equal(12.5, style.FontSize);
        Assert.Equal(2, style.X);
        Assert.Equal(3, style.Y);
    }

    [Fact]
    public void Normalizes_shared_text_font_tokens_before_constructing_a_request()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text font-family=\"  'Example'  \" font-weight=' BOLD ' font-style=' ITALIC ' font-size='12'/>"));
        reader.Read();

        SvgTextStyle style = SvgTextSyntax.Parse(reader);

        Assert.Equal("Example", style.Font.Family);
        Assert.Equal(700, style.Font.Weight);
        Assert.True(style.Font.Italic);
    }

    [Fact]
    public void Normalizes_a_quoted_direct_font_family()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text font-family=\"'Example'\"/>"));
        reader.Read();

        SvgTextStyle style = SvgTextSyntax.Parse(reader);

        Assert.Equal("Example", style.Font.Family);
    }

    [Fact]
    public void Rejects_a_missing_or_list_font_family()
    {
        using XmlReader missing = XmlReader.Create(new StringReader("<text/>"));
        missing.Read();
        using XmlReader list = XmlReader.Create(new StringReader("<text font-family='First, Second'/>"));
        list.Read();

        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextSyntax.Parse(missing)).ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextSyntax.Parse(list)).ErrorCode);
    }
}
