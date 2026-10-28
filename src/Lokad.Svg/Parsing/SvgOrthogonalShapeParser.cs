using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Connects policy-validated bounded orthogonal path/point-list syntax with temporary lowering storage and typed geometry handoff.</summary>
internal static class SvgOrthogonalShapeParser
{
    internal static SvgParsedGeometry ParsePath(XmlReader reader, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, frame);
        SvgMarkerStyle markers = SvgMarkerStyle.Resolve(context.ResolveStyle(reader), context.InheritedPresentation.Markers);
        context = context with { DeferPaintOpacity = true };
        SvgParsedGeometry parsed = ParsePathCore(reader, context, maximumGeometryElements, budget, frame, state);
        SvgParsedGeometry recorded = SvgPaintRecording.Attach(parsed, markers.HasMarkers ? source with { ElementOpacity = SvgPaintOpacity.Opaque } : source, reader, context, frame, maximumGeometryElements, state);
        return SvgMarkerRenderer.AttachVertices(recorded, reader, context, source, frame, state, markers, maximumGeometryElements);
    }

    internal static SvgParsedGeometry ParsePathCore(XmlReader reader, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgPaintResolver.RemoveInertServers(ref style, state, reader.LocalName)) context = context with { AuthoredStyle = style };
        using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        string d = renderData.Value;
        if (d.Length == 0)
        {
            using BudgetedParserList<SvgGeometry> empty = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            return new(empty.Detach(), renderData.CommandCount + 1);
        }
        if (SvgGradientSyntax.IsGradientFill(style.Fill) && SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)
            && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width))
        {
            // Resolve each paint through the ordinary fill-only or stroke-only route.
            // Recording outside this method owns opacity and the requested paint order.
            SvgParsedGeometry paintedFill = ParsePathCore(reader, context with { AuthoredStyle = style with { Stroke = "none" } }, maximumGeometryElements, budget, frame, state);
            try
            {
                SvgParsedGeometry paintedStroke = ParsePathCore(reader, context with { AuthoredStyle = style with { Fill = "none" } }, maximumGeometryElements, budget, frame, state);
                return ConcatParsedGeometries(paintedFill, paintedStroke, budget, context.WorkCounter, context.CancellationToken);
            }
            catch { paintedFill.Discard(); throw; }
        }
        bool paintOutline = TryResolveOutlineGradient(style, state, out SvgLinearGradient outlineCandidate);
        bool paintRadialOutline = TryResolveOutlineRadial(style, state, out SvgRadialGradient outlineRadialCandidate);
        bool paintPatternOutline = TryResolveOutlinePattern(style, state, context.WorkCounter, context.CancellationToken, out SvgPatternTile outlineTile);
        SvgComputedStyle fillStyle = (paintOutline || paintRadialOutline || paintPatternOutline) ? style with { Stroke = "none" } : style;
        if (SvgGradientSyntax.IsGradientFill(style.Fill))
        {
            if (state.Patterns.TryGetValue(SvgGradientSyntax.ParsePaintId(style.Fill), out _)) return SvgPatternSyntax.LowerPatternBands(reader, context, style, state, "path", true, maximumGeometryElements, budget, frame);
            return SvgGradientSyntax.LowerGradientBands(reader, context, style, state, "path", true, maximumGeometryElements, budget, frame);
        }
        (_, bool hasCurve, bool hasArc, bool hasFractional) = ScanPathData(d, context.WorkCounter, context.CancellationToken);
        int commandCount = renderData.CommandCount;
        SvgResolvedPaintableElement shape;
        SvgParsedGeometry filled;
        shape = context.ResolveShape(fillStyle, reader, true);
        if (!shape.Presentation.HasFill && !context.RetainInstanceStrokePath && SvgLocalUrl.ReadReference(reader, "mask") is null && SvgLocalUrl.ReadReference(reader, "clip-path") is null && state.MaskScopes.Count == 0)
        {
            // Unpainted fills contribute no area here. Stroke paint resolves its
            // required source box below; native stroking retains every subpath.
            using BudgetedParserList<SvgGeometry> empty = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            filled = new SvgParsedGeometry(empty.Detach(), commandCount + 1);
        }
        else if (hasCurve)
        {
            bool curveEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using BudgetedParserList<SvgPoint> noPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering curveLowering = new(noPoints, noEnds, "path", true, curveEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgParsedGeometry curved = curveLowering.LowerDeviceFill(d, commandCount, hasCurve, SvgPolygonMapping.FlattenedCurves, shape, frame, context.RetainInstanceStrokePath, budget);
            filled = curved with { ElementCount = commandCount + 1 };
        }
        else if (hasFractional || context.RetainInstanceStrokePath)
        {
            bool fractionalEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
            using BudgetedParserList<SvgPoint> noFractionPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noFractionEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering fractionalLowering = new(noFractionPoints, noFractionEnds, "path", true, fractionalEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgParsedGeometry fractional = fractionalLowering.LowerDeviceFill(d, commandCount, false, SvgPolygonMapping.FiniteEdges, shape, frame, context.RetainInstanceStrokePath, budget);
            filled = fractional with { ElementCount = commandCount + 1 };
        }
        else
        {
            using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, contourEnds, true);
            if (!paintOutline && !paintRadialOutline && !paintPatternOutline && SvgLocalUrl.ReadReference(reader, "mask") is null && SvgLocalUrl.ReadReference(reader, "clip-path") is null && state.MaskScopes.Count == 0) DropSingletonContours(points, contourEnds, context.WorkCounter, context.CancellationToken);
            if (contourEnds.Count == 0 || AllContoursDegenerate(contourEnds))
            {
                // Sealed subpaths with fewer than three points enclose no area, so the fill contributes nothing while stroke lowering still outlines every subpath.
                using BudgetedParserList<SvgGeometry> empty = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                filled = new SvgParsedGeometry(empty.Detach(), commandCount + 1);
            }
            else if (HasDiagonalContour(points, contourEnds))
            {
                bool diagonalEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
                using SvgOrthogonalGeometryLowering diagonalLowering = new(points, contourEnds, "path", true, diagonalEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                // Retain directed finite contours before contact normalization.
                // Solid orthogonal paths keep their checked integer-band fast path.
                filled = shape.Presentation.HasFill
                    ? diagonalLowering.LowerDeviceFill(d, commandCount, false, SvgPolygonMapping.FiniteEdges, shape, frame, context.RetainInstanceStrokePath, budget) with { ElementCount = commandCount + 1 }
                    : diagonalLowering.LowerPolygonFill(shape.Presentation.Color, false, shape.Transform, frame, budget);
            }
            else
            {
                SvgOrthogonalGeometryLowering.ValidateContours(points, contourEnds, "path", context.WorkCounter, context.CancellationToken);
                foreach (SvgPoint point in points.AsSpan())
                {
                    context.WorkCounter.Consume(context.CancellationToken);
                    SvgDevicePoint mapped = shape.Transform.Apply(point);
                    if (mapped.X < int.MinValue || mapped.X > int.MaxValue || mapped.Y < int.MinValue || mapped.Y > int.MaxValue)
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported transform exceeds the coordinate range.");
                }
                bool isEvenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
                // Authored paths keep native float placement and path coverage.
                // Rectangle bands lose both source mapping and the native path/rect distinction.
                using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, "path", true, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                filled = shape.Presentation.HasFill
                    ? lowering.LowerDeviceFill(d, commandCount, false, SvgPolygonMapping.FiniteEdges, shape, frame, context.RetainInstanceStrokePath, budget)
                    : lowering.Lower(shape.Presentation.Color, false, shape.Transform, frame);
            }
        }

        try
        {
            SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke;
            SvgParsedGeometry stroked;
            bool isStraightPath = !hasCurve && !hasFractional;
            bool paintPathOutline = paintOutline;
            bool paintPathRadial = paintRadialOutline;
            bool paintPathPattern = paintPatternOutline;
            bool mappedOutline = (paintPathOutline || paintPathRadial)
                && (!shape.Transform.IsAxisAligned || paintPathOutline && outlineCandidate.RequiresSourceMap);
            SvgLinearGradient pathGradient = default;
            SvgRadialGradient pathCircle = default;
            SvgComputedStyle pathStrokeStyle = style;
            SvgResolvedPaintableElement pathStrokeShape = shape;
            if (paintPathOutline || paintPathRadial || paintPathPattern)
            {
                if (paintPathOutline && !mappedOutline)
                {
                    pathGradient = SvgGradientSyntax.MapUserSpaceVector(shape.Transform, outlineCandidate, frame);
                    pathGradient = SvgGradientSyntax.MirrorBoundingBoxVector(shape.Transform, pathGradient);
                }
                else if (paintPathRadial && !mappedOutline) pathCircle = SvgGradientSyntax.MapRadialReference(shape.Transform, outlineRadialCandidate, frame);
                // Lower the outline exactly as a solid stroke so caps, joins, dashes and transforms behave identically, then repaint it below. The solid color never reaches the scene.
                pathStrokeStyle = style with { Stroke = "#ffffff" };
                pathStrokeShape = context.ResolveShape(pathStrokeStyle, reader, true);
            }

            SvgRectangle ResolveBoundingBoxOutlineBox(SvgParsedGeometry outline)
            {
                bool needsFillBox = (paintPathOutline && !outlineCandidate.IsUserSpace) || (paintPathRadial && !outlineRadialCandidate.IsUserSpace);
                if (!needsFillBox) return default;
                if (!shape.Presentation.HasFill)
                {
                    foreach (SvgGeometry geometry in outline.Geometries)
                    {
                        context.WorkCounter.Consume(context.CancellationToken);
                        if (geometry.AuthoredPath is not { StrokeStyle: {} strokeStyle } path) continue;
                        // Paint binds to the undashed centerline's native curve bounds,
                        // independently of fill closure, flattening and stroke extents.
                        SvgRectangle bounds = strokeStyle.SourceBounds ?? SvgPathBounds.Compute(path.Commands, context.WorkCounter, context.CancellationToken);
                        return path.SceneMap.Apply(bounds);
                    }
                    if (outline.Geometries.Length == 0) return default;
                }
                if (isStraightPath)
                {
                    using BudgetedParserList<int> boxEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                    using BudgetedParserList<SvgPoint> boxPoints = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, boxEnds, true);
                    return SvgGradientSyntax.BoxFromGradientBands(boxPoints, shape.Transform, frame, context);
                }
                return BoxFromFilledOutlineBounds();
            }

            SvgRectangle BoxFromFilledOutlineBounds()
                => BoxFromFilledGeometryBounds(filled, context.WorkCounter, context.CancellationToken);

            try
            {
                stroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, pathStrokeStyle, pathStrokeShape, "path", true);
                if (stroke is null) return filled;
                using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, "path", true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                stroked = strokeLowering.LowerStrokePath(d, commandCount, hasCurve, pathStrokeShape, stroke.Value.Color, pathStrokeShape.Presentation.IsPainted, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, budget);
                try
                {
                    if (paintPathOutline || paintPathRadial || paintPathPattern)
                    {
                        if (mappedOutline)
                        {
                            stroked = RepaintMappedStroke(stroked, paintPathOutline, outlineCandidate, outlineRadialCandidate, pathStrokeShape.Presentation.Opacity, state, context.WorkCounter, context.CancellationToken);
                            return ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);
                        }
                        SvgRectangle pathBox = ResolveBoundingBoxOutlineBox(stroked);
                        using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                        foreach (SvgGeometry existing in stroked.Geometries)
                        {
                            if (paintPathOutline) repainted.Add(existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, pathGradient, pathBox, pathStrokeShape.Presentation.Opacity, budget)));
                            else if (paintPathRadial) repainted.Add(existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, pathCircle, pathBox, pathStrokeShape.Presentation.Opacity, budget)));
                            else repainted.Add(existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, outlineTile, pathStrokeShape.Presentation.Opacity, SvgViewportFrame.MapGeometry(pathStrokeShape.Transform, frame), budget)));
                        }
                        stroked.Dispose();
                        stroked = new SvgParsedGeometry(repainted.Detach(), stroked.ElementCount);
                    }
                }
                catch { stroked.Discard(); throw; }
            }
            catch { filled.Discard(); throw; }
            return ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);
        }
        catch { filled.Discard(); throw; }
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

    internal static BudgetedParserList<SvgPoint> TokenizeBands(XmlReader reader, string elementName, bool isPath, int maximumGeometryElements, RenderBudget budget, SvgPaintableElementContext context, BudgetedParserList<int> contourEnds)
    {
        if (isPath)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            string d = renderData.Value;
            BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, contourEnds, false);
            try { SvgOrthogonalGeometryLowering.ValidateContours(points, contourEnds, elementName, context.WorkCounter, context.CancellationToken, true); }
            catch { points.Dispose(); throw; }
            return points;
        }
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        BudgetedParserList<SvgPoint> pointList = SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, elementName, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, false);
        try
        {
            contourEnds.Add(pointList.Count);
        }
        catch { pointList.Dispose(); throw; }
        return pointList;
    }

    internal static SvgParsedGeometry ParsePointList(XmlReader reader, string elementName, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, frame);
        SvgMarkerStyle markers = SvgMarkerStyle.Resolve(context.ResolveStyle(reader), context.InheritedPresentation.Markers);
        context = context with { DeferPaintOpacity = true };
        SvgParsedGeometry parsed = ParsePointListCore(reader, elementName, context, maximumGeometryElements, budget, frame, state);
        SvgParsedGeometry recorded = SvgPaintRecording.Attach(parsed, markers.HasMarkers ? source with { ElementOpacity = SvgPaintOpacity.Opaque } : source,
            reader, context, frame, maximumGeometryElements, state);
        return SvgMarkerRenderer.AttachVertices(recorded, reader, context, source, frame, state, markers, maximumGeometryElements);
    }

    internal static SvgParsedGeometry ParsePointListCore(XmlReader reader, string elementName, SvgPaintableElementContext context, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgPaintResolver.RemoveInertServers(ref style, state, reader.LocalName)) context = context with { AuthoredStyle = style };
        if (SvgGradientSyntax.IsGradientFill(style.Fill) && SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)
            && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width))
        {
            SvgParsedGeometry paintedFill = ParsePointListCore(reader, elementName, context with { AuthoredStyle = style with { Stroke = "none" } }, maximumGeometryElements, budget, frame, state);
            try
            {
                SvgParsedGeometry paintedStroke = ParsePointListCore(reader, elementName, context with { AuthoredStyle = style with { Fill = "none" } }, maximumGeometryElements, budget, frame, state);
                return ConcatParsedGeometries(paintedFill, paintedStroke, budget, context.WorkCounter, context.CancellationToken);
            }
            catch { paintedFill.Discard(); throw; }
        }
        bool paintOutline = TryResolveOutlineGradient(style, state, out SvgLinearGradient outlineCandidate);
        bool paintRadialOutline = TryResolveOutlineRadial(style, state, out SvgRadialGradient outlineRadialCandidate);
        bool paintPatternOutline = TryResolveOutlinePattern(style, state, context.WorkCounter, context.CancellationToken, out SvgPatternTile outlineTile);
        bool mappedOutline = (paintOutline || paintRadialOutline)
            && (!context.ResolveTransform(reader).IsAxisAligned || paintOutline && outlineCandidate.RequiresSourceMap);
        SvgComputedStyle fillStyle = (paintOutline || paintRadialOutline || paintPatternOutline) ? style with { Stroke = "none" } : style;
        string pointsValue = reader.GetAttribute("points") ?? string.Empty;
        bool shaderFill = SvgGradientSyntax.IsGradientFill(style.Fill);
        // Coordinate spelling must not select rectangle-band coverage.
        SvgResolvedPaintableElement shape;
        SvgParsedGeometry filled;
        shape = context.ResolveShape(shaderFill ? fillStyle with { Fill = "#ffffff" } : fillStyle, reader, true);
        bool evenOdd = shape.Style.FillRule is not null && shape.Style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        using BudgetedParserList<SvgFractionalPoint> points = SvgOrthogonalGeometryTokenizer.ParseFractionalPointList(pointsValue, elementName, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken, 0);
        using BudgetedParserList<SvgPoint> emptyPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<int> emptyEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using SvgOrthogonalGeometryLowering lowering = new(emptyPoints, emptyEnds, elementName, false, evenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        filled = lowering.LowerPointListFill(points, shape, frame, budget) with { ElementCount = points.Count + 1 };
        try
        {
            if (shaderFill) filled = RepaintFill(filled);
            SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke;
            SvgParsedGeometry stroked;
            SvgLinearGradient strokeGradient = default;
            SvgRadialGradient strokeCircle = default;
            SvgComputedStyle strokeStyle = shaderFill ? style with { Fill = "#ffffff" } : style;
            SvgResolvedPaintableElement strokeShape = shape;
            if (paintOutline || paintRadialOutline || paintPatternOutline)
            {
                if (paintOutline && !mappedOutline)
                {
                    strokeGradient = SvgGradientSyntax.MapUserSpaceVector(shape.Transform, outlineCandidate, frame);
                    strokeGradient = SvgGradientSyntax.MirrorBoundingBoxVector(shape.Transform, strokeGradient);
                }
                else if (paintRadialOutline && !mappedOutline) strokeCircle = SvgGradientSyntax.MapRadialReference(shape.Transform, outlineRadialCandidate, frame);
                // Lower the outline exactly as a solid stroke so caps, joins, dashes and transforms behave identically, then repaint it below. The solid color never reaches the scene.
                strokeStyle = strokeStyle with { Stroke = "#ffffff" };
                strokeShape = context.ResolveShape(strokeStyle, reader, true);
            }
            try
            {
                stroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, strokeStyle, strokeShape, elementName, true);
                if (stroke is null) return filled;
                stroked = lowering.LowerStrokePointList(points, elementName == "polygon", strokeShape, stroke.Value.Color, strokeShape.Presentation.IsPainted, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, budget);
                try
                {
                    if (paintOutline || paintRadialOutline || paintPatternOutline)
                    {
                        if (mappedOutline)
                        {
                            stroked = RepaintMappedStroke(stroked, paintOutline, outlineCandidate, outlineRadialCandidate, strokeShape.Presentation.Opacity, state, context.WorkCounter, context.CancellationToken);
                            return ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);
                        }
                        bool needsStrokeBox = (paintOutline && !outlineCandidate.IsUserSpace) || (paintRadialOutline && !outlineRadialCandidate.IsUserSpace);
                        SvgRectangle strokeBox = default;
                        if (needsStrokeBox && stroked.Geometries.Length > 0)
                        {
                            SvgAuthoredPath source = stroked.Geometries[0].AuthoredPath ?? throw new InvalidOperationException("Point-list strokes require retained source commands.");
                            strokeBox = source.SceneMap.Apply(source.StrokeStyle?.SourceBounds ?? SvgPathBounds.Compute(source.Commands, context.WorkCounter, context.CancellationToken));
                        }
                        using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                        foreach (SvgGeometry existing in stroked.Geometries)
                        {
                            if (paintOutline) repainted.Add(existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, strokeGradient, strokeBox, strokeShape.Presentation.Opacity, budget)));
                            else if (paintRadialOutline) repainted.Add(existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, strokeCircle, strokeBox, strokeShape.Presentation.Opacity, budget)));
                            else repainted.Add(existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, outlineTile, strokeShape.Presentation.Opacity, SvgViewportFrame.MapGeometry(strokeShape.Transform, frame), budget)));
                        }
                        stroked.Dispose();
                        stroked = new SvgParsedGeometry(repainted.Detach(), stroked.ElementCount);
                    }
                }
                catch { stroked.Discard(); throw; }
            }
            catch { filled.Discard(); throw; }
            return ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);
        }
        catch { filled.Discard(); throw; }

        SvgParsedGeometry RepaintFill(SvgParsedGeometry source)
        {
            if (source.Geometries.Length == 0) return source;
            string id = SvgGradientSyntax.ParsePaintId(style.Fill);
            SvgAffineTransform transform = shape.Transform;
            bool linear = state.Gradients.TryGetValue(id, out SvgLinearGradient gradient);
            SvgRadialGradient circle = default;
            bool radial = !linear && state.RadialGradients.TryGetValue(id, out circle);
            SvgPatternTile tile = default;
            bool pattern = !linear && !radial && state.Patterns.TryGetValue(id, out tile);
            if (!linear && !radial && !pattern) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local paint references are implemented.");
            if (linear || radial)
            {
                // Retain source bounds before placement; mapped endpoints can
                // round to a different extent even for an axis-aligned map.
                if (source.Geometries[0].AuthoredPath is {} path)
                {
                    bool userSpace = linear ? gradient.IsUserSpace : circle.IsUserSpace;
                    SvgRectangle sourceBox = userSpace ? default : SvgPathBounds.Compute(path.Commands, context.WorkCounter, context.CancellationToken);
                    if (linear) gradient = SvgGradientSyntax.BindLinearSourceMap(gradient, sourceBox, path.SceneMap, state, context.WorkCounter, context.CancellationToken);
                    else circle = SvgGradientSyntax.BindRadialSourceMap(circle, sourceBox, path.SceneMap, context.WorkCounter, context.CancellationToken);
                }
                else if (source.Geometries[0].Polygon.HasFill || !transform.IsAxisAligned)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Mapped point-list fills require retained source commands.");
            }
            SvgAffineTransform patternMap = SvgViewportFrame.MapGeometry(transform, frame);
            if (pattern)
            {
                context.StrokeHostMap.ComposeChecked(patternMap);
                tile = SvgPatternReferenceResolver.ForReplay(id, tile, state, context.WorkCounter, context.CancellationToken);
            }
            SvgRectangle box = linear && gradient.IsUserSpace || radial && circle.IsUserSpace || pattern ? default : BoxFromFilledGeometryBounds(source, context.WorkCounter, context.CancellationToken);
            byte opacity = SvgReferenceResolver.MultiplyAlpha(style.GetOpacity().Alpha, SvgColorParser.ParseOpacity(style.FillOpacity));
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            try
            {
                foreach (SvgGeometry existing in source.Geometries)
                {
                    context.WorkCounter.Consume(context.CancellationToken);
                    SvgGeometry mapped = linear ? existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, gradient, box, opacity, budget))
                        : radial ? existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, circle, box, opacity, budget))
                        : existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, tile, opacity, patternMap, budget));
                    try { repainted.Add(mapped); }
                    catch { mapped.Dispose(); throw; }
                }
                source.Dispose();
                return new SvgParsedGeometry(repainted.Detach(), source.ElementCount) { SourceObjectBox = source.SourceObjectBox, SourceObjectMap = source.SourceObjectMap };
            }
            catch { foreach (SvgGeometry geometry in repainted.AsSpan()) geometry.Dispose(); source.Discard(); throw; }
        }
    }

    internal static SvgParsedGeometry ConcatParsedGeometries(SvgParsedGeometry first, SvgParsedGeometry second, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        try
        {
            using BudgetedParserList<SvgGeometry> combined = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            combined.AddRange(first.Geometries);
            foreach (SvgGeometry stroke in second.Geometries) combined.Add(stroke with { RecordedPaint = SvgRecordedPaint.SourceStroke });
            int elements = first.ElementCount > int.MaxValue - second.ElementCount ? int.MaxValue : first.ElementCount + second.ElementCount;
            SvgParsedGeometry result = new(combined.Detach(), elements) { SourceObjectBox = first.SourceObjectBox, SourceObjectMap = first.SourceObjectMap };
            first.Dispose();
            second.Dispose();
            return result;
        }
        catch
        {
            first.Discard();
            second.Discard();
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

    /// <summary>Reads scene-space fill bounds for object-bounding-box paints. Axis-aligned authored
    /// paths map original source-curve extrema; other geometry retains its lowered bounds.</summary>
    internal static SvgRectangle BoxFromFilledGeometryBounds(SvgParsedGeometry filled, ParserWorkCounter workCounter, CancellationToken cancellationToken)
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
                    if (existing.AuthoredPath is { } path && path.StrokeStyle is null && path.SceneMap.IsAxisAligned)
                    {
                        SvgRectangle box = SvgPathBounds.Compute(path.Commands, workCounter, cancellationToken);
                        SvgDevicePoint first = SvgAuthoredPath.MapPoint(path.SceneMap, new(box.X, box.Y), new(0, 0));
                        SvgDevicePoint second = SvgAuthoredPath.MapPoint(path.SceneMap, new(box.X + box.Width, box.Y + box.Height), new(0, 0));
                        left = Math.Min(left, Math.Min(first.X, second.X));
                        right = Math.Max(right, Math.Max(first.X, second.X));
                        top = Math.Min(top, Math.Min(first.Y, second.Y));
                        bottom = Math.Max(bottom, Math.Max(first.Y, second.Y));
                        break;
                    }
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

    /// <summary>Repaints retained strokes using undashed source bounds and their composed scene maps.</summary>
    /// <remarks>Consumes the source storage on success or failure. The scene owns retained linear maps;
    /// geometry owns transferred commands. Every partial output is released on denial or cancellation.</remarks>
    private static SvgParsedGeometry RepaintMappedStroke(SvgParsedGeometry source, bool linear, SvgLinearGradient gradient,
        SvgRadialGradient radial, byte opacity, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        using BudgetedGeometryBuilder repainted = new(state.Budget, SvgMemoryCategory.Geometry, work, token);
        try
        {
            foreach (SvgGeometry geometry in source.Geometries)
            {
                work.Consume(token);
                SvgAuthoredPath path = geometry.AuthoredPath ?? throw new InvalidOperationException("Mapped strokes require retained source commands.");
                SvgRectangle box = path.StrokeStyle?.SourceBounds ?? SvgPathBounds.Compute(path.Commands, work, token);
                SvgGeometry mapped = geometry.TransferAuthoredPathTo(linear
                    ? SvgGradientPolygon.RepaintOutline(geometry.Polygon, SvgGradientSyntax.BindLinearSourceMap(gradient, box, path.SceneMap, state, work, token), box, opacity, state.Budget)
                    : SvgRadialPolygon.RepaintOutline(geometry.Polygon, SvgGradientSyntax.BindRadialSourceMap(radial, box, path.SceneMap, work, token), box, opacity, state.Budget));
                try { repainted.Add(mapped); }
                catch { mapped.Dispose(); throw; }
            }
            source.Dispose();
            return new(repainted.Detach(), source.ElementCount);
        }
        catch { foreach (SvgGeometry geometry in repainted.AsSpan()) geometry.Dispose(); source.Discard(); throw; }
    }

    /// <summary>Resolves a pattern-tile stroke for outline repainting, leaving all other strokes to the solid path.</summary>
    internal static bool TryResolveOutlinePattern(SvgComputedStyle style, SvgDocumentState state, ParserWorkCounter work, CancellationToken token, out SvgPatternTile tile)
    {
        tile = default;
        if (style.Stroke is not {} strokePaint || !SvgPatternSyntax.IsPatternStroke(strokePaint, state)) return false;
        string id = SvgPatternSyntax.ParsePatternId(strokePaint);
        if (!state.Patterns.TryGetValue(id, out tile)) return false;
        tile = SvgPatternReferenceResolver.ForReplay(id, tile, state, work, token);
        return true;
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

    internal static bool AllContoursDegenerate(BudgetedParserList<int> ends)
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
