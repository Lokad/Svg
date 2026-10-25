using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgTextDecorationTests
{
    [Theory]
    [InlineData("underline", 1)]
    [InlineData("line-through", 4)]
    [InlineData("solid underline overline", 3)]
    [InlineData("none solid", 0)]
    public void Parses_solid_line_sets(string value, int expected)
        => Assert.Equal((SvgTextDecorationLine)expected, SvgTextDecorationSyntax.Parse(value));

    [Theory]
    [InlineData("underline underline", SvgErrorCode.MalformedInput)]
    [InlineData("none underline", SvgErrorCode.MalformedInput)]
    [InlineData("solid solid", SvgErrorCode.MalformedInput)]
    [InlineData("wavy underline", SvgErrorCode.UnsupportedFeature)]
    public void Categorizes_duplicate_and_unimplemented_decorations(string value, SvgErrorCode expected)
        => Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgTextDecorationSyntax.Parse(value)).ErrorCode);

    [Fact]
    public void Propagation_keeps_the_declaring_paint_until_a_new_line_declaration_wins_the_cascade()
    {
        SvgTextGlyphPaint blue = new(new(new(20, 40, 80, 255), 1), true), red = new(new(new(180, 50, 20, 255), 1), true);
        SvgTextDecorationStyle inherited = new(SvgTextDecorationLine.Underline, blue);
        Assert.Same(inherited, SvgTextDecorationSyntax.Resolve(null, inherited, red));
        Assert.Same(inherited, SvgTextDecorationSyntax.Resolve("none", inherited, red));
        Assert.Equal(2, Assert.IsType<SvgTextDecorationStyle>(SvgTextDecorationSyntax.Resolve("underline", inherited, red)).UnderlineCount);
        using XmlReader reader = XmlReader.Create(new StringReader("<tspan text-decoration='none'/>"));
        reader.Read();
        InlineStyle inline = new() { TextDecoration = "overline" };
        InlineStyle sheet = new() { TextDecoration = "line-through", Important = InlineImportantProperties.TextDecoration };
        SvgTextDecorationStyle actual = Assert.IsType<SvgTextDecorationStyle>(SvgTextDecorationSyntax.Resolve(reader, inline, sheet, inherited, red));
        Assert.Equal(SvgTextDecorationLine.Underline | SvgTextDecorationLine.LineThrough, actual.Lines);
        Assert.Equal(red, actual.Paint);
        Assert.Null(actual.Paint.Decoration);
    }
}
