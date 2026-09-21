using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgTextSyntaxTests
{
    [Fact]
    public void Inherited_text_style_requires_a_positive_weight_and_font_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgInheritedTextStyle(null, 0, false, 16, SvgTextWhitespaceMode.Default, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgInheritedTextStyle(null, 400, false, 0, SvgTextWhitespaceMode.Default, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgInheritedTextStyle(null, 400, false, 16, (SvgTextWhitespaceMode)2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgInheritedTextStyle(null, 400, false, 16, SvgTextWhitespaceMode.Default, double.NaN));
    }

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
    public void Rejects_an_oversized_inline_font_value()
    {
        string family = new('A', SvgSyntaxLimits.MaximumValueTokenCharacters + 1);
        using XmlReader reader = XmlReader.Create(new StringReader($"<text style='font-family: {family}'/>"));
        reader.Read();

        SvgException exception = Assert.Throws<SvgException>(() => SvgTextSyntax.Parse(reader));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Theory]
    [InlineData("font-family:", SvgErrorCode.UnsupportedFeature)]
    [InlineData("font-family: Example !important", SvgErrorCode.UnsupportedFeature)]
    [InlineData("font-family:: Example", SvgErrorCode.MalformedInput)]
    public void Rejects_inline_font_declarations_outside_the_paint_style_subset(string declaration, SvgErrorCode expectedErrorCode)
    {
        using XmlReader reader = XmlReader.Create(new StringReader($"<text style='{declaration}'/>"));
        reader.Read();

        SvgException exception = Assert.Throws<SvgException>(() => SvgTextSyntax.Parse(reader));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
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
    public void Parses_scalar_letter_spacing_from_attributes_and_inline_style()
    {
        using XmlReader attribute = XmlReader.Create(new StringReader("<text font-family='Example' letter-spacing='2.5'/>"));
        attribute.Read();
        using XmlReader inline = XmlReader.Create(new StringReader("<text font-family='Example' letter-spacing='2.5' style='letter-spacing: -1.5'/>"));
        inline.Read();

        Assert.Equal(2.5, SvgTextSyntax.Parse(attribute).LetterSpacing);
        Assert.Equal(-1.5, SvgTextSyntax.Parse(inline).LetterSpacing);
    }

    [Fact]
    public void Parses_finite_text_coordinates_without_rectangle_quantization()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text font-family='Example' font-size='12.3' x='0.1' y='2.2' dx='3.3' dy='4.4'/>"));
        reader.Read();

        SvgTextStyle style = SvgTextSyntax.Parse(reader);

        Assert.Equal(12.3, style.FontSize);
        Assert.Equal(0.1, style.X);
        Assert.Equal(2.2, style.Y);
        Assert.Equal(3.3, style.Dx);
        Assert.Equal(4.4, style.Dy);
    }

    [Fact]
    public void Resolves_inherited_text_properties_with_inline_precedence()
    {
        using XmlReader ancestorReader = XmlReader.Create(new StringReader("<g font-family='Ancestor' font-weight='700' font-style='italic' font-size='12'/>"));
        ancestorReader.Read();
        SvgInheritedTextStyle ancestor = SvgTextInheritance.Resolve(ancestorReader, SvgInheritedTextStyle.Default, default, null, null);
        using XmlReader childReader = XmlReader.Create(new StringReader("<text font-family='Attribute' font-weight='400' style='font-family: Inline; font-size: 14'/>"));
        childReader.Read();

        SvgTextInlineFontProperties inline = SvgInlineStyleParser.Parse(childReader.GetAttribute("style"), new ParserWorkCounter(10), CancellationToken.None, true).TextFont;
        SvgInheritedTextStyle resolved = SvgTextInheritance.Resolve(childReader, ancestor, inline, null, null);

        Assert.Equal("Inline", resolved.Family);
        Assert.Equal(400, resolved.Weight);
        Assert.True(resolved.Italic);
        Assert.Equal(14, resolved.FontSize);
    }

    [Fact]
    public void Resolves_bounded_css_whitespace_over_xml_and_inherited_modes()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text xml:space='preserve'/>"));
        reader.Read();

        Assert.Equal(SvgTextWhitespaceMode.Default, SvgTextSyntax.ParseWhitespaceMode(reader, "normal", SvgTextWhitespaceMode.Preserve));
        Assert.Equal(SvgTextWhitespaceMode.Preserve, SvgTextSyntax.ParseWhitespaceMode(reader, "pre", SvgTextWhitespaceMode.Default));
        Assert.Equal(SvgTextWhitespaceMode.Preserve, SvgTextSyntax.ParseWhitespaceMode(reader, null, SvgTextWhitespaceMode.Default));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextSyntax.ParseWhitespaceMode(reader, "pre-wrap", SvgTextWhitespaceMode.Default)).ErrorCode);
    }

    [Fact]
    public void Admits_only_whitespace_tspan_styles()
    {
        using XmlReader accepted = XmlReader.Create(new StringReader("<tspan xml:space='preserve' style='white-space: normal'/>"));
        accepted.Read();
        using XmlReader rejected = XmlReader.Create(new StringReader("<tspan style='font-size: 12'/>"));
        rejected.Read();
        using XmlReader rejectedSpacing = XmlReader.Create(new StringReader("<tspan style='white-space: pre; letter-spacing: 2'/>"));
        rejectedSpacing.Read();

        Assert.True(SvgTextSyntax.TryParseTspanWhitespaceMode(accepted, SvgTextWhitespaceMode.Preserve, default, new ParserWorkCounter(100), CancellationToken.None, out SvgTextWhitespaceMode mode));
        Assert.Equal(SvgTextWhitespaceMode.Default, mode);
        Assert.False(SvgTextSyntax.TryParseTspanWhitespaceMode(rejected, SvgTextWhitespaceMode.Default, default, new ParserWorkCounter(100), CancellationToken.None, out _));
        Assert.False(SvgTextSyntax.TryParseTspanWhitespaceMode(rejectedSpacing, SvgTextWhitespaceMode.Default, default, new ParserWorkCounter(100), CancellationToken.None, out _));
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
