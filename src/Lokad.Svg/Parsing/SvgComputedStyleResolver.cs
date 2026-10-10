using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Combines inline declarations, the matched stylesheet declaration, and presentation attributes.</summary>
internal static class SvgComputedStyleResolver
{
    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle styleSheet, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => SvgComputedStyle.Resolve(reader, ReadInline(reader, workCounter, cancellationToken, false), styleSheet);

    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle styleSheet, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowsDirectTextFontProperties)
        => SvgComputedStyle.Resolve(reader, ReadInline(reader, workCounter, cancellationToken, allowsDirectTextFontProperties), styleSheet);

    private static InlineStyle ReadInline(XmlReader reader, ParserWorkCounter work, CancellationToken token, bool directText)
        => SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)
            ? SvgInlineStyleParser.ParseGeometry(reader.GetAttribute("style"), work, token)
            : SvgInlineStyleParser.Parse(reader.GetAttribute("style"), work, token, directText);

    internal static SvgComputedStyle Resolve(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Resolve(reader, default, workCounter, cancellationToken);
}
