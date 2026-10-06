using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Connects policy-validated bounded orthogonal path/point-list syntax with temporary lowering storage and typed geometry handoff.</summary>
internal static class SvgOrthogonalShapeParser
{
    internal static SvgParsedGeometry ParsePath(XmlReader reader, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        bool paintOutline = TryResolveOutlineGradient(style, state, out SvgLinearGradient outlineCandidate);
        bool paintRadialOutline = TryResolveOutlineRadial(style, state, out SvgRadialGradient outlineRadialCandidate);
        bool paintPatternOutline = TryResolveOutlinePattern(style, state, out SvgPatternTile outlineTile);
        SvgComputedStyle fillStyle = (paintOutline || paintRadialOutline || paintPatternOutline) ? style with { Stroke = "none" } : style;
        if (SvgGradientSyntax.IsGradientFill(style.Fill))
        {
            if (state.Patterns.TryGetValue(SvgGradientSyntax.ParsePaintId(style.Fill), out _)) return SvgPatternSyntax.LowerPatternBands(reader, context, style, state, "path", true, maximumGeometryElements, budget, frame);
            return SvgGradientSyntax.LowerGradientBands(reader, context, style, state, "path", true, maximumGeometryElements, budget, frame);
        }
        string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
        (int commandCount, bool hasCurve, bool hasArc, bool hasFractional) = ScanPathData(d, context.WorkCounter, context.CancellationToken);
        SvgResolvedPaintableElement shape;
        SvgParsedGeometry filled;
        shape = context.ResolveShape(fillStyle, reader, true);
        if (!shape.Presentation.HasFill && !paintOutline && !paintRadialOutline && !paintPatternOutline && reader.GetAttribute("mask") is null && reader.GetAttribute("clip-path") is null && state.MaskScopes.Count == 0)
        {
            // Unpainted fills contribute no area and feed no paint-server boxes, masks or clips here, so degenerate
            // subpaths skip fill validation while stroke lowering still outlines every subpath below.
            using BudgetedParserList<SvgGeometry> empty = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            filled = new SvgParsedGeometry(empty.Detach(), commandCount + 1);
        }
        else if (hasCurve)
        {
            bool curveEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using BudgetedParserList<SvgPoint> noPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering curveLowering = new(noPoints, noEnds, "path", true, curveEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgParsedGeometry curved = curveLowering.LowerDeviceFill(d, commandCount, hasArc, hasCurve, SvgPolygonMapping.FlattenedCurves, shape, frame, budget);
            filled = curved with { ElementCount = commandCount + 1 };
        }
        else if (hasFractional)
        {
            bool fractionalEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using BudgetedParserList<SvgPoint> noFractionPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noFractionEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering fractionalLowering = new(noFractionPoints, noFractionEnds, "path", true, fractionalEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgParsedGeometry fractional = fractionalLowering.LowerDeviceFill(d, commandCount, false, false, SvgPolygonMapping.FiniteEdges, shape, frame, budget);
            filled = fractional with { ElementCount = commandCount + 1 };
        }
        else
        {
            using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, contourEnds, true);
            if (!paintOutline && !paintRadialOutline && !paintPatternOutline && reader.GetAttribute("mask") is null && reader.GetAttribute("clip-path") is null && state.MaskScopes.Count == 0) DropSingletonContours(points, contourEnds, context.WorkCounter, context.CancellationToken);
            if (contourEnds.Count == 0 || AllContoursDegenerate(points, contourEnds))
            {
                // Sealed subpaths with fewer than three points enclose no area, so the fill contributes nothing while stroke lowering still outlines every subpath.
                using BudgetedParserList<SvgGeometry> empty = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                filled = new SvgParsedGeometry(empty.Detach(), commandCount + 1);
            }
            else if (HasDiagonalContour(points, contourEnds))
            {
                bool diagonalEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
                using SvgOrthogonalGeometryLowering diagonalLowering = new(points, contourEnds, "path", true, diagonalEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                filled = diagonalLowering.LowerPolygonFill(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame, budget);
            }
            else
            {
                SvgOrthogonalGeometryLowering.ValidateContours(points, contourEnds, "path", context.WorkCounter, context.CancellationToken);
                bool isEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
                // AA clips belong to the paint operation. Clipping orthogonal
                // bands here discards their native boundary bytes and paint route.
                if (!shape.Transform.IsAxisAligned || frame is { Clip.Antialiasing: not SvgClipAntialiasing.None })
                {
                    using SvgOrthogonalGeometryLowering polygonLowering = new(points, contourEnds, "path", true, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                    filled = polygonLowering.LowerPolygonFill(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame, budget);
                }
                else
                {
                    using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, "path", true, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                    filled = lowering.Lower(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame);
                }
            }
        }

        SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke;
        SvgParsedGeometry stroked;
        bool isStraightPath = !hasCurve && !hasFractional;
        bool paintPathOutline = paintOutline;
        bool paintPathRadial = paintRadialOutline;
        bool paintPathPattern = paintPatternOutline && !hasFractional;
        SvgLinearGradient pathGradient = default;
        SvgRadialGradient pathCircle = default;
        SvgComputedStyle pathStrokeStyle = style;
        SvgResolvedPaintableElement pathStrokeShape = shape;
        if (paintPathOutline || paintPathRadial || paintPathPattern)
        {
            if (paintPathOutline)
            {
                pathGradient = SvgGradientSyntax.MapUserSpaceVector(shape.Transform, outlineCandidate, frame);
                pathGradient = SvgGradientSyntax.MirrorBoundingBoxVector(shape.Transform, pathGradient);
            }
            else if (paintPathRadial) pathCircle = SvgGradientSyntax.MapUserSpaceRadial(shape.Transform, outlineRadialCandidate, frame);
            // Lower the outline exactly as a solid stroke so caps, joins, dashes and transforms behave identically, then repaint it below. The solid color never reaches the scene.
            pathStrokeStyle = style with { Stroke = "#ffffff" };
            pathStrokeShape = context.ResolveShape(pathStrokeStyle, reader, true);
        }

        SvgRectangle ResolveBoundingBoxOutlineBox()
        {
            bool needsFillBox = (paintPathOutline && !outlineCandidate.IsUserSpace) || (paintPathRadial && !outlineRadialCandidate.IsUserSpace);
            if (!needsFillBox) return default;
            if (isStraightPath)
            {
                using BudgetedParserList<int> boxEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using BudgetedParserList<SvgPoint> boxPoints = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, boxEnds, true);
                return SvgGradientSyntax.BoxFromGradientBands(boxPoints, shape.Transform, frame, context);
            }
            return BoxFromFilledOutlineBounds();
        }

        SvgRectangle BoxFromFilledOutlineBounds()
            => BoxFromFilledGeometryBounds(filled, context.CancellationToken);

        try
        {
            stroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, pathStrokeStyle, pathStrokeShape, "path", true);
            if (stroke is null) return filled;
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, "path", true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            stroked = strokeLowering.LowerStrokePath(d, commandCount, hasArc, hasCurve, pathStrokeShape, stroke.Value.Color, pathStrokeShape.Presentation.IsPainted, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, budget);
            if (paintPathOutline || paintPathRadial || paintPathPattern)
            {
                SvgRectangle pathBox = ResolveBoundingBoxOutlineBox();
                using BudgetedParserList<SvgGeometry> repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                foreach (SvgGeometry existing in stroked.Geometries)
                {
                    if (paintPathOutline) repainted.Add(SvgGradientPolygon.RepaintOutline(existing.Polygon, pathGradient, pathBox, pathStrokeShape.Presentation.Opacity, budget));
                    else if (paintPathRadial) repainted.Add(SvgRadialPolygon.RepaintOutline(existing.Polygon, pathCircle, pathBox, pathStrokeShape.Presentation.Opacity, budget));
                    else repainted.Add(SvgPatternPolygon.RepaintOutline(existing.Polygon, outlineTile, pathStrokeShape.Presentation.Opacity, budget));
                }
                stroked.Dispose();
                stroked = new SvgParsedGeometry(repainted.Detach(), stroked.ElementCount);
            }
        }
        catch { filled.Dispose(); throw; }
        return ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);
    }

