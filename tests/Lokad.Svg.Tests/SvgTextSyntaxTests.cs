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
        Assert.Equal(0, style.Dx);
        Assert.Equal(0, style.Dy);
        Assert.Equal(SvgTextAnchor.Start, style.Anchor);
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
    public void Applies_last_inline_font_declarations_over_presentation_attributes()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text font-family='Attribute' font-weight='400' style='font-family: First; font-family: Inline; font-weight: 700; font-style: italic; font-size: 12'/>"));
        reader.Read();

        SvgTextStyle style = SvgTextSyntax.Parse(reader);

        Assert.Equal("Inline", style.Font.Family);
        Assert.Equal(700, style.Font.Weight);
        Assert.True(style.Font.Italic);
        Assert.Equal(12, style.FontSize);
    }

    [Fact]
    public void Charges_inline_font_property_scans_to_parser_work()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text style='font-family: Example'/>"));
        reader.Read();

        SvgException exception = Assert.Throws<SvgException>(() => SvgTextSyntax.Parse(reader, new ParserWorkCounter(0), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Scans_one_inline_font_declaration_set_once()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text style='font-family: Example; font-weight: 700; font-style: italic; font-size: 12'/>"));
        reader.Read();
        var workCounter = new ParserWorkCounter(100);

        SvgTextSyntax.Parse(reader, workCounter, CancellationToken.None);

        Assert.Equal(1, workCounter.Used);
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
    public void Parses_admitted_text_anchors()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text font-family='Example' text-anchor='middle'/>"));
        reader.Read();

        SvgTextStyle style = SvgTextSyntax.Parse(reader);

        Assert.Equal(SvgTextAnchor.Middle, style.Anchor);
    }

    [Fact]
    public void Parses_scalar_direct_text_position_adjustments()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text font-family='Example' x='2' y='3' dx='4' dy='5'/>"));
        reader.Read();

        SvgTextStyle style = SvgTextSyntax.Parse(reader);

        Assert.Equal(2, style.X);
        Assert.Equal(3, style.Y);
        Assert.Equal(4, style.Dx);
        Assert.Equal(5, style.Dy);
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
