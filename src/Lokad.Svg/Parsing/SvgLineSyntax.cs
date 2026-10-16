using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Retains finite bounded line endpoints before style-driven lowering.</summary>
internal readonly record struct SvgLineSyntax(double X1, double Y1, double X2, double Y2, string? StrokeWidth)
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
        double x1 = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x1") ?? "0");
        double y1 = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y1") ?? "0");
        double x2 = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x2") ?? "0");
        double y2 = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y2") ?? "0");
        return new SvgLineSyntax(x1, y1, x2, y2, reader.GetAttribute("stroke-width"));
    }
}
