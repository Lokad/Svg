using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Maps the current streaming document context to the bounded stylesheet cascade.</summary>
internal static class SvgDocumentStyleResolver
{
    internal static InlineStyle Resolve(XmlReader reader, bool directChild, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => SvgStyleSheet.Resolve(reader, state.StyleRules, directChild, state.Groups.Any(static group => group.Name == "g"), state.Groups.Count > 0 && state.Groups[^1].Name == "g", workCounter, cancellationToken);
}
