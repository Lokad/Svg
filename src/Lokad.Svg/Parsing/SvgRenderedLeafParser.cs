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
            case "text":
                SvgElementAttributePolicy.ValidateText(reader, workCounter, cancellationToken);
                break;
            default:
                return false;
        }
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);

        switch (reader.LocalName)
        {
            case "rect":
                state.Scene.AddGeometry(SvgLeafParser.ParseRectangle(reader, context));
                return true;
            case "line":
                state.Scene.AddGeometry(SvgLeafParser.ParseLine(reader, context));
                return true;
            case "path":
                using (SvgParsedGeometry path = SvgOrthogonalShapeParser.ParsePath(reader, context, limits.MaximumPathCommands - state.GeometryElementCount, budget))
                {
                    state.GeometryElementCount += path.ElementCount;
                    state.Scene.AddGeometries(path.Geometries);
                }
                return true;
            case "polygon" or "polyline":
                using (SvgParsedGeometry pointList = SvgOrthogonalShapeParser.ParsePointList(reader, reader.LocalName, context, limits.MaximumPathCommands - state.GeometryElementCount, budget))
                {
                    state.GeometryElementCount += pointList.ElementCount;
                    state.Scene.AddGeometries(pointList.Geometries);
                }
                return true;
            case "text":
            {
                SvgResolvedDirectTextElement resolvedText = context.ResolveTextShape(reader);
                SvgResolvedPaintableElement textElement = resolvedText.PaintableElement;
                if (!textElement.Transform.Equals(AxisTransform.Identity)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text transforms are not implemented.");
                if (textElement.Style.FillRule is not null && !textElement.Style.FillRule.Equals("nonzero", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only the default nonzero fill rule is implemented for direct text.");
                SvgTextStyle textStyle = SvgTextSyntax.Parse(reader, resolvedText.InlineFont, context.InheritedTextStyle, resolvedText.LetterSpacing);
                SvgTextWhitespaceMode whitespaceMode = SvgTextSyntax.ParseWhitespaceMode(reader, resolvedText.WhiteSpace, context.InheritedTextStyle.WhitespaceMode);
                using BudgetedText text = ReadDirectTextContent(reader, whitespaceMode, SvgTextLimits.MaximumCharacters - state.TextCharacterCount, limits.MaximumNodes - state.NodeCount, limits.MaximumXmlDepth, limits.MaximumAttributesPerElement, budget, workCounter, cancellationToken, out int sourceCharacterCount, out int childNodeCount);
                state.TextCharacterCount += sourceCharacterCount;
                state.NodeCount += childNodeCount;
                if (text.Value.Length == 0) return true;
                if (!textElement.Presentation.IsPainted || !textElement.Presentation.HasFill) return true;
                if (fontSnapshot is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG text requires a prepared font snapshot.");
                using SvgTextRun run = SvgTextRunBuilder.Create(textStyle, text.Value, fontSnapshot, budget, workCounter, cancellationToken);
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

        static BudgetedText ReadDirectTextContent(XmlReader element, SvgTextWhitespaceMode whitespaceMode, int maximumCharacters, int remainingNodeCount, int maximumXmlDepth, int maximumAttributesPerElement, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken, out int sourceCharacterCount, out int childNodeCount)
        {
            using var text = new BudgetedTextBuffer(budget);
            var normalizer = new SvgTextWhitespaceNormalizer(whitespaceMode);
            if (element.IsEmptyElement)
            {
                sourceCharacterCount = normalizer.SourceCharacterCount;
                childNodeCount = 0;
                return text.Detach();
            }
            childNodeCount = 0;
            sourceCharacterCount = 0;
            using var whitespaceNormalizers = new BudgetedParserList<SvgTextWhitespaceNormalizer>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
            whitespaceNormalizers.Add(new SvgTextWhitespaceNormalizer(whitespaceMode));
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
                    SvgTextWhitespaceMode inheritedWhitespaceMode = whitespaceNormalizers[^1].Mode;
                    if (subtree.NamespaceURI != "http://www.w3.org/2000/svg" || subtree.LocalName != "tspan" || !SvgTextSyntax.TryParseTspanWhitespaceMode(subtree, inheritedWhitespaceMode, workCounter, cancellationToken, out SvgTextWhitespaceMode childWhitespaceMode)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only whitespace-only tspan text children are implemented.");
                    if (!subtree.IsEmptyElement) whitespaceNormalizers.Add(new SvgTextWhitespaceNormalizer(childWhitespaceMode));
                    continue;
                }
                if (subtree.NodeType == XmlNodeType.EndElement && subtree.Depth != 0)
                {
                    SvgTextWhitespaceNormalizer current = whitespaceNormalizers[^1];
                    current.CompleteElement();
                    whitespaceNormalizers.RemoveLast();
                }
            }
            return text.Detach();
        }
    }
}
