using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Maps the current streaming document context to the bounded stylesheet cascade.</summary>
internal static class SvgDocumentStyleResolver
{
    internal static InlineStyle Resolve(XmlReader reader, SvgDocumentState state, CancellationToken cancellationToken)
        => SvgStyleSheet.Resolve(reader, state.StyleRules, state.CssAncestors, cancellationToken);
}
