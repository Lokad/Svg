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

        switch (reader.LocalName)
        {
            case "rect":
                using (SvgParsedGeometry rectangle = SvgLeafParser.ParseRectangle(reader, context, state.EffectiveFrame, state))
                {
                    if (rectangle.Geometries.Length == 1)
                    {
                        SvgGeometry single = rectangle.Geometries[0];
                        SvgMaskSyntax.ValidateMaskScopes(state, single);
                        state.Scene.AddGeometry(SvgMaskSyntax.ApplyMaskedLeaf(reader, state, single));
                    }
                    else
                    {
                        SvgMaskSyntax.ApplyMaskToBands(reader, state, rectangle.Storage.Items);
                        SvgMaskSyntax.ValidateMaskScopeBands(state, rectangle.Storage.Items);
                        state.Scene.AddGeometries(rectangle.Geometries);
                    }
                }
                return true;
            case "line":
                using (SvgParsedGeometry line = SvgLeafParser.ParseLine(reader, context, SvgClipPathSyntax.ResolveLeafClip(reader, state), state))
                {
                    if (line.Geometries.Length == 1)
                    {
                        SvgGeometry single = line.Geometries[0];
                        SvgMaskSyntax.ValidateMaskScopes(state, single);
                        state.Scene.AddGeometry(SvgMaskSyntax.ApplyMaskedLeaf(reader, state, single));
                    }
                    else
                    {
                        SvgMaskSyntax.ApplyMaskToBands(reader, state, line.Storage.Items);
                        SvgMaskSyntax.ValidateMaskScopeBands(state, line.Storage.Items);
                        state.Scene.AddGeometries(line.Geometries);
                    }
                }
                return true;
            case "circle":
                using (SvgParsedGeometry circle = SvgLeafParser.ParseCircle(reader, context, SvgClipPathSyntax.ResolveLeafClip(reader, state), state))
                {
                    if (circle.Geometries.Length == 1)
                    {
                        SvgGeometry single = circle.Geometries[0];
                        SvgMaskSyntax.ValidateMaskScopes(state, single);
                        state.Scene.AddGeometry(SvgMaskSyntax.ApplyMaskedLeaf(reader, state, single));
                    }
                    else
                    {
                        SvgMaskSyntax.ApplyMaskToBands(reader, state, circle.Storage.Items);
                        SvgMaskSyntax.ValidateMaskScopeBands(state, circle.Storage.Items);
                        state.Scene.AddGeometries(circle.Geometries);
                    }
                }
                return true;
            case "ellipse":
                using (SvgParsedGeometry ellipse = SvgLeafParser.ParseEllipse(reader, context, SvgClipPathSyntax.ResolveLeafClip(reader, state), state))
                {
                    if (ellipse.Geometries.Length == 1)
                    {
                        SvgGeometry single = ellipse.Geometries[0];
                        SvgMaskSyntax.ValidateMaskScopes(state, single);
                        state.Scene.AddGeometry(SvgMaskSyntax.ApplyMaskedLeaf(reader, state, single));
                    }
                    else
                    {
                        SvgMaskSyntax.ApplyMaskToBands(reader, state, ellipse.Storage.Items);
                        SvgMaskSyntax.ValidateMaskScopeBands(state, ellipse.Storage.Items);
                        state.Scene.AddGeometries(ellipse.Geometries);
                    }
                }
                return true;
            case "path":
                using (SvgParsedGeometry path = SvgOrthogonalShapeParser.ParsePath(reader, context, limits.MaximumPathCommands - state.GeometryElementCount, budget, ResolveShapeClipFrame(reader, "path", true, state, limits, budget, context), state))
                {
                    state.GeometryElementCount += path.ElementCount;
                    SvgMaskSyntax.ApplyMaskToBands(reader, state, path.Storage.Items);
                    SvgMaskSyntax.ValidateMaskScopeBands(state, path.Storage.Items);
                    state.Scene.AddGeometries(path.Geometries);
                }
                return true;
            case "polygon" or "polyline":
                using (SvgParsedGeometry pointList = SvgOrthogonalShapeParser.ParsePointList(reader, reader.LocalName, context, limits.MaximumPathCommands - state.GeometryElementCount, budget, ResolveShapeClipFrame(reader, reader.LocalName, false, state, limits, budget, context), state))
                {
                    state.GeometryElementCount += pointList.ElementCount;
                    SvgMaskSyntax.ApplyMaskToBands(reader, state, pointList.Storage.Items);
                    SvgMaskSyntax.ValidateMaskScopeBands(state, pointList.Storage.Items);
                    state.Scene.AddGeometries(pointList.Geometries);
                }
                return true;
            case "image":
            {
                SvgViewportFrame? imageFrame = SvgClipPathSyntax.ResolveLeafClip(reader, state);
                SvgViewportFrame? imageParent = state.EffectiveFrame;
                SvgGeometry image = SvgLeafParser.ParseImage(reader, context, imageFrame, imageParent?.ViewportWidth ?? root.Width, imageParent?.ViewportHeight ?? root.Height, limits, budget);
                SvgMaskSyntax.ValidateMaskScopes(state, image);
                state.Scene.AddGeometry(SvgMaskSyntax.ApplyMaskedLeaf(reader, state, image));
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
                if (!textElement.Transform.Equals(AxisTransform.Identity)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text transforms are not implemented.");
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
            using BudgetedParserList<SvgPoint> points = TokenizeShapePoints(reader, elementName, isPath, limits.MaximumPathCommands - state.GeometryElementCount, budget, context);
            int minimumX = points[0].X;
            int minimumY = points[0].Y;
            int maximumX = points[0].X;
            int maximumY = points[0].Y;
            for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
            {
                if ((pointIndex & 4095) == 0) context.CancellationToken.ThrowIfCancellationRequested();
                SvgPoint vertex = points[pointIndex];
                if (vertex.X < minimumX) minimumX = vertex.X;
                if (vertex.Y < minimumY) minimumY = vertex.Y;
                if (vertex.X > maximumX) maximumX = vertex.X;
                if (vertex.Y > maximumY) maximumY = vertex.Y;
            }
            AxisTransform transform = context.ResolveTransform(reader);
            SvgRectangle placement = transform.Apply(new SvgRectangle(minimumX, minimumY, (double)maximumX - minimumX, (double)maximumY - minimumY, default, true, null));
            return SvgClipPathSyntax.ResolveLeafClipOrPlacement(reader, state, state.EffectiveFrame, placement);

            static BudgetedParserList<SvgPoint> TokenizeShapePoints(XmlReader shape, string name, bool pathData, int maximumGeometryElements, RenderBudget geometryBudget, SvgPaintableElementContext shapeContext)
            {
                try
                {
                    if (pathData)
                    {
                        string d = shape.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
                        using BudgetedParserList<int> contourEnds = new(geometryBudget, SvgMemoryCategory.Geometry, shapeContext.WorkCounter, shapeContext.CancellationToken);
                        return SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, geometryBudget, shapeContext.WorkCounter, shapeContext.CancellationToken, contourEnds);
                    }
                    string pointsValue = shape.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, name + " points are required.");
                    return SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, name, maximumGeometryElements, geometryBudget, shapeContext.WorkCounter, shapeContext.CancellationToken);
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