    /// <summary>Scans path data for command counts and curve, arc and fractional notation shared by solid and gradient lowering.</summary>
    internal static (int CommandCount, bool HasCurve, bool HasArc, bool HasFractional) ScanPathData(string value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int commands = 0;
        bool curve = false;
        bool arc = false;
        bool fractional = false;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) workCounter.Consume(cancellationToken);
            char letter = value[index];
            if (letter is 'A' or 'a') { arc = true; curve = true; commands++; }
            else if (letter is 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't') { curve = true; commands++; }
            else if (letter is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z') commands++;
            else if (letter is '.' or 'e' or 'E') fractional = true;
        }

        return (commands, curve, arc, fractional);
    }

    internal static BudgetedParserList<SvgPoint> TokenizeBands(XmlReader reader, string elementName, bool isPath, bool allowDiagonal, int maximumGeometryElements, RenderBudget budget, SvgPaintableElementContext context, BudgetedParserList<int> contourEnds)
    {
        if (isPath)
        {
            string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
            BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, contourEnds, false);
            try { SvgOrthogonalGeometryLowering.ValidateContours(points, contourEnds, elementName, context.WorkCounter, context.CancellationToken, allowDiagonal); }
            catch { points.Dispose(); throw; }
            return points;
        }
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        BudgetedParserList<SvgPoint> pointList = SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, elementName, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, false);
        try
        {
            if (!allowDiagonal) SvgOrthogonalGeometryLowering.ValidateSimpleContour(pointList, elementName, context.WorkCounter, context.CancellationToken);
            contourEnds.Add(pointList.Count);
        }
        catch { pointList.Dispose(); throw; }
        return pointList;
    }

    internal static SvgParsedGeometry ParsePointList(XmlReader reader, string elementName, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        bool paintOutline = TryResolveOutlineGradient(style, state, out SvgLinearGradient outlineCandidate);
        bool paintRadialOutline = TryResolveOutlineRadial(style, state, out SvgRadialGradient outlineRadialCandidate);
        bool paintPatternOutline = TryResolveOutlinePattern(style, state, out SvgPatternTile outlineTile);
        SvgComputedStyle fillStyle = (paintOutline || paintRadialOutline || paintPatternOutline) ? style with { Stroke = "none" } : style;
        if (SvgGradientSyntax.IsGradientFill(style.Fill))
        {
            if (state.Patterns.TryGetValue(SvgGradientSyntax.ParsePaintId(style.Fill), out _)) return SvgPatternSyntax.LowerPatternBands(reader, context, style, state, elementName, false, maximumGeometryElements, budget, frame);
            return SvgGradientSyntax.LowerGradientBands(reader, context, style, state, elementName, false, maximumGeometryElements, budget, frame);
        }
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        SvgResolvedPaintableElement shape;
        SvgParsedGeometry filled;
        bool hasFractional = pointsValue.Contains('.') || pointsValue.Contains('e') || pointsValue.Contains('E');
        if (hasFractional)
        {
            shape = context.ResolveShape(fillStyle, reader, true);
            bool fractionalEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using BudgetedParserList<SvgFractionalPoint> fractionalPoints = SvgOrthogonalGeometryTokenizer.ParseFractionalPointList(pointsValue, elementName, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<SvgPoint> noFractionPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noFractionEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering fractionalLowering = new(noFractionPoints, noFractionEnds, elementName, false, fractionalEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            filled = fractionalLowering.LowerFractionalPointList(fractionalPoints, SvgPolygonMapping.FiniteEdges, shape, frame, budget) with { ElementCount = fractionalPoints.Count + 1 };
            SvgStrokeStyleSyntax.SvgAdmittedStroke? fractionalStroke;
            SvgParsedGeometry fractionalStroked;
            SvgLinearGradient fractionalGradient = default;
            SvgRadialGradient fractionalCircle = default;
            SvgComputedStyle fractionalStrokeStyle = style;
            SvgResolvedPaintableElement fractionalStrokeShape = shape;
            if (paintOutline || paintRadialOutline || paintPatternOutline)
            {
                if (paintOutline)
                {
                    fractionalGradient = SvgGradientSyntax.MapUserSpaceVector(shape.Transform, outlineCandidate, frame);
                    fractionalGradient = SvgGradientSyntax.MirrorBoundingBoxVector(shape.Transform, fractionalGradient);
                }
                else if (paintRadialOutline) fractionalCircle = SvgGradientSyntax.MapUserSpaceRadial(shape.Transform, outlineRadialCandidate, frame);
                // Lower the outline exactly as a solid stroke so caps, joins, dashes and transforms behave identically, then repaint it below. The solid color never reaches the scene.
                fractionalStrokeStyle = style with { Stroke = "#ffffff" };
                fractionalStrokeShape = context.ResolveShape(fractionalStrokeStyle, reader, true);
            }
            try
            {
                fractionalStroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, fractionalStrokeStyle, fractionalStrokeShape, elementName, false);
                if (fractionalStroke is null) return filled;
                fractionalStroked = fractionalLowering.LowerStrokePointList(fractionalPoints, elementName == "polygon", false, fractionalStrokeShape, fractionalStroke.Value.Color, fractionalStrokeShape.Presentation.IsPainted, fractionalStroke.Value.Width, fractionalStroke.Value.MiterLimit, fractionalStroke.Value.Cap, fractionalStroke.Value.Join, fractionalStroke.Value.DashPattern, fractionalStroke.Value.DashOffset, fractionalStroke.Value.PathLength, fractionalStroke.Value.NonScalingStroke, frame, budget);
                if (paintOutline || paintRadialOutline || paintPatternOutline)
                {
                    bool needsFractionalBox = (paintOutline && !outlineCandidate.IsUserSpace) || (paintRadialOutline && !outlineRadialCandidate.IsUserSpace);
                    SvgRectangle fractionalBox = needsFractionalBox ? BoxFromFilledGeometryBounds(filled, context.CancellationToken) : default;
                    using BudgetedParserList<SvgGeometry> repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                    foreach (SvgGeometry existing in fractionalStroked.Geometries)
                    {
                        if (paintOutline) repainted.Add(SvgGradientPolygon.RepaintOutline(existing.Polygon, fractionalGradient, fractionalBox, fractionalStrokeShape.Presentation.Opacity, budget));
                        else if (paintRadialOutline) repainted.Add(SvgRadialPolygon.RepaintOutline(existing.Polygon, fractionalCircle, fractionalBox, fractionalStrokeShape.Presentation.Opacity, budget));
                        else repainted.Add(SvgPatternPolygon.RepaintOutline(existing.Polygon, outlineTile, fractionalStrokeShape.Presentation.Opacity, budget));
                    }
                    fractionalStroked.Dispose();
                    fractionalStroked = new SvgParsedGeometry(repainted.Detach(), fractionalStroked.ElementCount);
                }
            }
            catch { filled.Dispose(); throw; }
            return ConcatParsedGeometries(filled, fractionalStroked, budget, context.WorkCounter, context.CancellationToken);
        }
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, elementName, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, true);
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        contourEnds.Add(points.Count);
        if (AllContoursDegenerate(points, contourEnds))
        {
            // A sealed subpath with fewer than three points encloses no area, so the fill contributes nothing while stroke lowering still outlines it.
            shape = context.ResolveShape(fillStyle, reader, true);
            using BudgetedParserList<SvgGeometry> empty = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            filled = new SvgParsedGeometry(empty.Detach(), points.Count + 1);
        }
        else if (HasDiagonalContour(points, contourEnds))
        {
            shape = context.ResolveShape(fillStyle, reader, true);
            bool diagonalEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using SvgOrthogonalGeometryLowering diagonalLowering = new(points, contourEnds, elementName, false, diagonalEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            filled = diagonalLowering.LowerPolygonFill(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame, budget);
        }
        else
        {
            SvgOrthogonalGeometryLowering.ValidateSimpleContour(points, elementName, context.WorkCounter, context.CancellationToken);
            shape = context.ResolveShape(fillStyle, reader, true);
            bool isEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            // Keep the same contour/clip ownership as the path spelling above.
            if (!shape.Transform.IsAxisAligned || frame is { Clip.Antialiasing: not SvgClipAntialiasing.None })
            {
                using SvgOrthogonalGeometryLowering polygonLowering = new(points, contourEnds, elementName, false, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                filled = polygonLowering.LowerPolygonFill(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame, budget);
            }
            else
            {
                using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, false, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                filled = lowering.Lower(shape.Presentation.Color, shape.Presentation.HasFill, shape.Transform, frame);
            }
        }

        SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke;
        SvgParsedGeometry stroked;
        SvgLinearGradient outlineGradient = default;
        SvgRadialGradient outlineCircle = default;
        SvgComputedStyle strokeStyle = style;
        SvgResolvedPaintableElement strokeShape = shape;
        if (paintOutline || paintRadialOutline || paintPatternOutline)
        {
            if (paintOutline)
            {
                outlineGradient = SvgGradientSyntax.MapUserSpaceVector(shape.Transform, outlineCandidate, frame);
                outlineGradient = SvgGradientSyntax.MirrorBoundingBoxVector(shape.Transform, outlineGradient);
            }
            else if (paintRadialOutline) outlineCircle = SvgGradientSyntax.MapUserSpaceRadial(shape.Transform, outlineRadialCandidate, frame);
            // Lower the outline exactly as a solid stroke so caps, joins, dashes and transforms behave identically, then repaint it below. The solid color never reaches the scene.
            strokeStyle = style with { Stroke = "#ffffff" };
            strokeShape = context.ResolveShape(strokeStyle, reader, true);
        }
        try
        {
            stroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, strokeStyle, strokeShape, elementName, false);
            if (stroke is null) return filled;
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, false, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            stroked = strokeLowering.LowerStrokePolyline(points, elementName == "polygon", strokeShape.Transform, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, stroke.Value.Color, strokeShape.Presentation.IsPainted, frame, budget);
            if (paintOutline) stroked = RepaintOutlinesAsGradient(stroked, outlineGradient, SvgGradientSyntax.BoxFromGradientBands(points, shape.Transform, frame, context), strokeShape.Presentation.Opacity, budget);
            else if (paintRadialOutline) stroked = RepaintOutlinesAsRadial(stroked, outlineCircle, SvgGradientSyntax.BoxFromGradientBands(points, shape.Transform, frame, context), strokeShape.Presentation.Opacity, budget);
            else if (paintPatternOutline) stroked = RepaintOutlinesAsPattern(stroked, outlineTile, strokeShape.Presentation.Opacity, budget);
        }
        catch { filled.Dispose(); throw; }
        return ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);

        SvgParsedGeometry RepaintOutlinesAsGradient(SvgParsedGeometry white, SvgLinearGradient paint, SvgRectangle box, byte opacity, RenderBudget budget)
        {
            using BudgetedParserList<SvgGeometry> repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            foreach (SvgGeometry existing in white.Geometries) repainted.Add(SvgGradientPolygon.RepaintOutline(existing.Polygon, paint, box, opacity, budget));
            white.Dispose();
            return new SvgParsedGeometry(repainted.Detach(), white.ElementCount);
        }

        SvgParsedGeometry RepaintOutlinesAsRadial(SvgParsedGeometry white, SvgRadialGradient paint, SvgRectangle box, byte opacity, RenderBudget budget)
        {
            using BudgetedParserList<SvgGeometry> repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            foreach (SvgGeometry existing in white.Geometries) repainted.Add(SvgRadialPolygon.RepaintOutline(existing.Polygon, paint, box, opacity, budget));
            white.Dispose();
            return new SvgParsedGeometry(repainted.Detach(), white.ElementCount);
        }

        SvgParsedGeometry RepaintOutlinesAsPattern(SvgParsedGeometry white, SvgPatternTile paint, byte opacity, RenderBudget budget)
        {
            using BudgetedParserList<SvgGeometry> repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            foreach (SvgGeometry existing in white.Geometries) repainted.Add(SvgPatternPolygon.RepaintOutline(existing.Polygon, paint, opacity, budget));
            white.Dispose();
            return new SvgParsedGeometry(repainted.Detach(), white.ElementCount);
        }
    }

    internal static SvgParsedGeometry ConcatParsedGeometries(SvgParsedGeometry first, SvgParsedGeometry second, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
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

    /// <summary>Resolves a linear-gradient stroke for outline repainting, leaving all other strokes to the solid path.</summary>
    internal static bool TryResolveOutlineRadial(SvgComputedStyle style, SvgDocumentState state, out SvgRadialGradient circle)
    {
        circle = default;
        string? strokePaint = style.Stroke;
        return strokePaint is not null
            && SvgGradientSyntax.IsGradientStrokeUrl(strokePaint)
            && SvgLocalUrl.TryParseId(strokePaint, out string outlinePaintId)
            && state.RadialGradients.TryGetValue(outlinePaintId, out circle);
    }

    /// <summary>Reads output-space fill-geometry bounds for bounding-box outline paints, sharing one helper across path and fractional lowerings.</summary>
    internal static SvgRectangle BoxFromFilledGeometryBounds(SvgParsedGeometry filled, CancellationToken cancellationToken)
    {
        double left = double.PositiveInfinity;
        double top = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        double bottom = double.NegativeInfinity;
        foreach (SvgGeometry existing in filled.Geometries)
        {
            switch (existing.Kind)
            {
                case SvgGeometryKind.Rectangle:
                case SvgGeometryKind.OrthogonalBand:
                    left = Math.Min(left, existing.Rectangle.X);
                    top = Math.Min(top, existing.Rectangle.Y);
                    right = Math.Max(right, existing.Rectangle.X + existing.Rectangle.Width);
                    bottom = Math.Max(bottom, existing.Rectangle.Y + existing.Rectangle.Height);
                    break;
                case SvgGeometryKind.PolygonFill:
                    int vertexIndex = 0;
                    foreach (SvgDevicePoint vertex in existing.Polygon.Points)
                    {
                        if ((vertexIndex & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        vertexIndex++;
                        left = Math.Min(left, vertex.X);
                        top = Math.Min(top, vertex.Y);
                        right = Math.Max(right, vertex.X);
                        bottom = Math.Max(bottom, vertex.Y);
                    }
                    break;
                default:
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box gradient strokes on this fill are not implemented.");
            }
        }
        if (!(left <= right) || !(top <= bottom)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box gradient strokes on empty fills are not implemented.");
        return new SvgRectangle(left, top, right - left, bottom - top, default, true, null);
    }

    /// <summary>Resolves a pattern-tile stroke for outline repainting, leaving all other strokes to the solid path.</summary>
    internal static bool TryResolveOutlinePattern(SvgComputedStyle style, SvgDocumentState state, out SvgPatternTile tile)
    {
        tile = default;
        string? strokePaint = style.Stroke;
        return strokePaint is not null
            && SvgPatternSyntax.IsPatternStroke(strokePaint, state)
            && state.Patterns.TryGetValue(SvgPatternSyntax.ParsePatternId(strokePaint), out tile);
    }

    internal static bool TryResolveOutlineGradient(SvgComputedStyle style, SvgDocumentState state, out SvgLinearGradient gradient)
    {
        gradient = default;
        string? strokePaint = style.Stroke;
        return strokePaint is not null
            && SvgGradientSyntax.IsGradientStrokeUrl(strokePaint)
            && SvgLocalUrl.TryParseId(strokePaint, out string outlinePaintId)
            && state.Gradients.TryGetValue(outlinePaintId, out gradient);
    }

    internal static bool AllContoursDegenerate(BudgetedParserList<SvgPoint> contourPoints, BudgetedParserList<int> ends)
    {
        if (ends.Count == 0) return false;
        int start = 0;
        for (int index = 0; index < ends.Count; index++)
        {
            if (ends[index] - start >= 3) return false;
            start = ends[index];
        }

        return true;
    }

    /// <summary>Drops single-point and two-point subpaths from fill lowering in place; they enclose no area while strokes outline them separately.</summary>
    private static void DropSingletonContours(BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(contourEnds);
        ArgumentNullException.ThrowIfNull(workCounter);
        int sourceStart = 0;
        int target = 0;
        int targetEnd = 0;
        for (int contour = 0; contour < contourEnds.Count; contour++)
        {
            int sourceEnd = contourEnds[contour];
            if (sourceEnd - sourceStart >= 3)
            {
                for (int index = sourceStart; index < sourceEnd; index++)
                {
                    workCounter.Consume(cancellationToken);
                    points[target++] = points[index];
                }
                contourEnds[targetEnd++] = target;
                workCounter.Consume(cancellationToken);
            }
            sourceStart = sourceEnd;
        }
        while (points.Count > target) points.RemoveLast();
        while (contourEnds.Count > targetEnd) contourEnds.RemoveLast();
    }

    internal static bool HasDiagonalContour(BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds)
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
