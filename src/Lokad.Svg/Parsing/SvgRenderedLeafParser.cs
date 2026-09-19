using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Routes supported rendered leaves through their bounded lowerers outside XML traversal.</summary>
internal static class SvgRenderedLeafParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, bool isEligible, SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (!isEligible || reader.NamespaceURI != "http://www.w3.org/2000/svg") return false;
        SvgPaint inheritedPaint = state.Groups.Count == 0 ? root.Paint : state.Groups[^1].Paint;
        bool inheritedDisplayed = state.Groups.Count == 0 ? root.IsDisplayed : state.Groups[^1].IsDisplayed;
        bool inheritedVisibility = state.Groups.Count == 0 ? root.IsVisible : state.Groups[^1].IsVisible;
        AxisTransform transform = state.Groups.Count == 0 ? AxisTransform.Identity : state.Groups[^1].Transform;
        InlineStyle styleSheet = SvgStyleSheet.Resolve(reader, state.StyleRules, directChild, state.Groups.Any(static group => group.Name == "g"), state.Groups.Count > 0 && state.Groups[^1].Name == "g", workCounter, cancellationToken);

        switch (reader.LocalName)
        {
            case "rect":
                state.Scene.AddRectangle(SvgLeafParser.ParseRectangle(reader, inheritedPaint, inheritedDisplayed, inheritedVisibility, transform, styleSheet, namedColors, workCounter, cancellationToken));
                return true;
            case "line":
                state.Scene.AddRectangle(SvgLeafParser.ParseLine(reader, inheritedPaint, inheritedDisplayed, inheritedVisibility, transform, styleSheet, namedColors, workCounter, cancellationToken));
                return true;
            case "path":
                using (SvgParsedGeometry path = SvgLeafParser.ParseOrthogonalPath(reader, inheritedPaint, inheritedDisplayed, inheritedVisibility, transform, styleSheet, limits.MaximumPathCommands - state.GeometryElementCount, budget, workCounter, cancellationToken, namedColors))
                {
                    state.GeometryElementCount += path.ElementCount;
                    state.Scene.AddRectangles(path.Rectangles);
                }
                return true;
            case "polygon" or "polyline":
                using (SvgParsedGeometry pointList = SvgLeafParser.ParseOrthogonalPointList(reader, reader.LocalName, inheritedPaint, inheritedDisplayed, inheritedVisibility, transform, styleSheet, limits.MaximumPathCommands - state.GeometryElementCount, budget, workCounter, cancellationToken, namedColors))
                {
                    state.GeometryElementCount += pointList.ElementCount;
                    state.Scene.AddRectangles(pointList.Rectangles);
                }
                return true;
            default:
                return false;
        }
    }
}
