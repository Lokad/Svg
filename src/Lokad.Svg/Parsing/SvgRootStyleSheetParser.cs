using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the supported root stylesheets before rendered content is traversed.</summary>
internal static class SvgRootStyleSheetParser
{
    /// <summary>Rejects a root stylesheet that follows styleable content; earlier stylesheets accumulate in document order.</summary>
    internal static void ThrowIfLate(bool sawStyleableNode)
    {
        if (sawStyleableNode) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root-level stylesheet rules must precede supported definitions and rendered content.");
    }

    /// <summary>Rejects stylesheet element attributes, which are not implemented.</summary>
    internal static void ThrowIfAttributed(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.HasAttributes) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet attributes are not implemented.");
    }

    internal static void Parse(XmlReader reader, SvgRootAttributes root, SvgDocumentState state, int maximumRules, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ThrowIfLate(state.SawStyleableNode);
        ThrowIfAttributed(reader);
        using (OwnedStyleSheetText text = SvgStyleSheet.ReadElementText(reader, budget, workCounter, cancellationToken))
        {
            SvgStyleSheet.Parse(text.Value, maximumRules, state.StyleRules, state.Strings, budget, workCounter, cancellationToken);
        }
        if (SvgStyleSheet.TargetsRootId(root.Id, state.StyleRules, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules targeting the SVG root are not implemented.");
    }
}
