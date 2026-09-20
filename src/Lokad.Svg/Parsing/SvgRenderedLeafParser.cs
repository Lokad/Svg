using System.Xml;
using System.Text;
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
                if (fontSnapshot is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG text requires a prepared font snapshot.");
                SvgResolvedPaintableElement textElement = context.ResolveShape(reader, false);
                if (!textElement.Transform.Equals(AxisTransform.Identity)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text transforms are not implemented.");
                SvgTextStyle textStyle = SvgTextSyntax.Parse(reader);
                string text = ReadDirectTextContent(reader);
                SvgTextRun run = SvgTextRunBuilder.Create(textStyle, text, fontSnapshot, cancellationToken);
                if (textElement.Presentation.HasFill)
                {
                    foreach (SvgGlyphQuadraticContour[] contours in SvgTextRunBuilder.GetGlyphContours(run, cancellationToken)) state.Scene.AddGeometry(SvgGeometry.FromGlyphContours(new SvgGlyphGeometry(contours, textElement.Presentation.Color)));
                }
                return true;
        }
        return false;

        static string ReadDirectTextContent(XmlReader element)
        {
            if (element.IsEmptyElement) return string.Empty;
            var text = new StringBuilder();
            using XmlReader subtree = element.ReadSubtree();
            while (subtree.Read())
            {
                if (subtree.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    text.Append(subtree.Value);
                    continue;
                }
                if (subtree.NodeType == XmlNodeType.Element && subtree.Depth != 0)
                {
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text child elements are not implemented.");
                }
            }
            return text.ToString();
        }
    }
}
