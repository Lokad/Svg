using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Routes supported rendered leaves through their bounded lowerers outside XML traversal.</summary>
internal static class SvgRenderedLeafParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, bool isEligible, SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (!isEligible || reader.NamespaceURI != "http://www.w3.org/2000/svg") return false;
        switch (reader.LocalName)
        {
            case "rect":
                SvgElementAttributePolicy.ValidateRectangle(reader);
                break;
            case "line":
                SvgElementAttributePolicy.ValidateLine(reader);
                break;
            case "path":
                SvgElementAttributePolicy.ValidatePath(reader);
                break;
            case "polygon" or "polyline":
                SvgElementAttributePolicy.ValidatePointList(reader, reader.LocalName);
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
        }
        return false;
    }
}
