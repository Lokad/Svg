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
}
