using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPaintableElementContextTests
{
    [Fact]
    public void Resolves_one_typed_shape_element_after_style_and_transform_syntax()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<rect fill='currentColor' color='blue' display='none' transform='translate(4, 5)'/>"));
        Assert.True(reader.Read());
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase) { ["blue"] = new SvgColor(0, 0, 255, 255) };
        SvgPaintableElementContext context = new(new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), SvgInheritedTextStyle.Default, SvgInheritedStrokeStyle.Default, default, namedColors, new ParserWorkCounter(100), CancellationToken.None, 1.0, null);

        SvgResolvedPaintableElement element = context.ResolveShape(reader, true);

        Assert.Equal(new SvgComputedStyle("blue", "currentColor", null, null, "none", null, null, null) { PaintOrder = SvgPaintOrder.FillStrokeMarkers }, element.Style);
        Assert.Equal(SvgAffineTransform.FromScaleOffset(1, 1, 4, 5), element.Transform);
        Assert.Equal(1.0, element.OutputMagnification);
        Assert.Equal(new SvgColor(0, 0, 255, 255), element.Presentation.Color);
        Assert.False(element.Presentation.IsPainted);
    }

    [Fact]
    public void Retains_inline_text_font_declarations_from_the_paint_style_scan()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text style='fill: #f00; font-family: Example; font-weight: 700; font-style: italic; font-size: 12'/>"));
        Assert.True(reader.Read());
        SvgPaintableElementContext context = new(new SvgInheritedPresentation(SvgPaint.Default, true, true, SvgAffineTransform.Identity), SvgInheritedTextStyle.Default, SvgInheritedStrokeStyle.Default, default, new Dictionary<string, SvgColor>(), new ParserWorkCounter(100), CancellationToken.None, 1.0, null);

        SvgResolvedDirectTextElement element = context.ResolveTextShape(reader);

        Assert.Equal("#f00", element.PaintableElement.Style.Fill);
        Assert.Equal("Example", element.InlineFont.Family);
        Assert.Equal("700", element.InlineFont.Weight);
        Assert.Equal("italic", element.InlineFont.Style);
        Assert.Null(element.InlineFont.Size);
    }

    [Fact]
    public void Uses_the_root_or_current_group_as_one_inherited_presentation()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<svg xmlns='http://www.w3.org/2000/svg'><g><rect/></g></svg>"));
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        ParserWorkCounter workCounter = new(100);
        using SvgDocumentState state = new(new RenderBudget(10_000), workCounter, CancellationToken.None);
        SvgInheritedPresentation rootPresentation = new(new SvgPaint(new(new SvgColor(1, 2, 3, 255), 1), false, SvgPaintOpacity.Opaque, new(new SvgColor(4, 5, 6, 255), 1), false), true, true, SvgAffineTransform.Identity);
        SvgRootAttributes root = new(rootPresentation, SvgInheritedTextStyle.Default, SvgInheritedStrokeStyle.Default, null, null, null, null);

        SvgPaintableElementContext rootContext = SvgPaintableElementContext.Create(reader, root, state, new Dictionary<string, SvgColor>(), workCounter, CancellationToken.None);

        SvgInheritedPresentation groupPresentation = new(new SvgPaint(new(new SvgColor(7, 8, 9, 255), 1), true, new SvgPaintOpacity(128, 128 / 255f), new(new SvgColor(10, 11, 12, 255), 1), true), false, false, SvgAffineTransform.FromScaleOffset(2, 3, 4, 5));
        state.Groups.Add(new SvgGroupScope(1, SvgGroupKind.Group, groupPresentation, SvgInheritedTextStyle.Default, false, false, SvgInheritedStrokeStyle.Default, "evenodd"));
        SvgPaintableElementContext groupContext = SvgPaintableElementContext.Create(reader, root, state, new Dictionary<string, SvgColor>(), workCounter, CancellationToken.None);

        Assert.Equal(rootPresentation, rootContext.InheritedPresentation);
        Assert.Equal(groupPresentation, groupContext.InheritedPresentation);
        Assert.Null(rootContext.InheritedFillRule);
        Assert.Equal("evenodd", groupContext.InheritedFillRule);
    }
}

