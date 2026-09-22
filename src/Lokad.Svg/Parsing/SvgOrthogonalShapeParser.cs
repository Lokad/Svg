using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Connects policy-validated bounded orthogonal path/point-list syntax with temporary lowering storage and typed geometry handoff.</summary>
internal static class SvgOrthogonalShapeParser
{
    internal static SvgParsedGeometry ParsePath(XmlReader reader, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame)
    {
        string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        SvgOrthogonalGeometryLowering.ValidateSimpleContour(points, "path", context.WorkCounter, context.CancellationToken);
        SvgResolvedPaintableElement element = context.ResolveShape(reader, false);
        using SvgOrthogonalGeometryLowering lowering = new(points, "path", true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        return lowering.Lower(element.Presentation.Color, element.Presentation.HasFill, element.Transform, frame);
    }

    internal static SvgParsedGeometry ParsePointList(XmlReader reader, string elementName, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame)
    {
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, elementName, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        using SvgOrthogonalGeometryLowering lowering = new(points, elementName, false, true, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        SvgResolvedPaintableElement element = context.ResolveShape(reader, false);
        return lowering.Lower(element.Presentation.Color, element.Presentation.HasFill, element.Transform, frame);
    }
}
