using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Maps the current streaming document context to the bounded stylesheet cascade.</summary>
internal static class SvgDocumentStyleResolver
{
    internal static InlineStyle Resolve(XmlReader reader, bool directChild, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => SvgStyleSheet.Resolve(reader, state.StyleRules, directChild, state.GroupDepth != 0, state.Groups.Count > 0 && state.Groups[^1].Kind is SvgGroupKind.Group or SvgGroupKind.PatternGroup, workCounter, cancellationToken);
}
