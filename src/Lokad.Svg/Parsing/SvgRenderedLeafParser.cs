using System.Xml;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Routes supported rendered leaves through their bounded lowerers outside XML traversal.</summary>
internal static class SvgRenderedLeafParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, bool isEligible, SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, SvgFontSnapshot? fontSnapshot, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (!isEligible || reader.NamespaceURI != "http://www.w3.org/2000/svg") return false;
        if (state.DefinitionsDepth >= 0) return false;
        switch (reader.LocalName)
        {
            case "rect":
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                break;
            case "line":
                SvgElementAttributePolicy.ValidateLine(reader, workCounter, cancellationToken);
                break;
            case "path":
                SvgElementAttributePolicy.ValidatePath(reader, workCounter, cancellationToken);
                break;
            case "polygon" or "polyline":
                SvgElementAttributePolicy.ValidatePointList(reader, reader.LocalName, workCounter, cancellationToken);
                break;
            case "circle" or "ellipse":
                SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, workCounter, cancellationToken);
                break;
            case "text":
                SvgElementAttributePolicy.ValidateText(reader, workCounter, cancellationToken);
                break;
            case "image":
                SvgElementAttributePolicy.ValidateImage(reader, workCounter, cancellationToken);
                break;
            default:
                return false;
        }
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);

        SvgRenderedGroupSourceParser.CaptureLeaf(reader, context, state);
        switch (reader.LocalName)
        {
            case "rect":
                string? deferredRectClip = SvgClipPathSyntax.PendingClipId(reader, state);
                string? deferredRectMask = SvgMaskSyntax.PendingMaskId(reader, state);
                bool deferRectClip = deferredRectClip is not null;
                int rectFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferRectClip;
                try
                {
                    using (SvgParsedGeometry rectangle = SvgLeafParser.ParseRectangle(reader, context, state.EffectiveFrame, state))
                    {
                        if (rectangle.Geometries.Length == 1)
                        {
                            SvgGeometry single = rectangle.Geometries[0];
                            SvgMaskSyntax.ValidateMaskScopes(state, single);
                            state.Scene.AddGeometry(single);
                        }
                        else
                        {
                            SvgMaskSyntax.ValidateMaskScopeBands(state, rectangle.Storage.Items);
                            try { state.Scene.AddGeometries(rectangle.Geometries); }
                            catch { foreach (SvgGeometry geometry in rectangle.Geometries) geometry.Dispose(); throw; }
                        }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferRectClip && deferredRectClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(rectFirstGeometry, state.Scene.GeometryCount - rectFirstGeometry, deferredRectClip, state.EffectiveFrame, true));
                if (deferredRectMask is not null) SvgLeafMaskComposition.Retain(state, rectFirstGeometry, state.Scene.GeometryCount - rectFirstGeometry, deferredRectMask, state.EffectiveFrame, workCounter, cancellationToken);
                SvgPaintRecording.FinishScene(state, rectFirstGeometry, workCounter, cancellationToken);
                return true;
            case "line":
                string? deferredLineClip = SvgClipPathSyntax.PendingClipId(reader, state);
                string? deferredLineMask = SvgMaskSyntax.PendingMaskId(reader, state);
                bool deferLineClip = deferredLineClip is not null;
                int lineFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferLineClip;
                try
                {
                    using (SvgParsedGeometry line = SvgLeafParser.ParseLine(reader, context, SvgClipPathSyntax.ResolveLeafClip(reader, state), state))
                    {
                        if (line.Geometries.Length == 1)
                        {
                            SvgGeometry single = line.Geometries[0];
                            SvgMaskSyntax.ValidateMaskScopes(state, single);
                            state.Scene.AddGeometry(single);
                        }
                        else
                        {
                            SvgMaskSyntax.ValidateMaskScopeBands(state, line.Storage.Items);
                            try { state.Scene.AddGeometries(line.Geometries); }
                            catch { foreach (SvgGeometry geometry in line.Geometries) geometry.Dispose(); throw; }
                        }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferLineClip && deferredLineClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(lineFirstGeometry, state.Scene.GeometryCount - lineFirstGeometry, deferredLineClip, state.EffectiveFrame, false));
                if (deferredLineMask is not null) SvgLeafMaskComposition.Retain(state, lineFirstGeometry, state.Scene.GeometryCount - lineFirstGeometry, deferredLineMask, state.EffectiveFrame, workCounter, cancellationToken);
                SvgPaintRecording.FinishScene(state, lineFirstGeometry, workCounter, cancellationToken);
                return true;
            case "circle":
                string? deferredCircleClip = SvgClipPathSyntax.PendingClipId(reader, state);
                string? deferredCircleMask = SvgMaskSyntax.PendingMaskId(reader, state);
                bool deferCircleClip = deferredCircleClip is not null;
                int circleFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferCircleClip;
                try
                {
                    using (SvgParsedGeometry circle = SvgLeafParser.ParseCircle(reader, context, SvgClipPathSyntax.ResolveLeafClip(reader, state), state))
                    {
                        if (circle.Geometries.Length == 1)
                        {
                            SvgGeometry single = circle.Geometries[0];
                            SvgMaskSyntax.ValidateMaskScopes(state, single);
                            state.Scene.AddGeometry(single);
                        }
                        else
                        {
                            SvgMaskSyntax.ValidateMaskScopeBands(state, circle.Storage.Items);
                            try { state.Scene.AddGeometries(circle.Geometries); }
                            catch { foreach (SvgGeometry geometry in circle.Geometries) geometry.Dispose(); throw; }
                        }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferCircleClip && deferredCircleClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(circleFirstGeometry, state.Scene.GeometryCount - circleFirstGeometry, deferredCircleClip, state.EffectiveFrame, false));
                if (deferredCircleMask is not null) SvgLeafMaskComposition.Retain(state, circleFirstGeometry, state.Scene.GeometryCount - circleFirstGeometry, deferredCircleMask, state.EffectiveFrame, workCounter, cancellationToken);
                SvgPaintRecording.FinishScene(state, circleFirstGeometry, workCounter, cancellationToken);
                return true;
            case "ellipse":
                string? deferredEllipseClip = SvgClipPathSyntax.PendingClipId(reader, state);
                string? deferredEllipseMask = SvgMaskSyntax.PendingMaskId(reader, state);
                bool deferEllipseClip = deferredEllipseClip is not null;
                int ellipseFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferEllipseClip;
                try
                {
                    using (SvgParsedGeometry ellipse = SvgLeafParser.ParseEllipse(reader, context, SvgClipPathSyntax.ResolveLeafClip(reader, state), state))
                    {
                        if (ellipse.Geometries.Length == 1)
                        {
                            SvgGeometry single = ellipse.Geometries[0];
                            SvgMaskSyntax.ValidateMaskScopes(state, single);
                            state.Scene.AddGeometry(single);
                        }
                        else
                        {
                            SvgMaskSyntax.ValidateMaskScopeBands(state, ellipse.Storage.Items);
                            try { state.Scene.AddGeometries(ellipse.Geometries); }
                            catch { foreach (SvgGeometry geometry in ellipse.Geometries) geometry.Dispose(); throw; }
                        }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferEllipseClip && deferredEllipseClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(ellipseFirstGeometry, state.Scene.GeometryCount - ellipseFirstGeometry, deferredEllipseClip, state.EffectiveFrame, false));
                if (deferredEllipseMask is not null) SvgLeafMaskComposition.Retain(state, ellipseFirstGeometry, state.Scene.GeometryCount - ellipseFirstGeometry, deferredEllipseMask, state.EffectiveFrame, workCounter, cancellationToken);
                SvgPaintRecording.FinishScene(state, ellipseFirstGeometry, workCounter, cancellationToken);
                return true;
            case "path":
                string? deferredPathClip = SvgClipPathSyntax.PendingClipId(reader, state);
                string? deferredPathMask = SvgMaskSyntax.PendingMaskId(reader, state);
                bool deferPathClip = deferredPathClip is not null;
                int pathFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferPathClip;
                try
                {
                    using (SvgParsedGeometry path = SvgOrthogonalShapeParser.ParsePath(reader, context, limits.MaximumPathCommands - state.GeometryElementCount, budget, ResolveShapeClipFrame(reader, "path", true, state, limits, budget, context), state))
                    {
                        state.GeometryElementCount += path.ElementCount;
                        SvgMaskSyntax.ValidateMaskScopeBands(state, path.Storage.Items);
                        try { state.Scene.AddGeometries(path.Geometries); }
                            catch { foreach (SvgGeometry geometry in path.Geometries) geometry.Dispose(); throw; }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferPathClip && deferredPathClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(pathFirstGeometry, state.Scene.GeometryCount - pathFirstGeometry, deferredPathClip, state.EffectiveFrame, true));
                if (deferredPathMask is not null) SvgLeafMaskComposition.Retain(state, pathFirstGeometry, state.Scene.GeometryCount - pathFirstGeometry, deferredPathMask, state.EffectiveFrame, workCounter, cancellationToken);
                SvgPaintRecording.FinishScene(state, pathFirstGeometry, workCounter, cancellationToken);
                return true;
            case "polygon" or "polyline":
                string? deferredPointListClip = SvgClipPathSyntax.PendingClipId(reader, state);
                string? deferredPointListMask = SvgMaskSyntax.PendingMaskId(reader, state);
                bool deferPointListClip = deferredPointListClip is not null;
                int pointListFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferPointListClip;
                try
                {
                    using (SvgParsedGeometry pointList = SvgOrthogonalShapeParser.ParsePointList(reader, reader.LocalName, context, limits.MaximumPathCommands - state.GeometryElementCount, budget, ResolveShapeClipFrame(reader, reader.LocalName, false, state, limits, budget, context), state))
                    {
                        state.GeometryElementCount += pointList.ElementCount;
                        SvgMaskSyntax.ValidateMaskScopeBands(state, pointList.Storage.Items);
                        try { state.Scene.AddGeometries(pointList.Geometries); }
                            catch { foreach (SvgGeometry geometry in pointList.Geometries) geometry.Dispose(); throw; }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferPointListClip && deferredPointListClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(pointListFirstGeometry, state.Scene.GeometryCount - pointListFirstGeometry, deferredPointListClip, state.EffectiveFrame, true));
                if (deferredPointListMask is not null) SvgLeafMaskComposition.Retain(state, pointListFirstGeometry, state.Scene.GeometryCount - pointListFirstGeometry, deferredPointListMask, state.EffectiveFrame, workCounter, cancellationToken);
                SvgPaintRecording.FinishScene(state, pointListFirstGeometry, workCounter, cancellationToken);
                return true;
            case "image":
            {
                string? deferredImageClip = SvgClipPathSyntax.PendingClipId(reader, state);
                string? deferredImageMask = SvgMaskSyntax.PendingMaskId(reader, state);
                bool deferImageClip = deferredImageClip is not null;
                int imageFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferImageClip;
                try
                {
                    SvgViewportFrame? imageFrame = SvgClipPathSyntax.ResolveLeafClip(reader, state);
                    SvgViewportFrame? imageParent = state.EffectiveFrame;
                    SvgGeometry image = SvgLeafParser.ParseImage(reader, context, imageFrame, imageParent?.ViewportWidth ?? root.UserViewportWidth, imageParent?.ViewportHeight ?? root.UserViewportHeight, limits, budget);
                    SvgMaskSyntax.ValidateMaskScopes(state, image);
                    state.Scene.AddGeometry(image);
                }
                finally { state.DeferringLeafClip = false; }
                if (deferImageClip && deferredImageClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(imageFirstGeometry, state.Scene.GeometryCount - imageFirstGeometry, deferredImageClip, state.EffectiveFrame, false));
                if (deferredImageMask is not null) SvgLeafMaskComposition.Retain(state, imageFirstGeometry, state.Scene.GeometryCount - imageFirstGeometry, deferredImageMask, state.EffectiveFrame, workCounter, cancellationToken);
                SvgPaintRecording.FinishScene(state, imageFirstGeometry, workCounter, cancellationToken);
                return true;
            }
            case "text":
            {
                // Fail fast with the conversion-level category: the operation is already rejected as unsupported, so a later font lookup must not surface UnresolvedResource instead.
                if (state.HasUnsupportedContent) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
                if (reader.GetAttribute("clip-path") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The clip-path attribute on text is not implemented.");
                if (reader.GetAttribute("mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on text are not implemented.");
                SvgResolvedDirectTextElement resolvedText = context.ResolveTextShape(reader);
                SvgResolvedPaintableElement textElement = resolvedText.PaintableElement;
                if (!textElement.Transform.Equals(SvgAffineTransform.Identity)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text transforms are not implemented.");
                if (textElement.Style.FillRule is not null && !textElement.Style.FillRule.Equals("nonzero", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only the default nonzero fill rule is implemented for direct text.");
                SvgTextStyle textStyle = SvgTextSyntax.Parse(reader, resolvedText.InlineFont, context.InheritedTextStyle, resolvedText.LetterSpacing);
                SvgTextWhitespaceMode whitespaceMode = SvgTextSyntax.ParseWhitespaceMode(reader, resolvedText.WhiteSpace, context.InheritedTextStyle.WhitespaceMode);
                using BudgetedTextLayout text = ReadDirectTextContent(reader, state, whitespaceMode, textStyle.LetterSpacing, SvgTextLimits.MaximumCharacters - state.TextCharacterCount, limits.MaximumNodes - state.NodeCount, limits.MaximumXmlDepth, limits.MaximumAttributesPerElement, budget, workCounter, cancellationToken, out int sourceCharacterCount, out int childNodeCount);
                state.TextCharacterCount += sourceCharacterCount;
                state.NodeCount += childNodeCount;
                if (text.Value.Length == 0) return true;
                if (!textElement.Presentation.IsPainted || !textElement.Presentation.HasFill) return true;
                if (fontSnapshot is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG text requires a prepared font snapshot.");
                using SvgTextRun run = SvgTextRunBuilder.Create(textStyle, text.Value, text.LetterSpacingSpans, text.PositionAdjustments, fontSnapshot, budget, workCounter, cancellationToken);
                if (textElement.Presentation.HasFill)
                {
                    foreach (SvgGlyphContourStorage contours in SvgTextRunBuilder.GetGlyphContours(run, budget, workCounter, cancellationToken))
                    {
                        SvgGlyphGeometry glyph = SvgGlyphGeometry.Create(contours, textElement.Presentation.Color);
                        try { state.Scene.AddGeometry(SvgGeometry.FromGlyphContours(glyph)); }
                        catch { glyph.Dispose(); throw; }
                    }
                }
                return true;
            }
        }
        return false;

        static SvgViewportFrame? ResolveShapeClipFrame(XmlReader reader, string elementName, bool isPath, SvgDocumentState state, SvgRenderLimits limits, RenderBudget budget, SvgPaintableElementContext context)
        {
            string? reference = reader.GetAttribute("clip-path");
            if (reference is null) return SvgClipPathSyntax.ResolveLeafClip(reader, state);
            if (!state.ObjectClips.TryGetValue(SvgClipPathSyntax.ParseClipId(reference), out _)) return SvgClipPathSyntax.ResolveLeafClip(reader, state);
            double minimumX;
            double minimumY;
            double maximumX;
            double maximumY;
            try
            {
                using BudgetedParserList<SvgPoint> points = TokenizeShapePoints(reader, elementName, isPath, limits.MaximumPathCommands - state.GeometryElementCount, budget, context);
                minimumX = points[0].X;
                minimumY = points[0].Y;
                maximumX = points[0].X;
                maximumY = points[0].Y;
                for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
                {
                    if ((pointIndex & 4095) == 0) context.CancellationToken.ThrowIfCancellationRequested();
                    SvgPoint vertex = points[pointIndex];
                    if (vertex.X < minimumX) minimumX = vertex.X;
                    if (vertex.Y < minimumY) minimumY = vertex.Y;
                    if (vertex.X > maximumX) maximumX = vertex.X;
                    if (vertex.Y > maximumY) maximumY = vertex.Y;
                }
            }
            catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.UnsupportedFeature)
            {
                (minimumX, minimumY, maximumX, maximumY) = FlattenedShapeBounds(reader, elementName, isPath, limits.MaximumPathCommands - state.GeometryElementCount, budget, context);
            }
            SvgAffineTransform transform = context.ResolveTransform(reader);
            SvgRectangle placement = transform.Apply(new SvgRectangle(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY, default, true, null));
            return SvgClipPathSyntax.ResolveLeafClipOrPlacement(reader, state, state.EffectiveFrame, placement);

            static (double MinimumX, double MinimumY, double MaximumX, double MaximumY) FlattenedShapeBounds(XmlReader shape, string name, bool pathData, int maximumGeometryElements, RenderBudget geometryBudget, SvgPaintableElementContext shapeContext)
            {
                if (pathData)
                {
                    string d = shape.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
                    int boundsCommands = 0;
                    bool boundsHasCurve = false;
                    for (int scanIndex = 0; scanIndex < d.Length; scanIndex++)
                    {
                        if ((scanIndex & 4095) == 0) shapeContext.WorkCounter.Consume(shapeContext.CancellationToken);
                        char scanLetter = d[scanIndex];
                        if (scanLetter is 'A' or 'a' or 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't') { boundsHasCurve = true; boundsCommands++; }
                        else if (scanLetter is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z') boundsCommands++;
                    }

                    long wantedPoints = boundsHasCurve ? (long)boundsCommands * (SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand + 1) : (long)d.Length + boundsCommands + 1;
                    int pointCapacity = wantedPoints > SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints ? SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints : (int)wantedPoints;
                    if (pointCapacity < 1) pointCapacity = 1;
                    int contourCapacity = pointCapacity < maximumGeometryElements ? pointCapacity : maximumGeometryElements;
                    long scratchBytes;
                    try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)); }
                    catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Curve flattening exceeds the supported expansion range."); }
                    using RenderBudget.BudgetReservation scratch = geometryBudget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
                    SvgContourBuilder builder = new(pointCapacity, contourCapacity);
                    SvgPathDataParser.ParsePath(d, SvgAffineTransform.Identity, builder, new SvgFlattenPolicy(SvgOrthogonalGeometryLowering.ScaledFlatness(shapeContext.OutputMagnification), SvgOrthogonalGeometryLowering.FlattenMaxDepth, SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand), maximumGeometryElements, shapeContext.WorkCounter, shapeContext.CancellationToken, null);
                    if (builder.ContourCount == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths on curved or fractional contours are not implemented.");
                    return FlattenedContourBounds(builder, shapeContext.CancellationToken);
                }
                string pointsValue = shape.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, name + " points are required.");
                using BudgetedParserList<SvgFractionalPoint> fractional = SvgOrthogonalGeometryTokenizer.ParseFractionalPointList(pointsValue, name, maximumGeometryElements, geometryBudget, shapeContext.WorkCounter, shapeContext.CancellationToken);
                if (fractional.Count == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths on curved or fractional contours are not implemented.");
                double left = fractional[0].X;
                double top = fractional[0].Y;
                double right = fractional[0].X;
                double bottom = fractional[0].Y;
                for (int vertexIndex = 1; vertexIndex < fractional.Count; vertexIndex++)
                {
                    if ((vertexIndex & 4095) == 0) shapeContext.CancellationToken.ThrowIfCancellationRequested();
                    SvgFractionalPoint vertex = fractional[vertexIndex];
                    if (vertex.X < left) left = vertex.X;
                    if (vertex.Y < top) top = vertex.Y;
                    if (vertex.X > right) right = vertex.X;
                    if (vertex.Y > bottom) bottom = vertex.Y;
                }
                return (left, top, right, bottom);
            }

            static (double MinimumX, double MinimumY, double MaximumX, double MaximumY) FlattenedContourBounds(SvgContourBuilder builder, CancellationToken cancellationToken)
            {
                double left = double.PositiveInfinity;
                double top = double.PositiveInfinity;
                double right = double.NegativeInfinity;
                double bottom = double.NegativeInfinity;
                int vertexIndex = 0;
                for (int contour = 0; contour < builder.ContourCount; contour++)
                {
                    foreach (SvgDevicePoint vertex in builder.GetContour(contour))
                    {
                        if ((vertexIndex & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        vertexIndex++;
                        if (vertex.X < left) left = vertex.X;
                        if (vertex.Y < top) top = vertex.Y;
                        if (vertex.X > right) right = vertex.X;
                        if (vertex.Y > bottom) bottom = vertex.Y;
                    }
                }
                return (left, top, right, bottom);
            }

            static BudgetedParserList<SvgPoint> TokenizeShapePoints(XmlReader shape, string name, bool pathData, int maximumGeometryElements, RenderBudget geometryBudget, SvgPaintableElementContext shapeContext)
            {
                try
                {
                    if (pathData)
                    {
                        string d = shape.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
                        using BudgetedParserList<int> contourEnds = new(geometryBudget, SvgMemoryCategory.Geometry, shapeContext.WorkCounter, shapeContext.CancellationToken);
                        return SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, geometryBudget, shapeContext.WorkCounter, shapeContext.CancellationToken, contourEnds, false);
                    }
                    string pointsValue = shape.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, name + " points are required.");
                    return SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, name, maximumGeometryElements, geometryBudget, shapeContext.WorkCounter, shapeContext.CancellationToken, false);
                }
                catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.UnsupportedFeature)
                {
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths on curved or fractional contours are not implemented.");
                }
            }
        }

        static BudgetedTextLayout ReadDirectTextContent(XmlReader element, SvgDocumentState state, SvgTextWhitespaceMode whitespaceMode, double letterSpacing, int maximumCharacters, int remainingNodeCount, int maximumXmlDepth, int maximumAttributesPerElement, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken, out int sourceCharacterCount, out int childNodeCount)
        {
            using var text = new SvgTextLayoutBuffer(budget, letterSpacing, workCounter, cancellationToken);
            var normalizer = new SvgTextWhitespaceNormalizer(whitespaceMode, letterSpacing, 0, false);
            if (element.IsEmptyElement)
            {
                sourceCharacterCount = normalizer.SourceCharacterCount;
                childNodeCount = 0;
                return text.Detach();
            }
            childNodeCount = 0;
            sourceCharacterCount = 0;
            using var whitespaceNormalizers = new BudgetedParserList<SvgTextWhitespaceNormalizer>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
            whitespaceNormalizers.Add(normalizer);
            using RenderBudget.BudgetReservation chunkReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount(4_096, sizeof(char)), SvgMemoryCategory.Parser);
            char[] chunk = new char[4_096];
            using XmlReader subtree = element.ReadSubtree();
            while (subtree.Read())
            {
                workCounter.Consume(cancellationToken);
                if (subtree.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    int read;
                    do
                    {
                        read = subtree.ReadValueChunk(chunk, 0, chunk.Length);
                        if (read != 0)
                        {
                            if (read > maximumCharacters - sourceCharacterCount) throw new SvgException(SvgErrorCode.LimitExceeded, "The document direct text character count exceeds the admitted layout limit.");
                            sourceCharacterCount += read;
                            int currentScope = whitespaceNormalizers.Count - 1;
                            SvgTextWhitespaceNormalizer current = whitespaceNormalizers[currentScope];
                            bool introducesOutput = !current.HasOutput && (current.Mode == SvgTextWhitespaceMode.Preserve ? SvgTextWhitespaceNormalizer.HasText(chunk.AsSpan(0, read), workCounter, cancellationToken) : SvgTextWhitespaceNormalizer.HasNonWhitespaceText(chunk.AsSpan(0, read), workCounter, cancellationToken));
                            if (introducesOutput)
                            {
                                for (int ancestorScope = 0; ancestorScope < currentScope; ancestorScope++)
                                {
                                    SvgTextWhitespaceNormalizer ancestor = whitespaceNormalizers[ancestorScope];
                                    ancestor.ObserveChildContent(text, maximumCharacters, workCounter, cancellationToken);
                                    whitespaceNormalizers[ancestorScope] = ancestor;
                                }
                            }
                            current.Append(chunk.AsSpan(0, read), text, maximumCharacters, workCounter, cancellationToken);
                            whitespaceNormalizers[currentScope] = current;
                        }
                    } while (read != 0);
                    continue;
                }
                if (subtree.NodeType == XmlNodeType.Element && subtree.Depth != 0)
                {
                    if (subtree.Depth + element.Depth >= maximumXmlDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG XML nesting exceeds the configured limit.");
                    if (childNodeCount >= remainingNodeCount) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                    childNodeCount++;
                    if (subtree.AttributeCount > maximumAttributesPerElement) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG element attributes exceed the configured limit.");
                    SvgTextWhitespaceNormalizer inheritedNormalizer = whitespaceNormalizers[^1];
                    SvgTextSpanStyle inheritedStyle = new(inheritedNormalizer.Mode, inheritedNormalizer.LetterSpacing);
                    if (subtree.NamespaceURI != "http://www.w3.org/2000/svg" || subtree.LocalName != "tspan") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bounded text span children are implemented.");
                    SvgElementAttributePolicy.ValidateTextSpan(subtree, workCounter, cancellationToken);
                    InlineStyle styleSheet = SvgDocumentStyleResolver.Resolve(subtree, false, state, workCounter, cancellationToken);
                    if (!SvgTextSyntax.TryParseTspanStyle(subtree, inheritedStyle, styleSheet, workCounter, cancellationToken, out SvgTextSpanStyle childStyle)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only whitespace, letter-spacing, and scalar dx/dy tspan text children are implemented.");
                    if (!subtree.IsEmptyElement)
                    {
                        int childScopeDepth = whitespaceNormalizers.Count;
                        SvgTextPositionOffset? positionOffset = childStyle.PositionOffset;
                        bool ownsPositioningSpan = positionOffset.HasValue;
                        if (positionOffset is SvgTextPositionOffset offset) text.EnterPositioningSpan(offset, childScopeDepth, workCounter, cancellationToken);
                        whitespaceNormalizers.Add(new SvgTextWhitespaceNormalizer(childStyle.WhitespaceMode, childStyle.LetterSpacing, childScopeDepth, ownsPositioningSpan));
                    }
                    continue;
                }
                if (subtree.NodeType == XmlNodeType.EndElement && subtree.Depth != 0)
                {
                    SvgTextWhitespaceNormalizer current = whitespaceNormalizers[^1];
                    current.CompleteElement();
                    if (current.OwnsPositioningSpan) text.ExitPositioningSpan(current.ScopeDepth);
                    whitespaceNormalizers.RemoveLast();
                }
            }
            return text.Detach();
        }
    }
}
