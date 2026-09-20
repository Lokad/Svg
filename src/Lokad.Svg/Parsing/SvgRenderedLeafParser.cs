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
                if (fontSnapshot is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG text requires a prepared font snapshot.");
                SvgResolvedDirectTextElement resolvedText = context.ResolveTextShape(reader);
                SvgResolvedPaintableElement textElement = resolvedText.PaintableElement;
                if (!textElement.Transform.Equals(AxisTransform.Identity)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text transforms are not implemented.");
                if (textElement.Style.FillRule is not null && !textElement.Style.FillRule.Equals("nonzero", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only the default nonzero fill rule is implemented for direct text.");
                SvgTextStyle textStyle = SvgTextSyntax.Parse(reader, resolvedText.InlineFont, context.InheritedTextStyle);
                using BudgetedText text = ReadDirectTextContent(reader, SvgTextLimits.MaximumCharacters - state.TextCharacterCount, budget, workCounter, cancellationToken);
                state.TextCharacterCount += text.Value.Length;
                using SvgTextRun run = SvgTextRunBuilder.Create(textStyle, text.Value, fontSnapshot, budget, workCounter, cancellationToken);
                if (textElement.Presentation.HasFill)
                {
                    foreach (SvgGlyphQuadraticContour[] contours in SvgTextRunBuilder.GetGlyphContours(run, budget, workCounter, cancellationToken))
                    {
                        SvgGlyphGeometry glyph = SvgGlyphGeometry.Create(contours, textElement.Presentation.Color, budget);
                        try { state.Scene.AddGeometry(SvgGeometry.FromGlyphContours(glyph)); }
                        catch { glyph.Dispose(); throw; }
                    }
                }
                return true;
            }
        }
        return false;

        static BudgetedText ReadDirectTextContent(XmlReader element, int maximumCharacters, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            using var text = new BudgetedTextBuffer(budget);
            if (element.IsEmptyElement) return text.Detach();
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
                        if (read != 0) text.Append(chunk.AsSpan(0, read), maximumCharacters, workCounter, cancellationToken);
                    } while (read != 0);
                    continue;
                }
                if (subtree.NodeType == XmlNodeType.Element && subtree.Depth != 0)
                {
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text child elements are not implemented.");
                }
            }
            return text.Detach();
        }
    }
}
