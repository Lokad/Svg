using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLocalReferenceSyntaxTests
{
    [Theory]
    [InlineData("86.6025403784", 86.6025390625)]
    [InlineData("00000000000000000000000000000000000000002", 2)]
    [InlineData("1e-100", 0)]
    public void Local_offsets_retain_finite_native_length_scalars(string value, double expected)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<use href='#tile' x='" + value + "px'/>"));
        using BudgetedParserStringStore strings = new(new RenderBudget(1_000));
        Assert.True(reader.Read());
        SvgLocalReferenceSyntax syntax = Assert.IsType<SvgLocalReferenceSyntax>(SvgLocalReferenceSyntax.Parse(reader, strings, null, null, new(10_000), CancellationToken.None));
        Assert.Equal(expected, syntax.X);
        Assert.True(double.IsFinite(syntax.X));
    }

    [Fact]
    public void Parses_and_retains_bounded_local_use_syntax_before_style_resolution()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<use href='#tile' x='2.25' y='3' transform='translate(4, 5)'/>"));
        using BudgetedParserStringStore strings = new(new RenderBudget(1_000));
        Assert.True(reader.Read());

        SvgLocalReferenceSyntax syntax = Assert.IsType<SvgLocalReferenceSyntax>(SvgLocalReferenceSyntax.Parse(reader, strings, null, null, new(10_000), CancellationToken.None));

        Assert.Equal("tile", syntax.Id);
        Assert.Equal(2.25, syntax.X);
        Assert.Equal(3, syntax.Y);
        Assert.Null(syntax.Width);
        Assert.Null(syntax.Height);
    }

    [Fact]
    public void Validates_local_use_coordinates_before_retaining_the_identifier()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<use href='#tile' x='not-a-coordinate'/>"));
        RenderBudget budget = new(1_000);
        using BudgetedParserStringStore strings = new(budget);
        Assert.True(reader.Read());

        SvgException exception = Assert.Throws<SvgException>(() => SvgLocalReferenceSyntax.Parse(reader, strings, null, null, new(10_000), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Parser]);
    }

    [Fact]
    public void Resolves_legacy_xlink_href_use_references()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<use xmlns:xlink='http://www.w3.org/1999/xlink' xlink:href='#tile' x='2'/>"));
        using BudgetedParserStringStore strings = new(new RenderBudget(1_000));
        Assert.True(reader.Read());

        SvgLocalReferenceSyntax syntax = Assert.IsType<SvgLocalReferenceSyntax>(SvgLocalReferenceSyntax.Parse(reader, strings, null, null, new(10_000), CancellationToken.None));

        Assert.Equal("tile", syntax.Id);
        Assert.Equal(2, syntax.X);
    }

    [Fact]
    public void Renders_legacy_xlink_href_use_references_identically()
    {
        byte[] hrefPng = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs><rect id='r' width='2' height='2' fill='red'/></defs><use href='#r'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] xlinkPng = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='4' height='4'><defs><rect id='r' width='2' height='2' fill='red'/></defs><use xlink:href='#r'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None).ToArray();

        Assert.Equal(hrefPng, xlinkPng);
    }
}
