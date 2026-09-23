using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Connects policy-validated bounded orthogonal path/point-list syntax with temporary lowering storage and typed geometry handoff.</summary>
internal static class SvgOrthogonalShapeParser
{
    internal static SvgParsedGeometry ParsePath(XmlReader reader, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgGradientSyntax.IsGradientFill(style.Fill))
        {
            if (state.Patterns.TryGetValue(SvgGradientSyntax.ParsePaintId(style.Fill), out _)) return SvgPatternSyntax.LowerPatternBands(reader, context, style, state, "path", true, maximumGeometryElements, budget, frame);
            return SvgGradientSyntax.LowerGradientBands(reader, context, style, state, "path", true, maximumGeometryElements, budget, frame);
        }
        string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, contourEnds);
        SvgOrthogonalGeometryLowering.ValidateContours(points, contourEnds, "path", context.WorkCounter, context.CancellationToken);
        SvgResolvedPaintableElement element = context.ResolveShape(style, reader, false);
        bool isEvenOdd = element.Style.FillRule is not null && element.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, "path", true, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        return lowering.Lower(element.Presentation.Color, element.Presentation.HasFill, element.Transform, frame);
    }

    internal static BudgetedParserList<SvgPoint> TokenizeBands(XmlReader reader, string elementName, bool isPath, int maximumGeometryElements, RenderBudget budget, SvgPaintableElementContext context, BudgetedParserList<int> contourEnds)
    {
        if (isPath)
        {
            string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
            BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, contourEnds);
            try { SvgOrthogonalGeometryLowering.ValidateContours(points, contourEnds, elementName, context.WorkCounter, context.CancellationToken); }
            catch { points.Dispose(); throw; }
            return points;
        }
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        BudgetedParserList<SvgPoint> pointList = SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, elementName, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        try
        {
            SvgOrthogonalGeometryLowering.ValidateSimpleContour(pointList, elementName, context.WorkCounter, context.CancellationToken);
            contourEnds.Add(pointList.Count);
        }
        catch { pointList.Dispose(); throw; }
        return pointList;
    }

    internal static SvgParsedGeometry ParsePointList(XmlReader reader, string elementName, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgGradientSyntax.IsGradientFill(style.Fill))
        {
            if (state.Patterns.TryGetValue(SvgGradientSyntax.ParsePaintId(style.Fill), out _)) return SvgPatternSyntax.LowerPatternBands(reader, context, style, state, elementName, false, maximumGeometryElements, budget, frame);
            return SvgGradientSyntax.LowerGradientBands(reader, context, style, state, elementName, false, maximumGeometryElements, budget, frame);
        }
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, elementName, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        SvgOrthogonalGeometryLowering.ValidateSimpleContour(points, elementName, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        contourEnds.Add(points.Count);
        SvgResolvedPaintableElement element = context.ResolveShape(style, reader, false);
        bool isEvenOdd = element.Style.FillRule is not null && element.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, false, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        return lowering.Lower(element.Presentation.Color, element.Presentation.HasFill, element.Transform, frame);
    }
}