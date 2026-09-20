using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Combines inline declarations, the matched stylesheet declaration, and presentation attributes.</summary>
internal static class SvgComputedStyleResolver
{
    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle styleSheet, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => SvgComputedStyle.Resolve(reader, SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken), styleSheet);

    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle styleSheet, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowsDirectTextFontProperties)
        => SvgComputedStyle.Resolve(reader, SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, allowsDirectTextFontProperties), styleSheet);

    internal static SvgComputedStyle Resolve(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Resolve(reader, default, workCounter, cancellationToken);
}
