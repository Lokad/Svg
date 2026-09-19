using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the single supported root stylesheet before rendered content is traversed.</summary>
internal static class SvgRootStyleSheetParser
{
    internal static void Parse(XmlReader reader, SvgRootAttributes root, SvgDocumentState state, int maximumRules, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (state.SawStyleableNode || state.HasStyleSheet) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A single root-level rectangle stylesheet rule must precede supported definitions and rendered content.");
        if (reader.HasAttributes) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet attributes are not implemented.");
        SvgStyleSheet.Parse(SvgStyleSheet.ReadElementText(reader), maximumRules, state.StyleRules, budget, workCounter, cancellationToken);
        if (SvgStyleSheet.TargetsRootId(root.Id, state.StyleRules, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules targeting the SVG root are not implemented.");
        state.HasStyleSheet = true;
    }
}
