using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the policy-validated bounded local-use reference syntax before style resolution.</summary>
internal readonly record struct SvgLocalReferenceSyntax(string Id, double X, double Y)
{
    internal static SvgLocalReferenceSyntax Parse(XmlReader reader, BudgetedParserStringStore strings)
    {
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A use element requires an href.");
        if (href.Length < 2 || href[0] != '#') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local supported-geometry use references are implemented.");
        if (href.Length - 1 > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Local reference identifiers exceed the configured syntax limit.");
        double x = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0");
        double y = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        return new SvgLocalReferenceSyntax(strings.Copy(href.AsSpan(1)), x, y);
    }
}