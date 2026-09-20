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
        SvgPaintableElementContext context = new(new SvgInheritedPresentation(SvgPaint.Default, true, true, AxisTransform.Identity), default, namedColors, new ParserWorkCounter(100), CancellationToken.None);

        SvgResolvedPaintableElement element = context.ResolveShape(reader, true);

        Assert.Equal(new SvgComputedStyle("blue", "currentColor", null, null, "none", null, null, null), element.Style);
        Assert.Equal(new AxisTransform(1, 1, 4, 5), element.Transform);
        Assert.Equal(new SvgColor(0, 0, 255, 255), element.Presentation.Color);
        Assert.False(element.Presentation.IsPainted);
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
        SvgInheritedPresentation rootPresentation = new(new SvgPaint(new SvgColor(1, 2, 3, 255), false, 255, new SvgColor(4, 5, 6, 255), false), true, true, AxisTransform.Identity);
        SvgRootAttributes root = new(rootPresentation, null, 255, null, null, null);

        SvgPaintableElementContext rootContext = SvgPaintableElementContext.Create(reader, false, root, state, new Dictionary<string, SvgColor>(), workCounter, CancellationToken.None);

        SvgInheritedPresentation groupPresentation = new(new SvgPaint(new SvgColor(7, 8, 9, 255), true, 128, new SvgColor(10, 11, 12, 255), true), false, false, new AxisTransform(2, 3, 4, 5));
        state.Groups.Add(new SvgGroupScope(1, SvgGroupKind.Group, groupPresentation, false));
        SvgPaintableElementContext groupContext = SvgPaintableElementContext.Create(reader, false, root, state, new Dictionary<string, SvgColor>(), workCounter, CancellationToken.None);

        Assert.Equal(rootPresentation, rootContext.InheritedPresentation);
        Assert.Equal(groupPresentation, groupContext.InheritedPresentation);
    }
}
