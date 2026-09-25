using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded line attributes after XML syntax validation and before style-driven lowering.</summary>
/// <summary>Represents geometry parsed from a policy-validated line element.</summary>
internal readonly record struct SvgLineSyntax(int X1, int Y1, int X2, int Y2, string? StrokeWidth)
{
    /// <summary>Extends axis-aligned endpoints by half the even width for square caps. Callers validate axis alignment first.</summary>
    internal SvgLineSyntax WithSquareCaps(int strokeWidth)
    {
        int halfWidth = strokeWidth / 2;
        return X1 != X2
            ? this with { X1 = Math.Min(X1, X2) - halfWidth, X2 = Math.Max(X1, X2) + halfWidth }
            : this with { Y1 = Math.Min(Y1, Y2) - halfWidth, Y2 = Math.Max(Y1, Y2) + halfWidth };
    }
    internal static SvgLineSyntax Parse(XmlReader reader)
    {
        int x1 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("x1") ?? "0");
        int y1 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("y1") ?? "0");
        int x2 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("x2") ?? "0");
        int y2 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("y2") ?? "0");
        return new SvgLineSyntax(x1, y1, x2, y2, reader.GetAttribute("stroke-width"));
    }
}
