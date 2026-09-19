using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStyleSheetTests
{
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
