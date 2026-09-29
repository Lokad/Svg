using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgRootSyntaxTests
{
    [Fact]
    public void Parses_root_size_and_view_box_syntax_without_presentation_resolution()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<svg width='12.5px' height='7' viewBox='1 2 3 4' preserveAspectRatio='none'/>"));
        Assert.True(reader.Read());

        SvgRootSyntax syntax = SvgRootSyntax.Parse(reader);

        Assert.Equal(12.5, syntax.Width?.Value);
        Assert.Equal(7, syntax.Height?.Value);
        Assert.Equal(new SvgViewBox(1, 2, 3, 4, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None), syntax.ViewBox);
    }
}
