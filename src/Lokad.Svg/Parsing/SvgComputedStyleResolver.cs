using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Combines inline declarations, the matched stylesheet declaration, and presentation attributes.</summary>
internal static class SvgComputedStyleResolver
{
    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle styleSheet, CancellationToken cancellationToken)
        => SvgComputedStyle.Resolve(reader, ReadInline(reader, cancellationToken, false), styleSheet);

    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle styleSheet, CancellationToken cancellationToken, bool allowsDirectTextFontProperties)
        => SvgComputedStyle.Resolve(reader, ReadInline(reader, cancellationToken, allowsDirectTextFontProperties), styleSheet);

    private static InlineStyle ReadInline(XmlReader reader, CancellationToken token, bool directText)
        => SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)
            ? SvgInlineStyleParser.ParseGeometry(reader.GetAttribute("style"), token)
            : SvgInlineStyleParser.Parse(reader.GetAttribute("style"), token, directText);

    internal static SvgComputedStyle Resolve(XmlReader reader, CancellationToken cancellationToken)
        => Resolve(reader, default, cancellationToken);
}
