using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStyleSheetTests
{
    [Fact]
    public void Cascades_each_presentation_property_by_specificity_then_source_order()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle("red", "red", "0.25", null, "none", null, "0.5", null), 1);
        cascade.Apply(new InlineStyle(null, "blue", null, "none", "inline", "hidden", "0.75", "evenodd"), 1);
        cascade.Apply(new InlineStyle(null, "green", "0.5", null, null, null, null, null), 0);
        cascade.Apply(new InlineStyle(null, null, "0.5", null, null, null, null, null), 2);

        InlineStyle style = cascade.ToInlineStyle();

        Assert.Equal("red", style.Color);
        Assert.Equal("blue", style.Fill);
        Assert.Equal("0.5", style.FillOpacity);
        Assert.Equal("none", style.Stroke);
        Assert.Equal("inline", style.Display);
        Assert.Equal("hidden", style.Visibility);
        Assert.Equal("0.75", style.Opacity);
        Assert.Equal("evenodd", style.FillRule);
    }

    [Fact]
    public void Cascades_css_whitespace_by_specificity_then_source_order()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, null, null, null, null, null, null, null) { WhiteSpace = "pre" }, 1);
        cascade.Apply(new InlineStyle(null, null, null, null, null, null, null, null) { WhiteSpace = "normal" }, 0);
        cascade.Apply(new InlineStyle(null, null, null, null, null, null, null, null) { WhiteSpace = "normal" }, 1);

        Assert.Equal("normal", cascade.ToInlineStyle().WhiteSpace);
    }

    [Fact]
    public void Important_stylesheet_declarations_beat_higher_specificity_normal_declarations()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, "red", null, null, null, null, null, null), 100);
        cascade.Apply(new InlineStyle(null, "blue", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, 1);

        InlineStyle style = cascade.ToInlineStyle();

        Assert.Equal("blue", style.Fill);
        Assert.Equal(InlineImportantProperties.Fill, style.Important);
    }

    [Fact]
    public void Later_important_stylesheet_declarations_win_source_order_ties()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, "red", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, 1);
        cascade.Apply(new InlineStyle(null, "blue", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, 1);

        Assert.Equal("blue", cascade.ToInlineStyle().Fill);
    }

    [Fact]
    public void Normal_stylesheet_declarations_do_not_override_earlier_important_ones()
    {
        SvgStyleCascade cascade = new();
        cascade.Apply(new InlineStyle(null, "red", null, null, null, null, null, null) { Important = InlineImportantProperties.Fill }, 1);
        cascade.Apply(new InlineStyle(null, "blue", null, null, null, null, null, null), 101);

        Assert.Equal("red", cascade.ToInlineStyle().Fill);
    }

    [Theory]
    [InlineData("fill: red !important", "red", true)]
    [InlineData("fill: red!important", "red", true)]
    [InlineData("fill: red !IMPORTANT", "red", true)]
    [InlineData("fill: red ! important", "red", true)]
    [InlineData("fill: red", "red", false)]
    public void Parses_important_suffix_variants(string declaration, string expectedValue, bool expectedImportant)
    {
        InlineStyle style = SvgInlineStyleParser.Parse(declaration, new ParserWorkCounter(100), CancellationToken.None);

        Assert.Equal(expectedValue, style.Fill);
        Assert.Equal(expectedImportant, (style.Important & InlineImportantProperties.Fill) != 0);
    }

    [Fact]
    public void Important_beats_normal_within_one_declaration_block_regardless_of_order()
    {
        InlineStyle importantFirst = SvgInlineStyleParser.Parse("fill: red !important; fill: blue", new ParserWorkCounter(100), CancellationToken.None);
        InlineStyle importantLast = SvgInlineStyleParser.Parse("fill: red; fill: blue !important", new ParserWorkCounter(100), CancellationToken.None);

        Assert.Equal("red", importantFirst.Fill);
        Assert.Equal("blue", importantLast.Fill);
    }

    [Fact]
    public void Rejects_empty_important_declarations()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgInlineStyleParser.Parse("fill: !important", new ParserWorkCounter(100), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Checks_stylesheet_read_work_before_allocating_text_storage()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<style>x</style>"));
        Assert.True(reader.Read());
        RenderBudget budget = new(1);

        SvgException exception = Assert.Throws<SvgException>(() => SvgStyleSheet.ReadElementText(reader, budget, new ParserWorkCounter(0), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);
    }

    [Fact]
    public void Reserves_stylesheet_text_storage_before_incremental_reading()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<style>" + new string(' ', 4_096) + "</style>"));
        Assert.True(reader.Read());

        SvgException exception = Assert.Throws<SvgException>(() => SvgStyleSheet.ReadElementText(reader, new RenderBudget(8_223), new ParserWorkCounter(100), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Rejects_a_late_root_stylesheet(bool sawStyleableNode, bool hasStyleSheet, bool throws)
    {
        if (throws)
        {
            SvgException exception = Assert.Throws<SvgException>(() => SvgRootStyleSheetParser.ThrowIfLate(sawStyleableNode, hasStyleSheet));

            Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        }
        else
        {
            SvgRootStyleSheetParser.ThrowIfLate(sawStyleableNode, hasStyleSheet);
        }
    }

    [Theory]
    [InlineData("<style/>", false)]
    [InlineData("<style media='screen'/>", true)]
    public void Rejects_stylesheet_element_attributes(string element, bool throws)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(element));
        Assert.True(reader.Read());
        if (throws)
        {
            SvgException exception = Assert.Throws<SvgException>(() => SvgRootStyleSheetParser.ThrowIfAttributed(reader));

            Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        }
        else
        {
            SvgRootStyleSheetParser.ThrowIfAttributed(reader);
        }
    }
}
