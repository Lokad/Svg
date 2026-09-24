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
        (int commandCount, bool hasCurve, bool hasArc) = ScanPathCommands(d, context.WorkCounter, context.CancellationToken);
        bool hasFractional = d.Contains('.') || d.Contains('e') || d.Contains('E');
        SvgResolvedPaintableElement shape;
        SvgParsedGeometry filled;
        if (hasCurve)
        {
            if (hasFractional) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer path coordinates are implemented; fractional and scientific notation stay unsupported.");
            shape = context.ResolveShape(style, reader, true);
            bool curveEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using BudgetedParserList<SvgPoint> noPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering curveLowering = new(noPoints, noEnds, "path", true, curveEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgParsedGeometry curved = curveLowering.LowerDeviceFill(d, commandCount, hasArc, SvgPolygonMapping.FlattenedCurves, shape, frame, budget);
            filled = curved with { ElementCount = commandCount + 1 };
        }
        else if (hasFractional)
        {
            shape = context.ResolveShape(style, reader, true);
            bool fractionalEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using BudgetedParserList<SvgPoint> noFractionPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noFractionEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering fractionalLowering = new(noFractionPoints, noFractionEnds, "path", true, fractionalEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgParsedGeometry fractional = fractionalLowering.LowerDeviceFill(d, commandCount, false, SvgPolygonMapping.FiniteEdges, shape, frame, budget);
            filled = fractional with { ElementCount = commandCount + 1 };
        }
        else
        {
            using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, contourEnds);
            if (HasDiagonalContour(points, contourEnds))
            {
                shape = context.ResolveShape(style, reader, true);
                bool diagonalEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
                using SvgOrthogonalGeometryLowering diagonalLowering = new(points, contourEnds, "path", true, diagonalEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                filled = diagonalLowering.LowerPolygonFill(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame, budget);
            }
            else
            {
                SvgOrthogonalGeometryLowering.ValidateContours(points, contourEnds, "path", context.WorkCounter, context.CancellationToken);
                shape = context.ResolveShape(style, reader, true);
                bool isEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
                using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, "path", true, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                filled = lowering.Lower(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame);
            }
        }

        SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke;
        SvgParsedGeometry stroked;
        try
        {
            stroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, style, shape, "path");
            if (stroke is null) return filled;
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, "path", true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            stroked = strokeLowering.LowerStrokePath(d, commandCount, hasArc, hasCurve, shape, stroke.Value.Color, shape.Presentation.IsPainted, stroke.Value.Width, stroke.Value.MiterLimit, frame, budget);
        }
        catch { filled.Dispose(); throw; }
        return ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);

        static (int CommandCount, bool HasCurve, bool HasArc) ScanPathCommands(string value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            int commands = 0;
            bool curve = false;
            bool arc = false;
            for (int index = 0; index < value.Length; index++)
            {
                if ((index & 4095) == 0) workCounter.Consume(cancellationToken);
                char letter = value[index];
                if (letter is 'A' or 'a') { arc = true; curve = true; commands++; }
                else if (letter is 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't') { curve = true; commands++; }
                else if (letter is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z') commands++;
            }

            return (commands, curve, arc);
        }
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
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        contourEnds.Add(points.Count);
        SvgResolvedPaintableElement shape;
        SvgParsedGeometry filled;
        if (HasDiagonalContour(points, contourEnds))
        {
            shape = context.ResolveShape(style, reader, true);
            bool diagonalEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using SvgOrthogonalGeometryLowering diagonalLowering = new(points, contourEnds, elementName, false, diagonalEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            filled = diagonalLowering.LowerPolygonFill(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame, budget);
        }
        else
        {
            SvgOrthogonalGeometryLowering.ValidateSimpleContour(points, elementName, context.WorkCounter, context.CancellationToken);
            shape = context.ResolveShape(style, reader, true);
            bool isEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, false, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            filled = lowering.Lower(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame);
        }

        SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke;
        SvgParsedGeometry stroked;
        try
        {
            stroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, style, shape, elementName);
            if (stroke is null) return filled;
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, false, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            stroked = strokeLowering.LowerStrokePolyline(points, elementName == "polygon", shape.Transform, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Color, shape.Presentation.IsPainted, frame, budget);
        }
        catch { filled.Dispose(); throw; }
        return ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);
    }

    private static SvgParsedGeometry ConcatParsedGeometries(SvgParsedGeometry first, SvgParsedGeometry second, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        try
        {
            using BudgetedParserList<SvgGeometry> combined = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            combined.AddRange(first.Geometries);
            combined.AddRange(second.Geometries);
            int elements = first.ElementCount > int.MaxValue - second.ElementCount ? int.MaxValue : first.ElementCount + second.ElementCount;
            SvgParsedGeometry result = new(combined.Detach(), elements);
            first.Dispose();
            second.Dispose();
            return result;
        }
        catch
        {
            first.Dispose();
            second.Dispose();
            throw;
        }
    }

    private static bool HasDiagonalContour(BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds)
    {
        if (contourEnds.Count == 0 || contourEnds[contourEnds.Count - 1] != points.Count) return false;
        ReadOnlySpan<SvgPoint> span = points.AsSpan();
        int contourStart = 0;
        for (int contour = 0; contour < contourEnds.Count; contour++)
        {
            int contourEnd = contourEnds[contour];
            if (SvgContourTopology.Classify(span, contourStart, contourEnd) == SvgContourKind.Diagonal) return true;
            contourStart = contourEnd;
        }

        return false;
    }
}