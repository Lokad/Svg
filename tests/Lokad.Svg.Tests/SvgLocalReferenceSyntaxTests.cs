using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLocalReferenceSyntaxTests
{
    [Fact]
    public void Parses_and_retains_bounded_local_use_syntax_before_style_resolution()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<use href='#tile' x='2.25' y='3' transform='translate(4, 5)'/>"));
        using BudgetedParserStringStore strings = new(new RenderBudget(1_000));
        Assert.True(reader.Read());

        SvgLocalReferenceSyntax syntax = SvgLocalReferenceSyntax.Parse(reader, strings);

        Assert.Equal("tile", syntax.Id);
        Assert.Equal(2.25, syntax.X);
        Assert.Equal(3, syntax.Y);
        Assert.Equal(new AxisTransform(1, 1, 4, 5), syntax.Transform);
    }

    [Fact]
    public void Validates_local_use_coordinates_before_retaining_the_identifier()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<use href='#tile' x='not-a-coordinate'/>"));
        RenderBudget budget = new(1_000);
        using BudgetedParserStringStore strings = new(budget);
        Assert.True(reader.Read());

        SvgException exception = Assert.Throws<SvgException>(() => SvgLocalReferenceSyntax.Parse(reader, strings));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);
    }
}
