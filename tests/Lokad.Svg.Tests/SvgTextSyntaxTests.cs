using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgTextSyntaxTests
{
    [Theory]
    [InlineData("font-size:14px;font-size:18")]
    [InlineData("font-size:18!important;font-size:14px")]
    [InlineData("font-size:14px!important;font-size:18")]
    public void Invalid_unitless_css_font_size_preserves_the_valid_declaration(string declarations)
    {
        InlineStyle style = SvgInlineStyleParser.Parse(declarations, new ParserWorkCounter(100), CancellationToken.None, true);
        Assert.Equal("14px", style.TextFont.Size);
    }

    [Fact]
    public void Inherited_text_style_requires_a_positive_weight_and_font_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgInheritedTextStyle(null, 0, false, 16, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgInheritedTextStyle(null, 400, false, 0, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgInheritedTextStyle(null, 400, false, 16, (SvgTextWhitespaceMode)2, 0, SvgTextAnchor.Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgInheritedTextStyle(null, 400, false, 16, SvgTextWhitespaceMode.Default, double.NaN, SvgTextAnchor.Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgInheritedTextStyle(null, 400, false, 16, SvgTextWhitespaceMode.Default, 0, (SvgTextAnchor)99));
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
        Assert.Equal(16, style.FontSize);
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
    public void Root_lists_follow_normalized_scalar_boundaries_and_descendant_precedence()
    {
        SvgTextPositionLists lists = new("8 24 40", "28", "0 1", null, "0 10 -10");
        RenderBudget budget = new(10_000);
        using (OwnedParserArray<SvgTextPositionAdjustment> positions = lists.Build("A😀B", [new(3, 50, null, 2, -1)], budget, new(1_000), CancellationToken.None))
        {
            Assert.Equal(new SvgTextPositionAdjustment(0, 8, 28, 0, 0) { Rotation = 0 }, positions.Items[0]);
            Assert.Equal(new SvgTextPositionAdjustment(1, 24, null, 1, 0) { Rotation = 10 }, positions.Items[1]);
            Assert.Equal(new SvgTextPositionAdjustment(3, 50, null, 2, -1) { Rotation = -10 }, positions.Items[2]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("1 NaN")]
    [InlineData("1 2em")]
    [InlineData("1 20%")]
    [InlineData("1 1e309")]
    public void Root_lists_validate_every_value_before_layout(string value)
    {
        using XmlReader reader = XmlReader.Create(new StringReader($"<text x='{value}'/>"));
        reader.Read();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextSyntax.Parse(reader)).ErrorCode);
    }

    [Fact]
    public void Rotation_length_units_reject_before_text_layout()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><text font-family='Missing' rotate='0 10px'>AB</text></svg>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Denied_or_cancelled_list_preparation_releases_partial_storage()
    {
        SvgTextPositionLists lists = new("1 2 3 4 5", null, null, null, "0 10");
        RenderBudget denied = new(1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => lists.Build("ABCDE", [], denied, new(100), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(10_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => lists.Build("ABCDE", [], workDenied, new(1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        Assert.Throws<OperationCanceledException>(() => lists.Build("ABCDE", [], workDenied, new(100), new CancellationToken(true)));
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
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

    [Theory]
    [InlineData("word-spacing='2.5'", 2.5)]
    [InlineData("word-spacing='2' style='word-spacing:-1.5px'", -1.5)]
    [InlineData("word-spacing='2' style='word-spacing:normal'", 0)]
    public void Parses_finite_word_spacing_and_normal(string attributes, double expected)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text font-family='Example' " + attributes + "/>"));
        reader.Read();
        Assert.Equal(expected, SvgTextSyntax.Parse(reader).WordSpacing);
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
        Assert.Equal(12, resolved.FontSize);
    }

    [Theory]
    [InlineData("", (int)SvgTextAnchor.Middle)]
    [InlineData("text-anchor='inherit'", (int)SvgTextAnchor.Middle)]
    [InlineData("text-anchor='start'", (int)SvgTextAnchor.Start)]
    [InlineData("text-anchor='end'", (int)SvgTextAnchor.End)]
    public void Presentation_anchors_follow_container_and_direct_text_inheritance(string attribute, int expected)
    {
        using XmlReader ancestorReader = XmlReader.Create(new StringReader("<g text-anchor='middle'/>"));
        ancestorReader.Read();
        SvgInheritedTextStyle ancestor = SvgTextInheritance.Resolve(ancestorReader, SvgInheritedTextStyle.Default, default, null, null);
        Assert.Equal(SvgTextAnchor.Middle, ancestor.WithWhitespaceMode(SvgTextWhitespaceMode.Preserve).Anchor);
        using XmlReader textReader = XmlReader.Create(new StringReader("<text font-family='Example' " + attribute + "/>"));
        textReader.Read();
        Assert.Equal((SvgTextAnchor)expected, SvgTextSyntax.Parse(textReader, default(SvgTextInlineFontProperties), ancestor).Anchor);
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
    public void Admits_whitespace_font_spacing_and_scalar_position_tspan_styles()
    {
        using XmlReader accepted = XmlReader.Create(new StringReader("<tspan xml:space='preserve' x='0' y='12px' dx='2' dy='-4' letter-spacing='2' style='white-space: normal; letter-spacing: 3'/>"));
        accepted.Read();
        using XmlReader rejected = XmlReader.Create(new StringReader("<tspan style='font-size: 12'/>"));
        rejected.Read();
        using XmlReader rejectedSpacing = XmlReader.Create(new StringReader("<tspan style='white-space: pre; letter-spacing: 2'/>"));
        rejectedSpacing.Read();

        Assert.True(SvgTextSyntax.TryParseTspanStyle(accepted, new SvgTextSpanStyle(SvgTextWhitespaceMode.Preserve, 1), default, new ParserWorkCounter(100), CancellationToken.None, out SvgTextSpanStyle style));
        Assert.Equal(SvgTextWhitespaceMode.Default, style.WhitespaceMode);
        Assert.Equal(3, style.LetterSpacing);
        Assert.Equal(new SvgTextPositionOffset(0, 12, 2, -4), style.PositionOffset);
        Assert.True(SvgTextSyntax.TryParseTspanStyle(rejected, new(SvgTextWhitespaceMode.Default, 0), default, new ParserWorkCounter(100), CancellationToken.None, out SvgTextSpanStyle fontStyle));
        Assert.Equal(16, fontStyle.Font.FontSize);
        Assert.True(SvgTextSyntax.TryParseTspanWhitespaceMode(rejectedSpacing, SvgTextWhitespaceMode.Default, default, new ParserWorkCounter(100), CancellationToken.None, out _));
    }

    [Fact]
    public void Uses_the_initial_serif_request_and_rejects_a_list_font_family()
    {
        using XmlReader missing = XmlReader.Create(new StringReader("<text/>"));
        missing.Read();
        using XmlReader list = XmlReader.Create(new StringReader("<text font-family='First, Second'/>"));
        list.Read();

        Assert.Equal("serif", SvgTextSyntax.Parse(missing).Font.Family);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextSyntax.Parse(list)).ErrorCode);
    }

    [Theory]
    [InlineData("x", "1 2")]
    [InlineData("y", "0,0")]
    public void Admits_valid_absolute_span_lists(string axis, string value)
    {
        using XmlReader reader = XmlReader.Create(new StringReader($"<tspan {axis}='{value}'/>"));
        reader.Read();
        Assert.True(SvgTextSyntax.TryParseTspanStyle(reader, new(SvgTextWhitespaceMode.Default, 0), default,
            new ParserWorkCounter(100), CancellationToken.None, out SvgTextSpanStyle style));
        Assert.Equal(value, axis == "x" ? style.PositionLists.X : style.PositionLists.Y);
    }

    [Theory]
    [InlineData("x", "20%")]
    [InlineData("y", "1em")]
    [InlineData("x", "NaN")]
    [InlineData("y", "1e309")]
    public void Rejects_absolute_span_units_and_nonfinite_values(string axis, string value)
    {
        using XmlReader reader = XmlReader.Create(new StringReader($"<tspan {axis}='{value}'/>"));
        reader.Read();

        SvgException exception = Assert.Throws<SvgException>(() => SvgTextSyntax.TryParseTspanStyle(reader, new(SvgTextWhitespaceMode.Default, 0), default, new ParserWorkCounter(100), CancellationToken.None, out _));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
}
