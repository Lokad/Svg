using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgContainerInheritanceTests
{
    [Fact]
    public void Resolves_inherited_presentation_text_transform_and_opacity()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<g font-family='Example' font-size='20' opacity='0.5' transform='translate(1, 2)'/>"));
        Assert.True(reader.Read());

        SvgComputedStyle computedStyle = SvgComputedStyle.Resolve(reader, default, default);
        SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(
            reader,
            default,
            computedStyle,
            default,
            new SvgInheritedPresentation(SvgPaint.Default, true, true, AxisTransform.Identity),
            SvgInheritedTextStyle.Default,
            false,
            new Dictionary<string, SvgColor>());

        Assert.Equal(SvgPaint.Default, resolved.Presentation.Paint);
        Assert.True(resolved.Presentation.IsDisplayed);
        Assert.True(resolved.Presentation.IsVisible);
        Assert.Equal(new AxisTransform(1, 1, 1, 2), resolved.Presentation.Transform);
        Assert.Equal("Example", resolved.TextStyle.Family);
        Assert.Equal(20, resolved.TextStyle.FontSize);
        Assert.Null(resolved.FillRule);
        Assert.True(resolved.HasOpacityScope);
        Assert.Equal((byte)128, resolved.Opacity);
    }

    [Fact]
    public void Important_stylesheet_font_beats_normal_inline_font()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<g/>"));
        Assert.True(reader.Read());

        InlineStyle inline = SvgInlineStyleParser.Parse("font-family: Inline", new ParserWorkCounter(100), CancellationToken.None, true);
        InlineStyle styleSheet = SvgInlineStyleParser.Parse("font-family: Important !important", new ParserWorkCounter(100), CancellationToken.None, true);
        SvgComputedStyle computedStyle = SvgComputedStyle.Resolve(reader, inline, styleSheet);
        SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(
            reader,
            inline,
            computedStyle,
            styleSheet,
            new SvgInheritedPresentation(SvgPaint.Default, true, true, AxisTransform.Identity),
            SvgInheritedTextStyle.Default,
            false,
            new Dictionary<string, SvgColor>());

        Assert.Equal("Important", resolved.TextStyle.Family);
    }

    [Fact]
    public void Rejects_nested_group_opacity()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<g opacity='0.5'/>"));
        Assert.True(reader.Read());

        SvgComputedStyle nestedStyle = SvgComputedStyle.Resolve(reader, default, default);
        SvgException exception = Assert.Throws<SvgException>(() => SvgContainerInheritance.Resolve(
            reader,
            default,
            nestedStyle,
            default,
            new SvgInheritedPresentation(SvgPaint.Default, true, true, AxisTransform.Identity),
            SvgInheritedTextStyle.Default,
            true,
            new Dictionary<string, SvgColor>()));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
}
